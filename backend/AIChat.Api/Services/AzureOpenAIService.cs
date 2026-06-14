using System.ClientModel;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.AI.OpenAI;
using OpenAI.Chat;
using OpenAI.Embeddings;
using OpenAI.Images;
using Microsoft.Extensions.Options;
using AIChat.Api.Models;
using AppChatMessage = AIChat.Api.Models.ChatMessage;

namespace AIChat.Api.Services;

public class AzureOpenAIService : IAzureOpenAIService
{
    private const string ImageFetchHttpClient = "azure-openai-image-fetch";
    private const int MaxExtractionExistingMemoryChars = 4000;

    // On follow-up turns, only the most recent few AI-generated images are inlined as
    // image bytes (so the model can actually "see" and edit them). Older generated images
    // are represented by their tool-result text (prompt + caption) to bound context cost.
    private const int MaxInlinedGeneratedImages = 2;

    private readonly AzureOpenAIClient _client;
    private readonly ConcurrentDictionary<string, ChatClient> _chatClients = new();
    private readonly Lazy<EmbeddingClient?> _embeddingClient;
    private readonly Lazy<ImageClient?> _imageClient;
    private readonly AzureOpenAISettings _settings;
    private readonly MemorySettings _memorySettings;
    private readonly IImageStorageService _imageStorage;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AzureOpenAIService> _logger;

    public AzureOpenAIService(
        IOptions<AzureOpenAISettings> settings,
        IOptions<MemorySettings> memorySettings,
        IImageStorageService imageStorage,
        IHttpClientFactory httpClientFactory,
        ILogger<AzureOpenAIService> logger)
    {
        _logger = logger;
        _imageStorage = imageStorage;
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _memorySettings = memorySettings.Value;

        if (string.IsNullOrEmpty(_settings.Endpoint) || string.IsNullOrEmpty(_settings.ApiKey))
        {
            throw new InvalidOperationException("AzureOpenAI:Endpoint and ApiKey must be configured (via config/azure-openai.json or environment variables)");
        }

        _client = new AzureOpenAIClient(new Uri(_settings.Endpoint), new ApiKeyCredential(_settings.ApiKey));

        _embeddingClient = new Lazy<EmbeddingClient?>(() =>
            string.IsNullOrWhiteSpace(_memorySettings.EmbeddingDeploymentName)
                ? null
                : _client.GetEmbeddingClient(_memorySettings.EmbeddingDeploymentName));

        _imageClient = new Lazy<ImageClient?>(() =>
        {
            if (string.IsNullOrWhiteSpace(_settings.ImageGenerationModelId)) return null;
            var model = _settings.Models.FirstOrDefault(m => m.Id == _settings.ImageGenerationModelId);
            if (model is null)
            {
                _logger.LogWarning("ImageGenerationModelId={Id} not found in Models[]", _settings.ImageGenerationModelId);
                return null;
            }
            return _client.GetImageClient(model.DeploymentName);
        });
    }

    public bool IsImageGenerationAvailable =>
        _settings.EnableImageGeneration && _imageClient.Value is not null;

    public List<ModelInfo> GetAvailableModels() => _settings.Models;

    public string GetDefaultModel() => _settings.DefaultModel;

    public int GetDefaultContextSize() => _settings.DefaultContextSize;

    public List<int> GetContextSizeOptions() => _settings.ContextSizeOptions;

    public int GetDefaultMaxMessages() => _settings.DefaultMaxMessages;

    public List<int> GetMaxMessagesOptions() => _settings.MaxMessagesOptions;

    private ChatClient GetChatClient(string modelId)
    {
        return _chatClients.GetOrAdd(modelId, id =>
        {
            var model = _settings.Models.FirstOrDefault(m => m.Id == id)
                ?? _settings.Models.FirstOrDefault(m => m.Id == _settings.DefaultModel)
                ?? _settings.Models.First();

            return _client.GetChatClient(model.DeploymentName);
        });
    }

    public async IAsyncEnumerable<StreamEvent> StreamChatCompletionAsync(
        string userId,
        List<AppChatMessage> messages,
        string modelId,
        int maxContextSize,
        int maxMessages,
        bool allowImageGeneration,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var truncated = TruncateContext(messages, maxContextSize, maxMessages);
        _logger.LogInformation("Starting stream for model {ModelId}: {Truncated}/{Original} messages, allowImageGen={AllowImg}",
            modelId, truncated.Count, messages.Count, allowImageGeneration);

        var openAIMessages = await TranslateAsync(truncated, userId, cancellationToken);
        if (!openAIMessages.Any(m => m is SystemChatMessage))
        {
            openAIMessages.Insert(0, new SystemChatMessage(
                "You are a helpful AI assistant. Be concise and helpful in your responses."));
        }

        var chatClient = GetChatClient(modelId);
        var toolEnabled = allowImageGeneration && IsImageGenerationAvailable;

        var pass1Options = new ChatCompletionOptions();
        if (toolEnabled) pass1Options.Tools.Add(BuildImageTool());

        // ----- Pass 1 -----
        var pass1Text = new StringBuilder();
        var accumulator = new ToolCallAccumulator();

        await foreach (var update in chatClient
            .CompleteChatStreamingAsync(openAIMessages, pass1Options, cancellationToken)
            .WithCancellation(cancellationToken))
        {
            foreach (var part in update.ContentUpdate)
            {
                if (!string.IsNullOrEmpty(part.Text))
                {
                    pass1Text.Append(part.Text);
                    yield return new TextDelta(part.Text);
                }
            }
            foreach (var tcu in update.ToolCallUpdates) accumulator.Apply(tcu);
        }

        var toolCalls = accumulator.Build();
        if (toolCalls.Count == 0)
        {
            _logger.LogInformation("Pass 1 done, no tool calls. Text length={Len}", pass1Text.Length);
            yield break;
        }

        _logger.LogInformation("Pass 1 emitted {Count} tool call(s); executing", toolCalls.Count);

        // Append the assistant's tool-call turn to history.
        openAIMessages.Add(BuildAssistantToolCallMessage(toolCalls, pass1Text.ToString()));

        // ----- Tool execution -----
        var attachments = new List<MessageAttachment>();
        foreach (var tc in toolCalls)
        {
            if (tc.FunctionName != "generate_image")
            {
                openAIMessages.Add(new ToolChatMessage(tc.Id,
                    """{"status":"error","message":"unknown tool"}"""));
                continue;
            }

            yield return new ToolCallStart(tc.FunctionName, tc.Id);

            var (attachment, errorJson) = await TryExecuteImageToolAsync(userId, tc, cancellationToken);
            if (attachment is not null)
            {
                attachments.Add(attachment);
                yield return new AttachmentReady(attachment, tc.Id);
                openAIMessages.Add(new ToolChatMessage(tc.Id, BuildImageOkResultJson(attachment)));
            }
            else
            {
                openAIMessages.Add(new ToolChatMessage(tc.Id, errorJson ?? """{"status":"error"}"""));
            }
        }

        // ----- Pass 2: model writes natural-language wrap-up after seeing tool result(s) -----
        var pass2Text = new StringBuilder();
        var pass2Options = new ChatCompletionOptions(); // no tools; we don't want recursion

        await foreach (var update in chatClient
            .CompleteChatStreamingAsync(openAIMessages, pass2Options, cancellationToken)
            .WithCancellation(cancellationToken))
        {
            foreach (var part in update.ContentUpdate)
            {
                if (!string.IsNullOrEmpty(part.Text))
                {
                    pass2Text.Append(part.Text);
                    yield return new TextDelta(part.Text);
                }
            }
        }

        // Save the wrap-up next to each generated image as a sidecar; useful for later
        // reconstruction, search, or re-prompting. Best-effort — never fails the stream.
        if (pass2Text.Length > 0 && attachments.Count > 0)
        {
            var description = pass2Text.ToString();
            foreach (var att in attachments)
            {
                try { await _imageStorage.SaveDescriptionAsync(userId, att.Id, description, cancellationToken); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to save description for image {ImageId}", att.Id);
                }
            }
        }

        _logger.LogInformation("Stream complete: pass1Text={P1}, attachments={A}, pass2Text={P2}",
            pass1Text.Length, attachments.Count, pass2Text.Length);
    }

    private async Task<(MessageAttachment? Attachment, string? ErrorJson)> TryExecuteImageToolAsync(
        string userId, ChatToolCall tc, CancellationToken ct)
    {
        try
        {
            var (prompt, size) = ParseImageToolArgs(tc.FunctionArguments);
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return (null, """{"status":"error","message":"prompt is required"}""");
            }
            var attachment = await GenerateImageAsync(userId, prompt, size, ct);
            return (attachment, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "generate_image tool execution failed");
            var safeMsg = JsonEncodedText.Encode(ex.Message).ToString();
            return (null, $$"""{"status":"error","message":"{{safeMsg}}"}""");
        }
    }

    private static (string Prompt, string? Size) ParseImageToolArgs(BinaryData argumentsJson)
    {
        if (argumentsJson is null) return ("", null);
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson.ToMemory());
            var root = doc.RootElement;
            var prompt = root.TryGetProperty("prompt", out var p) ? p.GetString() ?? "" : "";
            var size = root.TryGetProperty("size", out var s) ? s.GetString() : null;
            return (prompt, size);
        }
        catch (JsonException)
        {
            return ("", null);
        }
    }

    private static string BuildImageOkResultJson(MessageAttachment a) =>
        JsonSerializer.Serialize(new
        {
            status = "ok",
            imageId = a.Id,
            prompt = a.Prompt,
            revisedPrompt = a.RevisedPrompt,
            width = a.Width,
            height = a.Height,
            note = "Image saved and shown to the user. Briefly describe what you generated; do not include the URL.",
        });

    private static AssistantChatMessage BuildAssistantToolCallMessage(List<ChatToolCall> toolCalls, string leadingText)
    {
        var asst = new AssistantChatMessage(toolCalls);
        if (!string.IsNullOrWhiteSpace(leadingText))
        {
            asst.Content.Add(ChatMessageContentPart.CreateTextPart(leadingText));
        }
        return asst;
    }

    private static ChatTool BuildImageTool()
    {
        const string schema = """
        {
          "type": "object",
          "properties": {
            "prompt": {
              "type": "string",
              "description": "Detailed English description of the image to generate. Refine vague user prompts; include style, composition, lighting, and mood when relevant."
            },
            "size": {
              "type": "string",
              "enum": ["1024x1024", "1792x1024", "1024x1792"],
              "description": "Image dimensions. 1792x1024 for landscape/wide subjects, 1024x1792 for portraits/tall subjects, 1024x1024 (default) otherwise."
            }
          },
          "required": ["prompt"]
        }
        """;

        return ChatTool.CreateFunctionTool(
            functionName: "generate_image",
            functionDescription: "Generates an image from a text prompt and shows it to the user inline. Use this whenever the user asks for a picture, drawing, illustration, design visualization, photo, etc.",
            functionParameters: BinaryData.FromString(schema));
    }

    /// <summary>
    /// Translates our persisted message schema into OpenAI's typed chat messages.
    /// Handles three non-trivial cases:
    ///  - User messages with image Attachments: read bytes from storage and inline
    ///    them as ImageParts (Azure OpenAI cannot fetch our internal URLs).
    ///  - Assistant messages with persisted ToolCalls: expand into the canonical
    ///    asst_tool_call → tool_result triple so the model sees the same shape on
    ///    follow-up turns as it produced originally. The tool_result text carries the
    ///    generation prompt plus the saved caption, and the most recent generated images
    ///    are additionally inlined as a follow-up user image message so the model can see them.
    ///  - Plain text messages: straightforward 1:1.
    /// </summary>
    private async Task<List<OpenAI.Chat.ChatMessage>> TranslateAsync(
        List<AppChatMessage> messages, string userId, CancellationToken ct)
    {
        var result = new List<OpenAI.Chat.ChatMessage>(messages.Count);
        var inlineImageIds = SelectRecentGeneratedImageIds(messages, MaxInlinedGeneratedImages);

        foreach (var m in messages)
        {
            switch (m.Role)
            {
                case "system":
                    result.Add(new SystemChatMessage(m.Content));
                    break;

                case "user":
                    result.Add(await BuildUserMessageAsync(m, userId, ct));
                    break;

                case "assistant":
                    if (m.ToolCalls is { Count: > 0 } toolCalls)
                    {
                        // Replay: assistant_tool_call (with optional leading text) +
                        // tool_result for each + assistant text wrap-up.
                        var calls = toolCalls.Select(tc =>
                            ChatToolCall.CreateFunctionToolCall(tc.Id, tc.Name, BinaryData.FromString(tc.ArgumentsJson)))
                            .ToList();

                        // We don't persist the leading text separately, so attach the
                        // current Content as the wrap-up after the tool results. Pass an
                        // empty leading string to BuildAssistantToolCallMessage so the
                        // tool_calls remain the only payload of the first asst message.
                        result.Add(BuildAssistantToolCallMessage(calls, leadingText: ""));

                        // Tool results — reconstruct from attachments (paired by index).
                        for (var i = 0; i < toolCalls.Count; i++)
                        {
                            var tc = toolCalls[i];
                            string toolResult;
                            if (tc.Name == "generate_image" && m.Attachments is { Count: > 0 } atts && i < atts.Count)
                            {
                                toolResult = await BuildImageReplayResultJsonAsync(userId, atts[i], ct);
                            }
                            else
                            {
                                toolResult = """{"status":"ok"}""";
                            }
                            result.Add(new ToolChatMessage(tc.Id, toolResult));
                        }

                        if (!string.IsNullOrWhiteSpace(m.Content))
                        {
                            result.Add(new AssistantChatMessage(m.Content));
                        }

                        // Inline the most recent generated image(s) as a follow-up user image
                        // message. Tool/assistant messages can't carry image parts in chat
                        // completions, so this synthetic user turn is how the model actually
                        // "sees" what it drew and can edit it on request.
                        await AppendInlinedImagesAsync(result, m, userId, inlineImageIds, ct);
                    }
                    else
                    {
                        result.Add(new AssistantChatMessage(m.Content));
                    }
                    break;

                case "tool":
                    if (!string.IsNullOrEmpty(m.ToolCallId))
                    {
                        result.Add(new ToolChatMessage(m.ToolCallId, m.Content));
                    }
                    break;

                default:
                    // Unknown role — fall back to user.
                    result.Add(new UserChatMessage(m.Content));
                    break;
            }
        }

        return result;
    }

    private async Task<UserChatMessage> BuildUserMessageAsync(
        AppChatMessage m, string userId, CancellationToken ct)
    {
        if (m.Attachments is null || m.Attachments.Count == 0)
        {
            return new UserChatMessage(m.Content);
        }

        var parts = new List<ChatMessageContentPart>();
        if (!string.IsNullOrWhiteSpace(m.Content))
        {
            parts.Add(ChatMessageContentPart.CreateTextPart(m.Content));
        }

        foreach (var att in m.Attachments)
        {
            var part = await TryBuildImagePartAsync(userId, att, ct);
            if (part is not null) parts.Add(part);
        }

        // If we somehow ended up with no content parts (e.g. all attachments missing),
        // fall back to text alone so the request doesn't fail.
        if (parts.Count == 0) parts.Add(ChatMessageContentPart.CreateTextPart(m.Content ?? ""));

        return new UserChatMessage(parts);
    }

    /// <summary>
    /// Reads a stored image attachment and builds an OpenAI image content part, or returns
    /// null if it isn't an image, the URL is unparseable, or the file is missing on disk.
    /// </summary>
    private async Task<ChatMessageContentPart?> TryBuildImagePartAsync(
        string userId, MessageAttachment att, CancellationToken ct)
    {
        if (att.Type != "image") return null;
        var filename = ExtractFilename(att.Url);
        if (filename is null) return null;

        var read = await _imageStorage.TryReadAsync(userId, filename, ct);
        if (read is null)
        {
            _logger.LogWarning("Image attachment not found on disk: {Filename}", filename);
            return null;
        }

        return ChatMessageContentPart.CreateImagePart(
            BinaryData.FromBytes(read.Value.Bytes), read.Value.MimeType);
    }

    /// <summary>
    /// Returns the attachment ids of the most recent <paramref name="max"/> AI-generated
    /// images across the conversation. These get their bytes inlined on replay; older ones
    /// are represented by tool-result text only.
    /// </summary>
    private static HashSet<string> SelectRecentGeneratedImageIds(List<AppChatMessage> messages, int max)
    {
        var ids = new List<string>();
        foreach (var m in messages)
        {
            if (m.Role != "assistant"
                || m.ToolCalls is not { Count: > 0 } tcs
                || m.Attachments is not { Count: > 0 } atts)
            {
                continue;
            }

            for (var i = 0; i < tcs.Count && i < atts.Count; i++)
            {
                if (tcs[i].Name == "generate_image" && atts[i].Type == "image")
                {
                    ids.Add(atts[i].Id);
                }
            }
        }

        return ids.Skip(Math.Max(0, ids.Count - max)).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Appends a synthetic user message carrying the bytes of this assistant turn's generated
    /// images that are in the recent-inline set, so the model can see them on follow-up turns.
    /// </summary>
    private async Task AppendInlinedImagesAsync(
        List<OpenAI.Chat.ChatMessage> result,
        AppChatMessage m,
        string userId,
        HashSet<string> inlineImageIds,
        CancellationToken ct)
    {
        if (m.Attachments is not { Count: > 0 } atts) return;

        var imageParts = new List<ChatMessageContentPart>();
        foreach (var att in atts)
        {
            if (!inlineImageIds.Contains(att.Id)) continue;
            var part = await TryBuildImagePartAsync(userId, att, ct);
            if (part is not null) imageParts.Add(part);
        }

        if (imageParts.Count == 0) return;

        imageParts.Insert(0, ChatMessageContentPart.CreateTextPart(
            "(Reference: image(s) you generated earlier in this conversation, shown so you can see and edit them.)"));
        result.Add(new UserChatMessage(imageParts));
    }

    /// <summary>
    /// Tool-result JSON for a replayed generated image: the original prompt plus the saved
    /// caption (the model's own description), so even when the bytes aren't inlined the model
    /// retains a faithful text representation of what it drew.
    /// </summary>
    private async Task<string> BuildImageReplayResultJsonAsync(
        string userId, MessageAttachment a, CancellationToken ct)
    {
        var caption = await _imageStorage.TryReadDescriptionAsync(userId, a.Id, ct);
        return JsonSerializer.Serialize(new
        {
            status = "ok",
            imageId = a.Id,
            prompt = a.Prompt,
            revisedPrompt = a.RevisedPrompt,
            width = a.Width,
            height = a.Height,
            caption = string.IsNullOrWhiteSpace(caption) ? null : caption,
        });
    }

    private static string? ExtractFilename(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var lastSlash = url.LastIndexOf('/');
        if (lastSlash < 0 || lastSlash == url.Length - 1) return null;
        var filename = url[(lastSlash + 1)..];
        // Strip query string if any.
        var q = filename.IndexOf('?');
        return q >= 0 ? filename[..q] : filename;
    }

    private class ToolCallAccumulator
    {
        private readonly Dictionary<int, Entry> _calls = new();

        public void Apply(StreamingChatToolCallUpdate update)
        {
            if (!_calls.TryGetValue(update.Index, out var entry))
            {
                entry = new Entry();
                _calls[update.Index] = entry;
            }
            if (!string.IsNullOrEmpty(update.ToolCallId)) entry.Id = update.ToolCallId;
            if (!string.IsNullOrEmpty(update.FunctionName)) entry.Name = update.FunctionName;
            // FunctionArgumentsUpdate can be a BinaryData wrapping null bytes on the
            // first tool-call delta; calling ToString() on that throws. Use ToMemory().
            if (update.FunctionArgumentsUpdate is { } argsBin)
            {
                var span = argsBin.ToMemory().Span;
                if (span.Length > 0)
                {
                    entry.Args.Append(System.Text.Encoding.UTF8.GetString(span));
                }
            }
        }

        public List<ChatToolCall> Build()
        {
            return _calls.OrderBy(kv => kv.Key)
                .Where(kv => !string.IsNullOrEmpty(kv.Value.Id) && !string.IsNullOrEmpty(kv.Value.Name))
                .Select(kv => ChatToolCall.CreateFunctionToolCall(
                    kv.Value.Id,
                    kv.Value.Name,
                    BinaryData.FromString(kv.Value.Args.Length == 0 ? "{}" : kv.Value.Args.ToString())))
                .ToList();
        }

        private class Entry
        {
            public string Id = "";
            public string Name = "";
            public StringBuilder Args = new();
        }
    }

    public async Task<List<ExtractedMemory>> ExtractMemoriesAsync(
        List<AppChatMessage> messages,
        List<Memory> existingMemories,
        CancellationToken cancellationToken = default)
    {
        var chatClient = GetChatClient(_settings.DefaultModel);
        var promptMessages = BuildExtractionPrompt(messages, existingMemories);

        var options = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat(),
            // Deterministic, conservative extraction: low temperature keeps the model
            // from inventing speculative or marginal "memories" and makes results reproducible.
            Temperature = 0f,
        };

        ClientResult<ChatCompletion> result;
        try
        {
            result = await chatClient.CompleteChatAsync(promptMessages, options, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Extraction LLM call failed");
            throw;
        }

        var text = result.Value.Content.FirstOrDefault()?.Text ?? "{}";
        _logger.LogDebug("Extraction raw response: {Text}", text);

        try
        {
            var parsed = JsonSerializer.Deserialize<ExtractionResponse>(text, ExtractionJsonOptions);
            return parsed?.Memories ?? new List<ExtractedMemory>();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse extraction response as JSON, returning empty. Raw: {Text}", text);
            return new List<ExtractedMemory>();
        }
    }

    public async Task<MessageAttachment> GenerateImageAsync(
        string userId,
        string prompt,
        string? size = null,
        CancellationToken cancellationToken = default)
    {
        var client = _imageClient.Value
            ?? throw new InvalidOperationException("Image generation is not configured");
        if (!_settings.EnableImageGeneration)
        {
            throw new InvalidOperationException("Image generation is disabled");
        }

        var (parsedSize, w, h) = ParseSize(size);

        // ResponseFormat is intentionally NOT set: gpt-image-1/2 deployments reject it
        // ("Unknown parameter: 'response_format'"), and they return base64 bytes by default.
        // DALL-E 3 also returns b64_json by default for the SDK, so this works for both.
        // The URL fallback below catches the rare case where bytes are absent.
        var options = new ImageGenerationOptions
        {
            Size = parsedSize,
        };

        _logger.LogInformation("Generating image for user {UserId}, prompt length={Len}, size={Size}",
            userId, prompt.Length, $"{w}x{h}");

        // Image generation can take 20-60s typically; cap at 2 min so a hung Azure
        // request doesn't block the chat indefinitely. The cancellation propagates
        // to the SDK call and is caught upstream as a tool-execution failure.
        using var imgCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        imgCts.CancelAfter(TimeSpan.FromMinutes(2));

        var result = await client.GenerateImageAsync(prompt, options, imgCts.Token);
        var img = result.Value;

        BinaryData? bytes = img.ImageBytes;
        if (bytes is null && img.ImageUri is not null)
        {
            // Defensive fallback: some deployments may ignore ResponseFormat=Bytes and
            // return a URL anyway. Fetch it ourselves so the rest of the pipeline doesn't care.
            var http = _httpClientFactory.CreateClient(ImageFetchHttpClient);
            var fetched = await http.GetByteArrayAsync(img.ImageUri, cancellationToken);
            bytes = BinaryData.FromBytes(fetched);
        }
        if (bytes is null)
        {
            throw new InvalidOperationException("Image generation returned neither bytes nor a URL");
        }

        return await _imageStorage.SaveAsync(
            userId,
            bytes.ToMemory(),
            mimeType: "image/png",
            prompt: prompt,
            revisedPrompt: img.RevisedPrompt,
            width: w,
            height: h,
            cancellationToken: cancellationToken);
    }

    private static (GeneratedImageSize Size, int Width, int Height) ParseSize(string? size)
    {
        // Default: 1024x1024 (works for both DALL-E 3 and gpt-image-*).
        if (string.IsNullOrWhiteSpace(size))
        {
            return (GeneratedImageSize.W1024xH1024, 1024, 1024);
        }

        var parts = size.Split('x', 2);
        if (parts.Length == 2 &&
            int.TryParse(parts[0], out var w) &&
            int.TryParse(parts[1], out var h) &&
            w > 0 && h > 0)
        {
            // Prefer the predefined values where they line up exactly (allows the SDK
            // to send DALL-E-style enum strings); otherwise fall through to a custom size.
            if (w == 1024 && h == 1024) return (GeneratedImageSize.W1024xH1024, 1024, 1024);
            if (w == 1792 && h == 1024) return (GeneratedImageSize.W1792xH1024, 1792, 1024);
            if (w == 1024 && h == 1792) return (GeneratedImageSize.W1024xH1792, 1024, 1792);
            return (new GeneratedImageSize(w, h), w, h);
        }

        return (GeneratedImageSize.W1024xH1024, 1024, 1024);
    }

    public async Task<float[]?> TryGenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        var client = _embeddingClient.Value;
        if (client is null || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            var result = await client.GenerateEmbeddingAsync(text, cancellationToken: cancellationToken);
            return result.Value.ToFloats().ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Embedding generation failed; retrieval will fall back to keyword overlap");
            return null;
        }
    }

    private static readonly JsonSerializerOptions ExtractionJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private class ExtractionResponse
    {
        public List<ExtractedMemory> Memories { get; set; } = new();
    }

    private static List<OpenAI.Chat.ChatMessage> BuildExtractionPrompt(
        List<AppChatMessage> messages,
        List<Memory> existingMemories)
    {
        var transcript = new StringBuilder();
        foreach (var msg in messages)
        {
            var speaker = msg.Role switch
            {
                "user" => "User",
                "assistant" => "Assistant",
                "system" => "System",
                _ => msg.Role,
            };
            transcript.Append(speaker).Append(": ");
            transcript.AppendLine(msg.Content);
            transcript.AppendLine();
        }

        var existingMemoryBlock = BuildExistingMemoryBlock(existingMemories);

        var system = """
You extract durable, long-term user information from a chat transcript so it can help in FUTURE, unrelated conversations.

Default to extracting NOTHING. Only record something if it would plausibly still be useful weeks later in a different conversation. When in doubt, leave it out. An empty result is the common, correct outcome.

Output a JSON object with this exact shape:
{
  "memories": [
    { "type": "fact" | "preference" | "summary", "content": "<= 200 chars", "existingMemoryId": "<id to update, or null for new memory>" }
  ]
}

Each "content" must be a self-contained, third-person statement that makes sense without the transcript. Always name the subject (e.g. "The user ..."), never use pronouns like "he/they/it" referring to transcript context.

DO record (only when clearly stated, not guessed):
- fact: stable identity or context — e.g. "The user is a backend engineer working mainly in C# and .NET.", "The user maintains an internal chat app called AIChat."
- preference: enduring style/tooling choices — e.g. "The user prefers concise answers without pleasantries.", "The user wants code comments written in English."

DO NOT record:
- The current task or question — e.g. "The user is debugging the ExtractionWorker class."
- Transient states or feelings — e.g. "The user is tired today.", "The user is in a hurry."
- Conversation-internal details — file paths, variable names, this session's code under discussion.
- Pleasantries, acknowledgements, or chit-chat — e.g. "The user said thanks."
- Anything speculative or inferred that the user did not actually state.
- PII the user did not explicitly volunteer (emails, phone numbers, addresses).
- Secrets — passwords, API keys, tokens, credentials.

"summary" is rarely appropriate. Use it ONLY to capture a long-term project/background spanning the whole conversation that a plain fact cannot express. If unsure, do not produce a summary.

Use the existing memories only to avoid duplicates and to update outdated ones:
- If an equivalent memory already exists, do not output it again.
- If the transcript clearly corrects or refines an existing memory, output the improved content and set existingMemoryId to that memory's id.
- For genuinely new information, set existingMemoryId to null or omit it.

If nothing qualifies, return {"memories": []}. Return ONLY the JSON object, no commentary or code fences.
""";

        return new List<OpenAI.Chat.ChatMessage>
        {
            new SystemChatMessage(system),
            new UserChatMessage(existingMemoryBlock + "\n\nTranscript:\n\n" + transcript.ToString()),
        };
    }

    private static string BuildExistingMemoryBlock(List<Memory> existingMemories)
    {
        if (existingMemories.Count == 0)
        {
            return "Existing memories already saved:\nNone.";
        }

        var sb = new StringBuilder();
        sb.AppendLine("Existing memories already saved:");

        var usedChars = 0;
        foreach (var memory in existingMemories
            .OrderByDescending(m => m.Type == MemoryType.Preference)
            .ThenByDescending(m => m.LastUsedAt)
            .ThenByDescending(m => m.CreatedAt))
        {
            var line = $"- [{memory.Id}] {memory.Type}: {memory.Content}";
            if (usedChars + line.Length > MaxExtractionExistingMemoryChars) break;
            sb.AppendLine(line);
            usedChars += line.Length;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Truncates conversation context by message count and size.
    /// System messages (e.g. the memory-augmented prompt prepended by ChatHub)
    /// are always preserved; only user/assistant messages are trimmed.
    /// </summary>
    private List<AppChatMessage> TruncateContext(List<AppChatMessage> messages, int maxContextSize, int maxMessages)
    {
        var systemMessages = messages.Where(m => m.Role == "system").ToList();
        var chatMessages = messages.Where(m => m.Role != "system").ToList();

        // Trim chat messages by count first (keep most recent).
        if (chatMessages.Count > maxMessages)
        {
            chatMessages = chatMessages.Skip(chatMessages.Count - maxMessages).ToList();
        }

        // Char budget left over after system messages.
        var systemChars = systemMessages.Sum(m => m.Content?.Length ?? 0);
        var chatBudget = Math.Max(0, maxContextSize - systemChars);

        // Walk backwards (newest first) and advance the start index as long as messages fit the budget.
        // Always keep at least the newest message (safety floor for an otherwise empty conversation).
        var startIdx = chatMessages.Count;
        var usedChars = 0;
        for (var i = chatMessages.Count - 1; i >= 0; i--)
        {
            var msgSize = chatMessages[i].Content?.Length ?? 0;
            if (usedChars + msgSize > chatBudget && startIdx < chatMessages.Count) break;
            startIdx = i;
            usedChars += msgSize;
        }

        // Tool messages are valid only when their preceding assistant_tool_call is present.
        // If our slice starts at a "tool" message, drop leading orphans so we don't send
        // an invalid sequence to OpenAI.
        while (startIdx < chatMessages.Count && chatMessages[startIdx].Role == "tool")
        {
            startIdx++;
        }

        var keptCount = chatMessages.Count - startIdx;
        var result = new List<AppChatMessage>(systemMessages.Count + keptCount);
        result.AddRange(systemMessages);
        for (var i = startIdx; i < chatMessages.Count; i++) result.Add(chatMessages[i]);

        if (result.Count < messages.Count)
        {
            _logger.LogInformation(
                "Context truncated: {OriginalCount} -> {NewCount} messages (system={SystemCount}, chat={ChatCount}, chars={Chars})",
                messages.Count, result.Count, systemMessages.Count, keptCount, systemChars + usedChars);
        }

        return result;
    }
}

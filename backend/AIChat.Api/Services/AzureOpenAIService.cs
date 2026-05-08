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
using AIChat.Api.Models;
using AppChatMessage = AIChat.Api.Models.ChatMessage;

namespace AIChat.Api.Services;

public class AzureOpenAIService : IAzureOpenAIService
{
    private const string ImageFetchHttpClient = "azure-openai-image-fetch";
    private const int MaxExtractionExistingMemoryChars = 4000;

    private readonly AzureOpenAIClient _client;
    private readonly ConcurrentDictionary<string, ChatClient> _chatClients = new();
    private readonly Lazy<EmbeddingClient?> _embeddingClient;
    private readonly Lazy<ImageClient?> _imageClient;
    private readonly AzureOpenAISettings _settings;
    private readonly IImageStorageService _imageStorage;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AzureOpenAIService> _logger;

    public AzureOpenAIService(
        IConfiguration configuration,
        IImageStorageService imageStorage,
        IHttpClientFactory httpClientFactory,
        ILogger<AzureOpenAIService> logger)
    {
        _logger = logger;
        _imageStorage = imageStorage;
        _httpClientFactory = httpClientFactory;
        _settings = configuration.GetSection("AzureOpenAI").Get<AzureOpenAISettings>()
            ?? throw new InvalidOperationException("AzureOpenAI settings are not configured");

        // Override with environment variables if set
        var envEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
        var envApiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");

        if (!string.IsNullOrEmpty(envEndpoint)) _settings.Endpoint = envEndpoint;
        if (!string.IsNullOrEmpty(envApiKey)) _settings.ApiKey = envApiKey;

        if (string.IsNullOrEmpty(_settings.Endpoint) || string.IsNullOrEmpty(_settings.ApiKey))
        {
            throw new InvalidOperationException("AzureOpenAI:Endpoint and ApiKey must be configured (via appsettings.json or environment variables)");
        }

        _client = new AzureOpenAIClient(new Uri(_settings.Endpoint), new ApiKeyCredential(_settings.ApiKey));

        _embeddingClient = new Lazy<EmbeddingClient?>(() =>
            string.IsNullOrWhiteSpace(_settings.EmbeddingDeploymentName)
                ? null
                : _client.GetEmbeddingClient(_settings.EmbeddingDeploymentName));

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
    ///    follow-up turns as it produced originally.
    ///  - Plain text messages: straightforward 1:1.
    /// </summary>
    private async Task<List<OpenAI.Chat.ChatMessage>> TranslateAsync(
        List<AppChatMessage> messages, string userId, CancellationToken ct)
    {
        var result = new List<OpenAI.Chat.ChatMessage>(messages.Count);

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
                                toolResult = BuildImageOkResultJson(atts[i]);
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
            if (att.Type != "image") continue;
            var filename = ExtractFilename(att.Url);
            if (filename is null) continue;

            var read = await _imageStorage.TryReadAsync(userId, filename, ct);
            if (read is null)
            {
                _logger.LogWarning("Vision input attachment not found on disk: {Filename}", filename);
                continue;
            }

            parts.Add(ChatMessageContentPart.CreateImagePart(
                BinaryData.FromBytes(read.Value.Bytes),
                read.Value.MimeType));
        }

        // If we somehow ended up with no content parts (e.g. all attachments missing),
        // fall back to text alone so the request doesn't fail.
        if (parts.Count == 0) parts.Add(ChatMessageContentPart.CreateTextPart(m.Content ?? ""));

        return new UserChatMessage(parts);
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
You analyze chat transcripts and extract durable user information worth remembering across future conversations.

Output a JSON object with this exact shape:
{
  "memories": [
    { "type": "fact" | "preference" | "summary", "content": "<= 300 chars", "existingMemoryId": "<id to update, or null for new memory>" }
  ]
}

Extract ONLY:
- facts: user's name, role, situation, skills, enduring context
- preferences: coding style, response style, tools they favor
- summaries: takeaways from a long conversation worth keeping (rare)

Do NOT extract:
- Transient state (the current question, what they are doing right now)
- PII the user did not explicitly share (emails/phones/addresses)
- Passwords, API keys, tokens, sensitive credentials
- Temporary context (weather, file paths, session-specific details)

Use existing memories only to prevent duplicates and identify real updates:
- Do not output a memory if it already exists with the same meaning.
- If the transcript corrects or materially refines an existing memory, return the updated content and set existingMemoryId to that memory's id.
- For brand-new memories, set existingMemoryId to null or omit it.

If nothing is worth remembering, return {"memories": []}. Empty is a valid, correct answer.
Return ONLY the JSON object, no commentary or code fences.
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

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
    private const string ImageEditApiVersion = "2025-04-01-preview";
    private const int MaxExtractionExistingMemoryChars = 4000;
    private const string GenerateImageToolName = "generate_image";
    private const string EditImageToolName = "edit_image";
    private const string GenerateVideoToolName = "generate_video";

    private readonly AzureOpenAIClient _client;
    private readonly ConcurrentDictionary<string, ChatClient> _chatClients = new();
    private readonly Lazy<EmbeddingClient?> _embeddingClient;
    private readonly Lazy<ImageClient?> _imageClient;
    private readonly Lazy<ModelInfo?> _videoGenerationModel;
    private readonly AzureOpenAISettings _settings;
    private readonly MemorySettings _memorySettings;
    private readonly IMediaStorageService _mediaStorage;
    private readonly IVideoGenerationService _videoGeneration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AzureOpenAIService> _logger;

    public AzureOpenAIService(
        IOptions<AzureOpenAISettings> settings,
        IOptions<MemorySettings> memorySettings,
        IMediaStorageService mediaStorage,
        IVideoGenerationService videoGeneration,
        IHttpClientFactory httpClientFactory,
        ILogger<AzureOpenAIService> logger)
    {
        _logger = logger;
        _mediaStorage = mediaStorage;
        _videoGeneration = videoGeneration;
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

        _videoGenerationModel = new Lazy<ModelInfo?>(() =>
        {
            if (string.IsNullOrWhiteSpace(_settings.VideoGenerationModelId)) return null;
            var model = _settings.Models.FirstOrDefault(m => m.Id == _settings.VideoGenerationModelId);
            if (model is null)
            {
                _logger.LogWarning("VideoGenerationModelId={Id} not found in Models[]", _settings.VideoGenerationModelId);
                return null;
            }
            return model;
        });
    }

    public bool IsImageGenerationAvailable =>
        _settings.EnableImageGeneration && _imageClient.Value is not null;

    public bool IsVideoGenerationAvailable =>
        _settings.EnableVideoGeneration && _videoGenerationModel.Value is not null;

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
        bool allowVideoGeneration,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var truncated = TruncateContext(messages, maxContextSize, maxMessages);
        _logger.LogInformation("Starting stream for model {ModelId}: {Truncated}/{Original} messages, allowImageGen={AllowImg}, allowVideoGen={AllowVideo}",
            modelId, truncated.Count, messages.Count, allowImageGeneration, allowVideoGeneration);

        var openAIMessages = await TranslateAsync(truncated, userId, cancellationToken);
        if (!openAIMessages.Any(m => m is SystemChatMessage))
        {
            openAIMessages.Insert(0, new SystemChatMessage(
                "You are a helpful AI assistant. Be concise and helpful in your responses."));
        }

        var chatClient = GetChatClient(modelId);
        var imageToolEnabled = allowImageGeneration && IsImageGenerationAvailable;
        var videoToolEnabled = allowVideoGeneration && IsVideoGenerationAvailable;

        var pass1Options = new ChatCompletionOptions();
        if (imageToolEnabled)
        {
            pass1Options.Tools.Add(BuildImageTool());
            pass1Options.Tools.Add(BuildEditImageTool());
        }
        if (videoToolEnabled) pass1Options.Tools.Add(BuildVideoTool());

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
            yield return new ToolCallStart(tc.FunctionName, tc.Id, ToolCallArgumentsToJson(tc.FunctionArguments));

            var (attachment, errorJson) = tc.FunctionName switch
            {
                GenerateImageToolName => await TryExecuteImageToolAsync(userId, tc, cancellationToken),
                EditImageToolName => await TryExecuteImageEditToolAsync(userId, truncated, tc, cancellationToken),
                GenerateVideoToolName => await TryExecuteVideoToolAsync(userId, tc, cancellationToken),
                _ => (null, """{"status":"error","message":"unknown tool"}"""),
            };

            if (attachment is not null)
            {
                attachments.Add(attachment);
                yield return new AttachmentReady(attachment, tc.Id);
                openAIMessages.Add(new ToolChatMessage(tc.Id, BuildMediaOkResultJson(attachment)));
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

        // Save the wrap-up next to each generated media item as a sidecar; useful for
        // reconstruction, search, or re-prompting. Best-effort — never fails the stream.
        if (pass2Text.Length > 0 && attachments.Count > 0)
        {
            var description = pass2Text.ToString();
            foreach (var att in attachments)
            {
                try { await _mediaStorage.SaveDescriptionAsync(userId, att.Id, description, cancellationToken); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to save description for media {MediaId}", att.Id);
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
            var args = ParseToolCallArgs<ImageToolCallArgs>(tc.FunctionArguments);
            if (string.IsNullOrWhiteSpace(args.Prompt))
            {
                return (null, """{"status":"error","message":"prompt is required"}""");
            }
            var attachment = await GenerateImageAsync(userId, args.Prompt, args.Size, ct);
            return (attachment, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "generate_image tool execution failed");
            var safeMsg = JsonEncodedText.Encode(ex.Message).ToString();
            return (null, $$"""{"status":"error","message":"{{safeMsg}}"}""");
        }
    }

    private async Task<(MessageAttachment? Attachment, string? ErrorJson)> TryExecuteImageEditToolAsync(
        string userId, List<AppChatMessage> messages, ChatToolCall tc, CancellationToken ct)
    {
        try
        {
            var args = ParseToolCallArgs<ImageEditToolCallArgs>(tc.FunctionArguments);
            if (string.IsNullOrWhiteSpace(args.Prompt))
            {
                return (null, """{"status":"error","message":"prompt is required"}""");
            }

            var source = FindGeneratedImageAttachment(messages, args.SourceImageId);
            if (source is null)
            {
                return (null, """{"status":"error","message":"no generated image is available to edit"}""");
            }

            var attachment = await EditImageAsync(userId, args.Prompt, source, args.Size, ct);
            return (attachment, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "edit_image tool execution failed");
            var safeMsg = JsonEncodedText.Encode(ex.Message).ToString();
            return (null, $$"""{"status":"error","message":"{{safeMsg}}"}""");
        }
    }

    private async Task<(MessageAttachment? Attachment, string? ErrorJson)> TryExecuteVideoToolAsync(
        string userId, ChatToolCall tc, CancellationToken ct)
    {
        try
        {
            var args = ParseToolCallArgs<VideoToolCallArgs>(tc.FunctionArguments);
            if (string.IsNullOrWhiteSpace(args.Prompt))
            {
                return (null, """{"status":"error","message":"prompt is required"}""");
            }
            var attachment = await GenerateVideoAsync(userId, args.Prompt, args.Size, args.DurationSeconds, args.RemixVideoId, ct);
            return (attachment, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "generate_video tool execution failed");
            var safeMsg = JsonEncodedText.Encode(ex.Message).ToString();
            return (null, $$"""{"status":"error","message":"{{safeMsg}}"}""");
        }
    }

    private static T ParseToolCallArgs<T>(BinaryData argumentsJson) where T : struct
    {
        if (argumentsJson is null) return default;
        try
        {
            return JsonSerializer.Deserialize<T>(argumentsJson.ToMemory().Span, ToolCallJsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string ToolCallArgumentsToJson(BinaryData argumentsJson)
    {
        if (argumentsJson is null) return "{}";
        var json = Encoding.UTF8.GetString(argumentsJson.ToMemory().Span);
        return string.IsNullOrWhiteSpace(json) ? "{}" : json;
    }

    private static string BuildMediaOkResultJson(MessageAttachment a) =>
        JsonSerializer.Serialize(new
        {
            status = "ok",
            mediaId = a.Id,
            type = a.Type,
            prompt = a.Prompt,
            revisedPrompt = a.RevisedPrompt,
            width = a.Width,
            height = a.Height,
            durationSeconds = a.DurationSeconds,
            providerMediaId = a.ProviderMediaId,
            note = "Media saved and shown to the user. Briefly describe what you generated; do not include the URL.",
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
            functionName: GenerateImageToolName,
            functionDescription: "Generates an image from a text prompt and shows it to the user inline. Use this whenever the user asks for a picture, drawing, illustration, design visualization, photo, etc.",
            functionParameters: BinaryData.FromString(schema));
    }

        private static ChatTool BuildEditImageTool()
        {
                const string schema = """
                {
                    "type": "object",
                    "properties": {
                        "prompt": {
                            "type": "string",
                            "description": "Detailed English edit instruction. Preserve all unchanged parts of the source image as much as possible."
                        },
                        "source_image_id": {
                            "type": "string",
                            "description": "Attachment id of the generated image to edit. Omit this to edit the most recent generated image."
                        },
                        "size": {
                            "type": "string",
                            "enum": ["1024x1024", "1792x1024", "1024x1792"],
                            "description": "Output image dimensions. Use the source image dimensions unless the user asks to resize."
                        }
                    },
                    "required": ["prompt"]
                }
                """;

                return ChatTool.CreateFunctionTool(
                        functionName: EditImageToolName,
                        functionDescription: "Edits an existing generated image and returns a new image. Use this instead of generate_image when the user asks to modify, recolor, remove, add, or otherwise change the previous image while preserving the rest.",
                        functionParameters: BinaryData.FromString(schema));
        }

        private static ChatTool BuildVideoTool()
        {
                const string schema = """
                {
                    "type": "object",
                    "properties": {
                        "prompt": {
                            "type": "string",
                            "description": "Detailed English description of the video to generate. Refine vague user prompts; include subject motion, camera movement, setting, style, lighting, and mood when relevant."
                        },
                        "size": {
                            "type": "string",
                            "enum": ["720x1280", "1280x720"],
                            "description": "Video dimensions. 1280x720 for landscape/wide scenes, 720x1280 (default) for portrait/tall scenes."
                        },
                        "duration_seconds": {
                            "type": "integer",
                            "enum": [4, 8, 12],
                            "description": "Video duration in seconds. Use 4 unless the user asks for a longer clip."
                        },
                        "remix_video_id": {
                            "type": "string",
                            "description": "Azure Sora video id (for example video_...) of a previous generated video to remix. Use this for follow-up edits to an earlier video when available."
                        }
                    },
                    "required": ["prompt"]
                }
                """;

                return ChatTool.CreateFunctionTool(
                        functionName: GenerateVideoToolName,
                        functionDescription: "Generates a short video from a text prompt and shows it to the user inline. Use this whenever the user asks for a video, clip, animation, moving scene, cinematic shot, etc.",
                        functionParameters: BinaryData.FromString(schema));
        }

    /// <summary>
    /// Translates our persisted message schema into OpenAI's typed chat messages.
    /// Handles three non-trivial cases:
    ///  - User messages with image attachments: read bytes from storage and inline
    ///    them as ImageParts (Azure OpenAI cannot fetch our internal URLs).
    ///  - Assistant messages with persisted ToolCalls: expand into the canonical
    ///    asst_tool_call → tool_result triple so the model sees the same shape on
    ///    follow-up turns as it produced originally. The tool_result text carries the
    ///    generation prompt plus the saved caption. The most recent generated images are
    ///    additionally inlined as a follow-up user image message; videos are added as
    ///    text references because chat completions do not accept video content parts.
    ///  - Plain text messages: straightforward 1:1.
    /// </summary>
    private async Task<List<OpenAI.Chat.ChatMessage>> TranslateAsync(
        List<AppChatMessage> messages, string userId, CancellationToken ct)
    {
        var result = new List<OpenAI.Chat.ChatMessage>(messages.Count);
        var mediaReferenceId = SelectMostRecentGeneratedMediaId(messages);

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
                            if (IsMediaToolCall(tc)
                                && m.Attachments is { Count: > 0 } atts
                                && i < atts.Count)
                            {
                                toolResult = await BuildMediaReplayResultJsonAsync(userId, atts[i], ct);
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

                        await AppendGeneratedMediaReferenceAsync(result, m, userId, mediaReferenceId, ct);
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

        var read = await _mediaStorage.TryReadAsync(userId, filename, ct);
        if (read is null)
        {
            _logger.LogWarning("Image attachment not found on disk: {Filename}", filename);
            return null;
        }

        return ChatMessageContentPart.CreateImagePart(
            BinaryData.FromBytes(read.Value.Bytes), read.Value.MimeType);
    }

    /// <summary>
    /// Returns the most recent generated media id. Images can be byte-inlined on replay;
    /// videos are represented by text because chat content parts do not support video.
    /// </summary>
    private static string? SelectMostRecentGeneratedMediaId(List<AppChatMessage> messages)
    {
        string? mediaId = null;
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
                if (IsGeneratedMedia(tcs[i], atts[i]))
                {
                    mediaId = atts[i].Id;
                }
            }
        }

        return mediaId;
    }

    /// <summary>
    /// Appends a synthetic user message that gives the model follow-up context for recent
    /// generated media. Images can be inlined as bytes; videos are represented by text.
    /// </summary>
    private async Task AppendGeneratedMediaReferenceAsync(
        List<OpenAI.Chat.ChatMessage> result,
        AppChatMessage m,
        string userId,
        string? referenceId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(referenceId) || m.Attachments is not { Count: > 0 } atts) return;

        var att = atts.FirstOrDefault(a => string.Equals(a.Id, referenceId, StringComparison.Ordinal));
        if (att is null) return;

        if (string.Equals(att.Type, "image", StringComparison.Ordinal))
        {
            var imagePart = await TryBuildImagePartAsync(userId, att, ct);
            if (imagePart is null) return;

            result.Add(new UserChatMessage(new[]
            {
                ChatMessageContentPart.CreateTextPart(
                    "(Reference: image you generated earlier in this conversation, shown so you can see and edit it.)"),
                imagePart,
            }));
            return;
        }

        if (string.Equals(att.Type, "video", StringComparison.Ordinal))
        {
            var caption = await _mediaStorage.TryReadDescriptionAsync(userId, att.Id, ct);
            result.Add(new UserChatMessage(
                "(Reference: video(s) you generated earlier in this conversation. You cannot see the video bytes here, but this text preserves the prompt, dimensions, duration, and caption for follow-up requests.)\n\n" +
                BuildVideoReferenceText(att, caption)));
        }
    }

    private static string BuildVideoReferenceText(MessageAttachment att, string? caption)
    {
        var details = new List<string> { $"videoId={att.Id}" };
        if (!string.IsNullOrWhiteSpace(att.ProviderMediaId)) details.Add($"remix_video_id={att.ProviderMediaId}");
        if (!string.IsNullOrWhiteSpace(att.Prompt)) details.Add($"prompt={att.Prompt}");
        if (att.Width is not null && att.Height is not null) details.Add($"size={att.Width}x{att.Height}");
        if (att.DurationSeconds is not null) details.Add($"durationSeconds={att.DurationSeconds}");
        if (!string.IsNullOrWhiteSpace(caption)) details.Add($"caption={caption}");
        return string.Join("; ", details);
    }

    private static bool IsMediaToolCall(MessageToolCall toolCall) =>
        IsMediaToolName(toolCall.Name);

    private static bool IsMediaToolName(string? toolName) =>
        string.Equals(toolName, GenerateImageToolName, StringComparison.Ordinal)
        || string.Equals(toolName, EditImageToolName, StringComparison.Ordinal)
        || string.Equals(toolName, GenerateVideoToolName, StringComparison.Ordinal);

    private static bool IsGeneratedImage(MessageToolCall toolCall, MessageAttachment attachment) =>
        (string.Equals(toolCall.Name, GenerateImageToolName, StringComparison.Ordinal)
            || string.Equals(toolCall.Name, EditImageToolName, StringComparison.Ordinal))
        && string.Equals(attachment.Type, "image", StringComparison.Ordinal);

    private static bool IsGeneratedVideo(MessageToolCall toolCall, MessageAttachment attachment) =>
        string.Equals(toolCall.Name, GenerateVideoToolName, StringComparison.Ordinal)
        && string.Equals(attachment.Type, "video", StringComparison.Ordinal);

    private static bool IsGeneratedMedia(MessageToolCall toolCall, MessageAttachment attachment) =>
        IsGeneratedImage(toolCall, attachment) || IsGeneratedVideo(toolCall, attachment);

    private static MessageAttachment? FindGeneratedImageAttachment(List<AppChatMessage> messages, string? sourceImageId)
    {
        MessageAttachment? latest = null;
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
                if (!IsGeneratedImage(tcs[i], atts[i])) continue;
                if (!string.IsNullOrWhiteSpace(sourceImageId)
                    && string.Equals(atts[i].Id, sourceImageId, StringComparison.Ordinal))
                {
                    return atts[i];
                }
                latest = atts[i];
            }
        }

        return string.IsNullOrWhiteSpace(sourceImageId) ? latest : null;
    }

    /// <summary>
    /// Tool-result JSON for replayed generated media: the original prompt plus the saved
    /// caption (the model's own description), so the model retains a faithful text
    /// representation even when the bytes aren't inlined.
    /// </summary>
    private async Task<string> BuildMediaReplayResultJsonAsync(
        string userId, MessageAttachment a, CancellationToken ct)
    {
        var caption = await _mediaStorage.TryReadDescriptionAsync(userId, a.Id, ct);
        return JsonSerializer.Serialize(new
        {
            status = "ok",
            mediaId = a.Id,
            type = a.Type,
            prompt = a.Prompt,
            revisedPrompt = a.RevisedPrompt,
            width = a.Width,
            height = a.Height,
            durationSeconds = a.DurationSeconds,
            providerMediaId = a.ProviderMediaId,
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

        var (parsedSize, w, h) = ParseImageSize(size);

        // ResponseFormat is intentionally NOT set: gpt-image-1/2 deployments reject it
        // ("Unknown parameter: 'response_format'"), and they return base64 bytes by default.
        // DALL-E 3 also returns b64_json by default for the SDK, so this works for both.
        // The URL fallback below catches the rare case where bytes are absent.
        var options = new ImageGenerationOptions
        {
            Size = parsedSize,
            Quality = GetImageQuality(prompt),
        };

        _logger.LogInformation("Generating image for user {UserId}, prompt length={Len}, size={Size}, quality={Quality}",
            userId, prompt.Length, $"{w}x{h}", GetImageQualityValue(prompt));

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

        return await _mediaStorage.SaveAsync(
            userId,
            bytes.ToMemory(),
            mimeType: "image/png",
            prompt: prompt,
            revisedPrompt: img.RevisedPrompt,
            width: w,
            height: h,
            cancellationToken: cancellationToken);
    }

    public async Task<MessageAttachment> EditImageAsync(
        string userId,
        string prompt,
        MessageAttachment sourceImage,
        string? size = null,
        CancellationToken cancellationToken = default)
    {
        var model = GetImageGenerationModel()
            ?? throw new InvalidOperationException("Image editing is not configured");
        if (!_settings.EnableImageGeneration)
        {
            throw new InvalidOperationException("Image generation is disabled");
        }

        var filename = ExtractFilename(sourceImage.Url)
            ?? throw new InvalidOperationException("Source image URL is invalid");
        var read = await _mediaStorage.TryReadAsync(userId, filename, cancellationToken)
            ?? throw new InvalidOperationException("Source image is not available");
        if (!IsSupportedImageEditMimeType(read.MimeType))
        {
            throw new InvalidOperationException($"Image edit source must be PNG or JPEG, got {read.MimeType}");
        }

        var editSizeText = size ?? (sourceImage.Width is not null && sourceImage.Height is not null
            ? $"{sourceImage.Width}x{sourceImage.Height}"
            : null);
        var (_, w, h) = ParseImageSize(editSizeText);

        _logger.LogInformation("Editing image for user {UserId}, source={SourceImageId}, prompt length={Len}, size={Size}",
            userId, sourceImage.Id, prompt.Length, $"{w}x{h}");

        using var imgCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        imgCts.CancelAfter(TimeSpan.FromMinutes(10));

        var (bytes, revisedPrompt) = await EditImageViaRestAsync(
            model.DeploymentName,
            prompt,
            filename,
            read.Bytes,
            read.MimeType,
            $"{w}x{h}",
            imgCts.Token);

        return await _mediaStorage.SaveAsync(
            userId,
            bytes,
            mimeType: "image/png",
            prompt: prompt,
            revisedPrompt: revisedPrompt,
            width: w,
            height: h,
            cancellationToken: cancellationToken);
    }

    private async Task<(ReadOnlyMemory<byte> Bytes, string? RevisedPrompt)> EditImageViaRestAsync(
        string deploymentName,
        string prompt,
        string filename,
        ReadOnlyMemory<byte> imageBytes,
        string mimeType,
        string size,
        CancellationToken cancellationToken)
    {
        var http = _httpClientFactory.CreateClient(ImageFetchHttpClient);
        var url = $"{_settings.Endpoint.TrimEnd('/')}/openai/deployments/{Uri.EscapeDataString(deploymentName)}/images/edits?api-version={ImageEditApiVersion}";
        var startedAt = DateTime.UtcNow;
        try
        {
            using var request = BuildImageEditRequest(url, prompt, filename, imageBytes, mimeType, size);
            using var response = await http.SendAsync(request, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return ParseImageEditResponse(responseText);
            }

            throw new InvalidOperationException($"Image edit failed after {(DateTime.UtcNow - startedAt).TotalMilliseconds}ms: {(int)response.StatusCode} {responseText}");
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Image edit failed before response after {(DateTime.UtcNow - startedAt).TotalMilliseconds}ms", ex);
        }
    }

    private HttpRequestMessage BuildImageEditRequest(
        string url,
        string prompt,
        string filename,
        ReadOnlyMemory<byte> imageBytes,
        string mimeType,
        string size)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("api-key", _settings.ApiKey);

        var form = new MultipartFormDataContent();
        form.Add(new StringContent(prompt), "prompt");
        form.Add(new StringContent(size), "size");
        form.Add(new StringContent(GetImageQualityValue(prompt)), "quality");
        form.Add(new StringContent("png"), "output_format");
        form.Add(new StringContent("1"), "n");

        var imageContent = new ByteArrayContent(imageBytes.ToArray());
        imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);
        form.Add(imageContent, "image", filename);
        request.Content = form;
        return request;
    }

    private static (ReadOnlyMemory<byte> Bytes, string? RevisedPrompt) ParseImageEditResponse(string responseText)
    {
        using var doc = JsonDocument.Parse(responseText);
        var item = doc.RootElement.GetProperty("data")[0];
        var b64 = item.TryGetProperty("b64_json", out var b64Element) ? b64Element.GetString() : null;
        if (string.IsNullOrWhiteSpace(b64))
        {
            throw new InvalidOperationException("Image edit response did not include b64_json");
        }

        var revisedPrompt = item.TryGetProperty("revised_prompt", out var revisedPromptElement)
            ? revisedPromptElement.GetString()
            : null;
        return (Convert.FromBase64String(b64), revisedPrompt);
    }

    private ModelInfo? GetImageGenerationModel() =>
        string.IsNullOrWhiteSpace(_settings.ImageGenerationModelId)
            ? null
            : _settings.Models.FirstOrDefault(m => m.Id == _settings.ImageGenerationModelId);

    private static bool IsSupportedImageEditMimeType(string mimeType) =>
        string.Equals(mimeType, "image/png", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mimeType, "image/jpeg", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mimeType, "image/jpg", StringComparison.OrdinalIgnoreCase);

    private static GeneratedImageQuality GetImageQuality(string prompt) =>
        new(GetImageQualityValue(prompt));

    private static string GetImageQualityValue(string prompt) =>
        LooksLikePersonImage(prompt) ? "high" : "medium";

    private static bool LooksLikePersonImage(string prompt)
    {
        var text = prompt.ToLowerInvariant();
        return text.Contains("person")
            || text.Contains("people")
            || text.Contains("portrait")
            || text.Contains("face")
            || text.Contains("human")
            || text.Contains("man")
            || text.Contains("woman")
            || text.Contains("boy")
            || text.Contains("girl")
            || text.Contains("人物")
            || text.Contains("人像")
            || text.Contains("肖像")
            || text.Contains("脸")
            || text.Contains("男人")
            || text.Contains("女人")
            || text.Contains("男孩")
            || text.Contains("女孩");
    }

    public async Task<MessageAttachment> GenerateVideoAsync(
        string userId,
        string prompt,
        string? size = null,
        int? durationSeconds = null,
        string? remixVideoId = null,
        CancellationToken cancellationToken = default)
    {
        var model = _videoGenerationModel.Value
            ?? throw new InvalidOperationException("Video generation is not configured");
        if (!_settings.EnableVideoGeneration)
        {
            throw new InvalidOperationException("Video generation is disabled");
        }

        var (width, height) = ParseSize(size, defaultWidth: 720, defaultHeight: 1280);
        var duration = ParseVideoDuration(durationSeconds);

        _logger.LogInformation("Generating video for user {UserId}, prompt length={Len}, size={Size}, duration={Duration}s",
            userId, prompt.Length, $"{width}x{height}", duration);

        var video = await _videoGeneration.GenerateAsync(
            model.DeploymentName,
            prompt,
            width,
            height,
            duration,
            remixVideoId,
            cancellationToken);

        return await _mediaStorage.SaveAsync(
            userId,
            video.Bytes,
            mimeType: video.MimeType,
            prompt: prompt,
            width: width,
            height: height,
            durationSeconds: duration,
            providerMediaId: video.ProviderVideoId,
            attachmentType: "video",
            cancellationToken: cancellationToken);
    }

    private static int ParseVideoDuration(int? durationSeconds) =>
        durationSeconds is 8 or 12 ? durationSeconds.Value : 4;

    private static readonly JsonSerializerOptions ToolCallJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly record struct ImageToolCallArgs
    {
        [JsonPropertyName("prompt")]
        public string Prompt { get; init; }

        [JsonPropertyName("size")]
        public string? Size { get; init; }
    }

    private readonly record struct ImageEditToolCallArgs
    {
        [JsonPropertyName("prompt")]
        public string Prompt { get; init; }

        [JsonPropertyName("source_image_id")]
        public string? SourceImageId { get; init; }

        [JsonPropertyName("size")]
        public string? Size { get; init; }
    }

    private readonly record struct VideoToolCallArgs
    {
        [JsonPropertyName("prompt")]
        public string Prompt { get; init; }

        [JsonPropertyName("size")]
        public string? Size { get; init; }

        [JsonPropertyName("duration_seconds")]
        public int? DurationSeconds { get; init; }

        [JsonPropertyName("remix_video_id")]
        public string? RemixVideoId { get; init; }
    }

    private static (GeneratedImageSize Size, int Width, int Height) ParseImageSize(string? size)
    {
        var (w, h) = ParseSize(size, defaultWidth: 1024, defaultHeight: 1024);

        // Prefer the predefined values where they line up exactly (allows the SDK
        // to send DALL-E-style enum strings); otherwise fall through to a custom size.
        if (w == 1024 && h == 1024) return (GeneratedImageSize.W1024xH1024, 1024, 1024);
        if (w == 1792 && h == 1024) return (GeneratedImageSize.W1792xH1024, 1792, 1024);
        if (w == 1024 && h == 1792) return (GeneratedImageSize.W1024xH1792, 1024, 1792);
        return (new GeneratedImageSize(w, h), w, h);
    }

    private static (int Width, int Height) ParseSize(string? size, int defaultWidth, int defaultHeight)
    {
        if (string.IsNullOrWhiteSpace(size)) return (defaultWidth, defaultHeight);
        var parts = size.Split('x', 2);
        if (parts.Length == 2
            && int.TryParse(parts[0], out var width)
            && int.TryParse(parts[1], out var height)
            && width > 0
            && height > 0)
        {
            return (width, height);
        }

        return (defaultWidth, defaultHeight);
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

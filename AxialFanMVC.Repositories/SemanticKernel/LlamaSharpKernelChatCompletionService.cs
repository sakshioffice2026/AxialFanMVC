using AxialFanMVC.Repositories.Inteface;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text.Json;

namespace AxialFanMVC.Repositories.SemanticKernel
{
    public class LlamaSharpKernelChatCompletionService : IChatCompletionService
    {
        public const string MaxTokensKey = "max_tokens";

        private const int DefaultMaxTokens = 300;
        private const int MinMaxTokens = 16;
        private const int MaxMaxTokens = 1024;

        private readonly ILlamaSharpChatService _chatService;

        public IReadOnlyDictionary<string, object?> Attributes { get; } = new Dictionary<string, object?>();

        public LlamaSharpKernelChatCompletionService(ILlamaSharpChatService chatService)
        {
            _chatService = chatService;
        }

        public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default)
        {
            var systemPrompt = string.Join("\n",
                chatHistory.Where(m => m.Role == AuthorRole.System).Select(m => m.Content));

            var userMessage = chatHistory.LastOrDefault(m => m.Role == AuthorRole.User)?.Content ?? string.Empty;

            var maxTokens = ResolveMaxTokens(executionSettings);

            var reply = await _chatService.ChatAsync(systemPrompt, userMessage, maxTokens);

            return new List<ChatMessageContent>
            {
                new ChatMessageContent(AuthorRole.Assistant, reply)
            };
        }

        public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var results = await GetChatMessageContentsAsync(chatHistory, executionSettings, kernel, cancellationToken);
            foreach (var result in results)
            {
                yield return new StreamingChatMessageContent(result.Role, result.Content);
            }
        }

        private static int ResolveMaxTokens(PromptExecutionSettings? settings)
        {
            if (settings?.ExtensionData is null
                || !settings.ExtensionData.TryGetValue(MaxTokensKey, out var raw)
                || raw is null)
                return DefaultMaxTokens;

            int value = raw switch
            {
                int i => i,
                long l => (int)l,
                double d => (int)d,
                string s when int.TryParse(s, out var parsed) => parsed,
                JsonElement el when el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n) => n,
                _ => DefaultMaxTokens
            };

            return Math.Clamp(value, MinMaxTokens, MaxMaxTokens);
        }
    }
}
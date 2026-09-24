using AxialFanMVC.Repositories.Inteface;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Services;

namespace AxialFanMVC.Repositories.SemanticKernel
{
    // Adapts the existing thread-safe LlamaSharpChatService (SemaphoreSlim-guarded
    // native llama.cpp context) to Semantic Kernel's IChatCompletionService
    // contract, so the rest of the orchestration layer talks to the model only
    // through the Kernel abstraction rather than to LLamaSharp directly.
    public class LlamaSharpKernelChatCompletionService : IChatCompletionService
    {
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

            var reply = await _chatService.ChatAsync(systemPrompt, userMessage);

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
    }
}
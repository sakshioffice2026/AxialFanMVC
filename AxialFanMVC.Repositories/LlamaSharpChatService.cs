using AxialFanMVC.Repositories.Inteface;
using LLama;
using LLama.Common;

namespace AxialFanMVC.Repositories
{
    public class LlamaSharpChatService : ILlamaSharpChatService
    {
        private readonly ILlamaModelProvider _modelProvider;
        private static readonly SemaphoreSlim _lock = new(1, 1);

        public LlamaSharpChatService(ILlamaModelProvider modelProvider)
        {
            _modelProvider = modelProvider;
        }

        public async Task<string> ChatAsync(string systemPrompt, string userMessage, int maxTokens = 300)
        {
            if (string.IsNullOrWhiteSpace(userMessage))
                return "Please type a question.";

            await _lock.WaitAsync();
            try
            {
                using var context = _modelProvider.ChatModel.CreateContext(_modelProvider.ChatParams);
                var executor = new InteractiveExecutor(context);

                var history = new ChatHistory();
                history.AddMessage(AuthorRole.System, systemPrompt);

                var session = new ChatSession(executor, history);

                var inferenceParams = new InferenceParams
                {
                    MaxTokens = maxTokens,
                    AntiPrompts = new List<string> { "User:", "<|end|>", "<|user|>" }
                };

                var sb = new System.Text.StringBuilder();

                await foreach (var token in session.ChatAsync(
                    new ChatHistory.Message(AuthorRole.User, userMessage),
                    inferenceParams))
                {
                    sb.Append(token);
                }

                var reply = sb.ToString().Trim();

                foreach (var anti in inferenceParams.AntiPrompts)
                    reply = reply.Replace(anti, string.Empty);

                return string.IsNullOrWhiteSpace(reply) ? "No response generated." : reply.Trim();
            }
            finally
            {
                _lock.Release();
            }
        }
    }
}
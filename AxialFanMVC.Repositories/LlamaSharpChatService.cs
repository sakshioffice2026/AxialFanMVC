using LLama;
using LLama.Common;
using LLama.Sampling;
using AxialFanMVC.Repositories.Inteface;
using System.Text;

namespace AxialFanMVC.Repositories
{
    public class LlamaSharpChatService : ILlamaSharpChatService
    {
        private readonly ILlamaModelProvider _modelProvider;

        public LlamaSharpChatService(ILlamaModelProvider modelProvider)
        {
            _modelProvider = modelProvider;
        }

        public async Task<string> CompleteAsync(string systemPrompt, string userMessage, int maxTokens = 300)
        {
            await _modelProvider.ChatLock.WaitAsync();
            try
            {
                using var context = _modelProvider.CreateChatContext();
                var executor = new InteractiveExecutor(context);

                var prompt = $"<|system|>\n{systemPrompt}\n<|user|>\n{userMessage}\n<|assistant|>\n";

                var inferenceParams = new InferenceParams
                {
                    MaxTokens = maxTokens,
                    AntiPrompts = new List<string> { "<|user|>", "<|system|>" },
                    SamplingPipeline = new DefaultSamplingPipeline
                    {
                        Temperature = 0.4f
                    }
                };

                var sb = new StringBuilder();
                await foreach (var token in executor.InferAsync(prompt, inferenceParams))
                {
                    sb.Append(token);
                }

                return sb.ToString().Trim();
            }
            finally
            {
                _modelProvider.ChatLock.Release();
            }
        }
    }
}
using AxialFanMVC.Repositories.Inteface;
using AxialFanMVC.Repositories.SemanticKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AxialFanMVC.Repositories
{
    // Builds a Kernel per use, wired to the in-process LLamaSharp chat
    // completion adapter. No external connectors (OpenAI/Anthropic) and no
    // local daemon endpoints (Ollama) are registered — inference happens
    // entirely in-process via LLamaSharp.
    public class KernelFactory : IKernelFactory
    {
        private readonly ILlamaSharpChatService _chatService;

        public KernelFactory(ILlamaSharpChatService chatService)
        {
            _chatService = chatService;
        }

        public Kernel CreateKernel()
        {
            var builder = Kernel.CreateBuilder();

            builder.Services.AddSingleton<IChatCompletionService>(
                new LlamaSharpKernelChatCompletionService(_chatService));

            return builder.Build();
        }
    }
}
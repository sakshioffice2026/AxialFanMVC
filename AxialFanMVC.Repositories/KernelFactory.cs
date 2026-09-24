using AxialFanMVC.Repositories.Inteface;
using AxialFanMVC.Repositories.Plugins;
using AxialFanMVC.Repositories.SemanticKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AxialFanMVC.Repositories
{
    public class KernelFactory : IKernelFactory
    {
        private readonly ILlamaSharpChatService _chatService;
        private readonly IAppDataQueryService _appDataQueryService;

        public KernelFactory(
            ILlamaSharpChatService chatService,
            IAppDataQueryService appDataQueryService)
        {
            _chatService = chatService;
            _appDataQueryService = appDataQueryService;
        }

        public Kernel CreateKernel()
        {
            var builder = Kernel.CreateBuilder();

            builder.Services.AddSingleton<IChatCompletionService>(
                new LlamaSharpKernelChatCompletionService(_chatService));

            var kernel = builder.Build();

            kernel.Plugins.AddFromObject(
                new AppDataQueryPlugin(_appDataQueryService),
                "AppData");

            return kernel;
        }
    }
}
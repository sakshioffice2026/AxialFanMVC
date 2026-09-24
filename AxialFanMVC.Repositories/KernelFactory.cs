using AxialFanMVC.Repositories.Inteface;
using AxialFanMVC.Repositories.Plugins;
using AxialFanMVC.Repositories.SemanticKernel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AxialFanMVC.Repositories
{
    public class KernelFactory : IKernelFactory
    {
        private readonly ILlamaSharpChatService _chatService;
        private readonly ILlamaSharpEmbeddingService _embeddingService;
        private readonly IAppDataQueryService _appDataQueryService;
        private readonly IConfiguration _configuration;

        public KernelFactory(
            ILlamaSharpChatService chatService,
            ILlamaSharpEmbeddingService embeddingService,
            IAppDataQueryService appDataQueryService,
            IConfiguration configuration)
        {
            _chatService = chatService;
            _embeddingService = embeddingService;
            _appDataQueryService = appDataQueryService;
            _configuration = configuration;
        }

        public Kernel CreateKernel()
        {
            var builder = Kernel.CreateBuilder();

            builder.Services.AddSingleton<IChatCompletionService>(
                new LlamaSharpKernelChatCompletionService(_chatService));

            var dimensions = int.TryParse(_configuration["LlamaModels:EmbeddingDimensions"], out var parsedDimensions)
                ? parsedDimensions
                : 768;

            builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
                new LlamaSharpKernelEmbeddingGenerator(_embeddingService, dimensions));

            var kernel = builder.Build();

            kernel.Plugins.AddFromObject(
                new AppDataQueryPlugin(_appDataQueryService),
                "AppData");

            return kernel;
        }
    }
}
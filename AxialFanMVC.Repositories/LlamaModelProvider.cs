using LLama;
using LLama.Common;
using AxialFanMVC.Repositories.Inteface;
using Microsoft.Extensions.Configuration;

namespace AxialFanMVC.Repositories
{
    public class LlamaModelProvider : ILlamaModelProvider
    {
        private readonly LLamaWeights _chatModel;
        private readonly LLamaWeights _embeddingModel;
        private readonly ModelParams _chatParams;
        private readonly ModelParams _embeddingParams;

        public SemaphoreSlim ChatLock { get; } = new SemaphoreSlim(1, 1);
        public SemaphoreSlim EmbeddingLock { get; } = new SemaphoreSlim(1, 1);

        public LLamaWeights ChatModel => _chatModel;
        public LLamaWeights EmbeddingModel => _embeddingModel;

        public LlamaModelProvider(IConfiguration config)
        {
            var chatModelPath = config["LlamaSharp:ChatModelPath"]
                ?? throw new InvalidOperationException("LlamaSharp:ChatModelPath is not configured.");
            var embeddingModelPath = config["LlamaSharp:EmbeddingModelPath"]
                ?? throw new InvalidOperationException("LlamaSharp:EmbeddingModelPath is not configured.");

            var contextSize = uint.TryParse(config["LlamaSharp:ContextSize"], out var cs) ? cs : 4096u;
            var gpuLayerCount = int.TryParse(config["LlamaSharp:GpuLayerCount"], out var gl) ? gl : 0;

            _chatParams = new ModelParams(chatModelPath)
            {
                ContextSize = contextSize,
                GpuLayerCount = gpuLayerCount
            };

            _embeddingParams = new ModelParams(embeddingModelPath)
            {
                ContextSize = contextSize,
                GpuLayerCount = gpuLayerCount,
                Embeddings = true
            };

            _chatModel = LLamaWeights.LoadFromFile(_chatParams);
            _embeddingModel = LLamaWeights.LoadFromFile(_embeddingParams);
        }

        public LLamaContext CreateChatContext() => _chatModel.CreateContext(_chatParams);

        public LLamaContext CreateEmbeddingContext() => _embeddingModel.CreateContext(_embeddingParams);

        public void Dispose()
        {
            _chatModel.Dispose();
            _embeddingModel.Dispose();
            ChatLock.Dispose();
            EmbeddingLock.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
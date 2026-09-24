using AxialFanMVC.Repositories.Inteface;
using LLama;
using LLama.Common;
using Microsoft.Extensions.Configuration;

namespace AxialFanMVC.Repositories
{
    public class LlamaModelProvider : ILlamaModelProvider
    {
        private bool _disposed;

        public LLamaWeights ChatModel { get; }
        public LLamaWeights EmbeddingModel { get; }
        public ModelParams ChatParams { get; }
        public ModelParams EmbeddingParams { get; }

        public LlamaModelProvider(IConfiguration config)
        {
            var chatPath = config["LlamaModels:ChatModelPath"]
                ?? throw new InvalidOperationException("LlamaModels:ChatModelPath not configured.");

            var embedPath = config["LlamaModels:EmbeddingModelPath"]
                ?? throw new InvalidOperationException("LlamaModels:EmbeddingModelPath not configured.");

            var contextSize = uint.TryParse(config["LlamaModels:ContextSize"], out var cs) ? cs : 4096u;
            var gpuLayers = int.TryParse(config["LlamaModels:GpuLayerCount"], out var gl) ? gl : 0;
            // Handbook chunks can exceed 512 tokens; nomic-embed-text-v1.5
            // was trained with a long context window, so this is safe to
            // raise. Configurable rather than re-hardcoded so it can be
            // tuned per embedding model without a code change.
            var embedContextSize = uint.TryParse(config["LlamaModels:EmbeddingContextSize"], out var ecs) ? ecs : 2048u;

            ChatParams = new ModelParams(chatPath)
            {
                ContextSize = contextSize,
                GpuLayerCount = gpuLayers
            };

            EmbeddingParams = new ModelParams(embedPath)
            {
                ContextSize = embedContextSize,
                GpuLayerCount = gpuLayers,
                Embeddings = true
            };

            ChatModel = LLamaWeights.LoadFromFile(ChatParams);
            EmbeddingModel = LLamaWeights.LoadFromFile(EmbeddingParams);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ChatModel.Dispose();
            EmbeddingModel.Dispose();
        }
    }
}
using AxialFanMVC.Repositories.Inteface;
using Microsoft.Extensions.AI;

namespace AxialFanMVC.Repositories.SemanticKernel
{
    public class LlamaSharpKernelEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        private readonly ILlamaSharpEmbeddingService _embeddingService;
        private readonly EmbeddingGeneratorMetadata _metadata;

        public LlamaSharpKernelEmbeddingGenerator(
            ILlamaSharpEmbeddingService embeddingService,
            int dimensions = 768,
            string modelId = "nomic-embed-text-v1.5")
        {
            _embeddingService = embeddingService;
            _metadata = new EmbeddingGeneratorMetadata(
                providerName: "LLamaSharp",
                providerUri: null,
                defaultModelId: modelId,
                defaultModelDimensions: dimensions);
        }

        public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var inputs = values.ToArray();
            var vectors = await _embeddingService.EmbedBatchAsync(inputs);

            var result = new GeneratedEmbeddings<Embedding<float>>();
            foreach (var vector in vectors)
            {
                cancellationToken.ThrowIfCancellationRequested();
                result.Add(new Embedding<float>(vector));
            }

            return result;
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            if (serviceKey is not null)
                return null;

            if (serviceType == typeof(EmbeddingGeneratorMetadata))
                return _metadata;

            if (serviceType.IsInstanceOfType(this))
                return this;

            return null;
        }

        public void Dispose()
        {
        }
    }
}
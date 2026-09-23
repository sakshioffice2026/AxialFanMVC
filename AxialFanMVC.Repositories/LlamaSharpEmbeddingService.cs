using AxialFanMVC.Repositories.Inteface;
using LLama;

namespace AxialFanMVC.Repositories
{
    public class LlamaSharpEmbeddingService : ILlamaSharpEmbeddingService
    {
        private readonly ILlamaModelProvider _modelProvider;
        private static readonly SemaphoreSlim _lock = new(1, 1);

        public LlamaSharpEmbeddingService(ILlamaModelProvider modelProvider)
        {
            _modelProvider = modelProvider;
        }

        public async Task<float[]> EmbedAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return Array.Empty<float>();

            await _lock.WaitAsync();
            try
            {
                var embedder = new LLamaEmbedder(
                    _modelProvider.EmbeddingModel,
                    _modelProvider.EmbeddingParams);

                var result = await embedder.GetEmbeddings(text);
                return result[0];
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<float[][]> EmbedBatchAsync(string[] texts)
        {
            if (texts == null || texts.Length == 0)
                return Array.Empty<float[]>();

            await _lock.WaitAsync();
            try
            {
                var embedder = new LLamaEmbedder(
                    _modelProvider.EmbeddingModel,
                    _modelProvider.EmbeddingParams);

                var results = new float[texts.Length][];
                for (int i = 0; i < texts.Length; i++)
                {
                    var emb = await embedder.GetEmbeddings(texts[i]);
                    results[i] = emb[0];
                }
                return results;
            }
            finally
            {
                _lock.Release();
            }
        }
    }
}
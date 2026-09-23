using LLama;
using AxialFanMVC.Repositories.Inteface;

namespace AxialFanMVC.Repositories
{
    public class LlamaSharpEmbeddingService : ILlamaSharpEmbeddingService
    {
        private readonly ILlamaModelProvider _modelProvider;

        public LlamaSharpEmbeddingService(ILlamaModelProvider modelProvider)
        {
            _modelProvider = modelProvider;
        }

        public async Task<float[]> GenerateEmbeddingAsync(string text)
        {
            await _modelProvider.EmbeddingLock.WaitAsync();
            try
            {
                using var context = _modelProvider.CreateEmbeddingContext();
                var embedder = new LLamaEmbedder(_modelProvider.EmbeddingModel, context.Params);

                var result = await embedder.GetEmbeddings(text);
                return result[0];
            }
            finally
            {
                _modelProvider.EmbeddingLock.Release();
            }
        }

        public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts)
        {
            await _modelProvider.EmbeddingLock.WaitAsync();
            try
            {
                using var context = _modelProvider.CreateEmbeddingContext();
                var embedder = new LLamaEmbedder(_modelProvider.EmbeddingModel, context.Params);

                var results = new List<float[]>();
                foreach (var text in texts)
                {
                    var embedding = await embedder.GetEmbeddings(text);
                    results.Add(embedding[0]);
                }

                return results;
            }
            finally
            {
                _modelProvider.EmbeddingLock.Release();
            }
        }
    }
}
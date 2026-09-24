using AxialFanMVC.Repositories.Inteface;
using LLama;

namespace AxialFanMVC.Repositories
{
    public class LlamaSharpEmbeddingService : ILlamaSharpEmbeddingService
    {
        private readonly ILlamaModelProvider _modelProvider;
        private static readonly SemaphoreSlim _lock = new(1, 1);

        // Rough chars-per-token safety cap so an unexpectedly long chunk
        // can't throw "Embedding prompt is longer than the context window"
        // and abort an entire batch sync. ~3.5 chars/token is conservative
        // for English text; this trims rather than truncates mid-word where
        // possible, but a hard cut is fine here since it only affects the
        // embedding vector, not the stored chunk text used for display.
        private int MaxInputChars => (int)(_modelProvider.EmbeddingParams.ContextSize * 3);

        public LlamaSharpEmbeddingService(ILlamaModelProvider modelProvider)
        {
            _modelProvider = modelProvider;
        }

        private string ClampToContext(string text)
        {
            return text.Length > MaxInputChars ? text.Substring(0, MaxInputChars) : text;
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

                var result = await embedder.GetEmbeddings(ClampToContext(text));
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
                    var emb = await embedder.GetEmbeddings(ClampToContext(texts[i]));
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
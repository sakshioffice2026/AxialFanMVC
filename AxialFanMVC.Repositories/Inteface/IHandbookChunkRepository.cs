using AxialFanMVC.Database;

namespace AxialFanMVC.Repositories.Inteface
{
    public interface IHandbookChunkRepository
    {
        /// <summary>
        /// Full-text search across handbook chunks, ranked by MySQL relevance score.
        /// Kept as a fallback path for SearchBySimilarityAsync in case the Ollama
        /// embeddings endpoint is briefly unreachable.
        /// </summary>
        Task<List<HandbookChunk>> SearchAsync(string query, int maxResults = 10);

        /// <summary>
        /// Semantic search: embeds the query via Ollama and ranks chunks by cosine
        /// similarity against their stored embedding vectors, rather than keyword
        /// overlap. Falls back to SearchAsync if the embedding call fails or no
        /// chunks have an embedding yet (i.e. before BackfillEmbeddingsAsync has run).
        /// </summary>
        Task<List<HandbookChunk>> SearchBySimilarityAsync(string query, int maxResults = 10);

        /// <summary>
        /// ONE-TIME OPERATION — embeds every chunk that doesn't have an embedding
        /// yet and saves it. Meant to be triggered once via a temporary controller
        /// action (see HandbookController) and not called as part of normal
        /// request handling. Safe to call again later (e.g. after adding new
        /// chunks) since it skips rows that already have an embedding.
        /// </summary>
        Task<int> BackfillEmbeddingsAsync();
    }
}
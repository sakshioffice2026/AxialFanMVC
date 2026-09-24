using AxialFanMVC.Database;

namespace AxialFanMVC.Repositories.Inteface
{
    public interface IHandbookChunkRepository
    {
        /// <summary>
        /// Full-text search across handbook chunks, ranked by MySQL relevance score.
        /// This is the FULLTEXT fallback path used by HandbookController.Index.
        /// </summary>
        Task<List<HandbookChunk>> SearchAsync(string query, int maxResults = 10);

        /// <summary>
        /// Legacy semantic search kept for interface compatibility.
        /// The active semantic path is IRetrievalService → Qdrant.
        /// Falls through to SearchAsync (MySQL FULLTEXT).
        /// </summary>
        Task<List<HandbookChunk>> SearchBySimilarityAsync(string query, int maxResults = 10);

        /// <summary>
        /// No-op — embedding backfill is now handled by
        /// IHandbookVectorSyncService.SyncAllToQdrantAsync.
        /// Kept on the interface so existing callers compile.
        /// </summary>
        Task<int> BackfillEmbeddingsAsync();
    }
}
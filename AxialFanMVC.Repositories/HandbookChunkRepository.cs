using AxialFanMVC.Database;
using AxialFanMVC.Repositories.Inteface;
using Microsoft.EntityFrameworkCore;

namespace AxialFanMVC.Repositories
{
    public class HandbookChunkRepository : IHandbookChunkRepository
    {
        private readonly AxialFanDbContext _db;
        private readonly ILlamaSharpEmbeddingService _embeddingService;

        public HandbookChunkRepository(
            AxialFanDbContext db,
            ILlamaSharpEmbeddingService embeddingService)
        {
            _db = db;
            _embeddingService = embeddingService;
        }

        public async Task<List<HandbookChunk>> SearchAsync(string query, int maxResults = 10)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<HandbookChunk>();

            return await _db.handbook_chunks
                .FromSqlInterpolated($@"
                SELECT *
                FROM handbook_chunks
                WHERE MATCH(text) AGAINST({query} IN NATURAL LANGUAGE MODE)
                ORDER BY MATCH(text) AGAINST({query} IN NATURAL LANGUAGE MODE) DESC
                LIMIT {maxResults}")
                .ToListAsync();
        }

        public async Task<List<HandbookChunk>> SearchBySimilarityAsync(string query, int maxResults = 10)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<HandbookChunk>();

            // SearchBySimilarityAsync is a legacy path kept for FULLTEXT fallback.
            // The active semantic search path is IRetrievalService → Qdrant.
            // This implementation falls straight through to MySQL FULLTEXT.
            return await SearchAsync(query, maxResults);
        }

        public Task<int> BackfillEmbeddingsAsync()
        {
            // Embedding backfill is now handled by IHandbookVectorSyncService.SyncAllToQdrantAsync
            // (which embeds via LLamaSharp and upserts into Qdrant).
            // The MySQL Embedding column is no longer written to.
            // This method is kept on the interface so HandbookController.BackfillEmbeddings
            // compiles; call SyncAllToQdrantAsync via /Handbook/SyncToQdrant instead.
            return Task.FromResult(0);
        }
    }
}
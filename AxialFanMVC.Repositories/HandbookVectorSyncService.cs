using AxialFanMVC.Database;
using AxialFanMVC.Repositories.Inteface;
using AxialFanMVC.Repositories.Models;
using Microsoft.EntityFrameworkCore;

namespace AxialFanMVC.Repositories
{
    // One-time / on-demand backfill: reads handbook_chunks from MySQL (ground
    // truth text), embeds each chunk locally via LLamaSharp, and upserts the
    // resulting vectors into Qdrant. MySQL stays the source of truth for the
    // text; Qdrant only ever holds a derived, rebuildable vector index.
    public class HandbookVectorSyncService : IHandbookVectorSyncService
    {
        private readonly AxialFanDbContext _db;
        private readonly ILlamaSharpEmbeddingService _embeddingService;
        private readonly IQdrantHandbookVectorService _vectorService;

        public HandbookVectorSyncService(
            AxialFanDbContext db,
            ILlamaSharpEmbeddingService embeddingService,
            IQdrantHandbookVectorService vectorService)
        {
            _db = db;
            _embeddingService = embeddingService;
            _vectorService = vectorService;
        }

        public async Task<int> SyncAllToQdrantAsync(int batchSize = 16)
        {
            await _vectorService.EnsureCollectionExistsAsync();

            var chunks = await _db.handbook_chunks
                .Where(c => c.QualityFlag != "excluded")
                .OrderBy(c => c.Id)
                .ToListAsync();

            var synced = 0;

            for (var i = 0; i < chunks.Count; i += batchSize)
            {
                var batch = chunks.Skip(i).Take(batchSize).ToList();
                var texts = batch.Select(c => c.Text).ToArray();

                var embeddings = await _embeddingService.EmbedBatchAsync(texts);

                var records = batch.Zip(embeddings, (chunk, vector) => new HandbookChunkVectorRecord
                {
                    Id = (ulong)chunk.Id,
                    ChunkKey = chunk.ChunkKey,
                    Chapter = chunk.Chapter,
                    ChapterTitle = chunk.ChapterTitle,
                    Page = chunk.Page,
                    Text = chunk.Text,
                    Vector = vector
                }).ToList();

                await _vectorService.UpsertBatchAsync(records);
                synced += records.Count;
            }

            return synced;
        }
    }
}
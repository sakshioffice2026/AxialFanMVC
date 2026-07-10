using AxialFanMVC.Database;
using AxialFanMVC.Repositories.Inteface;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Text;
using System.Text.Json;

namespace AxialFanMVC.Repositories
{
    public class HandbookChunkRepository : IHandbookChunkRepository
    {
        private readonly AxialFanDbContext _db;
        private readonly HttpClient _http;
        private readonly string _embeddingModel;

        // In-memory cache of parsed (chunkId, vector) pairs — the handbook corpus
        // is static reference data that doesn't change at runtime, so there's no
        // value in re-parsing 224 JSON float arrays on every chat message. Cleared
        // only by app restart or by BackfillEmbeddingsAsync (which repopulates it),
        // so a newly-embedded chunk shows up without a restart.
        private static List<(int Id, float[] Vector)>? _vectorCache;
        private static readonly object _cacheLock = new();

        public HandbookChunkRepository(AxialFanDbContext db, HttpClient http, IConfiguration config)
        {
            _db = db;
            _http = http;

            // Base URL is set on the HttpClient via Program.cs (same pattern as
            // OllamaChatRepository) — this is a separate embedding model, not the
            // chat model, since llama3.1:8b isn't built for embeddings.
            _embeddingModel = config["Ollama:EmbeddingModel"] ?? "nomic-embed-text";
        }

        public async Task<List<HandbookChunk>> SearchAsync(string query, int maxResults = 10)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<HandbookChunk>();

            // Natural language mode: MySQL ranks by relevance automatically,
            // no need for boolean operators (+/-) from the caller.
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

            List<(int Id, float[] Vector)> cache;
            try
            {
                cache = await GetVectorCacheAsync();
            }
            catch (HttpRequestException)
            {
                return await SearchAsync(query, maxResults);
            }

            if (cache.Count == 0)
            {
                // No chunks embedded yet (BackfillEmbeddingsAsync hasn't run) —
                // fall back rather than returning nothing.
                return await SearchAsync(query, maxResults);
            }

            float[] queryVector;
            try
            {
                queryVector = (await EmbedAsync(new[] { query }))[0];
            }
            catch (HttpRequestException)
            {
                return await SearchAsync(query, maxResults);
            }

            var topIds = cache
                .Select(c => (c.Id, Score: CosineSimilarity(queryVector, c.Vector)))
                .OrderByDescending(c => c.Score)
                .Take(maxResults)
                .Select(c => c.Id)
                .ToList();

            // Fetch the actual entities for the winning IDs, then restore the
            // ranked order — EF doesn't guarantee row order for an IN(...) query.
            var chunks = await _db.handbook_chunks
                .Where(c => topIds.Contains(c.Id))
                .ToListAsync();

            return topIds
                .Select(id => chunks.First(c => c.Id == id))
                .ToList();
        }

        public async Task<int> BackfillEmbeddingsAsync()
        {
            var pending = await _db.handbook_chunks
                .Where(c => c.Embedding == null)
                .ToListAsync();

            if (pending.Count == 0) return 0;

            // Batch through Ollama's /api/embed in groups rather than one chunk
            // per call — 20 is comfortably under typical request-size limits
            // while still cutting a 224-chunk backfill down to ~11 round trips.
            const int batchSize = 20;
            int embedded = 0;

            for (int i = 0; i < pending.Count; i += batchSize)
            {
                var batch = pending.Skip(i).Take(batchSize).ToList();
                var vectors = await EmbedAsync(batch.Select(c => c.Text).ToArray());

                for (int j = 0; j < batch.Count; j++)
                {
                    batch[j].Embedding = JsonSerializer.Serialize(vectors[j]);
                }

                embedded += batch.Count;
            }

            await _db.SaveChangesAsync();

            // Force the next SearchBySimilarityAsync call to reload from DB
            // instead of serving the stale (or empty) cache.
            lock (_cacheLock) { _vectorCache = null; }

            return embedded;
        }

        private async Task<List<(int Id, float[] Vector)>> GetVectorCacheAsync()
        {
            lock (_cacheLock)
            {
                if (_vectorCache != null) return _vectorCache;
            }

            var rows = await _db.handbook_chunks
                .Where(c => c.Embedding != null)
                .Select(c => new { c.Id, c.Embedding })
                .ToListAsync();

            var parsed = rows
                .Select(r => (r.Id, Vector: JsonSerializer.Deserialize<float[]>(r.Embedding!)!))
                .ToList();

            lock (_cacheLock) { _vectorCache = parsed; }
            return parsed;
        }

        // Calls Ollama's /api/embed, which accepts a batch of inputs and returns
        // one vector per input in the same order.
        private async Task<float[][]> EmbedAsync(string[] inputs)
        {
            var requestBody = new { model = _embeddingModel, input = inputs };
            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _http.PostAsync("/api/embed", content);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);

            return doc.RootElement
                .GetProperty("embeddings")
                .EnumerateArray()
                .Select(v => v.EnumerateArray().Select(x => x.GetSingle()).ToArray())
                .ToArray();
        }

        private static double CosineSimilarity(float[] a, float[] b)
        {
            double dot = 0, magA = 0, magB = 0;
            for (int i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                magA += a[i] * a[i];
                magB += b[i] * b[i];
            }
            if (magA == 0 || magB == 0) return 0;
            return dot / (Math.Sqrt(magA) * Math.Sqrt(magB));
        }
    }
}
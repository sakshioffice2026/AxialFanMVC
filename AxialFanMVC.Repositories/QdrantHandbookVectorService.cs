using AxialFanMVC.Repositories.Inteface;
using AxialFanMVC.Repositories.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.Qdrant;
using Qdrant.Client;

namespace AxialFanMVC.Repositories
{
    public class QdrantHandbookVectorService : IQdrantHandbookVectorService
    {
        private readonly QdrantVectorStore _vectorStore;
        private readonly VectorStoreCollection<ulong, HandbookChunkVectorRecord> _collection;
        private readonly string _collectionName;

        public QdrantHandbookVectorService(IConfiguration config)
        {
            var host = config["Qdrant:Host"] ?? "localhost";
            var port = int.TryParse(config["Qdrant:Port"], out var p) ? p : 6334;
            var https = bool.TryParse(config["Qdrant:Https"], out var h) && h;
            _collectionName = config["Qdrant:CollectionName"] ?? "handbook_chunks";

            var qdrantClient = new QdrantClient(host, port, https);
            _vectorStore = new QdrantVectorStore(qdrantClient, ownsClient: true);
            _collection = _vectorStore.GetCollection<ulong, HandbookChunkVectorRecord>(_collectionName);
        }

        public async Task EnsureCollectionExistsAsync()
        {
            await _collection.EnsureCollectionExistsAsync();
        }

        public async Task UpsertAsync(HandbookChunkVectorRecord record)
        {
            await _collection.UpsertAsync(record);
        }

        public async Task UpsertBatchAsync(IEnumerable<HandbookChunkVectorRecord> records)
        {
            await _collection.UpsertAsync(records);
        }

        public async Task<List<HandbookChunkVectorRecord>> SearchAsync(float[] queryVector, int top = 5)
        {
            var results = new List<HandbookChunkVectorRecord>();

            await foreach (var result in _collection.SearchAsync(queryVector, top))
            {
                results.Add(result.Record);
            }

            return results;
        }
    }
}
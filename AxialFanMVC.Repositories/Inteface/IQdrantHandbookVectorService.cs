using AxialFanMVC.Repositories.Models;

namespace AxialFanMVC.Repositories.Inteface
{
    public interface IQdrantHandbookVectorService
    {
        Task EnsureCollectionExistsAsync();
        Task UpsertAsync(HandbookChunkVectorRecord record);
        Task UpsertBatchAsync(IEnumerable<HandbookChunkVectorRecord> records);
        Task<List<HandbookChunkVectorRecord>> SearchAsync(float[] queryVector, int top = 5);
    }
}
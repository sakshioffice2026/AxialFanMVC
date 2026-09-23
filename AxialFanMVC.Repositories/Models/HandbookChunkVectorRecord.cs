using Microsoft.Extensions.VectorData;

namespace AxialFanMVC.Repositories.Models
{
    public class HandbookChunkVectorRecord
    {
        [VectorStoreKey]
        public ulong Id { get; set; }

        [VectorStoreData]
        public string ChunkKey { get; set; } = string.Empty;

        [VectorStoreData]
        public int? Chapter { get; set; }

        [VectorStoreData]
        public string? ChapterTitle { get; set; }

        [VectorStoreData]
        public int? Page { get; set; }

        [VectorStoreData]
        public string Text { get; set; } = string.Empty;

        [VectorStoreVector(Dimensions: 768, DistanceFunction = DistanceFunction.CosineSimilarity)]
        public ReadOnlyMemory<float> Vector { get; set; }
    }
}
namespace AxialFanMVC.Repositories.Inteface
{
    public interface ILlamaSharpEmbeddingService
    {
        Task<float[]> GenerateEmbeddingAsync(string text);
        Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts);
    }
}
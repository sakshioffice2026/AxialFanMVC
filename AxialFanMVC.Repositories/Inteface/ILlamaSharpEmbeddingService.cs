namespace AxialFanMVC.Repositories.Inteface;

public interface ILlamaSharpEmbeddingService
{
    Task<float[]> EmbedAsync(string text);

    Task<float[][]> EmbedBatchAsync(string[] texts);
}
using LLama;

namespace AxialFanMVC.Repositories.Inteface
{
    public interface ILlamaModelProvider : IDisposable
    {
        LLamaWeights ChatModel { get; }
        LLamaWeights EmbeddingModel { get; }
        LLamaContext CreateChatContext();
        LLamaContext CreateEmbeddingContext();
        SemaphoreSlim ChatLock { get; }
        SemaphoreSlim EmbeddingLock { get; }
    }
}
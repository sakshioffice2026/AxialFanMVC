using LLama;
using LLama.Common;

namespace AxialFanMVC.Repositories.Inteface
{
    public interface ILlamaModelProvider : IDisposable
    {
        LLamaWeights ChatModel { get; }
        LLamaWeights EmbeddingModel { get; }
        ModelParams ChatParams { get; }
        ModelParams EmbeddingParams { get; }
    }
}
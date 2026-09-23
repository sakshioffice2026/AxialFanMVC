using AxialFanMVC.Database;

namespace AxialFanMVC.Repositories.Inteface
{
    public interface IRagChatOrchestrator
    {
        /// <summary>
        /// Answers a user question using Qdrant semantic search over handbook
        /// chunks as RAG context, generated locally via Semantic Kernel + LLamaSharp.
        /// </summary>
        Task<string> AskAsync(string userMessage);

        /// <summary>
        /// Same RAG pipeline, with the design's own computed values (from MySQL,
        /// via EF Core) injected as a second, separate context block.
        /// </summary>
        Task<string> AskAboutDesignAsync(string userMessage, DesignResult result);
    }
}
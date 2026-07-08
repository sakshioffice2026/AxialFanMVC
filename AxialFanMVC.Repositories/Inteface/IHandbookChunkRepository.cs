using AxialFanMVC.Database;

namespace AxialFanMVC.Repositories.Inteface
{
    public interface IHandbookChunkRepository
    {
        /// <summary>
        /// Full-text search across handbook chunks, ranked by MySQL relevance score.
        /// </summary>
        Task<List<HandbookChunk>> SearchAsync(string query, int maxResults = 10);
    }
}
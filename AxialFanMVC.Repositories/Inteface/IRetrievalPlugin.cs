namespace AxialFanMVC.Repositories.Inteface;

public interface IRetrievalPlugin
{
    Task<IReadOnlyList<string>> SearchHandbookAsync(
        string query,
        int maxResults = 5,
        CancellationToken cancellationToken = default);
}
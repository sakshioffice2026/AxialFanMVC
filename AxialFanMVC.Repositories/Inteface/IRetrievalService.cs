namespace AxialFanMVC.Repositories.Inteface;

public interface IRetrievalService
{
    Task<IReadOnlyList<string>> SearchHandbookAsync(
        string query,
        int maxResults = 5,
        CancellationToken cancellationToken = default);
}
namespace AxialFanMVC.Repositories.Inteface
{
    public interface IHandbookVectorSyncService
    {
        Task<int> SyncAllToQdrantAsync(int batchSize = 16);
    }
}
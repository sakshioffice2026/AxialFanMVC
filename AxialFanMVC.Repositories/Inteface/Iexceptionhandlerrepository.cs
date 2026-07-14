namespace AxialFanMVC.Repositories.Inteface
{
    public interface IExceptionHandlerRepository
    {
        /// <summary>
        /// Persists an exception to the exception_logs table. Never throws —
        /// a logging failure must not mask or replace the original error.
        /// </summary>
        void SaveException(string className, string methodName, string error, int? userId = null);
    }
}
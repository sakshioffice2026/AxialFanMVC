using System;
using AxialFanMVC.Database;
using AxialFanMVC.Repositories.Inteface;

namespace AxialFanMVC.Repositories
{
    public class ExceptionHandlerRepository : IExceptionHandlerRepository
    {
        private readonly AxialFanDbContext _db;

        public ExceptionHandlerRepository(AxialFanDbContext db)
        {
            _db = db;
        }

        public void SaveException(string className, string methodName, string error, int? userId = null)
        {
            var entry = new ExceptionLogEntry
            {
                ClassName = className,
                MethodName = methodName,
                Error = error,
                CreatedAt = DateTime.UtcNow,
                UserId = userId
            };

            _db.exception_logs.Add(entry);

            try
            {
                _db.SaveChanges();
            }
            catch (Exception ex)
            {
                // Logging must never throw — swallow and fall back to console
                // so the original exception (already returned to the caller)
                // isn't masked by a secondary failure here.
                Console.WriteLine($"Error saving exception log: {ex.Message}");
            }
        }
    }
}
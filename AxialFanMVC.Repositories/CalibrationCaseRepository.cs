using AxialFanMVC.Database;
using AxialFanMVC.Repositories.Inteface;
using Microsoft.EntityFrameworkCore;

namespace AxialFanMVC.Repositories
{
    public class CalibrationCaseRepository : ICalibrationCaseRepository
    {
        private readonly AxialFanDbContext _db;
        public CalibrationCaseRepository(AxialFanDbContext db) => _db = db;

        public async Task<List<CalibrationCase>> GetAllWithPointsAsync() =>
            await _db.calibration_cases.Include(c => c.Points).ToListAsync();
    }
}
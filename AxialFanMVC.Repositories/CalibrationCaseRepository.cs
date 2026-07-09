using AxialFanMVC.Database;
using AxialFanMVC.Repositories.Inteface;
using Microsoft.EntityFrameworkCore;

namespace AxialFanMVC.Repositories
{
    public class CalibrationCaseRepository : ICalibrationCaseRepository
    {
        private readonly AxialFanDbContext _db;

        public CalibrationCaseRepository(AxialFanDbContext db)
        {
            _db = db;
        }

        public async Task<List<CalibrationCase>> GetAllWithPointsAsync()
        {
            return await _db.calibration_cases
                .AsNoTracking()
                .Include(c => c.Points)
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();
        }

        public async Task<List<CalibrationCase>> GetSimilarAsync(
            double tipDiameterMm, double hubRatio, int bladeCount,
            double tipDiameterTolerancePct = 15.0, double hubRatioTolerance = 0.05)
        {
            double diameterLow = tipDiameterMm * (1 - tipDiameterTolerancePct / 100.0);
            double diameterHigh = tipDiameterMm * (1 + tipDiameterTolerancePct / 100.0);
            double hubLow = hubRatio - hubRatioTolerance;
            double hubHigh = hubRatio + hubRatioTolerance;

            return await _db.calibration_cases
                .AsNoTracking()
                .Include(c => c.Points)
                .Where(c => c.BladeCount == bladeCount
                    && c.TipDiameterMm >= diameterLow && c.TipDiameterMm <= diameterHigh
                    && c.HubRatio >= hubLow && c.HubRatio <= hubHigh)
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();
        }

        public async Task<CalibrationCase?> GetByIdWithPointsAsync(int id)
        {
            return await _db.calibration_cases
                .AsNoTracking()
                .Include(c => c.Points)
                .FirstOrDefaultAsync(c => c.Id == id);
        }
    }
}
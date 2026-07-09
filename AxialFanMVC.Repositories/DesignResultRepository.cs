using AxialFanMVC.Database;
using AxialFanMVC.Models;
using AxialFanMVC.Repositories.Inteface;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AxialFanMVC.Repositories
{
    public class DesignResultRepository : IDesignResultRepository
    {
        private readonly AxialFanDbContext _db;

        public DesignResultRepository(AxialFanDbContext db) => _db = db;

        public async Task<DesignResult?> GetResultForUserAsync(int resultId, int userId) =>
            await _db.design_results
                .Include(r => r.DesignInput).ThenInclude(di => di.Project)
                .Include(r => r.DesignInput).ThenInclude(di => di.BladeProfile)
                .Include(r => r.PerformanceCurves)
                .Include(r => r.Drawings)
                .FirstOrDefaultAsync(r => r.Id == resultId &&
                                          r.DesignInput.Project.UserId == userId);

        public async Task<Drawing?> GetDrawingForUserAsync(int drawingId, int userId) =>
            await _db.drawings
                .Include(d => d.DesignResult).ThenInclude(r => r.DesignInput).ThenInclude(di => di.Project)
                .FirstOrDefaultAsync(d => d.Id == drawingId &&
                                          d.DesignResult.DesignInput.Project.UserId == userId);

        public async Task AddPerformanceCurveAsync(PerformanceCurve curve) =>
            await _db.performance_curves.AddAsync(curve);

        public async Task SaveChangesAsync() => await _db.SaveChangesAsync();

        public async Task<PerformanceCurve> AddManualCurveAsync(
            int designResultId, int createdByUserId, string label,
            double bladeAngleDeg, int speedRpm,
            List<double> q, List<double> dp, List<double> eta, List<double> kw)
        {
            // Source = "Manual" is what drives origin_type (the generated
            // column) to 'manual' — no separate flag to set, by design,
            // so the two can never disagree (see Phase 1 discussion).
            var curve_ = new PerformanceCurve
            {
                DesignResultId = designResultId,
                CreatedByUserId = createdByUserId,
                Label = string.IsNullOrWhiteSpace(label) ? "Manual curve" : label,
                BladeAngleDeg = bladeAngleDeg,
                SpeedRpm = speedRpm,
                Source = "Manual",
                ValidationStatus = "not_applicable", // physics rules aren't run against manual entries — see Phase 4 rationale
                QValues = string.Join(",", q),
                DpValues = string.Join(",", dp),
                EtaValues = string.Join(",", eta),
                KwValues = string.Join(",", kw)
            };

            await _db.performance_curves.AddAsync(curve_);
            await _db.SaveChangesAsync();
            return curve_;
        }

        public async Task ApplyValidationResultAsync(int performanceCurveId, PhysicsValidationResult result)
        {
            var curve = await _db.performance_curves.FindAsync(performanceCurveId);
            if (curve == null) return;

            curve.ValidationStatus = result.OverallStatus;
            curve.ValidationFlagsJson = JsonSerializer.Serialize(result.Flags);

            // The corrected curve values are what actually get displayed/exported —
            // Feature 1 requires we never silently overwrite without a flag, and
            // the flags above ARE that record. This write only happens if at
            // least one flag exists; an "ok" result with zero flags leaves the
            // original values untouched (nothing to correct).
            if (result.Flags.Any(f => f.Severity == "corrected"))
            {
                curve.DpValues = string.Join(",", result.CorrectedCurve.DpValues);
                curve.EtaValues = string.Join(",", result.CorrectedCurve.EtaValues);
                curve.KwValues = string.Join(",", result.CorrectedCurve.KwValues);
            }

            await _db.SaveChangesAsync();
        }

        public async Task<List<PerformanceCurve>> GetCurvesByOriginAsync(int designResultId, string originType) =>
            await _db.performance_curves
                .Where(c => c.DesignResultId == designResultId)
                .Where(c => EF.Property<string>(c, "origin_type") == originType)
                .ToListAsync();
    
      // DesignResultRepository.cs — add:
        public async Task<BladeProfile?> GetBladeProfileAsync(int bladeProfileId) =>
            await _db.blade_profiles.FindAsync(bladeProfileId);
    }
}
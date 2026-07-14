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
            var curve_ = new PerformanceCurve
            {
                DesignResultId = designResultId,
                CreatedByUserId = createdByUserId,
                Label = string.IsNullOrWhiteSpace(label) ? "Manual curve" : label,
                BladeAngleDeg = bladeAngleDeg,
                SpeedRpm = speedRpm,
                Source = "Manual",
                ValidationStatus = "not_applicable",
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

        public async Task<BladeProfile?> GetBladeProfileAsync(int bladeProfileId) =>
            await _db.blade_profiles.FindAsync(bladeProfileId);

        public async Task<DesignResult?> GetMostRecentResultForUserAsync(int userId) =>
            await _db.design_results
                .Include(r => r.DesignInput).ThenInclude(di => di.Project)
                .Where(r => r.DesignInput.Project.UserId == userId)
                .OrderByDescending(r => r.CalculatedAt)
                .FirstOrDefaultAsync();

        // ── BOM & Costing ──────────────────────────────────────────

        public async Task<List<CostRate>> GetCostRatesAsync() =>
            await _db.cost_rates
                .OrderBy(r => r.Category).ThenBy(r => r.RateKey)
                .ToListAsync();

        public async Task UpdateCostRatesAsync(List<(int Id, double RateValue)> updates)
        {
            var ids = updates.Select(u => u.Id).ToList();
            var rates = await _db.cost_rates.Where(r => ids.Contains(r.Id)).ToListAsync();
            var byId = updates.ToDictionary(u => u.Id, u => u.RateValue);

            foreach (var rate in rates)
                rate.RateValue = byId[rate.Id];

            await _db.SaveChangesAsync();
        }

        public async Task<List<BomLineItem>> GetBomLineItemsAsync(int designResultId) =>
            await _db.bom_line_items
                .Where(b => b.DesignResultId == designResultId)
                .OrderBy(b => b.SortOrder).ThenBy(b => b.Id)
                .ToListAsync();

        public async Task ReplaceAutoBomLineItemsAsync(int designResultId, List<BomLineItem> autoLines)
        {
            var existingAuto = await _db.bom_line_items
                .Where(b => b.DesignResultId == designResultId && b.Source == "Auto")
                .ToListAsync();
            _db.bom_line_items.RemoveRange(existingAuto);

            int order = 0;
            foreach (var line in autoLines)
            {
                line.DesignResultId = designResultId;
                line.Source = "Auto";
                line.SortOrder = order++;
                await _db.bom_line_items.AddAsync(line);
            }

            await _db.SaveChangesAsync();
        }

        public async Task<BomLineItem> AddManualBomLineItemAsync(
            int designResultId, int createdByUserId, string category,
            string description, double quantity, string? unit, double unitCost)
        {
            int nextOrder = (await _db.bom_line_items
                .Where(b => b.DesignResultId == designResultId)
                .Select(b => (int?)b.SortOrder)
                .MaxAsync()) is int max ? max + 1 : 0;

            var line = new BomLineItem
            {
                DesignResultId = designResultId,
                CreatedByUserId = createdByUserId,
                Source = "Manual",
                Category = string.IsNullOrWhiteSpace(category) ? "Manual" : category,
                Description = description,
                Quantity = quantity,
                Unit = unit,
                UnitCost = unitCost,
                LineTotal = quantity * unitCost,
                SortOrder = nextOrder
            };

            await _db.bom_line_items.AddAsync(line);
            await _db.SaveChangesAsync();
            return line;
        }

        public async Task<bool> DeleteManualBomLineItemAsync(int lineItemId, int userId)
        {
            var line = await _db.bom_line_items
                .Include(b => b.DesignResult).ThenInclude(r => r.DesignInput).ThenInclude(di => di.Project)
                .FirstOrDefaultAsync(b => b.Id == lineItemId
                    && b.Source == "Manual"
                    && b.DesignResult.DesignInput.Project.UserId == userId);

            if (line == null) return false;

            _db.bom_line_items.Remove(line);
            await _db.SaveChangesAsync();
            return true;
        }
    }
}
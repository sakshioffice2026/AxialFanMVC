using AxialFanMVC.Database;
using Microsoft.EntityFrameworkCore;

namespace AxialFanMVC.Services
{
    // ═══════════════════════════════════════════════════════════════
    // CostRateSeeder — ensures the CostRate table has a usable starting
    // point instead of sitting at RateValue = 0 for every row (which is
    // what BomCostingEngine falls back to for any rate it can't find).
    //
    // This app doesn't run EF migrations (Program.cs has
    // db.Database.Migrate() commented out), so EF's HasData() seeding
    // mechanism never actually executes — it only applies through a
    // migration. This runs at startup instead, the same way
    // ValidationFlagsBackfill does: idempotent, safe to run on every
    // boot, only inserts rows that don't already exist by
    // (Category, RateKey) so it never overwrites a rate someone has
    // already edited on /Bom/CostRates.
    //
    // Values are starting estimates, not live quotes — sourced from
    // Indian market listings (IndiaMART / steel-supplier price pages,
    // motor listings, mid-2026). Edit them on /Bom/CostRates once real
    // vendor rates are known; this seeder will never touch a row again
    // once it exists.
    // ═══════════════════════════════════════════════════════════════
    public static class CostRateSeeder
    {
        public static async Task RunAsync(AxialFanDbContext db)
        {
            var defaults = new List<CostRate>
            {
                // Material: ₹/kg, raw stock pricing
                new() { Category = "Material", RateKey = "Aluminum 6061-T6",   UnitLabel = "per_kg",         RateValue = 420 },
                new() { Category = "Material", RateKey = "Aluminum 5052-H32",  UnitLabel = "per_kg",         RateValue = 380 },
                new() { Category = "Material", RateKey = "Mild Steel A36",     UnitLabel = "per_kg",         RateValue = 55 },
                new() { Category = "Material", RateKey = "Stainless Steel 304", UnitLabel = "per_kg",        RateValue = 210 },

                // Motor: ₹/kW, blended small-to-mid IE2/IE3 3-phase induction
                // motor pricing. Real motors get cheaper per kW at larger
                // sizes — this is a flat estimate, not a curve.
                new() { Category = "Motor", RateKey = "MotorPerKw", UnitLabel = "per_kw", RateValue = 4000 },

                // Drive hardware: flat, ballpark
                new() { Category = "Drive", RateKey = "VBeltDriveSet", UnitLabel = "flat", RateValue = 5000 },
                new() { Category = "Drive", RateKey = "VfdUnit",       UnitLabel = "flat", RateValue = 15000 },

                // Misc / hardware allowance: % of subtotal
                new() { Category = "Misc", RateKey = "MiscAllowance", UnitLabel = "pct_of_subtotal", RateValue = 15 },
            };

            var existingKeys = await db.cost_rates
                .Select(r => new { r.Category, r.RateKey })
                .ToListAsync();
            var existingSet = existingKeys.Select(k => (k.Category, k.RateKey)).ToHashSet();

            var toInsert = defaults.Where(d => !existingSet.Contains((d.Category, d.RateKey))).ToList();

            if (toInsert.Count > 0)
            {
                db.cost_rates.AddRange(toInsert);
                await db.SaveChangesAsync();
            }

            Console.WriteLine($"CostRateSeeder: inserted {toInsert.Count} new default rate(s), {existingSet.Count} already present.");
        }
    }
}
    using AxialFanMVC.Database;
    using AxialFanMVC.Models;
    using Microsoft.AspNetCore.Mvc;

    namespace AxialFanMVC.Services
    {
        // ═══════════════════════════════════════════════════════════════
        // BomCostingEngine — turns a design + the editable CostRate table
        // into a Bill of Materials estimate. Pure calculation, no DB access
        // (same pattern as AeroCalcEngine/StructCalcEngine) — the caller
        // supplies the already-fetched CostRate rows.
        //
        // Known scope limits (deliberately not papered over):
        //   - Only blade material, motor, and drive-type hardware are
        //     derived from real design data. Hub, bearings, casing, and
        //     fasteners have no data source anywhere in this codebase, so
        //     they're folded into the single "Miscellaneous / hardware"
        //     allowance line rather than invented as separate line items
        //     with false precision.
        //   - If a required CostRate row is missing or its rate_value is
        //     still 0 (the seeded default), the line is still shown — at
        //     ₹0 — with a warning, so the BOM never silently omits a
        //     component the user would expect to see.
        // ═══════════════════════════════════════════════════════════════
        public static class BomCostingEngine
        {
            public static BomResult Calculate(
                DesignInput d, StructCalcEngine.StructCalcResult structResult, List<CostRate> rates)
            {
                var rateLookup = rates.ToDictionary(r => (r.Category, r.RateKey), r => r);
                var result = new BomResult();

                CostRate? Rate(string category, string key)
                {
                    if (rateLookup.TryGetValue((category, key), out var rate) && rate.RateValue > 0)
                        return rate;
                    result.Warnings.Add(rate == null
                        ? $"No cost rate configured for '{key}' — add it on the Cost Rates page. Line shown at \u20b90 until then."
                        : $"Cost rate for '{key}' is set to 0 — add a real value on the Cost Rates page. Line shown at \u20b90 until then.");
                    return rate; // may be null; caller falls back to 0
                }

                // 1. Blade material
                double bladeMassTotalKg = structResult.BladeMassKg * d.BladeCount;
                var materialRate = Rate("Material", structResult.MaterialUsed);
                AddLine(result, "Material", $"Blade material ({structResult.MaterialUsed}, {d.BladeCount} blades)",
                    bladeMassTotalKg, "kg", materialRate?.RateValue ?? 0);

                // 2. Motor
                var motorRate = Rate("Motor", "MotorPerKw");
                AddLine(result, "Motor", $"Motor ({d.MotorPowerKw:F1} kW)",
                    d.MotorPowerKw, "kW", motorRate?.RateValue ?? 0);

                // 3. Drive hardware — only for drive types that need extra
                //    purchased components beyond the motor itself. Direct
                //    Drive / Coupled need no additional line here.
                if (d.DriveType == "V-Belt Drive")
                {
                    var beltRate = Rate("Drive", "VBeltDriveSet");
                    AddLine(result, "Drive", "V-belt drive set (pulleys + belt)", 1, null, beltRate?.RateValue ?? 0);
                }
                else if (d.DriveType == "Direct VFD")
                {
                    var vfdRate = Rate("Drive", "VfdUnit");
                    AddLine(result, "Drive", "VFD unit", 1, null, vfdRate?.RateValue ?? 0);
                }

                result.Subtotal = result.Lines.Sum(l => l.LineTotal);

                // 4. Miscellaneous / hardware allowance — disclosed as a
                //    rough estimate covering everything this tool has no
                //    real data for (hub, bearings, casing, fasteners).
                var miscRate = Rate("Misc", "MiscAllowance");
                double miscPct = miscRate?.RateValue ?? 0;
                double miscTotal = result.Subtotal * (miscPct / 100.0);
                result.Lines.Add(new BomLineItemData
                {
                    Category = "Misc",
                    Description = $"Miscellaneous / hardware allowance ({miscPct:F0}% of subtotal — rough estimate, covers hub, bearings, casing, fasteners)",
                    Quantity = miscPct,
                    Unit = "%",
                    UnitCost = result.Subtotal,
                    LineTotal = miscTotal
                });

                result.GrandTotal = result.Subtotal + miscTotal;
                return result;
            }

            private static void AddLine(
                BomResult result, string category, string description,
                double quantity, string? unit, double unitCost)
            {
                result.Lines.Add(new BomLineItemData
                {
                    Category = category,
                    Description = description,
                    Quantity = quantity,
                    Unit = unit,
                    UnitCost = unitCost,
                    LineTotal = quantity * unitCost
                });
            }
        }
    }
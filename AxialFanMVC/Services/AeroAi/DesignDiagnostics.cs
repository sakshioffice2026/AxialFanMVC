using System.Globalization;
using System.Text;
using System.Text.Json;
using AxialFanMVC.Database;

namespace AxialFanMVC.Services.AeroAi
{
    public class DiagnosticRow
    {
        public string Metric { get; set; } = "";
        public string Unit { get; set; } = "";
        public double Before { get; set; }
        public double After { get; set; }
        public double Delta => After - Before;
        public int Decimals { get; set; } = 2;

        // 1 = improved, -1 = worse, 0 = neutral / not a quality metric
        public int Better { get; set; }
    }

    public class WarningAudit
    {
        public List<string> Resolved { get; set; } = new();
        public List<string> Remaining { get; set; } = new();
        public List<string> New { get; set; } = new();
    }

    // Step 3 of the AeroAi optimize flow: side-by-side delta table plus
    // a resolved / remaining / new warning audit.
    public static class DesignDiagnostics
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static List<DiagnosticRow> BuildDeltaTable(DesignResult before, SizingOutcome outcome)
        {
            var bi = before.DesignInput;
            var o = outcome.Optimized;
            var rows = new List<DiagnosticRow>();

            void Add(string metric, string unit, double b, double a, bool? higherIsBetter, int decimals)
            {
                int better = 0;
                double delta = a - b;
                if (higherIsBetter.HasValue && Math.Abs(delta) > Math.Pow(10, -decimals) / 2)
                    better = (delta > 0) == higherIsBetter.Value ? 1 : -1;

                rows.Add(new DiagnosticRow
                {
                    Metric = metric,
                    Unit = unit,
                    Before = b,
                    After = a,
                    Decimals = decimals,
                    Better = better
                });
            }

            Add("Tip diameter", "mm", bi.TipDiameterMm, o.Input.TipDiameterMm, null, 0);
            Add("Hub ratio", "", bi.HubRatio, o.Input.HubRatio, null, 3);
            Add("Blade angle", "deg", bi.BladeAngleDeg, o.Input.BladeAngleDeg, null, 2);
            Add("Blade count", "", bi.BladeCount, o.Input.BladeCount, null, 0);
            Add("Speed", "rpm", bi.SpeedRpm, o.Input.SpeedRpm, null, 0);
            Add("Motor power", "kW", bi.MotorPowerKw, o.Input.MotorPowerKw, false, 2);

            Add("Overall efficiency", "%", before.OverallEfficiencyPct, o.Aero.OverallEfficiencyPct, true, 2);
            Add("Shaft power", "kW", before.ShaftPowerKw, o.Aero.ShaftPowerKw, false, 3);
            Add("Noise (Lp)", "dB(A)", before.OverallNoiseDbA ?? 0, o.Sound.LpOverallDba, false, 1);
            Add("Tip speed", "m/s", before.TipSpeedMs, o.Aero.TipSpeedMs, false, 1);
            Add("Blade stress", "MPa", before.BladeStressMpa, o.Struct.TotalStressMpa, false, 1);
            Add("Safety factor", "", before.SafetyFactor, o.Struct.SafetyFactor, true, 2);
            Add("Estimated cost", "", outcome.Baseline.Cost, o.Cost, false, 0);

            return rows;
        }

        public static WarningAudit BuildWarningAudit(DesignResult before, SizingOutcome outcome)
        {
            var beforeWarnings = ParseWarnings(before.WarningMessages);
            var afterWarnings = outcome.Optimized.Warnings;

            return new WarningAudit
            {
                Resolved = beforeWarnings.Where(w => !afterWarnings.Contains(w)).ToList(),
                Remaining = beforeWarnings.Where(w => afterWarnings.Contains(w)).ToList(),
                New = afterWarnings.Where(w => !beforeWarnings.Contains(w)).ToList()
            };
        }

        public static string RenderTable(int sourceResultId, int proposedResultId, List<DiagnosticRow> rows)
        {
            string h1 = "Result #" + sourceResultId;
            string h2 = "Optimized";
            var sb = new StringBuilder();

            sb.AppendLine($"{"Metric",-20}{h1,12}{h2,12}{"Delta",11}");
            sb.AppendLine(new string('-', 55));

            foreach (var r in rows)
            {
                string fmt = "F" + r.Decimals;
                string mark = r.Better > 0 ? " +" : r.Better < 0 ? " -" : "";
                string label = string.IsNullOrEmpty(r.Unit) ? r.Metric : $"{r.Metric} ({r.Unit})";
                string delta = (r.Delta >= 0 ? "+" : "") + r.Delta.ToString(fmt, Inv);

                sb.AppendLine(
                    $"{Trim(label, 19),-20}{r.Before.ToString(fmt, Inv),12}{r.After.ToString(fmt, Inv),12}{delta,11}{mark}");
            }

            return sb.ToString().TrimEnd();
        }

        public static string RenderWarningAudit(WarningAudit audit)
        {
            var sb = new StringBuilder();

            sb.AppendLine($"Resolved warnings ({audit.Resolved.Count})");
            foreach (var w in audit.Resolved) sb.AppendLine("  + " + w);

            sb.AppendLine($"Remaining warnings ({audit.Remaining.Count})");
            foreach (var w in audit.Remaining) sb.AppendLine("  ~ " + w);

            sb.AppendLine($"New warnings ({audit.New.Count})");
            foreach (var w in audit.New) sb.AppendLine("  ! " + w);

            return sb.ToString().TrimEnd();
        }

        private static List<string> ParseWarnings(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<string>();
            try { return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>(); }
            catch { return new List<string>(); }
        }

        private static string Trim(string s, int max) => s.Length <= max ? s : s.Substring(0, max);
    }
}
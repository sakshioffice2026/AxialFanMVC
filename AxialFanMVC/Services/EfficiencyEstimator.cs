using AxialFanMVC.Database;
using AxialFanMVC.Models;

namespace AxialFanMVC.Services
{
    public static class EfficiencyEstimator
    {
        // Normalization scales — "how much does this quantity typically
        // vary across real axial fan designs" — used to make the distance
        // metric fair across quantities with very different units/ranges.
        // These are reasonable engineering judgment calls, not measured
        // statistics from your data; revisit once calibration_cases has
        // enough entries to compute real variance instead.
        private const double SpecificSpeedScale = 3.0;
        private const double HubRatioScale = 0.15;
        private const double BladeAngleScale = 10.0;
        private const double TipMachScale = 0.15;

        // Distance beyond which a "closest" calibration case is judged
        // too dissimilar to trust — falls back to the generic correlation
        // instead of quietly extrapolating from an unrelated fan.
        private const double MaxAcceptableDistance = 2.5;

        public static EfficiencyEstimationResult Estimate(
            DesignInput d, AeroCalcResult partial, double tipMachNumber,
            List<CalibrationCase> candidates)
        {
            var result = new EfficiencyEstimationResult();

            CalibrationCase? best = null;
            double bestDistance = double.MaxValue;

            foreach (var c in candidates)
            {
                if (c.Points.Count == 0) continue; // can't interpolate an empty curve

                double dNs = (c.SpecificSpeedOf(partial) - partial.SpecificSpeed) / SpecificSpeedScale;
                double dHub = (c.HubRatio - d.HubRatio) / HubRatioScale;
                double dAngle = (c.BladeAngleDeg - d.BladeAngleDeg) / BladeAngleScale;
                double dMach = (c.TipMachNumber - tipMachNumber) / TipMachScale;

                double distance = Math.Sqrt(dNs * dNs + dHub * dHub + dAngle * dAngle + dMach * dMach);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = c;
                }
            }

            if (best != null && bestDistance <= MaxAcceptableDistance)
            {
                result.Method = "CalibrationMatch";
                result.MatchedCaseDescription = $"{best.SourceType}" +
                    (string.IsNullOrEmpty(best.SourceDescription) ? "" : $" — {best.SourceDescription}");
                result.MatchDistance = Math.Round(bestDistance, 3);
                result.EfficiencyPct = InterpolateAtFlowCoefficient(best, partial.FlowCoefficient, result.Notes);
                result.Notes.Add($"Matched against calibration case (distance {bestDistance:F2}, source: {best.SourceType}). " +
                                   "Confidence depends entirely on how representative that case is of this design.");
            }
            else
            {
                result.Method = "CordierCorrelation";
                result.EfficiencyPct = CordierFallback(partial.SpecificSpeed);
                result.Notes.Add(best == null
                    ? "No calibration cases available — using a generic specific-speed correlation."
                    : $"Closest calibration case was too dissimilar (distance {bestDistance:F2} > {MaxAcceptableDistance:F1}) " +
                       "— using a generic specific-speed correlation instead of extrapolating from an unrelated design.");
                result.Notes.Add("This correlation's coefficients are literature-informed estimates, not calibrated " +
                                   "against your own test/CFD/manufacturer data. Add matching calibration_cases entries " +
                                   "to improve confidence for this design region.");
            }

            return result;
        }

        // Linear interpolation of EfficiencyPct at the design's flow
        // coefficient, using the calibration case's own measured curve
        // (points are expected pre-sorted by FlowCoefficient at entry
        // time; sorting defensively here in case they aren't).
        private static double InterpolateAtFlowCoefficient(CalibrationCase c, double targetPhi, List<string> notes)
        {
            var pts = c.Points.OrderBy(p => p.FlowCoefficient).ToList();

            if (targetPhi <= pts[0].FlowCoefficient)
            {
                notes.Add("Design flow coefficient is at or below this case's lowest tested point — clamped, not extrapolated.");
                return pts[0].EfficiencyPct;
            }
            if (targetPhi >= pts[^1].FlowCoefficient)
            {
                notes.Add("Design flow coefficient is at or above this case's highest tested point — clamped, not extrapolated.");
                return pts[^1].EfficiencyPct;
            }

            for (int i = 0; i < pts.Count - 1; i++)
            {
                if (targetPhi >= pts[i].FlowCoefficient && targetPhi <= pts[i + 1].FlowCoefficient)
                {
                    double span = pts[i + 1].FlowCoefficient - pts[i].FlowCoefficient;
                    double t = span == 0 ? 0 : (targetPhi - pts[i].FlowCoefficient) / span;
                    return pts[i].EfficiencyPct + t * (pts[i + 1].EfficiencyPct - pts[i].EfficiencyPct);
                }
            }
            return pts[0].EfficiencyPct; // unreachable given the bounds checks above
        }

        // Generic axial-fan peak-efficiency-vs-specific-speed correlation.
        // Shape: rises toward a peak around Ns≈4.5, falls off on either
        // side — consistent with the general Cordier-diagram pattern for
        // axial turbomachinery, but the exact peak location/width here are
        // reasonable defaults, NOT fitted to this company's fans. Treat
        // any efficiency from this path as a rough estimate only.
        private static double CordierFallback(double specificSpeed)
        {
            const double peakNs = 4.5;
            const double peakEfficiency = 82.0;
            const double width = 3.0;

            double d = (specificSpeed - peakNs) / width;
            double eff = peakEfficiency * Math.Exp(-0.5 * d * d);
            return Math.Clamp(eff, 30.0, 90.0); // never claim below-30 or above-90 from an unvalidated correlation
        }
    }

    // Small helper so the distance calc above can compare a calibration
    // case's own specific speed to the current design's — CalibrationCase
    // doesn't store SpecificSpeed directly (it stores the raw inputs it's
    // derived from), so this recomputes it the same way AeroCalcEngine does.
    public static class CalibrationCaseExtensions
    {
        public static double SpecificSpeedOf(this CalibrationCase c, AeroCalcResult _)
        {
            double omega = 2 * Math.PI * c.SpeedRpm / 60.0;
            // Uses the case's own stored flow/pressure — CalibrationCase
            // doesn't currently store FlowRateM3s/TotalPressurePa directly
            // at the case level (only per-point). Using the case's first
            // point as representative; see note below.
            var repPoint = c.Points.OrderBy(p => Math.Abs(p.FlowCoefficient - 0.3)).FirstOrDefault();
            if (repPoint == null) return double.NaN;
            return omega * Math.Sqrt(repPoint.FlowRateM3s) / Math.Pow(repPoint.PressureRisePa / c.DensityKgM3, 0.75);
        }
    }
}
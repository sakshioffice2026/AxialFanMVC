namespace AxialFanMVC.Models
{
    public class PhysicsValidationContext
    {
        public PerformanceCurveData? ComparisonCurveAtDifferentRpm { get; set; }
        public string CurveSource { get; set; } = "";
    }

    public class PhysicsValidationResult
    {
        public PerformanceCurveData CorrectedCurve { get; set; } = new();
        public List<PhysicsValidationFlag> Flags { get; set; } = new();

        public string OverallStatus =>
            Flags.Count == 0 ? "ok"
            : Flags.Any(f => f.Severity == "flagged") ? "flagged"
            : "corrected";
    }

    public class PhysicsValidationFlag
    {
        public string Rule { get; set; } = "";
        public string Severity { get; set; } = "";
        public string Message { get; set; } = "";
        public double? OriginalValue { get; set; }
        public double? CorrectedValue { get; set; }
        public double? FlowRateAtViolation { get; set; }
    }
}
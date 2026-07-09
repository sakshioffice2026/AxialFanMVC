namespace AxialFanMVC.Models
{
    public class PinnFeatureVector
    {
        public double FlowCoefficient { get; set; }
        public double PressureCoefficient { get; set; }
        public double SpecificSpeed { get; set; }
        public double TipMachNumber { get; set; }
        public double Solidity { get; set; }
        public double ReynoldsNumber { get; set; }
        public double TipSpeedMs { get; set; }
        public double ChordLengthMm { get; set; }
        public double HubRatio { get; set; }
        public int BladeCount { get; set; }
        public double? MaxCamberPct { get; set; }
        public double? MaxThicknessPct { get; set; }
        public bool HasBladeProfile { get; set; }
    }
}
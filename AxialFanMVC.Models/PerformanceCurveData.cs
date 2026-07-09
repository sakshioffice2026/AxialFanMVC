namespace AxialFanMVC.Models
{
    public class PerformanceCurveData
    {
        public double BladeAngleDeg { get; set; }
        public int SpeedRpm { get; set; }
        public List<double> QValues { get; set; } = new();
        public List<double> DpValues { get; set; } = new();
        public List<double> EtaValues { get; set; } = new();
        public List<double> KwValues { get; set; } = new();
    }
}
namespace AxialFanMVC.Models
{
    public class DashboardViewModel
    {
        public int ProjectCount { get; set; }
        public int TotalDesigns { get; set; }
        public double AvgEfficiencyPct { get; set; }
        public double AvgSafetyFactor { get; set; }
        public int OkCount { get; set; }
        public int WarningCount { get; set; }
        public int ErrorCount { get; set; }
        public int FlaggedCurveDesignCount { get; set; }
        public int RecentExportCount { get; set; }

        public List<DashboardTrendPoint> EfficiencyTrend { get; set; } = new();
        public List<DashboardRecentDesign> RecentDesigns { get; set; } = new();
        public List<DashboardProfileUsage> BladeProfileUsage { get; set; } = new();
    }

    public class DashboardTrendPoint
    {
        public string Label { get; set; } = "";
        public double EfficiencyPct { get; set; }
    }

    public class DashboardRecentDesign
    {
        public int ResultId { get; set; }
        public string ProjectName { get; set; } = "";
        public string BladeProfileName { get; set; } = "";
        public double FlowRateM3s { get; set; }
        public double TotalPressurePa { get; set; }
        public double OverallEfficiencyPct { get; set; }
        public string Status { get; set; } = "";
        public DateTime CalculatedAt { get; set; }
    }

    public class DashboardProfileUsage
    {
        public string ProfileName { get; set; } = "";
        public int Count { get; set; }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AxialFanMVC.Models
{
    public class EfficiencyEstimationResult
    {
        public double EfficiencyPct { get; set; }
        public string Method { get; set; } = "";          // "CalibrationMatch" | "CordierCorrelation"
        public string? MatchedCaseDescription { get; set; }
        public double? MatchDistance { get; set; }
        public List<string> Notes { get; set; } = new();
    }
}

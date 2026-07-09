using System;
using System.Collections.Generic;
using System.Linq;
using AxialFanMVC.Models;

namespace AxialFanMVC.Services
{
    public static class CurvePointLookup
    {
        public readonly record struct Point(
            double PressurePa, double EfficiencyPct, double PowerKw, bool WithinCurveRange);

        public static Point NearestAt(
            IReadOnlyList<double> q, IReadOnlyList<double> dp,
            IReadOnlyList<double> eta, IReadOnlyList<double> kw, double targetFlowM3s)
        {
            if (q.Count == 0)
                return new Point(0, 0, 0, false);

            int bestIdx = 0;
            double bestDelta = double.MaxValue;
            for (int i = 0; i < q.Count; i++)
            {
                double delta = Math.Abs(q[i] - targetFlowM3s);
                if (delta < bestDelta) { bestDelta = delta; bestIdx = i; }
            }

            bool withinRange = targetFlowM3s >= q.Min() && targetFlowM3s <= q.Max();
            return new Point(dp[bestIdx], eta[bestIdx], kw[bestIdx], withinRange);
        }

        public static Point NearestAt(PerformanceCurve curve, double targetFlowM3s)
        {
            var q = curve.QValues.Split(',').Select(double.Parse).ToList();
            var dp = curve.DpValues.Split(',').Select(double.Parse).ToList();
            var eta = curve.EtaValues.Split(',').Select(double.Parse).ToList();
            var kw = curve.KwValues.Split(',').Select(double.Parse).ToList();
            return NearestAt(q, dp, eta, kw, targetFlowM3s);
        }

        public static Point NearestAt(PerformanceCurveData curve, double targetFlowM3s) =>
            NearestAt(curve.QValues, curve.DpValues, curve.EtaValues, curve.KwValues, targetFlowM3s);
    }
}

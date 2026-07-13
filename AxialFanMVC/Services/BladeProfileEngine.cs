using AxialFanMVC.Database;
using AxialFanMVC.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AxialFanMVC.Services
{
    // ═══════════════════════════════════════════════════════════════
    // BladeProfileEngine
    //
    // Generates precise aerofoil coordinate data, dimensional tables,
    // and aerodynamic parameters for:
    //
    //   A) NACA 4-digit series  (4412, 2412, 0012, 2415, 4415, etc.)
    //   B) NACA 5-digit series  (23012, 23015, etc.)
    //   C) Custom profiles      (user-supplied x/y coordinate arrays)
    //
    // Coordinate system:
    //   x = 0 at leading edge, x = chord at trailing edge
    //   y = 0 at chord line
    //   All values normalised to chord = 1.0 (multiply by actual chord mm)
    //
    // References:
    //   Abbott & Von Doenhoff, "Theory of Wing Sections", 1959
    //   Jacobs, Ward, Pinkerton, NACA Report 460, 1933
    // ═══════════════════════════════════════════════════════════════

    public static class BladeProfileEngine
    {
        // ── Public: generate all profile data ─────────────────────
        public static BladeProfileData GenerateNaca4(string designation, double chordMm, int points = 100)
        {
            var naca = ParseNaca4(designation);
            var coords = GenerateNaca4Coordinates(naca.m, naca.p, naca.t, points);
            var dims = ComputeDimensions(coords, chordMm);
            var aero = ComputeAeroParams(naca.m, naca.p, naca.t, chordMm);

            return new BladeProfileData
            {
                Designation = designation.ToUpper(),
                Type = "NACA 4-digit",
                ChordMm = chordMm,
                MaxCamberPct = naca.m * 100,
                MaxCamberPos = naca.p * 10,          // tenths of chord
                MaxThicknessPct = naca.t * 100,
                UpperSurface = coords.Upper,
                LowerSurface = coords.Lower,
                CamberLine = coords.Camber,
                Dimensions = dims,
                AeroParams = aero,
                StationTable = BuildStationTable(coords, chordMm)
            };
        }

        public static BladeProfileData GenerateNaca5(string designation, double chordMm, int points = 100)
        {
            var naca = ParseNaca5(designation);
            var coords = GenerateNaca5Coordinates(naca.cl, naca.p, naca.t, points);
            var dims = ComputeDimensions(coords, chordMm);
            var aero = new AeroParameters
            {
                DesignLiftCoeff = naca.cl,
                MaxCamberLocation = naca.p * 100,
                ThicknessRatio = naca.t,
                LeadingEdgeRadius = 1.1019 * naca.t * naca.t * chordMm,
                TrailingEdgeAngle = ComputeTeAngle(naca.t),
                ApproxStallAngle = 12 + naca.cl * 4,
                ApproxZeroLiftAngle = -naca.cl * 6
            };

            return new BladeProfileData
            {
                Designation = designation.ToUpper(),
                Type = "NACA 5-digit",
                ChordMm = chordMm,
                MaxCamberPct = GetNaca5MaxCamber(naca.cl, naca.p) * 100,
                MaxCamberPos = naca.p * 100,
                MaxThicknessPct = naca.t * 100,
                UpperSurface = coords.Upper,
                LowerSurface = coords.Lower,
                CamberLine = coords.Camber,
                Dimensions = dims,
                AeroParams = aero,
                StationTable = BuildStationTable(coords, chordMm)
            };
        }

        public static BladeProfileData GenerateCustom(
            string name,
            double chordMm,
            List<(double x, double yUpper, double yLower)> rawCoords)
        {
            // Normalise to chord = 1 if not already
            double maxX = rawCoords.Max(c => c.x);
            var normalised = rawCoords
                .Select(c => (x: c.x / maxX, yU: c.yUpper / maxX, yL: c.yLower / maxX))
                .OrderBy(c => c.x)
                .ToList();

            var coords = new AerofoilCoords
            {
                Upper = normalised.Select(c => new Point2D(c.x, c.yU)).ToList(),
                Lower = normalised.Select(c => new Point2D(c.x, c.yL)).ToList(),
                Camber = normalised.Select(c => new Point2D(c.x, (c.yU + c.yL) / 2)).ToList()
            };

            double maxThick = normalised.Max(c => c.yU - c.yL);
            double thickPos = normalised.First(c => (c.yU - c.yL) == normalised.Max(r => r.yU - r.yL)).x;

            var dims = ComputeDimensions(coords, chordMm);
            var aero = new AeroParameters
            {
                ThicknessRatio = maxThick,
                MaxCamberLocation = coords.Camber.MaxBy(c => c.Y)?.X * 100 ?? 0,
                DesignLiftCoeff = double.NaN,  // not deterministic for custom
                LeadingEdgeRadius = double.NaN,
                TrailingEdgeAngle = double.NaN,
                ApproxStallAngle = double.NaN,
                ApproxZeroLiftAngle = double.NaN,
                Note = "Aerodynamic parameters not auto-computed for custom profiles. Use CFD or wind tunnel data."
            };

            return new BladeProfileData
            {
                Designation = name,
                Type = "Custom",
                ChordMm = chordMm,
                MaxCamberPct = coords.Camber.Max(c => c.Y) * 100,
                MaxCamberPos = (coords.Camber.MaxBy(c => c.Y)?.X ?? 0) * 100,
                MaxThicknessPct = maxThick * 100,
                UpperSurface = coords.Upper,
                LowerSurface = coords.Lower,
                CamberLine = coords.Camber,
                Dimensions = dims,
                AeroParams = aero,
                StationTable = BuildStationTable(coords, chordMm)
            };
        }

        // ── NACA 4-digit coordinate generation ────────────────────
        // Parameters: m = max camber (fraction), p = camber position (fraction),
        //             t = max thickness (fraction)
        private static AerofoilCoords GenerateNaca4Coordinates(double m, double p, double t, int n)
        {
            var upper = new List<Point2D>();
            var lower = new List<Point2D>();
            var camber = new List<Point2D>();

            // Cosine spacing for denser points near leading/trailing edges
            var xc = Enumerable.Range(0, n + 1)
                .Select(i => (1 - Math.Cos(i * Math.PI / n)) / 2)
                .ToArray();

            foreach (double x in xc)
            {
                // Thickness distribution (NACA standard)
                double yt = t / 0.20 * (
                    0.29690 * Math.Sqrt(x)
                  - 0.12600 * x
                  - 0.35160 * x * x
                  + 0.28430 * x * x * x
                  - 0.10150 * x * x * x * x);

                // Camber line and gradient (dyc_dx factored into
                // NacaCamberSlope so ComputeNaca4ZeroLiftAngleDeg's
                // integral uses this exact same camber-line model)
                double yc = m < 1e-9 ? 0
                    : x < p
                        ? m / (p * p) * (2 * p * x - x * x)
                        : m / ((1 - p) * (1 - p)) * (1 - 2 * p + 2 * p * x - x * x);
                double dyc_dx = NacaCamberSlope(x, m, p);

                double theta = Math.Atan(dyc_dx);

                upper.Add(new Point2D(x - yt * Math.Sin(theta), yc + yt * Math.Cos(theta)));
                lower.Add(new Point2D(x + yt * Math.Sin(theta), yc - yt * Math.Cos(theta)));
                camber.Add(new Point2D(x, yc));
            }

            return new AerofoilCoords { Upper = upper, Lower = lower, Camber = camber };
        }

        // ── NACA 5-digit coordinate generation ────────────────────
        // Uses the Theodorsen reflex camber formulation
        private static AerofoilCoords GenerateNaca5Coordinates(double cl, double p, double t, int n)
        {
            // Compute r and k1 from p (design lift coefficient table)
            double r = 3.33333 * p;  // approximate
            double k1 = ComputeNaca5K1(p);

            var upper = new List<Point2D>();
            var lower = new List<Point2D>();
            var camber = new List<Point2D>();

            var xc = Enumerable.Range(0, n + 1)
                .Select(i => (1 - Math.Cos(i * Math.PI / n)) / 2)
                .ToArray();

            foreach (double x in xc)
            {
                double yt = t / 0.20 * (
                    0.29690 * Math.Sqrt(x)
                  - 0.12600 * x
                  - 0.35160 * x * x
                  + 0.28430 * x * x * x
                  - 0.10150 * x * x * x * x);

                double yc, dyc_dx;
                if (x < p)
                {
                    yc = k1 / 6.0 * (x * x * x - 3 * p * x * x + p * p * (3 - p) * x);
                    dyc_dx = k1 / 6.0 * (3 * x * x - 6 * p * x + p * p * (3 - p));
                }
                else
                {
                    yc = k1 * p * p * p / 6.0 * (1 - x);
                    dyc_dx = -k1 * p * p * p / 6.0;
                }

                double theta = Math.Atan(dyc_dx);
                upper.Add(new Point2D(x - yt * Math.Sin(theta), yc + yt * Math.Cos(theta)));
                lower.Add(new Point2D(x + yt * Math.Sin(theta), yc - yt * Math.Cos(theta)));
                camber.Add(new Point2D(x, yc));
            }

            return new AerofoilCoords { Upper = upper, Lower = lower, Camber = camber };
        }

        // ── Station table: 20 spanwise stations with t/c, y_upper, y_lower ──
        private static List<StationRow> BuildStationTable(AerofoilCoords coords, double chordMm)
        {
            // Sample at standard NACA report stations
            double[] stations = { 0, 0.5, 1.25, 2.5, 5, 7.5, 10, 15, 20, 25, 30, 40, 50, 60, 70, 80, 90, 95, 100 };
            var table = new List<StationRow>();

            foreach (double xPct in stations)
            {
                double x = xPct / 100.0;
                double yU = Interpolate(coords.Upper, x);
                double yL = Interpolate(coords.Lower, x);
                double yC = Interpolate(coords.Camber, x);
                double tc = yU - yL;

                table.Add(new StationRow
                {
                    XPct = xPct,
                    XMm = Math.Round(x * chordMm, 2),
                    YUpperMm = Math.Round(yU * chordMm, 3),
                    YLowerMm = Math.Round(yL * chordMm, 3),
                    YCamberMm = Math.Round(yC * chordMm, 3),
                    ThicknessMm = Math.Round(tc * chordMm, 3),
                    ThicknessPct = Math.Round(tc * 100, 3)
                });
            }
            return table;
        }

        // ── Key dimensions ─────────────────────────────────────────
        private static ProfileDimensions ComputeDimensions(AerofoilCoords c, double chordMm)
        {
            double maxThick = c.Upper.Zip(c.Lower, (u, l) => u.Y - l.Y).Max();
            int mtIdx = c.Upper.Zip(c.Lower, (u, l) => u.Y - l.Y).ToList()
                                        .IndexOf(maxThick);
            double maxCamber = c.Camber.Max(p => p.Y);
            double mcX = c.Camber.MaxBy(p => p.Y)?.X ?? 0;

            // Leading edge radius approximation (circles fit to first 3 upper pts)
            double leR = EstimateLeadingEdgeRadius(c.Upper);

            return new ProfileDimensions
            {
                ChordMm = chordMm,
                MaxThicknessMm = Math.Round(maxThick * chordMm, 3),
                MaxThicknessPct = Math.Round(maxThick * 100, 2),
                MaxThicknessXPct = Math.Round(c.Upper[mtIdx].X * 100, 1),
                MaxThicknessXMm = Math.Round(c.Upper[mtIdx].X * chordMm, 2),
                MaxCamberMm = Math.Round(maxCamber * chordMm, 3),
                MaxCamberPct = Math.Round(maxCamber * 100, 2),
                MaxCamberXPct = Math.Round(mcX * 100, 1),
                MaxCamberXMm = Math.Round(mcX * chordMm, 2),
                LeadingEdgeRadiusMm = Math.Round(leR * chordMm, 3),
                LeadingEdgeRadiusPct = Math.Round(leR * 100, 3),
                TrailingEdgeThickMm = Math.Round((c.Upper.Last().Y - c.Lower.Last().Y) * chordMm, 3),
                MeanLineAngle = Math.Round(Math.Atan2(maxCamber, mcX) * 180 / Math.PI, 2)
            };
        }

        // ── NACA 4-digit camber-line slope (dyc/dx) ──────────────────
        // Single source of truth for the camber gradient, used by both
        // GenerateNaca4Coordinates (surface geometry) and
        // ComputeNaca4ZeroLiftAngleDeg (thin-aerofoil-theory integral)
        // below, so the two can never silently diverge.
        private static double NacaCamberSlope(double x, double m, double p)
        {
            if (m < 1e-9) return 0;
            return x < p
                ? 2 * m / (p * p) * (p - x)
                : 2 * m / ((1 - p) * (1 - p)) * (p - x);
        }

        // ── Thin-aerofoil-theory zero-lift angle ─────────────────────
        // Replaces the previous one-line shortcut
        //   alpha0 = -(m/p^2) * (p - 0.5) * 180/pi * 2
        // which returned the wrong sign for standard positive-camber
        // values (NACA 4412 came out +2.86 deg). Physically, a
        // positive-cambered section must have alpha_L0 <= 0 — camber
        // keeps the section lifting at zero or slightly negative
        // geometric incidence, it doesn't require positive incidence
        // to reach zero lift.
        //
        // This computes the real formula (Anderson, "Fundamentals of
        // Aerodynamics", Ch.4; Abbott & von Doenhoff, "Theory of Wing
        // Sections", 1959):
        //
        //   alpha_L0 = -(1/pi) * Integral_0^pi [ dyc/dx * (cos(theta0) - 1) ] dtheta0
        //   x = (c/2) * (1 - cos(theta0))     (c = 1, normalised chord)
        //
        // via composite trapezoidal rule (n=200 intervals — smooth,
        // closed-form-derivative integrand, no need for anything fancier),
        // over the SAME camber-line slope (NacaCamberSlope) that generates
        // the aerofoil's actual surface coordinates — not a second,
        // independent camber approximation.
        private static double ComputeNaca4ZeroLiftAngleDeg(double m, double p, int n = 200)
        {
            if (m < 1e-9) return 0; // symmetric profile — no camber, no offset

            double dTheta = Math.PI / n;
            double sum = 0;
            for (int i = 0; i <= n; i++)
            {
                double theta0 = i * dTheta;
                double x = (1 - Math.Cos(theta0)) / 2.0;
                double integrand = NacaCamberSlope(x, m, p) * (Math.Cos(theta0) - 1);
                double weight = (i == 0 || i == n) ? 0.5 : 1.0; // trapezoidal end-weighting
                sum += weight * integrand;
            }
            double integral = sum * dTheta;
            double alphaL0Rad = -(1.0 / Math.PI) * integral;
            return alphaL0Rad * 180.0 / Math.PI;
        }

        // ── Aerodynamic parameters (thin aerofoil theory estimates) ─
        private static AeroParameters ComputeAeroParams(double m, double p, double t, double chordMm)
        {
            // Zero-lift angle: real thin-aerofoil-theory integral (see
            // ComputeNaca4ZeroLiftAngleDeg above) — replaces a one-line
            // shortcut that returned the wrong sign for cambered profiles.
            double alpha0 = ComputeNaca4ZeroLiftAngleDeg(m, p);
            double clAlpha = 2 * Math.PI;  // per radian (thin aerofoil theory)
            double leR = 1.1019 * t * t;  // normalised LE radius

            return new AeroParameters
            {
                DesignLiftCoeff = m < 1e-9 ? 0 : m * 4 * (1 - p),  // rough estimate
                ThicknessRatio = t,
                MaxCamberLocation = p * 100,
                LeadingEdgeRadius = leR * chordMm,
                LeadingEdgeRadiusPct = leR * 100,
                TrailingEdgeAngle = ComputeTeAngle(t),
                LiftCurveSlope = clAlpha,
                ApproxZeroLiftAngle = Math.Round(alpha0, 2),
                ApproxStallAngle = Math.Round(12 + t * 60, 1),
                ApproxMaxCl = Math.Round(1.0 + 3.0 * m + 0.2 * t, 2),
                ApproxMinDrag = Math.Round(0.006 + 0.01 * t, 4),
                ReynoldsRange = $"3×10⁵ – 3×10⁶ (typical axial fan)",
                Note = "Parameters are thin-aerofoil theory estimates. Use CFD or tunnel data for precise values."
            };
        }

        // ── Helpers ────────────────────────────────────────────────
        private static (double m, double p, double t) ParseNaca4(string s)
        {
            s = s.Replace("NACA", "").Replace(" ", "").Trim();
            if (s.Length != 4) throw new ArgumentException($"Expected 4-digit NACA, got: {s}");
            return (
                m: int.Parse(s[0].ToString()) / 100.0,
                p: int.Parse(s[1].ToString()) / 10.0,
                t: int.Parse(s.Substring(2)) / 100.0
            );
        }

        private static (double cl, double p, double t) ParseNaca5(string s)
        {
            s = s.Replace("NACA", "").Replace(" ", "").Trim();
            if (s.Length != 5) throw new ArgumentException($"Expected 5-digit NACA, got: {s}");
            double cl = int.Parse(s[0].ToString()) * 3.0 / 20.0;  // design CL
            double p = int.Parse(s[1].ToString()) / 20.0;         // max camber position
            double t = int.Parse(s.Substring(3)) / 100.0;
            return (cl, p, t);
        }

        private static double ComputeNaca5K1(double p)
        {
            // Lookup table from NACA report (interpolated)
            var table = new[] { (0.05, 361.4), (0.10, 51.64), (0.15, 15.957), (0.20, 6.643), (0.25, 3.230) };
            if (p <= table[0].Item1) return table[0].Item2;
            if (p >= table[^1].Item1) return table[^1].Item2;
            for (int i = 0; i < table.Length - 1; i++)
            {
                if (p >= table[i].Item1 && p <= table[i + 1].Item1)
                {
                    double frac = (p - table[i].Item1) / (table[i + 1].Item1 - table[i].Item1);
                    return table[i].Item2 + frac * (table[i + 1].Item2 - table[i].Item2);
                }
            }
            return 3.230;
        }
        // Resolves a saved BladeProfile entity into full BladeProfileData for a
        // given chord length. Returns null if the profile has no usable geometry
        // definition (e.g. a custom profile with no CoordinateData yet) — callers
        // must treat null as "no profile shape info available," not an error.
        public static BladeProfileData? ResolveProfileData(BladeProfile? bp, double chordMm)
        {
            if (bp == null) return null;

            try
            {
                if (bp.Type == "NACA")
                {
                    return ResolveFromDesignation(bp.Name, chordMm);
                }
                // Custom profiles need real CoordinateData to generate from;
                // if it's missing (e.g. the seeded "Flat plate" entry), there's
                // nothing to build — fall through to the null return below.
            }
            catch { /* malformed designation/coordinate data — same defensive pattern used in AxialFanDrawingService */ }

            return null;
        }

        // Same NACA-designation-string resolution as ResolveProfileData's "NACA"
        // branch above, but for callers that only have a raw designation string
        // on hand — not a saved BladeProfile entity. This exists specifically
        // for CalibrationCase.BladeProfileDesignation (e.g. "NACA 4412"), so a
        // calibration case's actual airfoil can be resolved into real Cl/Cd
        // data instead of silently falling back to a generic flat-plate polar.
        // Returns null for a blank/unassigned designation or anything that
        // doesn't parse as a NACA 4- or 5-digit code — callers must treat null
        // as "no profile shape info available," same as ResolveProfileData.
        public static BladeProfileData? ResolveFromDesignation(string? designation, double chordMm)
        {
            if (string.IsNullOrWhiteSpace(designation)) return null;

            try
            {
                string d = designation.Replace("NACA", "").Replace(" ", "").Trim();
                return d.Length == 5
                    ? GenerateNaca5(d, chordMm)
                    : GenerateNaca4(d, chordMm);
            }
            catch { return null; }
        }
        private static double GetNaca5MaxCamber(double cl, double p)
            => cl * p / 3.0;  // rough approximation

        private static double ComputeTeAngle(double t)
            => Math.Round(2 * Math.Atan(1.16925 * t) * 180 / Math.PI, 2);

        private static double EstimateLeadingEdgeRadius(List<Point2D> upper)
        {
            if (upper.Count < 4) return 0;
            // Fit circle through first 3 points
            var p1 = upper[1]; var p2 = upper[2]; var p3 = upper[3];
            double ax = p1.X, ay = p1.Y, bx = p2.X, by = p2.Y, cx = p3.X, cy = p3.Y;
            double d = 2 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
            if (Math.Abs(d) < 1e-10) return 0.02;
            double ux = ((ax * ax + ay * ay) * (by - cy) + (bx * bx + by * by) * (cy - ay) + (cx * cx + cy * cy) * (ay - by)) / d;
            double uy = ((ax * ax + ay * ay) * (cx - bx) + (bx * bx + by * by) * (ax - cx) + (cx * cx + cy * cy) * (bx - ax)) / d;
            return Math.Sqrt((ax - ux) * (ax - ux) + (ay - uy) * (ay - uy));
        }

        private static double Interpolate(List<Point2D> pts, double x)
        {
            for (int i = 0; i < pts.Count - 1; i++)
            {
                if (pts[i].X <= x && pts[i + 1].X >= x)
                {
                    double t = (x - pts[i].X) / (pts[i + 1].X - pts[i].X + 1e-12);
                    return pts[i].Y + t * (pts[i + 1].Y - pts[i].Y);
                }
            }
            return 0;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Data transfer objects
    // ═══════════════════════════════════════════════════════════════
}
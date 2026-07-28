using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace AxialFanMVC.Business.Cfd
{
    /// <summary>
    /// Generates a blade surface (constant/triSurface/fan.stl) directly from the
    /// design's own calculated numbers (tip radius, hub ratio, blade count, RPM,
    /// axial velocity, and optionally a real 2D airfoil profile). Twist follows a
    /// blade-element law (local angle = atan2(axial velocity, blade speed) at each
    /// radial station) and chord follows a constant-target-solidity distribution —
    /// both computed per-station rather than fixed/constant across the span. This
    /// is still NOT a substitute for a CAD-exported blade: solidity is a single
    /// target value rather than one tuned to a real loading/diffusion-factor
    /// analysis, and the hub itself is not modelled as a solid. It exists so the
    /// CFD pipeline produces a real, geometry-consistent, aerodynamically-plausible
    /// result instead of failing on a missing file — replace with a real STL when
    /// one exists.
    ///
    /// Geometry construction (blade-element convention), verified for
    /// watertightness/outward-normal-orientation before being ported here:
    /// at radius r, on a blade centred at azimuth phi, the local frame is
    ///   eR = (cos phi, sin phi, 0)      radial
    ///   eT = (-sin phi, cos phi, 0)     tangential (rotation direction)
    ///   eZ = (0, 0, 1)                  axial
    /// Chord line c = cos(beta)*eT + sin(beta)*eZ, thickness line t = eR x c,
    /// where beta is the local blade angle from the plane of rotation, varying
    /// per radial station per the blade-element twist law above. N radial
    /// stations (hub to tip) are lofted; both loop ends are capped so each
    /// blade is an independent closed, manifold solid.
    /// </summary>
    public static class BladeStlGenerator
    {
        private readonly struct V3
        {
            public readonly double X, Y, Z;
            public V3(double x, double y, double z) { X = x; Y = y; Z = z; }
            public static V3 operator +(V3 a, V3 b) => new V3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
            public static V3 operator -(V3 a, V3 b) => new V3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
            public static V3 operator *(V3 a, double s) => new V3(a.X * s, a.Y * s, a.Z * s);
            public static V3 Cross(V3 a, V3 b) => new V3(
                a.Y * b.Z - a.Z * b.Y,
                a.Z * b.X - a.X * b.Z,
                a.X * b.Y - a.Y * b.X);
            public double Length() => Math.Sqrt(X * X + Y * Y + Z * Z);
            public V3 Normalized()
            {
                double len = Length();
                return len < 1e-15 ? new V3(0, 0, 1) : new V3(X / len, Y / len, Z / len);
            }
        }

        /// <param name="stlFilePath">Destination, e.g. {casePath}/constant/triSurface/fan.stl</param>
        /// <param name="tipRadiusM">Fan tip radius in metres.</param>
        /// <param name="hubRatio">DesignInput.HubRatio (hub radius / tip radius), 0-1.</param>
        /// <param name="bladeCount">DesignInput.BladeCount.</param>
        /// <param name="bladeAngleDeg">Retained for signature/call-site compatibility.
        /// No longer used to set the blade angle directly — see rpm/axialVelocityMs
        /// below — because a single constant angle for the whole span means only one
        /// radius is aerodynamically correct; the rest are stalled or barely loaded.</param>
        /// <param name="profileCoordinateJson">Optional BladeProfile.CoordinateData — a closed-loop
        /// 2D airfoil outline as either [[x,y],...] or [{"x":..,"y":..},...], chordwise-normalised
        /// 0..1. Null/empty/unparseable falls back to a NACA 4412-family default.</param>
        /// <param name="rpm">DesignInput.SpeedRpm — used with axialVelocityMs to compute the
        /// blade-element twist law (local blade angle vs. radius).</param>
        /// <param name="axialVelocityMs">Inlet axial velocity (matches __VELOCITY_INLET__ /
        /// LocalCfdOrchestrator's velocityMs) — the flow-through speed each station's local
        /// apparent-wind angle is computed against.</param>
        /// <param name="spanStations">Number of radial stations lofted from hub to tip
        /// (2 = old hub/tip-only behaviour; 6 gives a smooth twist curve).</param>
        /// <param name="targetSolidity">chord(r) x bladeCount / (2*pi*r), held constant across
        /// the span. 0.5 is a reasonable mid-range default absent a specific target from a
        /// real fan being matched — replace with a real value if you have one.</param>
        /// <returns>Triangle count written, for the caller to sanity-check.</returns>
        public static int Generate(
            string stlFilePath,
            double tipRadiusM,
            double hubRatio,
            int bladeCount,
            double bladeAngleDeg,
            string? profileCoordinateJson,
            double rpm,
            double axialVelocityMs,
            int spanStations = 6,
            double targetSolidity = 0.5)
        {
            if (tipRadiusM <= 0)
                throw new ArgumentOutOfRangeException(nameof(tipRadiusM), "Tip radius must be positive.");

            double clampedHubRatio = Math.Clamp(hubRatio <= 0 || hubRatio >= 1 ? 0.45 : hubRatio, 0.15, 0.85);
            int blades = bladeCount > 0 ? bladeCount : 6; // DesignInput.BladeCount default
            double hubRadiusM = tipRadiusM * clampedHubRatio;
            double span = tipRadiusM - hubRadiusM;

            if (span <= 0)
                throw new InvalidOperationException(
                    $"Hub radius ({hubRadiusM:F4} m) is not smaller than tip radius ({tipRadiusM:F4} m); check HubRatio.");

            List<(double X, double Y)> loop = TryParseProfile(profileCoordinateJson)
                ?? NacaFourDigitLoop(camber: 0.04, camberPosition: 0.4, thickness: 0.12, halfPointCount: 20);

            int stations = Math.Max(2, spanStations);
            double omegaRadS = Math.Max(rpm, 0.0) * Math.PI / 30.0;
            double axialVel = Math.Max(axialVelocityMs, 0.0);

            var radii = new double[stations];
            var betaRad = new double[stations];
            var chord = new double[stations];
            for (int s = 0; s < stations; s++)
            {
                double frac = (double)s / (stations - 1);
                double r = hubRadiusM + frac * span;
                radii[s] = r;

                // Blade-element twist law: angle the chord to match the local
                // apparent-flow direction, atan2(axial speed, blade speed at this
                // radius). atan2 handles U(r)=0 (rpm=0) cleanly as 90 deg (fully
                // feathered) rather than dividing by zero.
                double u = omegaRadS * r;
                betaRad[s] = Math.Atan2(axialVel, u);

                // Solidity-based chord: chord(r) = sigma * 2*pi*r / bladeCount,
                // constant target solidity across the span. Replaces the old
                // undocumented 0.35/0.6 hub-tip taper rule of thumb.
                chord[s] = targetSolidity * 2.0 * Math.PI * r / blades;
            }

            var triangles = new List<(V3 A, V3 B, V3 C)>();

            for (int k = 0; k < blades; k++)
            {
                double phi = 2.0 * Math.PI * k / blades;
                BuildBlade(radii, chord, betaRad, phi, loop, triangles);
            }

            WriteAsciiStl(stlFilePath, "fan", triangles);
            return triangles.Count;
        }

        private static void BuildBlade(
            double[] radii, double[] chord, double[] betaRad, double phi,
            List<(double X, double Y)> loop, List<(V3 A, V3 B, V3 C)> triangles)
        {
            var eR = new V3(Math.Cos(phi), Math.Sin(phi), 0.0);
            var eT = new V3(-Math.Sin(phi), Math.Cos(phi), 0.0);
            var eZ = new V3(0.0, 0.0, 1.0);
            int n = loop.Count;
            int stations = radii.Length;

            V3[] Station(double r, double ch, double beta)
            {
                // c/t recomputed per station: beta now varies with radius (the
                // twist), so the chord direction rotates station to station —
                // unlike the old two-station version where c/t were constant
                // for the whole blade.
                V3 c = eT * Math.Cos(beta) + eZ * Math.Sin(beta);
                V3 t = V3.Cross(eR, c);
                var pts = new V3[n];
                V3 baseP = eR * r;
                for (int i = 0; i < n; i++)
                {
                    var (xf, yf) = loop[i];
                    pts[i] = baseP + c * (xf * ch) + t * (yf * ch);
                }
                return pts;
            }

            var stationPts = new V3[stations][];
            for (int s = 0; s < stations; s++)
                stationPts[s] = Station(radii[s], chord[s], betaRad[s]);

            // Hub cap (fan triangulation from centroid) — winding verified to
            // point outward (away from tip) for a CCW-ordered airfoil loop.
            V3 hubCentroid = Centroid(stationPts[0]);
            for (int i = 0; i < n; i++)
                triangles.Add((hubCentroid, stationPts[0][i], stationPts[0][(i + 1) % n]));

            // Tip cap — opposite winding, points outward (away from hub).
            V3 tipCentroid = Centroid(stationPts[stations - 1]);
            for (int i = 0; i < n; i++)
                triangles.Add((tipCentroid, stationPts[stations - 1][(i + 1) % n], stationPts[stations - 1][i]));

            // Side loft between each pair of adjacent stations (generalises the
            // old single hub-tip loft to however many stations are lofted).
            for (int s = 0; s < stations - 1; s++)
            {
                V3[] lo = stationPts[s];
                V3[] hi = stationPts[s + 1];
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    triangles.Add((lo[i], hi[j], lo[j]));
                    triangles.Add((lo[i], hi[i], hi[j]));
                }
            }
        }

        private static V3 Centroid(V3[] pts)
        {
            double x = 0, y = 0, z = 0;
            foreach (var p in pts) { x += p.X; y += p.Y; z += p.Z; }
            return new V3(x / pts.Length, y / pts.Length, z / pts.Length);
        }

        /// <summary>
        /// Standard NACA 4-digit closed-loop outline (upper surface LE-&gt;TE,
        /// then lower surface TE-&gt;LE), cosine-spaced for leading-edge
        /// resolution. Default camber/thickness (0.04, 0.4, 0.12 == "4412"
        /// family) is a generic cambered section, not tuned to this design.
        /// </summary>
        internal static List<(double X, double Y)> NacaFourDigitLoop(
            double camber, double camberPosition, double thickness, int halfPointCount)
        {
            var xs = new double[halfPointCount + 1];
            for (int i = 0; i <= halfPointCount; i++)
                xs[i] = 0.5 * (1 - Math.Cos(Math.PI * i / halfPointCount));

            double Yt(double x) => 5 * thickness * (
                0.2969 * Math.Sqrt(x) - 0.1260 * x - 0.3516 * x * x + 0.2843 * x * x * x - 0.1015 * x * x * x * x);

            (double Yc, double DYc) Camber(double x)
            {
                if (camber == 0 || camberPosition == 0) return (0.0, 0.0);
                if (x < camberPosition)
                {
                    double yc = camber / (camberPosition * camberPosition) * (2 * camberPosition * x - x * x);
                    double dyc = 2 * camber / (camberPosition * camberPosition) * (camberPosition - x);
                    return (yc, dyc);
                }
                else
                {
                    double p1 = 1 - camberPosition;
                    double yc = camber / (p1 * p1) * ((1 - 2 * camberPosition) + 2 * camberPosition * x - x * x);
                    double dyc = 2 * camber / (p1 * p1) * (camberPosition - x);
                    return (yc, dyc);
                }
            }

            var upper = new List<(double, double)>();
            var lower = new List<(double, double)>();
            foreach (var x in xs)
            {
                var (yc, dyc) = Camber(x);
                double theta = Math.Atan(dyc);
                double yt = Yt(x);
                upper.Add((x - yt * Math.Sin(theta), yc + yt * Math.Cos(theta)));
                lower.Add((x + yt * Math.Sin(theta), yc - yt * Math.Cos(theta)));
            }

            var loop = new List<(double, double)>(upper);
            for (int i = lower.Count - 2; i >= 1; i--) // skip duplicate TE/LE endpoints
                loop.Add(lower[i]);
            return loop;
        }

        /// <summary>
        /// Parses BladeProfile.CoordinateData as either [[x,y],...] or
        /// [{"x":..,"y":..},...]. Returns null (caller falls back to NACA4)
        /// on missing/empty/malformed input rather than throwing — a bad
        /// stored profile shouldn't block the whole pipeline.
        /// </summary>
        internal static List<(double X, double Y)>? TryParseProfile(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

                var pts = new List<(double, double)>();
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.Array && el.GetArrayLength() >= 2)
                    {
                        pts.Add((el[0].GetDouble(), el[1].GetDouble()));
                    }
                    else if (el.ValueKind == JsonValueKind.Object)
                    {
                        double x = TryGetCaseInsensitive(el, "x");
                        double y = TryGetCaseInsensitive(el, "y");
                        pts.Add((x, y));
                    }
                    else
                    {
                        return null; // unrecognised element shape
                    }
                }

                return pts.Count >= 3 ? pts : null; // need a real polygon
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static double TryGetCaseInsensitive(JsonElement obj, string name)
        {
            foreach (var prop in obj.EnumerateObject())
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                    return prop.Value.GetDouble();
            throw new JsonException($"Missing \"{name}\" field in profile point.");
        }

        private static void WriteAsciiStl(string path, string solidName, List<(V3 A, V3 B, V3 C)> triangles)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var sb = new StringBuilder();
            sb.Append("solid ").Append(solidName).Append('\n');

            foreach (var (a, b, c) in triangles)
            {
                V3 normal = V3.Cross(b - a, c - a).Normalized();
                sb.Append("  facet normal ")
                  .Append(F(normal.X)).Append(' ').Append(F(normal.Y)).Append(' ').Append(F(normal.Z)).Append('\n');
                sb.Append("    outer loop\n");
                AppendVertex(sb, a);
                AppendVertex(sb, b);
                AppendVertex(sb, c);
                sb.Append("    endloop\n");
                sb.Append("  endfacet\n");
            }

            sb.Append("endsolid ").Append(solidName).Append('\n');
            File.WriteAllText(path, sb.ToString());
        }

        private static void AppendVertex(StringBuilder sb, V3 v)
        {
            sb.Append("      vertex ").Append(F(v.X)).Append(' ').Append(F(v.Y)).Append(' ').Append(F(v.Z)).Append('\n');
        }

        private static string F(double v) => v.ToString("G7", CultureInfo.InvariantCulture);
    }
}
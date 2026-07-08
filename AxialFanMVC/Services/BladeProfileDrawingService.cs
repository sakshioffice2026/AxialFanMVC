using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AxialFanMVC.Models;

namespace AxialFanMVC.Services
{
    // ═══════════════════════════════════════════════════════════════
    // BladeProfileDrawingService
    //
    // Generates precision SVG drawings for a blade profile:
    //   - Aerofoil outline (upper + lower surface)
    //   - Camber line (dashed)
    //   - Chord line
    //   - Full dimension lines (chord, max thickness, LE radius, etc.)
    //   - Station ordinate ticks at standard NACA stations
    //   - Dimension table annotation in margin
    //   - Title block with designation and chord
    // ═══════════════════════════════════════════════════════════════
    public static class BladeProfileDrawingService
    {
        // ── Main drawing entry points ─────────────────────────────

        public static string DrawAerofoilProfile(BladeProfileData profile)
            => BuildProfileSvg(profile);

        public static string DrawStationDiagram(BladeProfileData profile)
            => BuildStationSvg(profile);

        // ─────────────────────────────────────────────────────────
        // Aerofoil profile drawing (DWG-003)
        // Width: 820px  Height: 460px
        // Drawing area: x=60..760, y=60..340  →  700 × 280 px
        // Chord occupies full 700px width
        // ─────────────────────────────────────────────────────────
        private static string BuildProfileSvg(BladeProfileData p)
        {
            const double svgW  = 820;
            const double svgH  = 480;
            const double dLeft = 60;   // drawing area left
            const double dTop  = 80;   // drawing area top
            const double dW    = 700;  // drawing width (= chord)
            const double dH    = 200;  // half height above chord line
            double cy = dTop + dH;     // chord line y

            // Scale: chord normalised to 700px
            double sc = dW;

            Func<double, double> px = x => dLeft + x * sc;
            Func<double, double> py = y => cy - y * sc;   // y flipped (positive = up)

            var sb = new StringBuilder();
            sb.AppendLine($@"<svg xmlns=""http://www.w3.org/2000/svg"" width=""{svgW}"" height=""{svgH}"" viewBox=""0 0 {svgW} {svgH}"" font-family=""sans-serif"">");
            sb.AppendLine(@"<rect width=""100%"" height=""100%"" fill=""white""/>");

            // ── Chord line ─────────────────────────────────────────
            sb.AppendLine($@"<line x1=""{dLeft}"" y1=""{cy}"" x2=""{dLeft+dW}"" y2=""{cy}"" stroke=""#aaa"" stroke-width=""0.5"" stroke-dasharray=""8 4""/>");

            // ── Station ordinate ticks ─────────────────────────────
            double[] stations = { 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9 };
            foreach (double x in stations)
            {
                double tx = px(x);
                sb.AppendLine($@"<line x1=""{tx:F1}"" y1=""{cy-4}"" x2=""{tx:F1}"" y2=""{cy+4}"" stroke=""#ccc"" stroke-width=""0.5""/>");
                sb.AppendLine($@"<text x=""{tx:F1}"" y=""{cy+14}"" font-size=""8"" text-anchor=""middle"" fill=""#bbb"">{x*100:F0}%</text>");
            }

            // ── Camber line (dashed blue) ──────────────────────────
            var camberPath = "M " + string.Join(" L ",
                p.CamberLine.Select(pt => $"{px(pt.X):F2},{py(pt.Y):F2}"));
            sb.AppendLine($@"<path d=""{camberPath}"" fill=""none"" stroke=""#185FA5"" stroke-width=""1"" stroke-dasharray=""5 3"" opacity=""0.6""/>");

            // ── Upper surface ──────────────────────────────────────
            var upperPath = "M " + string.Join(" L ",
                p.UpperSurface.Select(pt => $"{px(pt.X):F2},{py(pt.Y):F2}"));
            sb.AppendLine($@"<path d=""{upperPath}"" fill=""none"" stroke=""#1a1a1a"" stroke-width=""2""/>");

            // ── Lower surface ──────────────────────────────────────
            var lowerPath = "M " + string.Join(" L ",
                p.LowerSurface.Select(pt => $"{px(pt.X):F2},{py(pt.Y):F2}"));
            sb.AppendLine($@"<path d=""{lowerPath}"" fill=""none"" stroke=""#1a1a1a"" stroke-width=""2""/>");

            // ── Fill aerofoil ──────────────────────────────────────
            var fillPath = "M " + string.Join(" L ",
                p.UpperSurface.Select(pt => $"{px(pt.X):F2},{py(pt.Y):F2}"))
              + " L " + string.Join(" L ",
                p.LowerSurface.AsEnumerable().Reverse().Select(pt => $"{px(pt.X):F2},{py(pt.Y):F2}"))
              + " Z";
            sb.AppendLine($@"<path d=""{fillPath}"" fill=""rgba(24,95,165,0.07)"" stroke=""none""/>");

            // ── Max thickness dimension line ───────────────────────
            var d = p.Dimensions;
            double mtx = px(d.MaxThicknessXPct / 100.0);
            double mtu = py( p.UpperSurface.OrderBy(q => Math.Abs(q.X - d.MaxThicknessXPct/100.0)).First().Y);
            double mtl = py(-p.UpperSurface.OrderBy(q => Math.Abs(q.X - d.MaxThicknessXPct/100.0)).First().Y);
            // use interpolated lower
            double mtlY = py(p.LowerSurface.OrderBy(q => Math.Abs(q.X - d.MaxThicknessXPct/100.0)).First().Y);
            sb.AppendLine($@"<line x1=""{mtx:F1}"" y1=""{mtu:F1}"" x2=""{mtx:F1}"" y2=""{mtlY:F1}"" stroke=""#c00"" stroke-width=""0.8"" stroke-dasharray=""3 2""/>");
            sb.AppendLine($@"<text x=""{mtx+4:F1}"" y=""{(mtu+mtlY)/2+4:F1}"" font-size=""9"" fill=""#c00"">{d.MaxThicknessMm:F2}mm ({d.MaxThicknessPct:F1}%c)</text>");

            // ── Max camber dimension line ──────────────────────────
            double mcx = px(d.MaxCamberXPct / 100.0);
            double mcyU = py(d.MaxCamberMm / p.ChordMm);
            sb.AppendLine($@"<line x1=""{mcx:F1}"" y1=""{cy}"" x2=""{mcx:F1}"" y2=""{mcyU:F1}"" stroke=""#185FA5"" stroke-width=""0.8"" stroke-dasharray=""3 2""/>");
            sb.AppendLine($@"<text x=""{mcx+4:F1}"" y=""{(cy+mcyU)/2:F1}"" font-size=""9"" fill=""#185FA5"">{d.MaxCamberMm:F2}mm ({d.MaxCamberPct:F1}%c)</text>");

            // ── Chord dimension (horizontal) ───────────────────────
            double dimY = dTop + dH * 2 + 32;
            sb.AppendLine($@"<line x1=""{dLeft}"" y1=""{dimY}"" x2=""{dLeft+dW}"" y2=""{dimY}"" stroke=""#444"" stroke-width=""0.7"" marker-start=""url(#ds3)"" marker-end=""url(#de3)""/>");
            sb.AppendLine($@"<text x=""{dLeft+dW/2:F0}"" y=""{dimY+13}"" font-size=""10"" text-anchor=""middle"" fill=""#333"">Chord = {p.ChordMm:F1} mm</text>");

            // ── Leading edge radius callout ────────────────────────
            sb.AppendLine($@"<circle cx=""{dLeft:F1}"" cy=""{cy:F1}"" r=""{d.LeadingEdgeRadiusMm*sc/p.ChordMm:F2}"" fill=""none"" stroke=""#e67e00"" stroke-width=""0.8"" stroke-dasharray=""3 2""/>");
            sb.AppendLine($@"<text x=""{dLeft+10}"" y=""{cy-18}"" font-size=""9"" fill=""#e67e00"">r_LE = {d.LeadingEdgeRadiusMm:F3}mm ({d.LeadingEdgeRadiusPct:F3}%c)</text>");

            // ── Dimension markers defs ─────────────────────────────
            sb.AppendLine(@"<defs>
  <marker id=""ds3"" viewBox=""0 0 8 8"" refX=""1"" refY=""4"" markerWidth=""5"" markerHeight=""5"" orient=""auto""><path d=""M7 1L1 4L7 7"" fill=""none"" stroke=""#444"" stroke-width=""1""/></marker>
  <marker id=""de3"" viewBox=""0 0 8 8"" refX=""7"" refY=""4"" markerWidth=""5"" markerHeight=""5"" orient=""auto""><path d=""M1 1L7 4L1 7"" fill=""none"" stroke=""#444"" stroke-width=""1""/></marker>
</defs>");

            // ── Annotation legend ──────────────────────────────────
            sb.AppendLine($@"<rect x=""625"" y=""72"" width=""180"" height=""88"" rx=""4"" fill=""#f8f9fa"" stroke=""#ddd"" stroke-width=""0.5""/>");
            sb.AppendLine($@"<text x=""635"" y=""88"" font-size=""9.5"" font-weight=""600"" fill=""#185FA5"">{p.Designation}</text>");
            sb.AppendLine($@"<text x=""635"" y=""102"" font-size=""9"" fill=""#555"">Max thickness: {d.MaxThicknessPct:F1}% c at {d.MaxThicknessXPct:F1}% c</text>");
            sb.AppendLine($@"<text x=""635"" y=""116"" font-size=""9"" fill=""#555"">Max camber: {d.MaxCamberPct:F1}% c at {d.MaxCamberXPct:F1}% c</text>");
            sb.AppendLine($@"<text x=""635"" y=""130"" font-size=""9"" fill=""#555"">LE radius: {d.LeadingEdgeRadiusPct:F3}% c</text>");
            sb.AppendLine($@"<text x=""635"" y=""144"" font-size=""9"" fill=""#555"">TE angle: {p.AeroParams.TrailingEdgeAngle:F2}°</text>");
            sb.AppendLine($@"<text x=""635"" y=""158"" font-size=""9"" fill=""#555"">Chord: {p.ChordMm:F1} mm</text>");

            // ── Legend lines ───────────────────────────────────────
            sb.AppendLine($@"<line x1=""10"" y1=""160"" x2=""40"" y2=""160"" stroke=""#1a1a1a"" stroke-width=""2""/><text x=""45"" y=""164"" font-size=""8.5"" fill=""#333"">Surface</text>");
            sb.AppendLine($@"<line x1=""10"" y1=""172"" x2=""40"" y2=""172"" stroke=""#185FA5"" stroke-width=""1"" stroke-dasharray=""5 3""/><text x=""45"" y=""176"" font-size=""8.5"" fill=""#333"">Camber line</text>");
            sb.AppendLine($@"<line x1=""10"" y1=""184"" x2=""40"" y2=""184"" stroke=""#aaa"" stroke-width=""0.5"" stroke-dasharray=""8 4""/><text x=""45"" y=""188"" font-size=""8.5"" fill=""#333"">Chord line</text>");

            // ── Title block ────────────────────────────────────────
            sb.AppendLine($@"<rect x=""0"" y=""440"" width=""{svgW}"" height=""40"" fill=""#f0f4fa"" stroke=""#bbb"" stroke-width=""0.5""/>");
            sb.AppendLine(@"<line x1=""0"" y1=""440"" x2=""820"" y2=""440"" stroke=""#999"" stroke-width=""1""/>");
            sb.AppendLine($@"<text x=""12"" y=""456"" font-size=""10"" font-weight=""bold"" fill=""#185FA5"">DWG-003  BLADE PROFILE — {p.Designation}</text>");
            sb.AppendLine($@"<text x=""12"" y=""470"" font-size=""9"" fill=""#555"">Chord {p.ChordMm:F1}mm · Max t/c {p.MaxThicknessPct:F1}% · Camber {p.MaxCamberPct:F1}% at {p.MaxCamberPos:F0}% chord · Type: {p.Type}</text>");
            sb.AppendLine($@"<text x=""800"" y=""456"" font-size=""9"" text-anchor=""end"" fill=""#777"">AxialFlow Designer</text>");
            sb.AppendLine($@"<text x=""800"" y=""470"" font-size=""9"" text-anchor=""end"" fill=""#777"">{DateTime.UtcNow:yyyy-MM-dd}</text>");

            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────────
        // Station ordinate table drawing (DWG-004)
        // Tabulates y/c values at 19 standard NACA chord stations
        // ─────────────────────────────────────────────────────────
        private static string BuildStationSvg(BladeProfileData p)
        {
            const double svgW = 760;
            const double svgH = 520;
            var sb = new StringBuilder();
            sb.AppendLine($@"<svg xmlns=""http://www.w3.org/2000/svg"" width=""{svgW}"" height=""{svgH}"" viewBox=""0 0 {svgW} {svgH}"" font-family=""sans-serif"">");
            sb.AppendLine(@"<rect width=""100%"" height=""100%"" fill=""white""/>");

            // Header
            sb.AppendLine($@"<text x=""380"" y=""28"" font-size=""13"" font-weight=""bold"" text-anchor=""middle"" fill=""#185FA5"">Station ordinate table — {p.Designation}</text>");
            sb.AppendLine($@"<text x=""380"" y=""44"" font-size=""10"" text-anchor=""middle"" fill=""#666"">Chord = {p.ChordMm:F1} mm · All y values in mm</text>");

            // Table
            double tx = 30, ty = 62;
            double[] colW = { 60, 72, 72, 72, 72, 72, 72, 72 };
            string[] headers = { "x/c (%)", "x (mm)", "y_upper", "y_lower", "y_camber", "t (mm)", "t/c (%)", "Sym." };
            string[] hFills  = { "#E6F1FB", "#E6F1FB", "#EAF3DE", "#EAF3DE", "#FAEEDA", "#FCEBEB", "#FCEBEB", "#F1EFE8" };
            string[] hText   = { "#0C447C", "#0C447C", "#27500A", "#27500A", "#633806", "#791F1F", "#791F1F", "#444441" };

            // Draw header row
            double cx2 = tx;
            for (int col = 0; col < headers.Length; col++)
            {
                sb.AppendLine($@"<rect x=""{cx2}"" y=""{ty}"" width=""{colW[col]}"" height=""20"" fill=""{hFills[col]}"" stroke=""#bbb"" stroke-width=""0.5""/>");
                sb.AppendLine($@"<text x=""{cx2 + colW[col]/2:F1}"" y=""{ty+13}"" font-size=""9"" font-weight=""600"" text-anchor=""middle"" fill=""{hText[col]}"">{headers[col]}</text>");
                cx2 += colW[col];
            }
            ty += 20;

            // Data rows
            for (int i = 0; i < p.StationTable.Count; i++)
            {
                var row = p.StationTable[i];
                bool isSymm = Math.Abs(row.YUpperMm + row.YLowerMm) < 0.01;
                string rowFill = i % 2 == 0 ? "#ffffff" : "#f8f9fb";

                string[] vals = {
                    $"{row.XPct:F2}",
                    $"{row.XMm:F2}",
                    $"{row.YUpperMm:F3}",
                    $"{row.YLowerMm:F3}",
                    $"{row.YCamberMm:F3}",
                    $"{row.ThicknessMm:F3}",
                    $"{row.ThicknessPct:F3}",
                    isSymm ? "●" : "○"
                };

                cx2 = tx;
                for (int col = 0; col < vals.Length; col++)
                {
                    sb.AppendLine($@"<rect x=""{cx2}"" y=""{ty}"" width=""{colW[col]}"" height=""18"" fill=""{rowFill}"" stroke=""#ddd"" stroke-width=""0.5""/>");
                    string fc = col == 6
                        ? (row.ThicknessPct > p.MaxThicknessPct * 0.98 ? "#185FA5" : "#333")
                        : "#333";
                    sb.AppendLine($@"<text x=""{cx2 + colW[col]/2:F1}"" y=""{ty+12}"" font-size=""8.5"" text-anchor=""middle"" fill=""{fc}"">{vals[col]}</text>");
                    cx2 += colW[col];
                }
                ty += 18;
            }

            // Peak thickness marker
            sb.AppendLine($@"<text x=""30"" y=""{ty+14}"" font-size=""8.5"" fill=""#185FA5"">● Blue = max thickness station  ({p.Dimensions.MaxThicknessXPct:F1}% chord)</text>");

            // Title block
            sb.AppendLine($@"<rect x=""0"" y=""480"" width=""{svgW}"" height=""40"" fill=""#f0f4fa"" stroke=""#bbb"" stroke-width=""0.5""/>");
            sb.AppendLine($@"<line x1=""0"" y1=""480"" x2=""{svgW}"" y2=""480"" stroke=""#999"" stroke-width=""1""/>");
            sb.AppendLine($@"<text x=""12"" y=""496"" font-size=""10"" font-weight=""bold"" fill=""#185FA5"">DWG-004  ORDINATE TABLE — {p.Designation}</text>");
            sb.AppendLine($@"<text x=""12"" y=""510"" font-size=""9"" fill=""#555"">{p.StationTable.Count} stations · Chord {p.ChordMm:F1}mm · t/c {p.MaxThicknessPct:F1}% · Camber {p.MaxCamberPct:F1}%</text>");
            sb.AppendLine($@"<text x=""{svgW-10}"" y=""496"" font-size=""9"" text-anchor=""end"" fill=""#777"">AxialFlow Designer</text>");
            sb.AppendLine($@"<text x=""{svgW-10}"" y=""510"" font-size=""9"" text-anchor=""end"" fill=""#777"">{DateTime.UtcNow:yyyy-MM-dd}</text>");

            sb.AppendLine("</svg>");
            return sb.ToString();
        }
    }
}

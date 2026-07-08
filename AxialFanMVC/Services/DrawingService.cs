using AxialFanMVC.Database;
namespace AxialFanMVC.Services;

/// <summary>
/// Generates SVG engineering drawings for axial fan designs.
/// DWG-001 — Front Elevation  (face-on view)
/// DWG-002 — Cross Section    (side cut view)
/// DWG-003 — Blade Profile    (aerofoil section)
/// </summary>
public static class DrawingService
{
    // ── Public Entry Point ────────────────────────────────────────
    public static List<Drawing> GenerateAll(DesignResult result)
    {
        var input = result.DesignInput;

        return new List<Drawing>
        {
            new Drawing
            {
                DesignResultId = result.Id,
                DrawingType    = "front_elevation",
                SvgData        = FrontElevation(input, result)
            },
            new Drawing
            {
                DesignResultId = result.Id,
                DrawingType    = "cross_section",
                SvgData        = CrossSection(input, result)
            },
            new Drawing
            {
                DesignResultId = result.Id,
                DrawingType    = "blade_profile",
                SvgData        = BladeProfile(input, result)
            }
        };
    }

    // ─────────────────────────────────────────────────────────────
    // DWG-001 — Front Elevation
    // Face-on ring view showing blades arranged around hub
    // ─────────────────────────────────────────────────────────────
    private static string FrontElevation(DesignInput d, DesignResult r)
    {
        const int W = 500, H = 520;
        const int cx = 250, cy = 260;

        double tipR = 190.0;
        double hubR = tipR * d.HubRatio;
        double spanPx = tipR - hubR;
        int blades = d.BladeCount;
        double bladeW = Math.Min(14, spanPx * 0.18);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($@"<svg viewBox=""0 0 {W} {H}""
    xmlns=""http://www.w3.org/2000/svg""
    style=""background:#f8f9fa;
           border:1px solid #dee2e6;
           border-radius:6px;
           max-width:100%;"">
  <defs>
    <marker id=""arrowFE"" markerWidth=""8"" markerHeight=""8""
            refX=""4"" refY=""3"" orient=""auto"">
      <path d=""M0,0 L0,6 L8,3 z"" fill=""#555""/>
    </marker>
  </defs>

  <!-- Title block -->
  <rect x=""0"" y=""0"" width=""{W}"" height=""30""
        fill=""#1864ab""/>
  <text x=""10"" y=""20"" font-family=""Arial,sans-serif""
        font-size=""12"" font-weight=""bold"" fill=""white"">
    DWG-001  FRONT ELEVATION
  </text>
  <text x=""{W - 10}"" y=""20"" font-family=""Arial,monospace""
        font-size=""9"" fill=""#adb5bd""
        text-anchor=""end"">
    D={d.TipDiameterMm:F0}mm  Z={blades}  β={d.BladeAngleDeg:F1}°
  </text>

  <!-- Outer casing -->
  <circle cx=""{cx}"" cy=""{cy}"" r=""{tipR + 15}""
          fill=""#e9ecef"" stroke=""#868e96""
          stroke-width=""10""/>

  <!-- Tip clearance ring -->
  <circle cx=""{cx}"" cy=""{cy}"" r=""{tipR + 3}""
          fill=""none"" stroke=""#ffe066""
          stroke-width=""3"" opacity=""0.8""/>

  <!-- Tip diameter reference circle -->
  <circle cx=""{cx}"" cy=""{cy}"" r=""{tipR}""
          fill=""none"" stroke=""#ced4da""
          stroke-width=""1"" stroke-dasharray=""5,3""/>
");

        // Draw blades
        for (int i = 0; i < blades; i++)
        {
            double angleDeg = i * 360.0 / blades - 90.0;
            double angleRad = angleDeg * Math.PI / 180.0;
            double midR = (hubR + tipR) / 2.0;
            double bx = cx + midR * Math.Cos(angleRad);
            double by = cy + midR * Math.Sin(angleRad);
            double rot = angleDeg + 90.0 + d.BladeAngleDeg;

            sb.AppendLine($@"  <rect
    x=""{bx - bladeW / 2:F1}"" y=""{by - spanPx / 2:F1}""
    width=""{bladeW:F1}"" height=""{spanPx:F1}""
    rx=""2""
    fill=""#4dabf7"" stroke=""#1971c2"" stroke-width=""1.2""
    opacity=""0.9""
    transform=""rotate({rot:F1},{bx:F1},{by:F1})""/>");
        }

        // Hub circle
        sb.AppendLine($@"
  <!-- Hub -->
  <circle cx=""{cx}"" cy=""{cy}"" r=""{hubR:F1}""
          fill=""#dee2e6"" stroke=""#495057"" stroke-width=""2""/>

  <!-- Hub centre cross -->
  <line x1=""{cx - 14}"" y1=""{cy}"" x2=""{cx + 14}"" y2=""{cy}""
        stroke=""#495057"" stroke-width=""2""/>
  <line x1=""{cx}"" y1=""{cy - 14}"" x2=""{cx}"" y2=""{cy + 14}""
        stroke=""#495057"" stroke-width=""2""/>
  <circle cx=""{cx}"" cy=""{cy}"" r=""5""
          fill=""#495057""/>

  <!-- Tip diameter dimension -->
  <line x1=""{cx - tipR:F1}"" y1=""{cy + tipR + 28:F1}""
        x2=""{cx + tipR:F1}"" y2=""{cy + tipR + 28:F1}""
        stroke=""#555"" stroke-width=""1""
        marker-start=""url(#arrowFE)""
        marker-end=""url(#arrowFE)""/>
  <text x=""{cx}"" y=""{cy + tipR + 42:F1}""
        text-anchor=""middle""
        font-family=""Arial,monospace"" font-size=""11""
        fill=""#333"">
    ⌀ {d.TipDiameterMm:F0} mm
  </text>

  <!-- Hub diameter dimension -->
  <line x1=""{cx - hubR:F1}"" y1=""{cy - hubR - 12:F1}""
        x2=""{cx + hubR:F1}"" y2=""{cy - hubR - 12:F1}""
        stroke=""#868e96"" stroke-width=""1""
        stroke-dasharray=""4,2""
        marker-start=""url(#arrowFE)""
        marker-end=""url(#arrowFE)""/>
  <text x=""{cx}"" y=""{cy - hubR - 18:F1}""
        text-anchor=""middle""
        font-family=""Arial,monospace"" font-size=""9""
        fill=""#666"">
    ⌀ hub {r.HubDiameterMm:F0} mm
  </text>
</svg>");

        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────────
    // DWG-002 — Cross Section (Side View)
    // ─────────────────────────────────────────────────────────────
    private static string CrossSection(DesignInput d, DesignResult r)
    {
        const int W = 580, H = 340;
        const int mL = 70, mT = 50, mR = 50, mB = 60;

        int drawW = W - mL - mR;
        int drawH = H - mT - mB;
        double tipR = drawH / 2.0;
        double hubR = tipR * d.HubRatio;
        double span = tipR - hubR;
        double axLen = drawW * 0.65;
        double startX = mL + drawW * 0.18;
        double endX = startX + axLen;
        double centY = mT + drawH / 2.0;

        // chord in px
        double chordScale = axLen / (d.TipDiameterMm * 1.5);
        double chordPx = Math.Min(r.ChordLengthMm * chordScale, axLen * 0.35);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($@"<svg viewBox=""0 0 {W} {H}""
    xmlns=""http://www.w3.org/2000/svg""
    style=""background:#f8f9fa;
           border:1px solid #dee2e6;
           border-radius:6px;
           max-width:100%;"">
  <defs>
    <marker id=""arrCS"" markerWidth=""7"" markerHeight=""7""
            refX=""3"" refY=""3"" orient=""auto"">
      <path d=""M0,0 L0,6 L7,3 z"" fill=""#555""/>
    </marker>
    <pattern id=""hatch"" width=""6"" height=""6""
             patternUnits=""userSpaceOnUse""
             patternTransform=""rotate(45)"">
      <line x1=""0"" y1=""0"" x2=""0"" y2=""6""
            stroke=""#adb5bd"" stroke-width=""1.2""/>
    </pattern>
  </defs>

  <!-- Title block -->
  <rect x=""0"" y=""0"" width=""{W}"" height=""30""
        fill=""#1864ab""/>
  <text x=""10"" y=""20"" font-family=""Arial,sans-serif""
        font-size=""12"" font-weight=""bold"" fill=""white"">
    DWG-002  CROSS SECTION — SIDE VIEW
  </text>
  <text x=""{W - 10}"" y=""20""
        font-family=""Arial,monospace""
        font-size=""9"" fill=""#adb5bd"" text-anchor=""end"">
    Span={r.BladeSpanMm:F1}mm  Chord={r.ChordLengthMm:F1}mm
  </text>

  <!-- Casing walls -->
  <rect x=""{startX - 12:F1}"" y=""{centY - tipR - 16:F1}""
        width=""{axLen + 24:F1}"" height=""16""
        fill=""url(#hatch)"" stroke=""#868e96"" stroke-width=""1""/>
  <rect x=""{startX - 12:F1}"" y=""{centY + tipR:F1}""
        width=""{axLen + 24:F1}"" height=""16""
        fill=""url(#hatch)"" stroke=""#868e96"" stroke-width=""1""/>

  <!-- Tip clearance highlight -->
  <rect x=""{startX:F1}"" y=""{centY - tipR - 16:F1}""
        width=""{axLen:F1}""
        height=""{r.TipClearanceMm * 0.8:F1}""
        fill=""#ffe066"" opacity=""0.7""/>

  <!-- Flow direction arrow -->
  <line x1=""{mL - 40}"" y1=""{centY}""
        x2=""{startX - 5:F1}"" y2=""{centY}""
        stroke=""#1971c2"" stroke-width=""2""
        marker-end=""url(#arrCS)""/>
  <text x=""{mL - 38}"" y=""{centY - 6}""
        font-family=""Arial,sans-serif""
        font-size=""10"" fill=""#1971c2"" font-weight=""bold"">
    Q →
  </text>

  <!-- Centreline -->
  <line x1=""{startX - 25:F1}"" y1=""{centY}""
        x2=""{endX + 25:F1}"" y2=""{centY}""
        stroke=""#adb5bd"" stroke-width=""1""
        stroke-dasharray=""8,4""/>

  <!-- Hub cylinder -->
  <rect x=""{startX:F1}"" y=""{centY - hubR:F1}""
        width=""{axLen:F1}"" height=""{hubR * 2:F1}""
        fill=""#dee2e6"" stroke=""#495057"" stroke-width=""1.5""/>

  <!-- Blade (simplified aerofoil rectangle) -->
  <rect x=""{startX + axLen * 0.38:F1}""
        y=""{centY - tipR:F1}""
        width=""{chordPx:F1}""
        height=""{span:F1}""
        rx=""3""
        fill=""#74c0fc"" stroke=""#1971c2""
        stroke-width=""1.5"" opacity=""0.88""
        transform=""rotate({-d.BladeAngleDeg * 0.3:F1},
                   {startX + axLen * 0.38 + chordPx / 2:F1},
                   {centY - hubR:F1})""/>

  <!-- Tip diameter annotation -->
  <line x1=""{endX + 18:F1}"" y1=""{centY - tipR:F1}""
        x2=""{endX + 18:F1}"" y2=""{centY + tipR:F1}""
        stroke=""#555"" stroke-width=""1""
        marker-start=""url(#arrCS)""
        marker-end=""url(#arrCS)""/>
  <text x=""{endX + 23:F1}"" y=""{centY + 4}""
        font-family=""Arial,monospace""
        font-size=""10"" fill=""#333"">
    ⌀{d.TipDiameterMm:F0}
  </text>

  <!-- Hub diameter annotation -->
  <line x1=""{startX - 18:F1}"" y1=""{centY - hubR:F1}""
        x2=""{startX - 18:F1}"" y2=""{centY + hubR:F1}""
        stroke=""#868e96"" stroke-width=""1""
        stroke-dasharray=""4,2""
        marker-start=""url(#arrCS)""
        marker-end=""url(#arrCS)""/>
  <text x=""{startX - 60:F1}"" y=""{centY + 4}""
        font-family=""Arial,monospace""
        font-size=""9"" fill=""#666"">
    ⌀{r.HubDiameterMm:F0}
  </text>

  <!-- Blade span annotation -->
  <line x1=""{startX + axLen * 0.08:F1}""
        y1=""{centY - tipR:F1}""
        x2=""{startX + axLen * 0.08:F1}""
        y2=""{centY - hubR:F1}""
        stroke=""#2f9e44"" stroke-width=""1.2""
        marker-start=""url(#arrCS)""
        marker-end=""url(#arrCS)""/>
  <text x=""{startX + axLen * 0.08 + 4:F1}""
        y=""{centY - (tipR + hubR) / 2 + 4:F1}""
        font-family=""Arial,monospace""
        font-size=""9"" fill=""#2f9e44"">
    span {r.BladeSpanMm:F1}mm
  </text>

  <!-- Tip clearance label -->
  <text x=""{startX + 4:F1}""
        y=""{centY - tipR - 3:F1}""
        font-family=""Arial,monospace""
        font-size=""8"" fill=""#e67700"">
    gap {r.TipClearanceMm:F1}mm
  </text>
</svg>");

        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────────
    // DWG-003 — Blade Profile (Aerofoil Section)
    // NACA 4412 normalised coordinates
    // ─────────────────────────────────────────────────────────────
    private static string BladeProfile(DesignInput d, DesignResult r)
    {
        const int W = 540, H = 260;

        // NACA 4412 — 21 stations 0..1
        double[] xCoords = {
            0, 0.05, 0.10, 0.15, 0.20, 0.25, 0.30,
            0.35, 0.40, 0.45, 0.50, 0.55, 0.60,
            0.65, 0.70, 0.75, 0.80, 0.85, 0.90, 0.95, 1.0
        };
        double[] upper = {
            0, 0.126, 0.200, 0.233, 0.248, 0.252, 0.247,
            0.234, 0.214, 0.188, 0.158, 0.128, 0.098,
            0.070, 0.046, 0.026, 0.011, 0.002, 0, 0, 0
        };
        double[] lower = {
            0, -0.058, -0.073, -0.076, -0.074, -0.068,
            -0.059, -0.049, -0.038, -0.027, -0.018,
            -0.010, -0.004, 0.001, 0.004, 0.005,
            0.005, 0.003, 0, 0, 0
        };

        double mL = 55, mT = 55;
        double chordPx = W - mL - 40;
        double scaleY = (H - mT - 50) / 2.0 / (upper.Max() * chordPx);
        double baseX = mL;
        double baseY = mT + (H - mT - 50) / 2.0 + 20;

        // Build polygon points (upper surface forward,
        //                       lower surface backward)
        var upperPts = xCoords
            .Zip(upper, (x, y) =>
                $"{baseX + x * chordPx:F1}," +
                $"{baseY - y * chordPx * scaleY:F1}");

        var lowerPts = xCoords
            .Zip(lower, (x, y) =>
                $"{baseX + x * chordPx:F1}," +
                $"{baseY - y * chordPx * scaleY:F1}")
            .Reverse();

        string pts = string.Join(" ", upperPts)
                   + " "
                   + string.Join(" ", lowerPts);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($@"<svg viewBox=""0 0 {W} {H}""
    xmlns=""http://www.w3.org/2000/svg""
    style=""background:#f8f9fa;
           border:1px solid #dee2e6;
           border-radius:6px;
           max-width:100%;"">
  <defs>
    <marker id=""arrBP"" markerWidth=""7"" markerHeight=""7""
            refX=""3"" refY=""3"" orient=""auto"">
      <path d=""M0,0 L0,6 L7,3 z"" fill=""#555""/>
    </marker>
  </defs>

  <!-- Title block -->
  <rect x=""0"" y=""0"" width=""{W}"" height=""30""
        fill=""#1864ab""/>
  <text x=""10"" y=""20"" font-family=""Arial,sans-serif""
        font-size=""12"" font-weight=""bold"" fill=""white"">
    DWG-003  BLADE AEROFOIL SECTION (NACA 4412)
  </text>
  <text x=""{W - 10}"" y=""20""
        font-family=""Arial,monospace""
        font-size=""9"" fill=""#adb5bd"" text-anchor=""end"">
    Chord={r.ChordLengthMm:F1}mm  β={d.BladeAngleDeg:F1}°
  </text>

  <!-- Chord reference line -->
  <line x1=""{baseX:F1}"" y1=""{baseY:F1}""
        x2=""{baseX + chordPx:F1}"" y2=""{baseY:F1}""
        stroke=""#ced4da"" stroke-width=""1""
        stroke-dasharray=""8,4""/>

  <!-- Aerofoil shape -->
  <polygon points=""{pts}""
           fill=""#74c0fc""
           stroke=""#1971c2""
           stroke-width=""1.8""
           opacity=""0.92""/>

  <!-- Leading edge dot -->
  <circle cx=""{baseX:F1}"" cy=""{baseY:F1}""
          r=""4"" fill=""#1971c2""/>

  <!-- Trailing edge dot -->
  <circle cx=""{baseX + chordPx:F1}"" cy=""{baseY:F1}""
          r=""4"" fill=""#1971c2""/>

  <!-- LE / TE labels -->
  <text x=""{baseX - 4:F1}"" y=""{baseY - 16:F1}""
        font-family=""Arial,monospace""
        font-size=""10"" fill=""#2f9e44"" font-weight=""bold"">
    LE
  </text>
  <text x=""{baseX + chordPx + 4:F1}"" y=""{baseY - 16:F1}""
        font-family=""Arial,monospace""
        font-size=""10"" fill=""#2f9e44"" font-weight=""bold"">
    TE
  </text>

  <!-- Chord dimension -->
  <line x1=""{baseX:F1}"" y1=""{baseY + 32:F1}""
        x2=""{baseX + chordPx:F1}"" y2=""{baseY + 32:F1}""
        stroke=""#555"" stroke-width=""1""
        marker-start=""url(#arrBP)""
        marker-end=""url(#arrBP)""/>
  <text x=""{baseX + chordPx / 2:F1}"" y=""{baseY + 46:F1}""
        text-anchor=""middle""
        font-family=""Arial,monospace""
        font-size=""11"" fill=""#333"">
    chord = {r.ChordLengthMm:F1} mm
  </text>

  <!-- Blade angle label -->
  <text x=""{baseX + chordPx + 8:F1}"" y=""{baseY - 4:F1}""
        font-family=""Arial,monospace""
        font-size=""10"" fill=""#e67700"" font-weight=""bold"">
    β = {d.BladeAngleDeg:F1}°
  </text>

  <!-- Flow direction arrow -->
  <line x1=""{baseX - 38:F1}"" y1=""{baseY - 10:F1}""
        x2=""{baseX - 8:F1}"" y2=""{baseY - 10:F1}""
        stroke=""#1971c2"" stroke-width=""2""
        marker-end=""url(#arrBP)""/>
  <text x=""{baseX - 44:F1}"" y=""{baseY - 16:F1}""
        font-family=""Arial,monospace""
        font-size=""9"" fill=""#1971c2"">
    V∞
  </text>
</svg>");

        return sb.ToString();
    }
}
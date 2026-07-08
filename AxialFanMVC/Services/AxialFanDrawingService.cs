using netDxf;
using netDxf.Entities;
using netDxf.Tables;
using netDxf.Header;
using System.Linq;
using AxialFanMVC.Database;
using ModelPoint2D = AxialFanMVC.Models.Point2D;
using ModelBladeProfileData = AxialFanMVC.Models.BladeProfileData;

namespace AxialFanMVC.Services;

/// <summary>
/// Generates 7 AutoCAD DXF engineering drawings for the axial fan.
/// Uses netDxf 2023.11.10 — works in ASP.NET Core .NET 8, no UI deps.
///
/// DWG-001  Front Elevation         (face-on, blades + hub + casing)
/// DWG-002  Cross Section           (side cut, hub + blade chord)
/// DWG-003  Blade Aerofoil Profile  (NACA section with dimensions)
/// DWG-004  Blade Angle Drawing     (pitch angle detail)
/// DWG-005  Hub Detail              (hub geometry + shaft bore)
/// DWG-006  Casing Detail           (casing dimensions + motor mounting)
/// DWG-007  General Arrangement     (overall assembly + BOM table)
/// </summary>
public static class AxialFanDrawingService
{
    // ══════════════════════════════════════════════════════════════
    // DWG-001 — Front Elevation
    // ══════════════════════════════════════════════════════════════
    public static byte[] FrontElevationDxf(DesignInput d, DesignResult r)
    {
        var doc = CreateDocument();
        double tipR = d.TipDiameterMm / 2.0;
        double hubR = tipR * d.HubRatio;
        double span = tipR - hubR;
        double bladeW = Math.Min(14.0, span * 0.18);
        int blades = d.BladeCount;

        // Outer casing
        doc.Entities.Add(new Circle(Vector2.Zero, tipR + 15) { Layer = doc.Layers["CASING"] });
        // Tip clearance ring
        doc.Entities.Add(new Circle(Vector2.Zero, tipR + 3) { Layer = doc.Layers["TIP_CLEARANCE"], Linetype = doc.Linetypes["DASHED"] });
        // Tip diameter circle
        doc.Entities.Add(new Circle(Vector2.Zero, tipR) { Layer = doc.Layers["TIP_DIAMETER"], Linetype = doc.Linetypes["DASHED"] });

        // Blades — radial rectangle from hub to tip
        for (int i = 0; i < blades; i++)
        {
            double aRad = i * 2 * Math.PI / blades;
            double rCos = Math.Cos(aRad);
            double rSin = Math.Sin(aRad);
            double pitchRad = d.BladeAngleDeg * Math.PI / 180.0;
            double pCos = Math.Cos(aRad + Math.PI / 2.0 + pitchRad);
            double pSin = Math.Sin(aRad + Math.PI / 2.0 + pitchRad);
            double hw = bladeW / 2.0;

            doc.Entities.Add(new Polyline2D(new List<Polyline2DVertex>
            {
                new(hubR * rCos - hw * pCos, hubR * rSin - hw * pSin),
                new(hubR * rCos + hw * pCos, hubR * rSin + hw * pSin),
                new(tipR * rCos + hw * pCos, tipR * rSin + hw * pSin),
                new(tipR * rCos - hw * pCos, tipR * rSin - hw * pSin)
            }, true)
            { Layer = doc.Layers["BLADES"] });
        }

        // Hub circle
        doc.Entities.Add(new Circle(Vector2.Zero, hubR) { Layer = doc.Layers["HUB"] });
        // Shaft bore (estimated 25% of hub diameter)
        double shaftR = hubR * 0.25;
        doc.Entities.Add(new Circle(Vector2.Zero, shaftR) { Layer = doc.Layers["HUB"] });

        // Centre cross
        AddLine(doc, -tipR * 1.1, 0, tipR * 1.1, 0, "CENTERLINE");
        AddLine(doc, 0, -tipR * 1.1, 0, tipR * 1.1, "CENTERLINE");

        // Tip diameter dimension
        double dimY = -(tipR + 30);
        AddDimHoriz(doc, -tipR, tipR, dimY, $"D={d.TipDiameterMm:F0} mm (Tip)", 5.0);

        // Hub diameter dimension
        AddDimHoriz(doc, -hubR, hubR, hubR + 25, $"D={r.HubDiameterMm:F0} mm (Hub)", 4.5);

        // Blade count label
        doc.Entities.Add(MakeText($"No. of Blades: {blades}", 4.5, tipR + 20, 20, doc.Layers["ANNOTATION"]));
        doc.Entities.Add(MakeText($"Blade Angle: {d.BladeAngleDeg:F1} deg", 4.5, tipR + 20, 8, doc.Layers["ANNOTATION"]));

        AddTitleBlock(doc, "DWG-001", "FRONT ELEVATION",
            $"D={d.TipDiameterMm:F0}mm  Z={blades}  N={d.SpeedRpm}RPM", tipR);

        return SaveToBytes(doc);
    }

    // ══════════════════════════════════════════════════════════════
    // DWG-002 — Cross Section
    // ══════════════════════════════════════════════════════════════
    public static byte[] CrossSectionDxf(DesignInput d, DesignResult r)
    {
        var doc = CreateDocument();
        double tipR = d.TipDiameterMm / 2.0;
        double hubR = tipR * d.HubRatio;
        double axLen = d.TipDiameterMm * 1.2;
        double endX = axLen;
        double cDraw = Math.Min(r.ChordLengthMm, axLen * 0.35);

        // Top casing wall (hatched)
        AddBox(doc, 0, tipR, endX, tipR + 20, "CASING");
        // Bottom casing wall
        AddBox(doc, 0, -tipR - 20, endX, -tipR, "CASING");
        // Hub cylinder
        AddBox(doc, 0, -hubR, endX, hubR, "HUB");
        // Shaft bore
        double shaftR = hubR * 0.25;
        AddBox(doc, 0, -shaftR, endX, shaftR, "CENTERLINE");

        // Centreline
        doc.Entities.Add(new Line(new Vector3(-30, 0, 0), new Vector3(endX + 30, 0, 0))
        { Layer = doc.Layers["CENTERLINE"], Linetype = doc.Linetypes["CENTER"] });

        // Tip clearance line
        doc.Entities.Add(new Line(new Vector3(0, tipR + r.TipClearanceMm, 0), new Vector3(endX, tipR + r.TipClearanceMm, 0))
        { Layer = doc.Layers["TIP_CLEARANCE"], Linetype = doc.Linetypes["DASHED"] });

        // Blade (chord rectangle)
        double bx = axLen * 0.38;
        AddBox(doc, bx, hubR, bx + cDraw, tipR, "BLADES");

        // Flow arrow
        AddLine(doc, -60, 0, -5, 0, "ANNOTATION");
        doc.Entities.Add(MakeText("FLOW ->", 5.0, -60, 6, doc.Layers["ANNOTATION"]));

        // Dimensions
        double dX = endX + 20;
        AddDimVert(doc, dX, -tipR, tipR, $"D={d.TipDiameterMm:F0}mm", 4.5);
        AddDimVert(doc, -20, -hubR, hubR, $"Hub={r.HubDiameterMm:F0}mm", 4.0);
        AddDimVert(doc, bx - 15, hubR, tipR, $"Span={r.BladeSpanMm:F1}mm", 4.0);
        AddDimHoriz(doc, bx, bx + cDraw, hubR - 20, $"Chord={r.ChordLengthMm:F1}mm", 4.0);
        AddDimHoriz(doc, 0, endX, -tipR - 35, $"Axial Length={axLen:F0}mm", 4.5);

        // Gap label
        doc.Entities.Add(MakeText($"Tip Gap={r.TipClearanceMm:F1}mm", 3.5, 5, tipR + 4, doc.Layers["ANNOTATION"]));

        AddTitleBlock(doc, "DWG-002", "CROSS SECTION - SIDE VIEW",
            $"Span={r.BladeSpanMm:F1}mm  Chord={r.ChordLengthMm:F1}mm  Gap={r.TipClearanceMm:F1}mm", tipR);
        return SaveToBytes(doc);
    }

    // ══════════════════════════════════════════════════════════════
    // DWG-003 — Blade Aerofoil Profile
    // ══════════════════════════════════════════════════════════════
    public static byte[] BladeProfileDxf(DesignInput d, DesignResult r)
    {
        var doc = CreateDocument();
        double chord = r.ChordLengthMm;
        const string naca = "4412";

        ModelBladeProfileData? profile = null;
        try { profile = BladeProfileEngine.GenerateNaca4(naca, chord, points: 80); }
        catch { }

        if (profile != null)
        {
            // Upper and lower surface splines
            doc.Entities.Add(new Spline(
                profile.UpperSurface.Select(p => new Vector3(p.X * chord, p.Y * chord, 0)).ToList())
            { Layer = doc.Layers["BLADE_SURFACE"] });
            doc.Entities.Add(new Spline(
                profile.LowerSurface.Select(p => new Vector3(p.X * chord, p.Y * chord, 0)).ToList())
            { Layer = doc.Layers["BLADE_SURFACE"] });

            // Camber line
            doc.Entities.Add(new Polyline2D(
                profile.CamberLine.Select(p => new Polyline2DVertex(p.X * chord, p.Y * chord)).ToList(), false)
            { Layer = doc.Layers["CAMBER_LINE"], Linetype = doc.Linetypes["DASHED"] });

            // Station ticks
            foreach (double xf in new[] { 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9 })
            {
                double tx = xf * chord;
                double yu = InterpolateProfile(profile.UpperSurface, xf) * chord;
                double yl = InterpolateProfile(profile.LowerSurface, xf) * chord;
                AddLine(doc, tx, yl - 3, tx, yu + 3, "STATION_TICKS");
                doc.Entities.Add(MakeText($"{xf * 100:F0}%", 3.0, tx, yl - 8, doc.Layers["ANNOTATION"]));
            }

            // Max thickness
            var dims = profile.Dimensions;
            double mtx = dims.MaxThicknessXPct / 100.0 * chord;
            double mtu = InterpolateProfile(profile.UpperSurface, dims.MaxThicknessXPct / 100.0) * chord;
            double mtl = InterpolateProfile(profile.LowerSurface, dims.MaxThicknessXPct / 100.0) * chord;
            AddLine(doc, mtx, mtu, mtx, mtl, "DIMENSIONS");
            AddLine(doc, mtx, (mtu + mtl) / 2, mtx + 15, (mtu + mtl) / 2, "DIMENSIONS");
            doc.Entities.Add(MakeText($"t={dims.MaxThicknessMm:F2}mm ({dims.MaxThicknessPct:F1}%c)", 3.0, mtx + 18, (mtu + mtl) / 2 + 2, doc.Layers["DIMENSIONS"]));

            // Max camber
            double mcx = dims.MaxCamberXPct / 100.0 * chord;
            double mcyu = dims.MaxCamberMm;
            AddLine(doc, mcx, 0, mcx, mcyu, "DIMENSIONS");
            AddLine(doc, mcx, mcyu / 2, mcx - 15, mcyu / 2, "DIMENSIONS");
            doc.Entities.Add(MakeText($"f={dims.MaxCamberMm:F2}mm ({dims.MaxCamberPct:F1}%c)", 3.0, mcx - 18, mcyu / 2 + 2, doc.Layers["DIMENSIONS"]));

            // LE radius
            doc.Entities.Add(new Circle(Vector2.Zero, dims.LeadingEdgeRadiusMm) { Layer = doc.Layers["DIMENSIONS"], Linetype = doc.Linetypes["DASHED"] });
            doc.Entities.Add(MakeText($"r_LE={dims.LeadingEdgeRadiusMm:F3}mm", 3.5, 5, mtu + 8, doc.Layers["ANNOTATION"]));
        }
        else
        {
            // Fallback hardcoded NACA 4412
            double[] xN = { 0, 0.05, 0.10, 0.15, 0.20, 0.25, 0.30, 0.35, 0.40, 0.45, 0.50, 0.55, 0.60, 0.65, 0.70, 0.75, 0.80, 0.85, 0.90, 0.95, 1.0 };
            double[] yU = { 0, 0.126, 0.200, 0.233, 0.248, 0.252, 0.247, 0.234, 0.214, 0.188, 0.158, 0.128, 0.098, 0.070, 0.046, 0.026, 0.011, 0.002, 0, 0, 0 };
            double[] yL = { 0, -0.058, -0.073, -0.076, -0.074, -0.068, -0.059, -0.049, -0.038, -0.027, -0.018, -0.010, -0.004, 0.001, 0.004, 0.005, 0.005, 0.003, 0, 0, 0 };
            doc.Entities.Add(new Spline(xN.Zip(yU, (x, y) => new Vector3(x * chord, y * chord, 0)).ToList()) { Layer = doc.Layers["BLADE_SURFACE"] });
            doc.Entities.Add(new Spline(xN.Zip(yL, (x, y) => new Vector3(x * chord, y * chord, 0)).ToList()) { Layer = doc.Layers["BLADE_SURFACE"] });
        }

        // Chord line
        AddLine(doc, 0, 0, chord, 0, "CENTERLINE");
        doc.Entities.Add(MakeText("LE", 5.0, -4, 7, doc.Layers["ANNOTATION"]));
        doc.Entities.Add(MakeText("TE", 5.0, chord + 3, 7, doc.Layers["ANNOTATION"]));

        // Chord dimension
        double cdY = -chord * 0.18;
        AddDimHoriz(doc, 0, chord, cdY, $"Chord={chord:F1}mm", 5.0);

        doc.Entities.Add(MakeText($"Blade Angle B={d.BladeAngleDeg:F1}deg", 5.0, chord + 5, -8, doc.Layers["ANNOTATION"]));
        doc.Entities.Add(MakeText($"Profile: NACA {naca}", 5.0, chord + 5, -20, doc.Layers["ANNOTATION"]));
        AddLine(doc, -40, -8, -5, -8, "ANNOTATION");
        doc.Entities.Add(MakeText("V_inf", 4.5, -45, -4, doc.Layers["ANNOTATION"]));

        AddTitleBlock(doc, "DWG-003", $"BLADE AEROFOIL SECTION (NACA {naca})",
            $"Chord={chord:F1}mm  B={d.BladeAngleDeg:F1}deg  Span={r.BladeSpanMm:F1}mm", chord * 0.4);
        return SaveToBytes(doc);
    }

    // ══════════════════════════════════════════════════════════════
    // DWG-004 — Blade Angle Drawing
    // Shows pitch angle, chord line, inlet/outlet velocity triangles
    // ══════════════════════════════════════════════════════════════
    public static byte[] BladeAngleDxf(DesignInput d, DesignResult r)
    {
        var doc = CreateDocument();
        double chord = r.ChordLengthMm;
        double betaRad = d.BladeAngleDeg * Math.PI / 180.0;

        // ── Rotor plane (horizontal reference line) ───────────────
        AddLine(doc, -50, 0, chord + 100, 0, "CENTERLINE");
        doc.Entities.Add(MakeText("ROTOR PLANE", 4.5, chord + 20, 4, doc.Layers["ANNOTATION"]));

        // ── Chord line at blade angle ─────────────────────────────
        double cx2 = chord * Math.Cos(betaRad);
        double cy2 = chord * Math.Sin(betaRad);
        AddLine(doc, 0, 0, cx2, cy2, "BLADES");
        doc.Entities.Add(MakeText("CHORD LINE", 4.0, cx2 + 5, cy2 + 5, doc.Layers["BLADES"]));

        // ── Blade angle arc ───────────────────────────────────────
        double arcR = chord * 0.35;
        var arc = new Arc(Vector2.Zero, arcR, 0, d.BladeAngleDeg);
        arc.Layer = doc.Layers["DIMENSIONS"];
        doc.Entities.Add(arc);
        // Angle label
        double midAngle = d.BladeAngleDeg / 2.0 * Math.PI / 180.0;
        double lx = (arcR + 10) * Math.Cos(midAngle);
        double ly = (arcR + 10) * Math.Sin(midAngle);
        doc.Entities.Add(MakeText($"b={d.BladeAngleDeg:F1}deg", 5.0, lx, ly, doc.Layers["DIMENSIONS"]));

        // ── Axial velocity — calculated from saved fields ────────────
        // Va = Q / A  where A = π(tipR² - hubR²)
        // We derive Q from FlowCoefficient = Va / U_tip
        // FlowCoefficient and TipSpeedMs are both saved in DesignResult
        double tipR_m = d.TipDiameterMm / 2000.0;           // tip radius in metres
        double hubR_m = tipR_m * d.HubRatio;                 // hub radius in metres
        double annulus = Math.PI * (tipR_m * tipR_m - hubR_m * hubR_m);
        double Q = d.FlowRateM3s;                      // m³/s from DesignInput
        double Va = annulus > 0 ? Q / annulus : 10.0;   // axial velocity m/s
        double U = r.TipSpeedMs > 0 ? r.TipSpeedMs : 60.0; // tip speed m/s
        // Scale vectors to drawing (1 m/s = 5 mm)
        double vScale = 4.0;
        double vaLen = Va * vScale;
        double uLen = U * vScale;

        // Draw velocity triangle below rotor plane
        double triY = -80;
        // Axial (Va) — vertical
        AddLine(doc, 80, triY, 80, triY + vaLen, "ANNOTATION");
        doc.Entities.Add(MakeText($"Va={Va:F1}m/s", 4.0, 84, triY + vaLen / 2, doc.Layers["ANNOTATION"]));

        // Tip speed (U) — horizontal
        AddLine(doc, 80, triY, 80 + uLen, triY, "ANNOTATION");
        doc.Entities.Add(MakeText($"U={U:F1}m/s", 4.0, 80 + uLen / 2, triY - 8, doc.Layers["ANNOTATION"]));

        // Resultant (W) — hypotenuse
        AddLine(doc, 80, triY, 80 + uLen, triY + vaLen, "BLADES");
        doc.Entities.Add(MakeText("W (Relative velocity)", 4.0, 80 + uLen / 2 + 5, triY + vaLen / 2, doc.Layers["BLADES"]));

        doc.Entities.Add(MakeText("VELOCITY TRIANGLE", 5.0, 75, triY - 20, doc.Layers["TITLE_BLOCK"]));

        // ── Stagger angle reference lines ─────────────────────────
        // Perpendicular to chord = normal to blade
        double nx = -Math.Sin(betaRad) * 40;
        double ny = Math.Cos(betaRad) * 40;
        AddLine(doc, cx2 / 2 - nx, cy2 / 2 - ny, cx2 / 2 + nx, cy2 / 2 + ny, "CENTERLINE");

        // Annotations
        doc.Entities.Add(MakeText($"Blade Count Z={d.BladeCount}", 4.5, -50, chord * 0.3, doc.Layers["ANNOTATION"]));
        doc.Entities.Add(MakeText($"Speed N={d.SpeedRpm} RPM", 4.5, -50, chord * 0.3 - 12, doc.Layers["ANNOTATION"]));
        doc.Entities.Add(MakeText($"Chord={chord:F1}mm", 4.5, -50, chord * 0.3 - 24, doc.Layers["ANNOTATION"]));

        AddTitleBlock(doc, "DWG-004", "BLADE ANGLE DETAIL",
            $"Blade Angle b={d.BladeAngleDeg:F1}deg  Va={Va:F1}m/s  U={U:F1}m/s", chord * 0.5);
        return SaveToBytes(doc);
    }

    // ══════════════════════════════════════════════════════════════
    // DWG-005 — Hub Detail
    // Hub cylinder with shaft bore, keyway, bolt holes
    // ══════════════════════════════════════════════════════════════
    public static byte[] HubDetailDxf(DesignInput d, DesignResult r)
    {
        var doc = CreateDocument();
        double hubR = r.HubDiameterMm / 2.0;
        double hubL = hubR * 1.4;   // hub length = 70% of diameter
        double shR = hubR * 0.25;  // shaft radius (25% of hub radius)
        double kwW = shR * 0.25;   // keyway width
        double kwD = shR * 0.15;   // keyway depth
        int boltN = 6;           // bolt holes in hub flange
        double boltPCD = hubR * 0.7; // bolt PCD radius
        double boltR = 5.0;          // bolt hole radius

        // ── FRONT VIEW (left side) ────────────────────────────────
        double fvX = 0; // front view origin X

        // Hub outer circle
        doc.Entities.Add(new Circle(new Vector2(fvX, 0), hubR)
        { Layer = doc.Layers["HUB"] });

        // Shaft bore
        doc.Entities.Add(new Circle(new Vector2(fvX, 0), shR)
        { Layer = doc.Layers["CENTERLINE"] });

        // Keyway (rectangle at top of bore)
        AddBox(doc, fvX - kwW / 2, shR - kwD, fvX + kwW / 2, shR + kwD, "HUB");

        // Blade attachment bolt pattern on hub face
        for (int i = 0; i < boltN; i++)
        {
            double a = i * 2 * Math.PI / boltN;
            double bx = fvX + boltPCD * Math.Cos(a);
            double by = boltPCD * Math.Sin(a);
            doc.Entities.Add(new Circle(new Vector2(bx, by), boltR)
            { Layer = doc.Layers["HUB"] });
        }

        // Centre cross
        AddLine(doc, fvX - hubR - 15, 0, fvX + hubR + 15, 0, "CENTERLINE");
        AddLine(doc, fvX, -hubR - 15, fvX, hubR + 15, "CENTERLINE");

        // Front view dimensions
        AddDimHoriz(doc, fvX - hubR, fvX + hubR, -(hubR + 25), $"Hub OD={r.HubDiameterMm:F0}mm", 4.5);
        AddDimHoriz(doc, fvX - shR, fvX + shR, shR + 20, $"Shaft Bore={shR * 2:F0}mm", 4.0);
        doc.Entities.Add(MakeText($"Bolt PCD={boltPCD * 2:F0}mm", 4.0, fvX + boltPCD + 8, boltPCD, doc.Layers["DIMENSIONS"]));
        doc.Entities.Add(MakeText($"Bolt Hole D={boltR * 2:F0}mm x {boltN}", 4.0, fvX + boltPCD + 8, boltPCD - 10, doc.Layers["DIMENSIONS"]));
        doc.Entities.Add(MakeText("KEYWAY", 3.5, fvX + kwW / 2 + 5, shR + kwD / 2, doc.Layers["ANNOTATION"]));

        // ── SIDE VIEW (right side) ────────────────────────────────
        double svX = hubR * 2.8; // side view origin X

        // Hub cylinder outline
        AddBox(doc, svX, -hubR, svX + hubL, hubR, "HUB");
        // Shaft bore in side view
        AddLine(doc, svX, shR, svX + hubL, shR, "CENTERLINE");
        AddLine(doc, svX, -shR, svX + hubL, -shR, "CENTERLINE");
        // Centreline
        AddLine(doc, svX - 15, 0, svX + hubL + 15, 0, "CENTERLINE");

        // Side view dimensions
        AddDimVert(doc, svX + hubL + 20, -hubR, hubR, $"OD={r.HubDiameterMm:F0}mm", 4.5);
        AddDimVert(doc, svX - 18, -shR, shR, $"Bore={shR * 2:F0}mm", 4.0);
        AddDimHoriz(doc, svX, svX + hubL, -(hubR + 25), $"Hub Length={hubL:F0}mm", 4.5);

        // Material annotation
        doc.Entities.Add(MakeText("MATERIAL: Al Alloy 6061-T6", 4.5, fvX, -(hubR + 55), doc.Layers["ANNOTATION"]));
        doc.Entities.Add(MakeText("SURFACE FINISH: Ra 1.6 (shaft bore)", 4.5, fvX, -(hubR + 67), doc.Layers["ANNOTATION"]));

        AddTitleBlock(doc, "DWG-005", "HUB DETAIL",
            $"Hub OD={r.HubDiameterMm:F0}mm  Hub Ratio={d.HubRatio:F2}  N={d.SpeedRpm}RPM", hubR + 30);
        return SaveToBytes(doc);
    }

    // ══════════════════════════════════════════════════════════════
    // DWG-006 — Casing Detail + Motor Mounting
    // ══════════════════════════════════════════════════════════════
    public static byte[] CasingDetailDxf(DesignInput d, DesignResult r)
    {
        var doc = CreateDocument();
        double tipR = d.TipDiameterMm / 2.0;
        double casR = tipR + 15;     // casing inner radius
        double casT = 6.0;           // casing wall thickness
        double casL = d.TipDiameterMm * 0.6; // casing axial length

        // Motor dimensions (estimated from kW rating)
        double motL = 150 + d.MotorPowerKw * 20;
        double motR = 50 + d.MotorPowerKw * 5;
        double motPCD = motR * 1.6;
        int motBolts = 4;
        double motBoltR = 6.0;

        // ── CASING CROSS SECTION (left view) ─────────────────────
        double cvX = 0;
        // Casing inner circle
        doc.Entities.Add(new Circle(new Vector2(cvX, 0), casR)
        { Layer = doc.Layers["CASING"] });
        // Casing outer circle
        doc.Entities.Add(new Circle(new Vector2(cvX, 0), casR + casT)
        { Layer = doc.Layers["CASING"] });
        // Motor mounting plate circle
        doc.Entities.Add(new Circle(new Vector2(cvX, 0), motPCD + motBoltR + 10)
        { Layer = doc.Layers["HUB"], Linetype = doc.Linetypes["DASHED"] });

        // Motor mounting bolt holes
        for (int i = 0; i < motBolts; i++)
        {
            double a = (i * 2 * Math.PI / motBolts) + Math.PI / 4;
            double bx = cvX + motPCD * Math.Cos(a);
            double by = motPCD * Math.Sin(a);
            doc.Entities.Add(new Circle(new Vector2(bx, by), motBoltR)
            { Layer = doc.Layers["HUB"] });
            doc.Entities.Add(MakeText("M12", 3.0, bx + motBoltR + 2, by, doc.Layers["ANNOTATION"]));
        }

        // Centre lines
        AddLine(doc, cvX - casR - 20, 0, cvX + casR + 20, 0, "CENTERLINE");
        AddLine(doc, cvX, -casR - 20, cvX, casR + 20, "CENTERLINE");

        // Casing dimensions
        AddDimHoriz(doc, cvX - casR, cvX + casR, -(casR + 30), $"Casing ID={casR * 2:F0}mm", 4.5);
        AddDimHoriz(doc, cvX - (casR + casT), cvX + (casR + casT), -(casR + 45), $"Casing OD={(casR + casT) * 2:F0}mm", 4.0);
        doc.Entities.Add(MakeText($"Wall Thickness={casT:F0}mm", 4.0, cvX + casR + 15, 10, doc.Layers["DIMENSIONS"]));
        doc.Entities.Add(MakeText($"Motor PCD={motPCD * 2:F0}mm", 4.0, cvX + casR + 15, -5, doc.Layers["DIMENSIONS"]));
        doc.Entities.Add(MakeText($"Mounting Bolts: {motBolts}x M12", 4.0, cvX + casR + 15, -20, doc.Layers["DIMENSIONS"]));

        // ── MOTOR MOUNTING SIDE VIEW (right side) ────────────────
        double mvX = (casR + casT) * 2.5;
        // Casing side view
        AddBox(doc, mvX, -casR - casT, mvX + casL, casR + casT, "CASING");
        AddLine(doc, mvX - 15, 0, mvX + casL + 15, 0, "CENTERLINE");

        // Motor body
        double mStart = mvX + casL;
        AddBox(doc, mStart, -motR, mStart + motL, motR, "HUB");
        // Motor shaft extension
        AddLine(doc, mStart, motR * 0.25, mStart + motL * 0.3, motR * 0.25, "CENTERLINE");
        AddLine(doc, mStart, -motR * 0.25, mStart + motL * 0.3, -motR * 0.25, "CENTERLINE");

        // Motor mounting plate
        AddLine(doc, mStart, -(casR + casT), mStart, casR + casT, "HUB");

        // Side view dimensions
        AddDimHoriz(doc, mvX, mvX + casL, -(casR + casT + 25), $"Casing Length={casL:F0}mm", 4.5);
        AddDimHoriz(doc, mStart, mStart + motL, -(casR + casT + 25), $"Motor Length={motL:F0}mm (est.)", 4.0);
        AddDimVert(doc, mStart + motL + 20, -motR, motR, $"Motor D={motR * 2:F0}mm (est.)", 4.0);

        // Material and spec notes
        doc.Entities.Add(MakeText("CASING MATERIAL: Mild Steel / GI Sheet", 4.5, cvX, -(casR + 65), doc.Layers["ANNOTATION"]));
        doc.Entities.Add(MakeText($"Motor Rating: {d.MotorPowerKw:F2}kW  {d.MotorPoles}", 4.5, cvX, -(casR + 77), doc.Layers["ANNOTATION"]));
        doc.Entities.Add(MakeText("All mounting bolts: M12 Grade 8.8", 4.5, cvX, -(casR + 89), doc.Layers["ANNOTATION"]));

        AddTitleBlock(doc, "DWG-006", "CASING DETAIL + MOTOR MOUNTING",
            $"Fan OD={d.TipDiameterMm:F0}mm  Motor={d.MotorPowerKw:F2}kW  {d.MotorPoles}", casR + 40);
        return SaveToBytes(doc);
    }

    // ══════════════════════════════════════════════════════════════
    // DWG-007 — General Arrangement Drawing
    // Full assembly side view + BOM table
    // ══════════════════════════════════════════════════════════════
    public static byte[] GeneralArrangementDxf(DesignInput d, DesignResult r)
    {
        var doc = CreateDocument();
        double tipR = d.TipDiameterMm / 2.0;
        double hubR = r.HubDiameterMm / 2.0;
        double casR = tipR + 15;
        double casT = 6.0;
        double casL = d.TipDiameterMm * 0.6;
        double motL = 150 + d.MotorPowerKw * 20;
        double motR = 50 + d.MotorPowerKw * 5;
        double shR = hubR * 0.25;
        double totalL = casL + motL + 40;

        // ── ASSEMBLY SIDE VIEW ────────────────────────────────────
        // Casing
        AddBox(doc, 0, -casR - casT, casL, casR + casT, "CASING");

        // Hub cylinder
        AddBox(doc, 0, -hubR, casL, hubR, "HUB");

        // Shaft
        AddBox(doc, -20, -shR, casL + motL + 20, shR, "CENTERLINE");

        // Centreline
        AddLine(doc, -30, 0, totalL + 30, 0, "CENTERLINE");

        // Blades (simplified as lines in side view)
        double bx = casL * 0.38;
        double cDraw = Math.Min(r.ChordLengthMm, casL * 0.35);
        AddBox(doc, bx, hubR, bx + cDraw, casR, "BLADES");
        AddBox(doc, bx, -casR, bx + cDraw, -hubR, "BLADES");

        // Tip clearance
        AddLine(doc, 0, casR + r.TipClearanceMm, casL, casR + r.TipClearanceMm, "TIP_CLEARANCE");

        // Motor body
        AddBox(doc, casL + 10, -motR, casL + motL + 10, motR, "HUB");
        doc.Entities.Add(MakeText("MOTOR", 5.0, casL + motL / 2 + 10, motR + 8, doc.Layers["ANNOTATION"]));

        // Motor mounting plate
        AddLine(doc, casL, -(casR + casT), casL, casR + casT, "HUB");

        // Overall dimensions
        AddDimHoriz(doc, 0, casL, -(casR + casT + 30), $"Casing={casL:F0}mm", 4.5);
        AddDimHoriz(doc, 0, totalL, -(casR + casT + 50), $"Overall Length={totalL:F0}mm", 5.0);
        AddDimVert(doc, -25, -casR - casT, casR + casT, $"OD={(casR + casT) * 2:F0}mm", 4.5);
        AddDimVert(doc, casL + motL + 35, -motR, motR, $"Motor D={motR * 2:F0}mm", 4.0);

        // Flow direction
        AddLine(doc, -50, 0, -10, 0, "ANNOTATION");
        doc.Entities.Add(MakeText("AIR FLOW ->", 4.5, -55, 6, doc.Layers["ANNOTATION"]));

        // Item balloons
        double[] bx2 = { casL * 0.5, bx + cDraw / 2, hubR + 5, casL + motL / 2 + 10, -15 };
        double[] by2 = { casR + casT + 8, casR + 8, 0, 0, 0 };
        string[] items = { "1", "2", "3", "4", "5" };
        for (int i = 0; i < Math.Min(items.Length, bx2.Length); i++)
        {
            doc.Entities.Add(new Circle(new Vector2(bx2[i], by2[i]), 8) { Layer = doc.Layers["ANNOTATION"] });
            doc.Entities.Add(MakeText(items[i], 5.0, bx2[i] - 2.5, by2[i] - 3, doc.Layers["ANNOTATION"]));
        }

        // ── BILL OF MATERIALS TABLE ───────────────────────────────
        double[] bom = BuildBomTable(doc, d, r, 0, -(casR + casT + 90));

        AddTitleBlock(doc, "DWG-007", "GENERAL ARRANGEMENT DRAWING",
            $"Fan D={d.TipDiameterMm:F0}mm  Q={d.FlowRateM3s:F3}m3/s  dP={d.TotalPressurePa:F0}Pa  N={d.SpeedRpm}RPM", casR + 50);
        return SaveToBytes(doc);
    }

    // ══════════════════════════════════════════════════════════════
    // BOM TABLE helper — draws BOM in DXF with SIZE column
    // ══════════════════════════════════════════════════════════════
    private static double[] BuildBomTable(DxfDocument doc, DesignInput d, DesignResult r, double ox, double oy)
    {
        // ── Derive sizes from design data ─────────────────────────
        double tipR = d.TipDiameterMm / 2.0;
        double hubR = r.HubDiameterMm / 2.0;
        double casR = tipR + 15;
        double casT = 6.0;
        double casL = d.TipDiameterMm * 0.6;
        double hubL = r.HubDiameterMm * 0.7;
        double shaftD = r.HubDiameterMm * 0.25 * 2;  // shaft diameter mm
        double motL = 150 + d.MotorPowerKw * 20;
        double motD = (50 + d.MotorPowerKw * 5) * 2;
        double frameW = (casR + casT) * 2 + 40;
        double boltPCD = hubR * 0.7 * 2;

        // ── Column widths: ITEM | DESCRIPTION | MATERIAL | SIZE | QTY | MASS | STANDARD | NOTES
        double[] widths = { 18, 110, 75, 110, 22, 55, 65, 85 };
        string[] headers = { "ITEM", "DESCRIPTION", "MATERIAL", "SIZE / DIMENSIONS", "QTY", "MASS(est.)", "STANDARD", "NOTES" };
        double rowH = 13;
        double tableW = widths.Sum();

        // ── BOM rows — all sizes from actual calculated results ───
        var bomRows = new[]
        {
            new {
                Item = "1",
                Desc = "Casing Assembly",
                Mat  = "IS 2062 Mild Steel",
                Size = $"ID={casR*2:F0}x L={casL:F0}x t={casT:F0}mm",
                Qty  = "1",
                Mass = $"{d.TipDiameterMm * 0.015:F1} kg",
                Std  = "IS 2062 Gr.A",
                Note = "Welded, rolled plate"
            },
            new {
                Item = "2",
                Desc = $"Blade NACA 4412 (Set of {d.BladeCount})",
                Mat  = "Al Alloy 6061-T6",
                Size = $"Chord={r.ChordLengthMm:F1}mm Span={r.BladeSpanMm:F1}mm t/c=12%",
                Qty  = $"{d.BladeCount}",
                Mass = $"{r.ChordLengthMm * 0.02:F2} kg ea",
                Std  = "IS 733",
                Note = $"b={d.BladeAngleDeg:F1}deg Tip D={d.TipDiameterMm:F0}mm"
            },
            new {
                Item = "3",
                Desc = "Hub Assembly",
                Mat  = "Al Alloy 6061-T6",
                Size = $"OD={r.HubDiameterMm:F0}mm ID={shaftD:F0}mm L={hubL:F0}mm",
                Qty  = "1",
                Mass = $"{r.HubDiameterMm * 0.012:F1} kg",
                Std  = "IS 733",
                Note = $"Hub ratio={d.HubRatio:F2} PCD={boltPCD:F0}mm"
            },
            new {
                Item = "4",
                Desc = "Shaft",
                Mat  = "EN8 / C45 Steel",
                Size = $"D={shaftD:F0}mm L={hubL + 80:F0}mm",
                Qty  = "1",
                Mass = $"{shaftD * 0.008:F1} kg",
                Std  = "IS 2073 EN8",
                Note = $"Keyway {shaftD*0.25:F0}x{shaftD*0.15:F0}mm"
            },
            new {
                Item = "5",
                Desc = $"Motor {d.MotorPowerKw:F2} kW",
                Mat  = "TEFC Induction Motor",
                Size = $"Frame D={motD:F0}mm L={motL:F0}mm (est.)",
                Qty  = "1",
                Mass = $"{d.MotorPowerKw * 8:F0} kg",
                Std  = "IS 12615 IE2",
                Note = $"{d.MotorPoles}  N={d.SpeedRpm}RPM"
            },
            new {
                Item = "6",
                Desc = "Motor Mounting Plate",
                Mat  = "IS 2062 Mild Steel",
                Size = $"D={(casR+casT)*2:F0}mm t=10mm",
                Qty  = "1",
                Mass = $"{(casR + casT) * 0.005:F1} kg",
                Std  = "IS 2062 Gr.A",
                Note = "4xM12 bolt holes"
            },
            new {
                Item = "7",
                Desc = "Mounting Frame / Base",
                Mat  = "IS 2062 Mild Steel",
                Size = $"{frameW:F0}x{frameW:F0}x150mm angle",
                Qty  = "1",
                Mass = "8.0 kg",
                Std  = "IS 2062 Gr.A",
                Note = "Bolted, painted"
            },
            new {
                Item = "8",
                Desc = "Blade Fixing Bolts",
                Mat  = "Carbon Steel Gr.8.8",
                Size = "M8 x 30mm",
                Qty  = $"{d.BladeCount * 4}",
                Mass = "0.05 kg ea",
                Std  = "IS 1367 Gr.8.8",
                Note = "With spring washer+nut"
            },
            new {
                Item = "9",
                Desc = "Casing Mounting Bolts",
                Mat  = "Carbon Steel Gr.8.8",
                Size = "M12 x 50mm",
                Qty  = "16",
                Mass = "0.10 kg ea",
                Std  = "IS 1367 Gr.8.8",
                Note = "With flat+spring washer"
            },
            new {
                Item = "10",
                Desc = "Shaft Bearing (Pair)",
                Mat  = "Bearing Steel",
                Size = $"ID={shaftD:F0}mm OD={shaftD*2.2:F0}mm",
                Qty  = "2",
                Mass = $"{shaftD * 0.003:F2} kg ea",
                Std  = "SKF/FAG 62 series",
                Note = "Deep groove ball bearing"
            },
        };

        // ── Draw BOM title ────────────────────────────────────────
        AddBox(doc, ox, oy - rowH, ox + tableW, oy, "TITLE_BLOCK");
        doc.Entities.Add(MakeText("BILL OF MATERIALS", 5.5,
            ox + tableW / 2 - 50, oy + 5, doc.Layers["TITLE_BLOCK"]));

        // ── Draw header row ───────────────────────────────────────
        double hY = oy - rowH;
        double cx = ox;
        foreach (var (h, w) in headers.Zip(widths))
        {
            AddBox(doc, cx, hY - rowH, cx + w, hY, "DIMENSIONS");
            doc.Entities.Add(MakeText(h, 3.2, cx + 2, hY - rowH + 4, doc.Layers["DIMENSIONS"]));
            cx += w;
        }

        // ── Draw data rows ────────────────────────────────────────
        double rowY = hY - rowH;
        int rowIdx = 0;
        foreach (var row in bomRows)
        {
            string[] vals = { row.Item, row.Desc, row.Mat, row.Size, row.Qty, row.Mass, row.Std, row.Note };
            cx = ox;
            string rowLayer = (rowIdx % 2 == 0) ? "HUB" : "CASING";
            foreach (var (v, w) in vals.Zip(widths))
            {
                AddBox(doc, cx, rowY - rowH, cx + w, rowY, rowLayer);
                doc.Entities.Add(MakeText(v, 3.0, cx + 2, rowY - rowH + 4, doc.Layers["ANNOTATION"]));
                cx += w;
            }
            rowY -= rowH;
            rowIdx++;
        }

        // ── Total mass estimate ───────────────────────────────────
        double totalMass = d.TipDiameterMm * 0.015
                         + d.BladeCount * r.ChordLengthMm * 0.02
                         + r.HubDiameterMm * 0.012
                         + shaftD * 0.008
                         + d.MotorPowerKw * 8
                         + 8.0 + 5.0;
        AddBox(doc, ox, rowY - rowH, ox + tableW, rowY, "TITLE_BLOCK");
        doc.Entities.Add(MakeText(
            $"ESTIMATED TOTAL ASSEMBLY MASS:  {totalMass:F1} kg   |   " +
            $"Fan OD: {d.TipDiameterMm:F0}mm   |   " +
            $"Q: {d.FlowRateM3s:F3} m3/s   |   " +
            $"dP: {d.TotalPressurePa:F0} Pa   |   " +
            $"N: {d.SpeedRpm} RPM",
            3.5, ox + 4, rowY - rowH + 4, doc.Layers["TITLE_BLOCK"]));
        rowY -= rowH;

        return new double[] { ox, rowY };
    }

    // ══════════════════════════════════════════════════════════════
    // Private drawing helpers
    // ══════════════════════════════════════════════════════════════

    private static void AddLine(DxfDocument doc, double x1, double y1, double x2, double y2, string layerName)
    {
        doc.Entities.Add(new Line(new Vector3(x1, y1, 0), new Vector3(x2, y2, 0))
        { Layer = doc.Layers[layerName] });
    }

    private static void AddBox(DxfDocument doc, double x1, double y1, double x2, double y2, string layerName)
    {
        doc.Entities.Add(new Polyline2D(new List<Polyline2DVertex>
        {
            new(x1, y1), new(x2, y1), new(x2, y2), new(x1, y2)
        }, true)
        { Layer = doc.Layers[layerName] });
    }

    private static void AddDimHoriz(DxfDocument doc, double x1, double x2, double y, string label, double h)
    {
        double tk = h * 1.2;
        doc.Entities.Add(new Line(new Vector3(x1, y, 0), new Vector3(x2, y, 0)) { Layer = doc.Layers["DIMENSIONS"] });
        doc.Entities.Add(new Line(new Vector3(x1, y - tk, 0), new Vector3(x1, y + tk, 0)) { Layer = doc.Layers["DIMENSIONS"] });
        doc.Entities.Add(new Line(new Vector3(x2, y - tk, 0), new Vector3(x2, y + tk, 0)) { Layer = doc.Layers["DIMENSIONS"] });
        doc.Entities.Add(MakeText(label, h, (x1 + x2) / 2 - label.Length * h * 0.28, y - h * 2.2, doc.Layers["DIMENSIONS"]));
    }

    private static void AddDimVert(DxfDocument doc, double x, double y1, double y2, string label, double h)
    {
        double tk = h * 1.2;
        doc.Entities.Add(new Line(new Vector3(x, y1, 0), new Vector3(x, y2, 0)) { Layer = doc.Layers["DIMENSIONS"] });
        doc.Entities.Add(new Line(new Vector3(x - tk, y1, 0), new Vector3(x + tk, y1, 0)) { Layer = doc.Layers["DIMENSIONS"] });
        doc.Entities.Add(new Line(new Vector3(x - tk, y2, 0), new Vector3(x + tk, y2, 0)) { Layer = doc.Layers["DIMENSIONS"] });
        doc.Entities.Add(MakeText(label, h, x + h, (y1 + y2) / 2 - h / 2, doc.Layers["DIMENSIONS"]));
    }

    private static void AddTitleBlock(DxfDocument doc, string dwgNo, string title, string subtitle, double refSize)
    {
        double y = -(refSize + 35);
        doc.Entities.Add(MakeText($"{dwgNo}  {title}", 6.0, 0, y, doc.Layers["TITLE_BLOCK"]));
        doc.Entities.Add(MakeText(subtitle, 4.5, 0, y - 10, doc.Layers["ANNOTATION"]));
        doc.Entities.Add(MakeText("AxialFlow Designer  |  For Engineering Reference Only", 3.5, 0, y - 20, doc.Layers["ANNOTATION"]));
    }

    private static DxfDocument CreateDocument()
    {
        var doc = new DxfDocument();

        var dashed = new Linetype("DASHED", new LinetypeSegment[]
            { new LinetypeSimpleSegment(6.0), new LinetypeSimpleSegment(-2.0) }, "Dashed");
        var center = new Linetype("CENTER", new LinetypeSegment[]
            { new LinetypeSimpleSegment(12.0), new LinetypeSimpleSegment(-3.0),
              new LinetypeSimpleSegment(2.0),  new LinetypeSimpleSegment(-3.0) }, "Center");
        doc.Linetypes.Add(dashed);
        doc.Linetypes.Add(center);

        doc.Layers.Add(new Layer("CASING") { Color = new AciColor(8) });
        doc.Layers.Add(new Layer("HUB") { Color = new AciColor(9) });
        doc.Layers.Add(new Layer("BLADES") { Color = new AciColor(5) });
        doc.Layers.Add(new Layer("CENTERLINE") { Color = AciColor.Red });
        doc.Layers.Add(new Layer("TIP_DIAMETER") { Color = AciColor.Yellow });
        doc.Layers.Add(new Layer("TIP_CLEARANCE") { Color = AciColor.Yellow });
        doc.Layers.Add(new Layer("BLADE_SURFACE") { Color = new AciColor(5) });
        doc.Layers.Add(new Layer("CAMBER_LINE") { Color = new AciColor(4) });
        doc.Layers.Add(new Layer("STATION_TICKS") { Color = new AciColor(9) });
        doc.Layers.Add(new Layer("DIMENSIONS") { Color = AciColor.Default });
        doc.Layers.Add(new Layer("ANNOTATION") { Color = AciColor.Green });
        doc.Layers.Add(new Layer("TITLE_BLOCK") { Color = new AciColor(5) });

        return doc;
    }

    private static Text MakeText(string content, double height, double x, double y, Layer layer)
        => new Text(content, new Vector2(x, y), height) { Layer = layer };

    private static byte[] SaveToBytes(DxfDocument doc)
    {
        using var ms = new MemoryStream();
        doc.Save(ms);
        return ms.ToArray();
    }

    private static double InterpolateProfile(List<ModelPoint2D> pts, double x)
    {
        for (int i = 0; i < pts.Count - 1; i++)
            if (pts[i].X <= x && pts[i + 1].X >= x)
            {
                double t = (x - pts[i].X) / (pts[i + 1].X - pts[i].X + 1e-12);
                return pts[i].Y + t * (pts[i + 1].Y - pts[i].Y);
            }
        return 0;
    }
}

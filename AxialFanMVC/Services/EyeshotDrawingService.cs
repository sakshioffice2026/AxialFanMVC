//using AxialFanMVC.Database;
//using devDept.Eyeshot;
//using devDept.Eyeshot.Entities;
//using devDept.Eyeshot.Translators;
//using devDept.Geometry;
//using netDxf.GTE;

//namespace AxialFanMVC.Services;

//public static class EyeshotDrawingService
//{
//    // ── DWG-001 Front Elevation ──────────────────────────────────
//    //    public static byte[] FrontElevationDwg(DesignInput d, DesignResult r)
//    //    {
//    //        DesignDocument model = new DesignDocument();

//    //        double tipR = d.TipDiameterMm / 2.0;
//    //        double hubR = tipR * d.HubRatio;
//    //        double bladeW = Math.Min(14.0, (tipR - hubR) * 0.18);
//    //        int blades = d.BladeCount;

//    //        // Add layers
//    //        model.Layers.Add(new Layer("CASING", System.Drawing.Color.Gray));
//    //        model.Layers.Add(new Layer("BLADES", System.Drawing.Color.Blue));
//    //        model.Layers.Add(new Layer("HUB", System.Drawing.Color.DarkGray));
//    //        model.Layers.Add(new Layer("DIMS", System.Drawing.Color.Black));
//    //        model.Layers.Add(new Layer("CENTER", System.Drawing.Color.Red));

//    //        // Outer casing circle
//    //        model.Entities.Add(new Circle(Point3D.Origin, tipR + 15)
//    //        { LayerName = "CASING" });

//    //        // Hub circle
//    //        model.Entities.Add(new Circle(Point3D.Origin, hubR)
//    //        { LayerName = "HUB" });

//    //        // Blades — polylines
//    //        for (int i = 0; i < blades; i++)
//    //        {
//    //            double a = i * 2 * Math.PI / blades;
//    //            double pitch = d.BladeAngleDeg * Math.PI / 180.0;
//    //            double pAngle = a + Math.PI / 2.0 + pitch;
//    //            double pCos = Math.Cos(pAngle), pSin = Math.Sin(pAngle);
//    //            double rCos = Math.Cos(a), rSin = Math.Sin(a);
//    //            double hw = bladeW / 2.0;

//    //            LinearPath pts = new LinearPath(new Point3D[]
//    //            {
//    //                new Point3D(hubR*rCos - hw*pCos, hubR*rSin - hw*pSin, 0),
//    //                new Point3D(hubR*rCos + hw*pCos, hubR*rSin + hw*pSin, 0),
//    //                new Point3D(tipR*rCos + hw*pCos, tipR*rSin + hw*pSin, 0),
//    //                new Point3D(tipR*rCos - hw*pCos, tipR*rSin - hw*pSin, 0),
//    //            });
//    //            model.Entities.Add(pts);
//    //        }

//    //        // Centre cross
//    //        model.Entities.Add(new Line(
//    //            new Point3D(-tipR * 1.1, 0, 0), new Point3D(tipR * 1.1, 0, 0))
//    //        { LayerName = "CENTER" });
//    //        model.Entities.Add(new Line(
//    //            new Point3D(0, -tipR * 1.1, 0), new Point3D(0, tipR * 1.1, 0))
//    //        { LayerName = "CENTER" });

//    //        LinearDim tipDiaDim = new LinearDim(
//    //      Plane.XY,
//    //      new Point3D(-tipR, 0, 0),
//    //      new Point3D(tipR, 0, 0),
//    //      new Point3D(0, -(tipR + 35), 0),
//    //      10
//    //  )
//    //        {
//    //            LayerName = "DIMS",
//    //            ExtLineOffset = 0
//    //        };

//    //        model.Entities.Add(tipDiaDim);
//    //        Text titleText = new Text(
//    //      new Point3D(0, -(tipR + 55), 0), // center align instead of -tipR
//    //      $"DWG-001  FRONT ELEVATION   D={d.TipDiameterMm:F0}mm  Z={blades}  β={d.BladeAngleDeg:F1}°",
//    //      8.0
//    //  )
//    //        {
//    //            LayerName = "DIMS",
//    //            Alignment = Text.alignmentType.MiddleCenter
//    //        };

//    //        model.Entities.Add(titleText);

//    //        return ToBytes(model, "dwg");
//    //    }

//    //    // ── DWG-002 Cross Section ────────────────────────────────────
//    //    public static byte[] CrossSectionDwg(DesignInput d, DesignResult r)
//    //    {
//    //        DesignDocument model = new DesignDocument();
//    //        SetupLayers(model);

//    //        double tipR = d.TipDiameterMm / 2.0;
//    //        double hubR = r.HubDiameterMm / 2.0;
//    //        double axLen = d.TipDiameterMm * 1.2;
//    //        double chordPx = Math.Min(r.ChordLengthMm, axLen * 0.35);

//    //        // Casing walls (top + bottom)
//    //        AddBox(model, 0, tipR, axLen, tipR + 20, "CASING");
//    //        AddBox(model, 0, -tipR - 20, axLen, -tipR, "CASING");

//    //        // Hub cylinder
//    //        AddBox(model, 0, -hubR, axLen, hubR, "HUB");

//    //        // Blade chord rectangle
//    //        double bx = axLen * 0.38;
//    //        AddBox(model, bx, hubR, bx + chordPx, tipR, "BLADES");
//    //        AddBox(model, bx, -tipR, bx + chordPx, -hubR, "BLADES");

//    //        // Centreline
//    //        model.Entities.Add(new Line(
//    //            new Point3D(-30, 0, 0), new Point3D(axLen + 30, 0, 0))
//    //        { LayerName = "CENTER" });

//    //        LinearDim tipDiaDim = new LinearDim(
//    //             Plane.XY,
//    //             new Point3D(axLen + 20, -tipR, 0),
//    //             new Point3D(axLen + 20, tipR, 0),
//    //             new Point3D(axLen + 40, 0, 0), // offset point (shifted right)
//    //             10
//    //         )
//    //        {
//    //            LayerName = "DIMS",
//    //            ExtLineOffset = 0
//    //        };

//    //        model.Entities.Add(tipDiaDim);

//    //        LinearDim chordDim = new LinearDim(
//    //                Plane.XY,
//    //                new Point3D(bx, hubR, 0),
//    //                new Point3D(bx + chordPx, hubR, 0),
//    //                new Point3D(bx + chordPx / 2, hubR - 20, 0),
//    //                10
//    //            )
//    //        {
//    //            LayerName = "DIMS",
//    //            ExtLineOffset = 0
//    //        };

//    //        model.Entities.Add(chordDim);
//    //        Text titleText = new Text(
//    //     new Point3D(0, -(tipR + 50), 0),
//    //     $"DWG-002  CROSS SECTION   Span={r.BladeSpanMm:F1}mm  Chord={r.ChordLengthMm:F1}mm",
//    //     8.0
//    // )
//    //        {
//    //            LayerName = "DIMS",
//    //            Alignment = Text.alignmentType.MiddleCenter
//    //        };

//    //        model.Entities.Add(titleText);

//    //        return ToBytes(model, "dwg");
//    //    }

//    //    // ── DWG-003 Blade Aerofoil Profile ──────────────────────────
//    //    public static byte[] BladeProfileDwg(DesignInput d, DesignResult r)
//    //    {
//    //        DesignDocument model = new DesignDocument();
//    //        SetupLayers(model);

//    //        double chord = r.ChordLengthMm;

//    //        // NACA 4412 upper/lower surface points
//    //        double[] xn = { 0, .05, .10, .15, .20, .25, .30, .35, .40, .45, .50, .55, .60, .65, .70, .75, .80, .85, .90, .95, 1.0 };
//    //        double[] yU = { 0, .126, .200, .233, .248, .252, .247, .234, .214, .188, .158, .128, .098, .070, .046, .026, .011, .002, 0, 0, 0 };
//    //        double[] yL = { 0, -.058, -.073, -.076, -.074, -.068, -.059, -.049, -.038, -.027, -.018, -.010, -.004, .001, .004, .005, .005, .003, 0, 0, 0 };

//    //        var upperPts = xn.Zip(yU, (x, y) => new Point3D(x * chord, y * chord, 0)).ToArray();
//    //        var lowerPts = xn.Zip(yL, (x, y) => new Point3D(x * chord, y * chord, 0)).ToArray();

//    //        model.Entities.Add(new LinearPath(upperPts) { LayerName = "BLADES" });
//    //        model.Entities.Add(new LinearPath(lowerPts) { LayerName = "BLADES" });

//    //        // Chord reference line
//    //        model.Entities.Add(new Line(
//    //            new Point3D(0, 0, 0), new Point3D(chord, 0, 0))
//    //        { LayerName = "CENTER" });

//    //        // LE / TE markers
//    //        model.Entities.Add(new Text(
//    //            new Point3D(-5, 5, 0),
//    //            "LE",
//    //            5.0)
//    //        {
//    //            LayerName = "DIMS",
//    //            Alignment = Text.alignmentType.MiddleCenter
//    //        });

//    //        model.Entities.Add(new Text(
//    //            new Point3D(chord + 3, 5, 0),
//    //            "TE",
//    //            5.0)
//    //        {
//    //            LayerName = "DIMS",
//    //            Alignment = Text.alignmentType.MiddleCenter
//    //        });

//    //        LinearDim chordDim = new LinearDim(
//    //    Plane.XY,
//    //    new Point3D(0, 0, 0),
//    //    new Point3D(chord, 0, 0),
//    //    new Point3D(chord / 2, -chord * 0.18, 0),
//    //    10
//    //)
//    //        {
//    //            LayerName = "DIMS",
//    //            ExtLineOffset = 0
//    //        };

//    //        // Optional: override text if you want "Chord = ..."
//    //        chordDim.TextOverride = $"Chord = {chord:F1} mm";

//    //        model.Entities.Add(chordDim);

//    //        Text titleText = new Text(
//    //     new Point3D(chord / 2, -chord * 0.30, 0), // center for better alignment
//    //     $"DWG-003  BLADE PROFILE NACA 4412  β={d.BladeAngleDeg:F1}°",
//    //     7.0
//    // )
//    //        {
//    //            LayerName = "DIMS",
//    //            Alignment = Text.alignmentType.MiddleCenter
//    //        };

//    //        model.Entities.Add(titleText);

//    //        return ToBytes(model, "dwg");
//    //    }

//    // ── Helpers ──────────────────────────────────────────────────
//    public static byte[] FrontElevationDwg(DesignInput d, DesignResult r)
//    {
//        DesignDocument model = new DesignDocument();
//        SetupLayers(model);

//        double tipR = d.TipDiameterMm / 2.0;
//        double hubR = tipR * d.HubRatio;
//        double bladeW = Math.Min(14.0, (tipR - hubR) * 0.18);
//        int blades = d.BladeCount;

//        // Outer casing
//        model.Entities.Add(new Circle(Point3D.Origin, tipR + 15)
//        { LayerName = "CASING" });

//        // Hub
//        model.Entities.Add(new Circle(Point3D.Origin, hubR)
//        { LayerName = "HUB" });

//        // Blades
//        for (int i = 0; i < blades; i++)
//        {
//            double a = i * 2 * Math.PI / blades;
//            double pitch = d.BladeAngleDeg * Math.PI / 180.0;

//            double pAngle = a + Math.PI / 2.0 + pitch;
//            double pCos = Math.Cos(pAngle), pSin = Math.Sin(pAngle);
//            double rCos = Math.Cos(a), rSin = Math.Sin(a);
//            double hw = bladeW / 2.0;

//            model.Entities.Add(new LinearPath(new[]
//            {
//            new Point3D(hubR*rCos - hw*pCos, hubR*rSin - hw*pSin, 0),
//            new Point3D(hubR*rCos + hw*pCos, hubR*rSin + hw*pSin, 0),
//            new Point3D(tipR*rCos + hw*pCos, tipR*rSin + hw*pSin, 0),
//            new Point3D(tipR*rCos - hw*pCos, tipR*rSin - hw*pSin, 0)
//        })
//            { LayerName = "BLADES" });
//        }

//        // Center cross
//        model.Entities.Add(new Line(new Point3D(-tipR, 0), new Point3D(tipR, 0)) { LayerName = "CENTER" });
//        model.Entities.Add(new Line(new Point3D(0, -tipR), new Point3D(0, tipR)) { LayerName = "CENTER" });

//        // Diameter dimension
//        var dim = new LinearDim(
//            Plane.XY,
//            new Point3D(-tipR, 0),
//            new Point3D(tipR, 0),
//            new Point3D(0, -(tipR + 40), 0),
//            10)
//        { LayerName = "DIMS" };

//        dim.TextOverride = $"⌀ {d.TipDiameterMm:F0} mm";
//        model.Entities.Add(dim);

//        // Title
//        model.Entities.Add(new Text(
//            new Point3D(0, -(tipR + 65), 0),
//            $"DWG-001  FRONT ELEVATION   Z={blades}  β={d.BladeAngleDeg:F1}°",
//            8)
//        {
//            LayerName = "DIMS",
//            Alignment = Text.alignmentType.MiddleCenter
//        });

//        return ToBytes(model, "dwg");
//    }
//    public static byte[] CrossSectionDwg(DesignInput d, DesignResult r)
//    {
//        DesignDocument model = new DesignDocument();
//        SetupLayers(model);

//        double tipR = d.TipDiameterMm / 2.0;
//        double hubR = r.HubDiameterMm / 2.0;
//        double axLen = d.TipDiameterMm * 1.2;
//        double chordPx = Math.Min(r.ChordLengthMm, axLen * 0.35);

//        double bx = axLen * 0.38;

//        // Casing
//        AddBox(model, 0, tipR, axLen, tipR + 20, "CASING");
//        AddBox(model, 0, -tipR - 20, axLen, -tipR, "CASING");

//        // Hub
//        AddBox(model, 0, -hubR, axLen, hubR, "HUB");

//        // Blade block
//        AddBox(model, bx, hubR, bx + chordPx, tipR, "BLADES");
//        AddBox(model, bx, -tipR, bx + chordPx, -hubR, "BLADES");

//        // Centerlines
//        model.Entities.Add(new Line(new Point3D(-30, 0), new Point3D(axLen + 30, 0)) { LayerName = "CENTER" });
//        model.Entities.Add(new Line(new Point3D(axLen / 2, -tipR - 30), new Point3D(axLen / 2, tipR + 30)) { LayerName = "CENTER" });

//        // Tip diameter
//        var tipDim = new LinearDim(
//            Plane.XY,
//            new Point3D(axLen + 20, -tipR),
//            new Point3D(axLen + 20, tipR),
//            new Point3D(axLen + 40, 0),
//            10)
//        { LayerName = "DIMS" };

//        tipDim.TextOverride = $"⌀ {d.TipDiameterMm:F0} mm";
//        model.Entities.Add(tipDim);

//        // Hub diameter
//        var hubDim = new LinearDim(
//            Plane.XY,
//            new Point3D(axLen + 60, -hubR),
//            new Point3D(axLen + 60, hubR),
//            new Point3D(axLen + 80, 0),
//            10)
//        { LayerName = "DIMS" };

//        hubDim.TextOverride = $"⌀ {r.HubDiameterMm:F0} mm";
//        model.Entities.Add(hubDim);

//        // Chord
//        var chordDim = new LinearDim(
//            Plane.XY,
//            new Point3D(bx, hubR),
//            new Point3D(bx + chordPx, hubR),
//            new Point3D(bx + chordPx / 2, hubR - 25),
//            10)
//        { LayerName = "DIMS" };

//        chordDim.TextOverride = $"{r.ChordLengthMm:F1} mm";
//        model.Entities.Add(chordDim);

//        // Title
//        model.Entities.Add(new Text(
//            new Point3D(axLen / 2, -(tipR + 70), 0),
//            $"DWG-002  CROSS SECTION   Span={r.BladeSpanMm:F1}mm",
//            8)
//        {
//            LayerName = "DIMS",
//            Alignment = Text.alignmentType.MiddleCenter
//        });

//        return ToBytes(model, "dwg");
//    }
//    public static byte[] BladeProfileDwg(DesignInput d, DesignResult r)
//    {
//        DesignDocument model = new DesignDocument();
//        SetupLayers(model);

//        double chord = r.ChordLengthMm;

//        double[] xn = { 0, .05, .10, .15, .20, .25, .30, .35, .40, .45, .50, .55, .60, .65, .70, .75, .80, .85, .90, .95, 1.0 };
//        double[] yU = { 0, .126, .200, .233, .248, .252, .247, .234, .214, .188, .158, .128, .098, .070, .046, .026, .011, .002, 0, 0, 0 };
//        double[] yL = { 0, -.058, -.073, -.076, -.074, -.068, -.059, -.049, -.038, -.027, -.018, -.010, -.004, .001, .004, .005, .005, .003, 0, 0, 0 };

//        var upperPts = xn.Zip(yU, (x, y) => new Point3D(x * chord, y * chord, 0)).ToArray();
//        var lowerPts = xn.Zip(yL, (x, y) => new Point3D(x * chord, y * chord, 0)).ToArray();

//        // CLOSED AIRFOIL
//        var airfoil = upperPts.Concat(lowerPts.Reverse()).ToArray();

//        model.Entities.Add(new LinearPath(airfoil)
//        { LayerName = "BLADES" });

//        // Chord line
//        model.Entities.Add(new Line(new Point3D(0, 0), new Point3D(chord, 0))
//        { LayerName = "CENTER" });

//        // LE / TE
//        model.Entities.Add(new Text(new Point3D(-8, 8), "LE", 5)
//        { LayerName = "DIMS", Alignment = Text.alignmentType.MiddleCenter });

//        model.Entities.Add(new Text(new Point3D(chord + 8, 8), "TE", 5)
//        { LayerName = "DIMS", Alignment = Text.alignmentType.MiddleCenter });

//        // Chord dimension
//        var dim = new LinearDim(
//            Plane.XY,
//            new Point3D(0, 0),
//            new Point3D(chord, 0),
//            new Point3D(chord / 2, -chord * 0.2),
//            10)
//        { LayerName = "DIMS" };

//        dim.TextOverride = $"{chord:F1} mm";
//        model.Entities.Add(dim);

//        // Title
//        model.Entities.Add(new Text(
//            new Point3D(chord / 2, -chord * 0.35),
//            $"DWG-003  BLADE PROFILE NACA 4412  β={d.BladeAngleDeg:F1}°",
//            7)
//        {
//            LayerName = "DIMS",
//            Alignment = Text.alignmentType.MiddleCenter
//        });

//        return ToBytes(model, "dwg");
//    }
//    private static void SetupLayers(DesignDocument model)
//    {
//        model.Layers.Add(new Layer("CASING", System.Drawing.Color.Gray));
//        model.Layers.Add(new Layer("BLADES", System.Drawing.Color.Blue));
//        model.Layers.Add(new Layer("HUB", System.Drawing.Color.DarkGray));
//        model.Layers.Add(new Layer("DIMS", System.Drawing.Color.Black));
//        model.Layers.Add(new Layer("CENTER", System.Drawing.Color.Red));
//    }

//    private static void AddBox(DesignDocument m,
//        double x1, double y1, double x2, double y2, string layer)
//    {
//        m.Entities.Add(new LinearPath(new[]
//        {
//            new Point3D(x1,y1,0), new Point3D(x2,y1,0),
//            new Point3D(x2,y2,0), new Point3D(x1,y2,0)
//        })
//        { LayerName = layer });
//    }

//    private static byte[] ToBytes(DesignDocument model, string format)
//    {
//        string tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

//        if (format == "dwg")
//        {
//            string filePath = tempPath + ".dwg";

//            WriteAutodeskParams auto = new WriteAutodeskParams(model);
//            WriteAutodesk writer = new WriteAutodesk(auto, filePath);
//            writer.DoWork();

//            return File.ReadAllBytes(filePath);
//        }
//        else if (format == "pdf")
//        {
//            string filePath = tempPath + ".pdf";

//            Write3DPdfParams pdfParams = new Write3DPdfParams(model);
//            Write3DPDF writer = new Write3DPDF(pdfParams, filePath);
//            writer.DoWork();

//            return File.ReadAllBytes(filePath);
//        }
//        //else if (format == "step")
//        //{
//        //    string filePath = tempPath + ".step";
          
//        //    WriteSTEP writer = new WriteSTEP(model);
//        //    writer.DoWork(writer, filePath);
//        //    return File.ReadAllBytes(filePath);
//        //}

//        return Array.Empty<byte>();
//    }
//}
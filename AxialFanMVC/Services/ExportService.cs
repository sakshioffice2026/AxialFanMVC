using AxialFanMVC.Database;
using QuestPDF.Fluent;
using AxialFanMVC.Models;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Text;
using SkiaSharp;

namespace AxialFanMVC.Services;

public class ExportService
{
    private readonly string _exportDir;

    public ExportService(IConfiguration config, IWebHostEnvironment env)
    {
        // QuestPDF.Settings.License is a process-wide static setting.
        // Set it once here so every PDF-generating method in this class
        // (and any other QuestPDF usage in the app) is covered — instead
        // of relying on individual controller actions to set it inline
        // right before calling GeneratePdf().
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

        var relDir = config["AppSettings:ExportDirectory"]
                     ?? "wwwroot/exports";
        _exportDir = Path.Combine(env.ContentRootPath, relDir);
        Directory.CreateDirectory(_exportDir);
    }

    // ── CSV Export ────────────────────────────────────────────────
    public (byte[] Bytes, string FileName) ExportCsv(DesignResult result)
    {
        var di = result.DesignInput;
        var sb = new StringBuilder();

        sb.AppendLine("AxialFlow Designer - Design Report");
        sb.AppendLine($"Project,{di.Project?.Name ?? ""}");
        sb.AppendLine($"Calculated,{result.CalculatedAt:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Status,{result.Status}");
        sb.AppendLine();

        sb.AppendLine("INPUT PARAMETERS");
        sb.AppendLine("Parameter,Value,Unit");
        sb.AppendLine($"Media Type,{di.MediaType},");
        sb.AppendLine($"Temperature,{di.TemperatureCelsius},°C");
        sb.AppendLine($"Inlet Pressure,{di.InletPressurePa},Pa");
        sb.AppendLine($"Air Density,{di.DensityKgM3},kg/m3");
        sb.AppendLine($"Flow Rate,{di.FlowRateM3s},m3/s");
        sb.AppendLine($"Static Pressure,{di.StaticPressurePa},Pa");
        sb.AppendLine($"Total Pressure Rise,{di.TotalPressurePa},Pa");
        sb.AppendLine($"Fan Speed,{di.SpeedRpm},RPM");
        sb.AppendLine($"Motor Configuration,{di.MotorPoles},");
        sb.AppendLine($"Blade Count,{di.BladeCount},");
        sb.AppendLine($"Tip Diameter,{di.TipDiameterMm},mm");
        sb.AppendLine($"Hub Ratio,{di.HubRatio},");
        sb.AppendLine($"Blade Angle,{di.BladeAngleDeg},deg");
        sb.AppendLine($"Target Efficiency,{di.TargetEfficiencyPct},%");
        sb.AppendLine($"Motor Power,{di.MotorPowerKw},kW");
        sb.AppendLine();

        sb.AppendLine("AERODYNAMIC RESULTS");
        sb.AppendLine("Parameter,Value,Unit");
        sb.AppendLine($"Specific Speed,{result.SpecificSpeed:F4},");
        sb.AppendLine($"Tip Speed,{result.TipSpeedMs:F2},m/s");
        sb.AppendLine($"Hub Diameter,{result.HubDiameterMm:F1},mm");
        sb.AppendLine($"Blade Span,{result.BladeSpanMm:F1},mm");
        sb.AppendLine($"Chord Length,{result.ChordLengthMm:F1},mm");
        sb.AppendLine($"Flow Coefficient,{result.FlowCoefficient:F4},");
        sb.AppendLine($"Pressure Coefficient,{result.PressureCoefficient:F4},");
        sb.AppendLine($"Overall Efficiency,{result.OverallEfficiencyPct:F2},%");
        sb.AppendLine($"Shaft Power,{result.ShaftPowerKw:F3},kW");
        sb.AppendLine();

        sb.AppendLine("STRUCTURAL RESULTS");
        sb.AppendLine("Parameter,Value,Unit");
        sb.AppendLine("Material,Al 6061-T6,");
        sb.AppendLine("Yield Strength,270,MPa");
        sb.AppendLine($"Tip Clearance,{result.TipClearanceMm:F1},mm");
        sb.AppendLine($"Blade Stress,{result.BladeStressMpa:F2},MPa");
        sb.AppendLine($"Safety Factor,{result.SafetyFactor:F2},");
        sb.AppendLine();

        sb.AppendLine("ACOUSTIC RESULTS");
        sb.AppendLine("Parameter,Value,Unit");
        sb.AppendLine($"Overall Noise (Lp),{result.OverallNoiseDbA:F1},dB(A)");
        sb.AppendLine($"Sound Power Level (Lw),{result.SoundPowerLevelDb:F1},dB");
        sb.AppendLine($"Blade Passing Frequency,{result.BladePassingFrequencyHz:F1},Hz");
        sb.AppendLine($"Tip Mach Number,{result.TipMachNumber:F3},");
        sb.AppendLine($"Noise Rating (NR),{result.NoiseRatingValue:F1} ({result.NoiseRating}),");
        sb.AppendLine();

        // Performance curve data
        var curve = result.PerformanceCurves?.FirstOrDefault();
        if (curve != null)
        {
            sb.AppendLine($"PERFORMANCE CURVE " +
                          $"(Angle={curve.BladeAngleDeg} deg  " +
                          $"RPM={curve.SpeedRpm})");
            sb.AppendLine("Q (m3/s),dP (Pa),Eta (%),Power (kW)");

            var qArr = curve.QValues.Split(',');
            var dpArr = curve.DpValues.Split(',');
            var etaArr = curve.EtaValues.Split(',');
            var kwArr = curve.KwValues.Split(',');

            for (int i = 0; i < qArr.Length; i++)
                sb.AppendLine($"{qArr[i]},{dpArr[i]}," +
                              $"{etaArr[i]},{kwArr[i]}");
        }

        return (Encoding.UTF8.GetBytes(sb.ToString()),
                $"AxialFan_Result_{result.Id}_" +
                $"{result.CalculatedAt:yyyyMMdd}.csv");
    }

    // ── HTML Report Export ────────────────────────────────────────
    public (byte[] Bytes, string FileName) ExportHtmlReport(
        DesignResult result)
    {
        var di = result.DesignInput;
        var warnings = result.WarningMessages != null
            ? System.Text.Json.JsonSerializer
                .Deserialize<List<string>>(result.WarningMessages)
              ?? new()
            : new List<string>();

        var sb = new StringBuilder();
        sb.AppendLine($@"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""utf-8""/>
<title>AxialFlow Report #{result.Id}</title>
<style>
  body  {{ font-family:Arial,sans-serif; font-size:11pt;
           margin:20mm; color:#222; }}
  h1    {{ font-size:18pt; color:#1864ab;
           border-bottom:2px solid #1864ab;
           padding-bottom:6px; }}
  h2    {{ font-size:13pt; color:#495057; margin-top:20px;
           border-left:4px solid #1864ab;
           padding-left:8px; }}
  table {{ border-collapse:collapse; width:100%; margin:10px 0; }}
  th    {{ background:#1864ab; color:white; padding:6px 10px;
           text-align:left; font-size:10pt; }}
  td    {{ padding:5px 10px;
           border-bottom:1px solid #dee2e6;
           font-size:10pt; }}
  tr:nth-child(even) td {{ background:#f8f9fa; }}
  .warn {{ background:#fff3cd;
           border-left:4px solid #ffc107;
           padding:8px 12px; margin:10px 0; }}
  .meta {{ color:#868e96; font-size:9pt; }}
  @media print {{ body {{ margin:10mm; }} }}
</style>
</head>
<body>
<h1>AxialFlow Designer — Design Report</h1>
<p class=""meta"">
  Project: <strong>{di.Project?.Name}</strong> |
  Result #: {result.Id} |
  Calculated: {result.CalculatedAt:dd MMM yyyy HH:mm} |
  Status: {result.Status.ToUpper()}
</p>");

        if (warnings.Any())
        {
            sb.AppendLine("<h2>Design Warnings</h2>");
            foreach (var w in warnings)
                sb.AppendLine($@"<div class=""warn"">⚠ {w}</div>");
        }

        sb.AppendLine(@"<h2>Input Parameters</h2>
<table>
<tr><th>Parameter</th><th>Value</th><th>Unit</th></tr>");

        void Row(string l, string v, string u = "") =>
            sb.AppendLine(
                $"<tr><td>{l}</td>" +
                $"<td><strong>{v}</strong></td>" +
                $"<td>{u}</td></tr>");

        Row("Media Type", di.MediaType);
        Row("Temperature", $"{di.TemperatureCelsius:F1}", "°C");
        Row("Inlet Pressure", $"{di.InletPressurePa:F0}", "Pa");
        Row("Air Density", $"{di.DensityKgM3:F3}", "kg/m³");
        Row("Flow Rate", $"{di.FlowRateM3s:F3}", "m³/s");
        Row("Total Pressure", $"{di.TotalPressurePa:F0}", "Pa");
        Row("Fan Speed", $"{di.SpeedRpm}", "RPM");
        Row("Blade Count", $"{di.BladeCount}");
        Row("Tip Diameter", $"{di.TipDiameterMm:F0}", "mm");
        Row("Hub Ratio", $"{di.HubRatio:F2}");
        Row("Blade Angle", $"{di.BladeAngleDeg:F1}", "°");
        Row("Target Efficiency", $"{di.TargetEfficiencyPct:F1}", "%");
        Row("Motor Power", $"{di.MotorPowerKw:F2}", "kW");
        sb.AppendLine("</table>");

        sb.AppendLine(@"<h2>Aerodynamic Results</h2>
<table>
<tr><th>Parameter</th><th>Value</th><th>Unit</th></tr>");
        Row("Specific Speed (Ωs)", $"{result.SpecificSpeed:F4}");
        Row("Tip Speed", $"{result.TipSpeedMs:F2}", "m/s");
        Row("Hub Diameter", $"{result.HubDiameterMm:F1}", "mm");
        Row("Blade Span", $"{result.BladeSpanMm:F1}", "mm");
        Row("Chord Length", $"{result.ChordLengthMm:F1}", "mm");
        Row("Flow Coefficient (Φ)", $"{result.FlowCoefficient:F4}");
        Row("Pressure Coefficient (Ψ)", $"{result.PressureCoefficient:F4}");
        Row("Overall Efficiency", $"{result.OverallEfficiencyPct:F2}", "%");
        Row("Shaft Power", $"{result.ShaftPowerKw:F3}", "kW");
        sb.AppendLine("</table>");

        sb.AppendLine(@"<h2>Structural Results</h2>
<table>
<tr><th>Parameter</th><th>Value</th><th>Unit</th></tr>");
        Row("Material", "Al 6061-T6");
        Row("Yield Strength", "270", "MPa");
        Row("Tip Clearance", $"{result.TipClearanceMm:F1}", "mm");
        Row("Blade Stress", $"{result.BladeStressMpa:F2}", "MPa");
        Row("Safety Factor", $"{result.SafetyFactor:F2}",
            result.SafetyFactor >= 2.0 ? "PASS" : "FAIL");
        sb.AppendLine("</table>");

        sb.AppendLine(@"<h2>Acoustic Results</h2>
<table>
<tr><th>Parameter</th><th>Value</th><th>Unit</th></tr>");
        Row("Overall Noise (Lp)", $"{result.OverallNoiseDbA:F1}", "dB(A)");
        Row("Sound Power Level (Lw)", $"{result.SoundPowerLevelDb:F1}", "dB");
        Row("Blade Passing Frequency", $"{result.BladePassingFrequencyHz:F1}", "Hz");
        Row("Tip Mach Number", $"{result.TipMachNumber:F3}");
        Row("Noise Rating (NR)", $"{result.NoiseRatingValue:F1} ({result.NoiseRating})");
        sb.AppendLine("</table>");

        var curve = result.PerformanceCurves?.FirstOrDefault();
        if (curve != null)
        {
            sb.AppendLine(
                $"<h2>Performance Curve " +
                $"(β={curve.BladeAngleDeg}°, " +
                $"N={curve.SpeedRpm} RPM)</h2>");
            sb.AppendLine(@"<table>
<tr><th>Q (m³/s)</th><th>ΔP (Pa)</th>
    <th>η (%)</th><th>Power (kW)</th></tr>");
            var qArr = curve.QValues.Split(',');
            var dpArr = curve.DpValues.Split(',');
            var etaArr = curve.EtaValues.Split(',');
            var kwArr = curve.KwValues.Split(',');
            for (int i = 0; i < qArr.Length; i++)
                sb.AppendLine(
                    $"<tr><td>{qArr[i]}</td>" +
                    $"<td>{dpArr[i]}</td>" +
                    $"<td>{etaArr[i]}</td>" +
                    $"<td>{kwArr[i]}</td></tr>");
            sb.AppendLine("</table>");
        }

        sb.AppendLine(@"<p class=""meta""
    style=""margin-top:30px;border-top:1px solid #dee2e6;
           padding-top:8px;"">
Generated by AxialFlow Designer.
For engineering reference only.
</p>
</body></html>");

        return (Encoding.UTF8.GetBytes(sb.ToString()),
                $"AxialFan_Report_{result.Id}_" +
                $"{result.CalculatedAt:yyyyMMdd}.html");
    }


    // ── Result Summary PDF with client-captured chart snapshots ─────────
    // Produces a focused "Result Summary" PDF: the same three cards the user
    // sees on the Result page (Input Parameters, Aerodynamic Results,
    // Structural Results), plus the two performance charts captured exactly
    // as rendered client-side (including any pinned comparison curves) and a
    // matching comparison table — no wizard-level fields that aren't shown
    // on this page.
    public (byte[] Bytes, string FileName) ExportPdfSummaryWithCharts(
        DesignResult result, byte[] qdpImage, byte[] etaImage, List<CurveComparisonRow> curves)
    {
        var di = result.DesignInput;
        var proj = di.Project;

        var warnings = result.WarningMessages != null
            ? System.Text.Json.JsonSerializer.Deserialize<List<string>>(result.WarningMessages) ?? new()
            : new List<string>();

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().Column(col =>
                {
                    col.Item().Text("AxialFlow Designer — Design Result Summary").FontSize(16).Bold();
                    col.Item().PaddingTop(4).Text(t =>
                    {
                        t.Span("Project: ").SemiBold();
                        t.Span(proj?.Name ?? "—");
                        t.Span("      Result ID: ").SemiBold();
                        t.Span($"#{result.Id}");
                        t.Span("      Calculated: ").SemiBold();
                        t.Span(result.CalculatedAt.ToString("dd MMM yyyy HH:mm"));
                    });
                    col.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Grey.Medium);
                });

                page.Content().PaddingTop(10).Column(col =>
                {
                    col.Spacing(10);

                    col.Item().Background(result.Status == "warning" ? Colors.Orange.Lighten4 : Colors.Green.Lighten4)
                        .Padding(6)
                        .Text($"Status: {result.Status.ToUpperInvariant()}").Bold();

                    // ── Three-column card layout matching the Result page ──
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Element(c => Card(c, "Input Parameters", Colors.Grey.Lighten2, Colors.Black, table =>
                        {
                            Row(table, "Flow Rate", $"{di.FlowRateM3s:F3} m³/s");
                            Row(table, "Total Pressure", $"{di.TotalPressurePa:F0} Pa");
                            Row(table, "Speed", $"{di.SpeedRpm} RPM");
                            Row(table, "Blade Count", $"{di.BladeCount}");
                            Row(table, "Tip Diameter", $"{di.TipDiameterMm:F0} mm");
                            Row(table, "Blade Angle", $"{di.BladeAngleDeg:F1} °");
                            Row(table, "Blade Profile", di.BladeProfile?.Name ?? "—");
                        }));

                        row.ConstantItem(10);

                        row.RelativeItem().Element(c => Card(c, "Aerodynamic Results", Colors.Blue.Darken1, Colors.White, table =>
                        {
                            Row(table, "Specific Speed (Ωs)", $"{result.SpecificSpeed:F4}");
                            Row(table, "Tip Speed", $"{result.TipSpeedMs:F2} m/s");
                            Row(table, "Hub Diameter", $"{result.HubDiameterMm:F1} mm");
                            Row(table, "Blade Span", $"{result.BladeSpanMm:F1} mm");
                            Row(table, "Overall Efficiency", $"{result.OverallEfficiencyPct:F1} %", true, Colors.Green.Darken1);
                            Row(table, "Shaft Power", $"{result.ShaftPowerKw:F3} kW", true, Colors.Blue.Darken2);
                        }));

                        row.ConstantItem(10);

                        row.RelativeItem().Element(c => Card(c, "Structural Results", Colors.Orange.Darken1, Colors.White, table =>
                        {
                            Row(table, "Material", "Al 6061-T6");
                            Row(table, "Yield Strength", "270 MPa");
                            Row(table, "Tip Clearance", $"{result.TipClearanceMm:F1} mm");
                            Row(table, "Blade Stress", $"{result.BladeStressMpa:F2} MPa");
                            var sfColor = result.SafetyFactor >= 2.0 ? Colors.Green.Darken1 : Colors.Red.Darken1;
                            Row(table, "Safety Factor", $"{result.SafetyFactor:F2}  (min 2.0)", true, sfColor);
                        }));
                    });

                    if (warnings.Any())
                    {
                        col.Item().Background(Colors.Red.Lighten5).Padding(6).Column(wcol =>
                        {
                            wcol.Item().Text("Warnings").Bold().FontColor(Colors.Red.Darken2);
                            foreach (var w in warnings)
                                wcol.Item().Text($"⚠ {w}").FontColor(Colors.Red.Darken2).FontSize(8);
                        });
                    }

                    // ── Charts — captured exactly as shown on screen ────
                    col.Item().Text("Performance Curves").Bold();
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten1).Padding(4)
                            .Image(qdpImage).FitWidth();
                        row.ConstantItem(8);
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten1).Padding(4)
                            .Image(etaImage).FitWidth();
                    });

                    // ── Comparison table — only if more than the design curve was shown ──
                    if (curves.Count > 1)
                    {
                        col.Item().Text("Curve Comparison").Bold();
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.ConstantColumn(16);
                                c.RelativeColumn(3);
                                c.RelativeColumn(2);
                                c.RelativeColumn(2);
                            });
                            table.Header(h =>
                            {
                                h.Cell().Element(HeaderCell).Text("");
                                h.Cell().Element(HeaderCell).Text("Curve");
                                h.Cell().Element(HeaderCell).Text("Blade Angle");
                                h.Cell().Element(HeaderCell).Text("Speed");
                            });
                            foreach (var c in curves)
                            {
                                table.Cell().Element(BodyCell)
                                    .Height(10).Width(10)
                                    .Background(ParseColor(c.Color));
                                table.Cell().Element(BodyCell)
                                    .Text(txt =>
                                    {
                                        var span = txt.Span(c.Label);
                                        if (c.IsDesign) span.SemiBold();
                                    });
                                table.Cell().Element(BodyCell).Text($"{c.Angle}°");
                                table.Cell().Element(BodyCell).Text($"{c.Rpm} RPM");
                            }
                        });
                    }
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Generated by AxialFlow Designer  ·  ");
                    t.Span(DateTime.Now.ToString("dd MMM yyyy HH:mm"));
                    t.Span("  ·  Page ");
                    t.CurrentPageNumber();
                    t.Span(" of ");
                    t.TotalPages();
                });
            });
        });

        var bytes = document.GeneratePdf();
        var fileName = $"AxialFan_Result_{result.Id}.pdf";
        return (bytes, fileName);

        // ── local helpers ──────────────────────────────────────────────
        static void Card(QuestPDF.Infrastructure.IContainer container, string title, string headerBg, string headerFg,
         Action<TableDescriptor> buildRows)
        {
            container.Column(col =>
            {
                col.Item().Background(headerBg).Padding(6)
                    .Text(title).FontColor(headerFg).Bold();
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(3);
                        c.RelativeColumn(2);
                    });
                    buildRows(table);
                });
            });
        }

        static void Row(TableDescriptor table, string label, string value,
        bool highlight = false, string? highlightColor = null)
        {
            table.Cell().Element(c => (highlight ? c.Background(Colors.Grey.Lighten4) : c).PaddingVertical(3).PaddingHorizontal(4))
                .Text(label).FontColor(Colors.Grey.Darken2);

            table.Cell().Element(c => (highlight ? c.Background(Colors.Grey.Lighten4) : c).PaddingVertical(3).PaddingHorizontal(4))
                .Text(txt =>
                {
                    var span = txt.Span(value).SemiBold();
                    if (highlightColor != null) span.FontColor(highlightColor);
                });
        }

        static QuestPDF.Infrastructure.IContainer HeaderCell(QuestPDF.Infrastructure.IContainer c) =>
            c.Background(Colors.Grey.Lighten3).Padding(4).DefaultTextStyle(x => x.Bold());

        static QuestPDF.Infrastructure.IContainer BodyCell(QuestPDF.Infrastructure.IContainer c) =>
            c.PaddingVertical(3).PaddingHorizontal(4);

        static string ParseColor(string hex) => hex; // QuestPDF accepts standard "#rrggbb" strings directly
    }

    // ── Standalone charts-only PDF with client-captured snapshots ───────
    // Just the two performance charts + curve comparison table — no
    // Input/Aerodynamic/Structural cards. For when the user only wants
    // the charts, not the full design summary.
    public (byte[] Bytes, string FileName) ExportChartsOnlyPdf(
        DesignResult result, byte[] qdpImage, byte[] etaImage, List<CurveComparisonRow> curves)
    {
        var proj = result.DesignInput.Project;

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().Column(col =>
                {
                    col.Item().Text("AxialFlow Designer — Performance Curves").FontSize(16).Bold();
                    col.Item().PaddingTop(4).Text(t =>
                    {
                        t.Span("Project: ").SemiBold();
                        t.Span(proj?.Name ?? "—");
                        t.Span("      Result ID: ").SemiBold();
                        t.Span($"#{result.Id}");
                        t.Span("      Blade Angle / Speed: ").SemiBold();
                        t.Span($"{result.DesignInput.BladeAngleDeg:F0}° / {result.DesignInput.SpeedRpm} RPM");
                    });
                    col.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Grey.Medium);
                });

                page.Content().PaddingTop(10).Column(col =>
                {
                    col.Spacing(10);

                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten1).Padding(4)
                            .Image(qdpImage).FitWidth();
                        row.ConstantItem(8);
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten1).Padding(4)
                            .Image(etaImage).FitWidth();
                    });

                    if (curves.Count > 1)
                    {
                        col.Item().Text("Curve Comparison").Bold();
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.ConstantColumn(16);
                                c.RelativeColumn(3);
                                c.RelativeColumn(2);
                                c.RelativeColumn(2);
                            });
                            table.Header(h =>
                            {
                                h.Cell().Element(HeaderCell).Text("");
                                h.Cell().Element(HeaderCell).Text("Curve");
                                h.Cell().Element(HeaderCell).Text("Blade Angle");
                                h.Cell().Element(HeaderCell).Text("Speed");
                            });
                            foreach (var c in curves)
                            {
                                table.Cell().Element(BodyCell).Height(10).Width(10).Background(c.Color);
                                table.Cell().Element(BodyCell).Text(txt =>
                                {
                                    var span = txt.Span(c.Label);
                                    if (c.IsDesign) span.SemiBold();
                                });
                                table.Cell().Element(BodyCell).Text($"{c.Angle}°");
                                table.Cell().Element(BodyCell).Text($"{c.Rpm} RPM");
                            }
                        });
                    }
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Generated by AxialFlow Designer  ·  ");
                    t.Span(DateTime.Now.ToString("dd MMM yyyy HH:mm"));
                    t.Span("  ·  Page ");
                    t.CurrentPageNumber();
                    t.Span(" of ");
                    t.TotalPages();
                });
            });
        });

        var bytes = document.GeneratePdf();
        return (bytes, $"AxialFan_Charts_{result.Id}.pdf");

        static QuestPDF.Infrastructure.IContainer HeaderCell(QuestPDF.Infrastructure.IContainer c) =>
            c.Background(Colors.Grey.Lighten3).Padding(4).DefaultTextStyle(x => x.Bold());

        static QuestPDF.Infrastructure.IContainer BodyCell(QuestPDF.Infrastructure.IContainer c) =>
            c.PaddingVertical(3).PaddingHorizontal(4);
    }

    // ── Server-rendered charts-only PDF (no client snapshot needed) ─────
    // Used from list views (e.g. Reports) where there's no chart on
    // screen to capture. Uses the saved PerformanceCurve if one exists,
    // otherwise generates one from the design point on the fly (not
    // persisted to the DB).
    public (byte[] Bytes, string FileName) ExportChartsOnlyPdf(DesignResult result)
    {
        var di = result.DesignInput;

        List<double> q, dp, eta, kw;
        string curveLabel;
        var saved = result.PerformanceCurves?.FirstOrDefault();
        if (saved != null)
        {
            q = saved.QValues.Split(',').Select(double.Parse).ToList();
            dp = saved.DpValues.Split(',').Select(double.Parse).ToList();
            eta = saved.EtaValues.Split(',').Select(double.Parse).ToList();
            kw = saved.KwValues.Split(',').Select(double.Parse).ToList();
            curveLabel = $"{saved.BladeAngleDeg:F0}° / {saved.SpeedRpm} RPM";
        }
        else
        {
            var aero = AeroCalcEngine.Calculate(di);
            var profileData = BladeProfileEngine.ResolveProfileData(di.BladeProfile, aero.ChordLengthMm);
            // BladeElementEngine.GenerateCurves (Wallis 1961 / Dixon Ch.7 BET)
            // instead of AeroCalcEngine.GenerateCurves' tuned-constant fit —
            // matches the production path in CurveGeneration.cs.
            var generated = BladeElementEngine.GenerateCurves(di, aero, profileData, di.BladeAngleDeg, di.SpeedRpm);

            q = generated.QValues; dp = generated.DpValues; eta = generated.EtaValues; kw = generated.KwValues;
            curveLabel = $"{di.BladeAngleDeg:F0}° / {di.SpeedRpm} RPM";
        }

        // QuestPDF's Canvas() API was removed (throws NotImplementedException
        // since 2024.3.0). Render the charts offscreen with SkiaSharp into
        // PNG bytes instead, then embed them with .Image() like the
        // client-snapshot overload above does.
        const int chartWidth = 520, chartHeight = 280;
        byte[] qdpPng = RenderChartPng(chartWidth, chartHeight, (canvas, size) =>
            DrawSingleLineChart(canvas, size, q, dp,
                "Flow Rate Q (m³/s)", "ΔP (Pa)", new SKColor(0x0d, 0x6e, 0xfd), fillArea: true));

        byte[] etaPng = RenderChartPng(chartWidth, chartHeight, (canvas, size) =>
            DrawDualLineChart(canvas, size, q, eta, kw,
                "Flow Rate Q (m³/s)", "η (%)", "kW",
                new SKColor(0x0d, 0x6e, 0xfd), new SKColor(0x19, 0x87, 0x54)));

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().Column(col =>
                {
                    col.Item().Text("AxialFlow Designer — Performance Curves").FontSize(16).Bold();
                    col.Item().PaddingTop(4).Text(t =>
                    {
                        t.Span("Project: ").SemiBold();
                        t.Span(di.Project?.Name ?? "—");
                        t.Span("      Result ID: ").SemiBold();
                        t.Span($"#{result.Id}");
                        t.Span("      Blade Angle / Speed: ").SemiBold();
                        t.Span(curveLabel);
                    });
                    col.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Grey.Medium);
                });

                page.Content().PaddingTop(15).Row(row =>
                {
                    row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten1).Padding(8)
                        .Image(qdpPng).FitWidth();

                    row.ConstantItem(10);

                    row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten1).Padding(8)
                        .Image(etaPng).FitWidth();
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Generated by AxialFlow Designer  ·  ");
                    t.Span(DateTime.Now.ToString("dd MMM yyyy HH:mm"));
                });
            });
        });

        var bytes = document.GeneratePdf();
        return (bytes, $"AxialFan_Charts_{result.Id}.pdf");
    }

    // ── Full PDF Report ──────────────────────────────────────────────
    // Consolidates what used to be scattered across four separate,
    // each-incomplete PDF paths:
    //   - EyeshotPdf(resultId)        → Input/Aero/Structural, no Acoustic, no charts
    //   - EyeshotPdf(POST w/ charts)  → Input/Structural (no Aero table!), no Acoustic
    //   - PdfWithCharts               → commented out / dead
    //   - ChartsOnlyPdf               → charts + curve table only, no result data at all
    // None of them included Acoustic Results, and only two of the four
    // included charts at all — which is exactly the pair of gaps reported.
    // This is now the ONE canonical "full report" PDF: every section the
    // Results page shows (Input, Aerodynamic, Structural, Acoustic),
    // warnings, AND the same server-rendered ΔP/η charts ChartsOnlyPdf
    // already used SkiaSharp for — so it needs no client-side chart
    // snapshot and works from a single GET request.
    public (byte[] Bytes, string FileName) ExportFullPdfReport(DesignResult result)
    {
        var di = result.DesignInput;
        var warnings = result.WarningMessages != null
            ? System.Text.Json.JsonSerializer.Deserialize<List<string>>(result.WarningMessages) ?? new()
            : new List<string>();

        List<double> q, dp, eta, kw;
        string curveLabel;
        var saved = result.PerformanceCurves?
            .Where(c => c.Source == "PINN")
            .OrderByDescending(c => c.GeneratedAt)
            .FirstOrDefault()
            ?? result.PerformanceCurves?.FirstOrDefault();

        if (saved != null)
        {
            q = saved.QValues.Split(',').Select(double.Parse).ToList();
            dp = saved.DpValues.Split(',').Select(double.Parse).ToList();
            eta = saved.EtaValues.Split(',').Select(double.Parse).ToList();
            kw = saved.KwValues.Split(',').Select(double.Parse).ToList();
            curveLabel = $"{saved.BladeAngleDeg:F0}° / {saved.SpeedRpm} RPM ({saved.Source})";
        }
        else
        {
            var aero = AeroCalcEngine.Calculate(di);
            var profileData = BladeProfileEngine.ResolveProfileData(di.BladeProfile, aero.ChordLengthMm);
            var generated = BladeElementEngine.GenerateCurves(di, aero, profileData, di.BladeAngleDeg, di.SpeedRpm);
            q = generated.QValues; dp = generated.DpValues; eta = generated.EtaValues; kw = generated.KwValues;
            curveLabel = $"{di.BladeAngleDeg:F0}° / {di.SpeedRpm} RPM (no saved curve — regenerated for this export)";
        }

        const int chartWidth = 480, chartHeight = 220;
        byte[] qdpPng = RenderChartPng(chartWidth, chartHeight, (canvas, size) =>
            DrawSingleLineChart(canvas, size, q, dp,
                "Flow Rate Q (m³/s)", "ΔP (Pa)", new SKColor(0x0d, 0x6e, 0xfd), fillArea: true));

        byte[] etaPng = RenderChartPng(chartWidth, chartHeight, (canvas, size) =>
            DrawDualLineChart(canvas, size, q, eta, kw,
                "Flow Rate Q (m³/s)", "η (%)", "kW",
                new SKColor(0x0d, 0x6e, 0xfd), new SKColor(0x19, 0x87, 0x54)));

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(30);
                page.Size(PageSizes.A4);
                page.DefaultTextStyle(x => x.FontSize(9.5f));

                page.Header().Text($"AxialFlow Designer — Design Report #{result.Id}").FontSize(16).Bold();

                page.Content().Column(col =>
                {
                    col.Spacing(4);

                    col.Item().PaddingTop(6).Text(
                        $"Project: {di.Project?.Name}   |   Status: {result.Status.ToUpper()}   |   Calculated: {result.CalculatedAt:dd MMM yyyy HH:mm}");

                    if (warnings.Any())
                    {
                        col.Item().PaddingTop(8).Text("Warnings").Bold();
                        foreach (var w in warnings)
                            col.Item().Text($"⚠ {w}").FontSize(8.5f);
                    }

                    void SectionTable(string title, params (string Label, string Value)[] rows)
                    {
                        col.Item().PaddingTop(12).Text(title).Bold().FontSize(11);
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });
                            foreach (var (label, value) in rows)
                            {
                                table.Cell().Text(label);
                                table.Cell().Text(value);
                            }
                        });
                    }

                    SectionTable("Input Parameters",
                        ("Media Type", di.MediaType),
                        ("Flow Rate", $"{di.FlowRateM3s:F3} m³/s"),
                        ("Total Pressure", $"{di.TotalPressurePa:F0} Pa"),
                        ("Fan Speed", $"{di.SpeedRpm} RPM"),
                        ("Blade Count", $"{di.BladeCount}"),
                        ("Tip Diameter", $"{di.TipDiameterMm:F0} mm"),
                        ("Hub Ratio", $"{di.HubRatio:F2}"),
                        ("Blade Angle", $"{di.BladeAngleDeg:F1} °"),
                        ("Blade Profile", di.BladeProfile?.Name ?? "—"),
                        ("Motor Power", $"{di.MotorPowerKw:F2} kW"));

                    SectionTable("Aerodynamic Results",
                        ("Specific Speed (Ωs)", $"{result.SpecificSpeed:F4}"),
                        ("Tip Speed", $"{result.TipSpeedMs:F2} m/s"),
                        ("Hub Diameter", $"{result.HubDiameterMm:F1} mm"),
                        ("Blade Span", $"{result.BladeSpanMm:F1} mm"),
                        ("Chord Length", $"{result.ChordLengthMm:F1} mm"),
                        ("Flow Coefficient (Φ)", $"{result.FlowCoefficient:F4}"),
                        ("Pressure Coefficient (Ψ)", $"{result.PressureCoefficient:F4}"),
                        ("Overall Efficiency", $"{result.OverallEfficiencyPct:F2} %"),
                        ("Shaft Power", $"{result.ShaftPowerKw:F3} kW"));

                    SectionTable("Structural Results",
                        ("Material", "Al 6061-T6"),
                        ("Yield Strength", "270 MPa"),
                        ("Tip Clearance", $"{result.TipClearanceMm:F1} mm"),
                        ("Blade Stress", $"{result.BladeStressMpa:F2} MPa"),
                        ("Safety Factor", $"{result.SafetyFactor:F2} ({(result.SafetyFactor >= 2.0 ? "PASS" : "FAIL")})"));

                    // Previously missing from every PDF export path — the
                    // whole reason this method exists.
                    SectionTable("Acoustic Results",
                        ("Overall Noise (Lp)", $"{result.OverallNoiseDbA:F1} dB(A)"),
                        ("Sound Power Level (Lw)", $"{result.SoundPowerLevelDb:F1} dB"),
                        ("Blade Passing Frequency", $"{result.BladePassingFrequencyHz:F1} Hz"),
                        ("Tip Mach Number", $"{result.TipMachNumber:F3}"),
                        ("Noise Rating (NR)", $"{result.NoiseRatingValue:F1} ({result.NoiseRating})"));

                    col.Item().PaddingTop(14).Text($"Performance Curve — {curveLabel}").Bold().FontSize(11);
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten1).Padding(4)
                            .Image(qdpPng).FitWidth();
                        row.ConstantItem(8);
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten1).Padding(4)
                            .Image(etaPng).FitWidth();
                    });
                });

                page.Footer().AlignCenter().Text("Generated by AxialFlow Designer — for engineering reference only.");
            });
        });

        return (document.GeneratePdf(), $"AxialFan_FullReport_{result.Id}.pdf");
    }

    // ── Offscreen SkiaSharp render → PNG bytes ──────────────────────────
    // QuestPDF no longer exposes a live canvas to draw on, so charts are
    // rasterized independently here, then embedded into the PDF as images.
    private static byte[] RenderChartPng(int width, int height, Action<SKCanvas, QuestPDF.Infrastructure.Size> draw)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        draw(canvas, new QuestPDF.Infrastructure.Size(width, height));
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    // ── Chart drawing helpers (SkiaSharp v3, used by ExportChartsOnlyPdf) ──
    // Note: SKPaint no longer carries TextSize/TextAlign (SkiaSharp v3+).
    // Text size/alignment now live on SKFont, passed separately to DrawText.

    private static void DrawSingleLineChart(SKCanvas canvas, QuestPDF.Infrastructure.Size size,
        List<double> xVals, List<double> yVals, string xLabel, string yLabel, SKColor lineColor, bool fillArea)
    {
        const float marginLeft = 45, marginBottom = 35, marginTop = 10, marginRight = 10;
        float plotW = size.Width - marginLeft - marginRight;
        float plotH = size.Height - marginTop - marginBottom;

        double xMin = xVals.Min(), xMax = xVals.Max();
        double yMax = Math.Max(1, yVals.Max() * 1.1);

        float ToX(double x) => marginLeft + (float)((x - xMin) / (xMax - xMin == 0 ? 1 : xMax - xMin)) * plotW;
        float ToY(double y) => marginTop + plotH - (float)(y / yMax) * plotH;

        using var axisPaint = new SKPaint { Color = SKColors.Black, StrokeWidth = 1, IsAntialias = true };
        using var gridPaint = new SKPaint { Color = new SKColor(0xE0, 0xE0, 0xE0), StrokeWidth = 1, IsAntialias = true };
        using var grayFont = new SKFont { Size = 8 };
        using var labelFont = new SKFont { Size = 10 };
        using var grayPaint = new SKPaint { Color = SKColors.Gray, IsAntialias = true };
        using var blackTextPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };

        for (int i = 0; i <= 5; i++)
        {
            double yVal = yMax * i / 5;
            float yPix = ToY(yVal);
            canvas.DrawLine(marginLeft, yPix, marginLeft + plotW, yPix, gridPaint);
            canvas.DrawText(yVal.ToString("F0"), marginLeft - 8, yPix + 3, SKTextAlign.Right, grayFont, grayPaint);
        }

        canvas.DrawLine(marginLeft, marginTop, marginLeft, marginTop + plotH, axisPaint);
        canvas.DrawLine(marginLeft, marginTop + plotH, marginLeft + plotW, marginTop + plotH, axisPaint);

        for (int i = 0; i <= 5; i++)
        {
            double xVal = xMin + (xMax - xMin) * i / 5;
            float xPix = ToX(xVal);
            canvas.DrawText(xVal.ToString("F1"), xPix, marginTop + plotH + 14, SKTextAlign.Center, grayFont, grayPaint);
        }

        canvas.DrawText(xLabel, marginLeft + plotW / 2, size.Height - 4, SKTextAlign.Center, labelFont, blackTextPaint);
        canvas.Save();
        canvas.RotateDegrees(-90, 12, marginTop + plotH / 2);
        canvas.DrawText(yLabel, 12, marginTop + plotH / 2, SKTextAlign.Center, labelFont, blackTextPaint);
        canvas.Restore();

        using var path = new SKPath();
        for (int i = 0; i < xVals.Count; i++)
        {
            float px = ToX(xVals[i]), py = ToY(yVals[i]);
            if (i == 0) path.MoveTo(px, py); else path.LineTo(px, py);
        }

        if (fillArea)
        {
            using var fillPath = new SKPath();
            fillPath.AddPath(path);
            fillPath.LineTo(ToX(xVals[^1]), marginTop + plotH);
            fillPath.LineTo(ToX(xVals[0]), marginTop + plotH);
            fillPath.Close();
            using var fillPaint = new SKPaint { Color = lineColor.WithAlpha(40), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawPath(fillPath, fillPaint);
        }

        using var linePaint = new SKPaint
        {
            Color = lineColor,
            StrokeWidth = 2.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round
        };
        canvas.DrawPath(path, linePaint);
    }

    private static void DrawDualLineChart(SKCanvas canvas, QuestPDF.Infrastructure.Size size,
        List<double> xVals, List<double> yLeftVals, List<double> yRightVals,
        string xLabel, string yLeftLabel, string yRightLabel, SKColor leftColor, SKColor rightColor)
    {
        const float marginLeft = 40, marginRight = 40, marginBottom = 35, marginTop = 10;
        float plotW = size.Width - marginLeft - marginRight;
        float plotH = size.Height - marginTop - marginBottom;

        double xMin = xVals.Min(), xMax = xVals.Max();
        double yLeftMax = Math.Max(10, yLeftVals.Max() * 1.1);
        double yRightMax = Math.Max(0.1, yRightVals.Max() * 1.1);

        float ToX(double x) => marginLeft + (float)((x - xMin) / (xMax - xMin == 0 ? 1 : xMax - xMin)) * plotW;
        float ToYLeft(double y) => marginTop + plotH - (float)(y / yLeftMax) * plotH;
        float ToYRight(double y) => marginTop + plotH - (float)(y / yRightMax) * plotH;

        using var axisPaint = new SKPaint { Color = SKColors.Black, StrokeWidth = 1, IsAntialias = true };
        using var gridPaint = new SKPaint { Color = new SKColor(0xE0, 0xE0, 0xE0), StrokeWidth = 1, IsAntialias = true };
        using var font8 = new SKFont { Size = 8 };
        using var font10 = new SKFont { Size = 10 };
        using var leftPaint = new SKPaint { Color = leftColor, IsAntialias = true };
        using var rightPaint = new SKPaint { Color = rightColor, IsAntialias = true };
        using var grayPaint = new SKPaint { Color = SKColors.Gray, IsAntialias = true };
        using var blackTextPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };

        for (int i = 0; i <= 5; i++)
        {
            float yPix = marginTop + plotH - plotH * i / 5f;
            canvas.DrawLine(marginLeft, yPix, marginLeft + plotW, yPix, gridPaint);
            canvas.DrawText((yLeftMax * i / 5).ToString("F0"), marginLeft - 8, yPix + 3, SKTextAlign.Right, font8, leftPaint);
            canvas.DrawText((yRightMax * i / 5).ToString("F1"), marginLeft + plotW + 8, yPix + 3, SKTextAlign.Left, font8, rightPaint);
        }

        canvas.DrawLine(marginLeft, marginTop, marginLeft, marginTop + plotH, axisPaint);
        canvas.DrawLine(marginLeft, marginTop + plotH, marginLeft + plotW, marginTop + plotH, axisPaint);
        canvas.DrawLine(marginLeft + plotW, marginTop, marginLeft + plotW, marginTop + plotH, axisPaint);

        for (int i = 0; i <= 5; i++)
        {
            double xVal = xMin + (xMax - xMin) * i / 5;
            float xPix = ToX(xVal);
            canvas.DrawText(xVal.ToString("F1"), xPix, marginTop + plotH + 14, SKTextAlign.Center, font8, grayPaint);
        }

        canvas.DrawText(xLabel, marginLeft + plotW / 2, size.Height - 4, SKTextAlign.Center, font10, blackTextPaint);

        void DrawSeries(List<double> yVals, Func<double, float> toY, SKColor color, bool dashed)
        {
            using var path = new SKPath();
            for (int i = 0; i < xVals.Count; i++)
            {
                float px = ToX(xVals[i]), py = toY(yVals[i]);
                if (i == 0) path.MoveTo(px, py); else path.LineTo(px, py);
            }
            using var paint = new SKPaint
            {
                Color = color,
                StrokeWidth = 2.25f,
                Style = SKPaintStyle.Stroke,
                IsAntialias = true,
                StrokeCap = SKStrokeCap.Round,
                PathEffect = dashed ? SKPathEffect.CreateDash(new float[] { 6, 3 }, 0) : null
            };
            canvas.DrawPath(path, paint);
        }

        DrawSeries(yLeftVals, ToYLeft, leftColor, dashed: false);
        DrawSeries(yRightVals, ToYRight, rightColor, dashed: true);
    }
}
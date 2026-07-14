using AxialFanMVC.Database;
using AxialFanMVC.Models;
using AxialFanMVC.Repositories.Inteface;
using AxialFanMVC.Services;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using System.Security.Claims;

namespace AxialFanMVC.Controllers;

[Authorize]
public class ExportController : Controller
{
    private readonly AxialFanDbContext _db;
    private readonly ExportService _exportSvc;
    private readonly string _exportDir;
    private readonly IExceptionHandlerRepository _exceptionHandlerRepository;

    public ExportController(
        AxialFanDbContext db,
        ExportService exportSvc,
        IConfiguration config,
        IWebHostEnvironment env,
        IExceptionHandlerRepository exceptionHandlerRepository)
    {
        _db = db;
        _exportSvc = exportSvc;
        _exceptionHandlerRepository = exceptionHandlerRepository;

        var relDir = config["AppSettings:ExportDirectory"] ?? "wwwroot/exports";
        _exportDir = Path.Combine(env.ContentRootPath, relDir);
        Directory.CreateDirectory(_exportDir);
    }

    private int CurrentUserId =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // ── GET /Export/Csv?resultId=5 ────────────────────────────────
    public async Task<IActionResult> Csv(int resultId)
    {
        try
        {
            var result = await LoadResult(resultId);
            if (result == null) return NotFound();

            await LogExport(result.DesignInput.ProjectId, "csv");
            var (bytes, fileName) = _exportSvc.ExportCsv(result);
            return File(bytes, "text/csv", fileName);
        }
        catch (Exception ex)
        {
            _exceptionHandlerRepository.SaveException(
                nameof(ExportController),
                nameof(Csv),
                ex.ToString());

            return RedirectToAction("Index", "Home");
        }
    }

    // ── GET /Export/Report?resultId=5 ─────────────────────────────
    public async Task<IActionResult> Report(int resultId)
    {
        try
        {
            var result = await LoadResult(resultId);
            if (result == null) return NotFound();

            await LogExport(result.DesignInput.ProjectId, "html");
            var (bytes, fileName) = _exportSvc.ExportHtmlReport(result);
            return File(bytes, "text/html", fileName);
        }
        catch (Exception ex)
        {
            _exceptionHandlerRepository.SaveException(
                nameof(ExportController),
                nameof(Report),
                ex.ToString());

            return RedirectToAction("Index", "Home");
        }
    }

    // ── GET /Export/Excel?resultId=5 ──────────────────────────────
    // Single-design report as a .xlsx workbook (input + results).
    public async Task<IActionResult> Excel(int resultId)
    {
        try
        {
            var result = await LoadResult(resultId);
            if (result == null) return NotFound();

            var di = result.DesignInput;

            using var wb = new ClosedXML.Excel.XLWorkbook();
            var ws = wb.Worksheets.Add("Design Report");

            void SectionHeader(ref int row, string title)
            {
                ws.Cell(row, 1).Value = title;
                ws.Cell(row, 1).Style.Font.Bold = true;
                ws.Cell(row, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1864ab");
                ws.Cell(row, 1).Style.Font.FontColor = XLColor.White;
                ws.Range(row, 1, row, 3).Merge();
                row++;
            }

            void Row(ref int row, string label, string value, string unit = "")
            {
                ws.Cell(row, 1).Value = label;
                ws.Cell(row, 2).Value = value;
                ws.Cell(row, 3).Value = unit;
                row++;
            }

            int r = 1;
            ws.Cell(r, 1).Value = $"AxialFlow Designer — Design Report #{result.Id}";
            ws.Cell(r, 1).Style.Font.Bold = true;
            ws.Cell(r, 1).Style.Font.FontSize = 14;
            r += 2;

            Row(ref r, "Project", di.Project?.Name ?? "");
            Row(ref r, "Status", result.Status.ToUpperInvariant());
            Row(ref r, "Calculated", result.CalculatedAt.ToString("dd MMM yyyy HH:mm"));
            r++;

            SectionHeader(ref r, "Input Parameters");
            Row(ref r, "Media Type", di.MediaType);
            Row(ref r, "Flow Rate", di.FlowRateM3s.ToString("F3"), "m3/s");
            Row(ref r, "Total Pressure", di.TotalPressurePa.ToString("F0"), "Pa");
            Row(ref r, "Fan Speed", di.SpeedRpm.ToString(), "RPM");
            Row(ref r, "Blade Count", di.BladeCount.ToString());
            Row(ref r, "Tip Diameter", di.TipDiameterMm.ToString("F0"), "mm");
            Row(ref r, "Hub Ratio", di.HubRatio.ToString("F2"));
            Row(ref r, "Blade Angle", di.BladeAngleDeg.ToString("F1"), "deg");
            Row(ref r, "Target Efficiency", di.TargetEfficiencyPct.ToString("F1"), "%");
            Row(ref r, "Motor Power", di.MotorPowerKw.ToString("F2"), "kW");
            r++;

            SectionHeader(ref r, "Aerodynamic Results");
            Row(ref r, "Specific Speed", result.SpecificSpeed.ToString("F4"));
            Row(ref r, "Tip Speed", result.TipSpeedMs.ToString("F2"), "m/s");
            Row(ref r, "Hub Diameter", result.HubDiameterMm.ToString("F1"), "mm");
            Row(ref r, "Blade Span", result.BladeSpanMm.ToString("F1"), "mm");
            Row(ref r, "Chord Length", result.ChordLengthMm.ToString("F1"), "mm");
            Row(ref r, "Flow Coefficient", result.FlowCoefficient.ToString("F4"));
            Row(ref r, "Pressure Coefficient", result.PressureCoefficient.ToString("F4"));
            Row(ref r, "Overall Efficiency", result.OverallEfficiencyPct.ToString("F2"), "%");
            Row(ref r, "Shaft Power", result.ShaftPowerKw.ToString("F3"), "kW");
            r++;

            SectionHeader(ref r, "Structural Results");
            Row(ref r, "Material", result.MaterialUsed);
            Row(ref r, "Yield Strength", result.YieldStrengthMpa.ToString("F0"), "MPa");
            Row(ref r, "Tip Clearance", result.TipClearanceMm.ToString("F1"), "mm");
            Row(ref r, "Blade Stress", result.BladeStressMpa.ToString("F2"), "MPa");
            Row(ref r, "Safety Factor", result.SafetyFactor.ToString("F2"),
                result.SafetyFactor >= 2.0 ? "PASS" : "FAIL");
            r++;

            SectionHeader(ref r, "Acoustic Results");
            Row(ref r, "Overall Noise (Lp)", $"{result.OverallNoiseDbA:F1}", "dB(A)");
            Row(ref r, "Sound Power Level (Lw)", $"{result.SoundPowerLevelDb:F1}", "dB");
            Row(ref r, "Blade Passing Frequency", $"{result.BladePassingFrequencyHz:F1}", "Hz");
            Row(ref r, "Tip Mach Number", $"{result.TipMachNumber:F3}");
            Row(ref r, "Noise Rating (NR)", $"{result.NoiseRatingValue:F1} ({result.NoiseRating})");

            ws.Columns().AdjustToContents();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);

            await LogExport(di.ProjectId, "xlsx");
            return File(ms.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"AxialFan_Report_{result.Id}_{result.CalculatedAt:yyyyMMdd}.xlsx");
        }
        catch (Exception ex)
        {
            _exceptionHandlerRepository.SaveException(
                nameof(ExportController),
                nameof(Excel),
                ex.ToString());

            return RedirectToAction("Index", "Home");
        }
    }

    // ── GET /Export/Word?resultId=5 ───────────────────────────────
    // Single-design report as a .docx document (input + results).
    public async Task<IActionResult> Word(int resultId)
    {
        try
        {
            var result = await LoadResult(resultId);
            if (result == null) return NotFound();

            var di = result.DesignInput;

            using var ms = new MemoryStream();
            using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
            {
                var mainPart = doc.AddMainDocumentPart();
                mainPart.Document = new DocumentFormat.OpenXml.Wordprocessing.Document();
                var body = mainPart.Document.AppendChild(new Body());

                void Heading(string text, int size = 28) =>
                    body.AppendChild(new Paragraph(
                        new ParagraphProperties(new SpacingBetweenLines { Before = "200", After = "120" }),
                        new Run(
                            new RunProperties(new Bold(), new FontSize { Val = size.ToString() }),
                            new Text(text))));

                void KeyValueTable(params (string Label, string Value, string Unit)[] rows)
                {
                    var table = new Table();
                    var props = new TableProperties(
                        new TableBorders(
                            new TopBorder { Val = BorderValues.Single, Size = 4 },
                            new BottomBorder { Val = BorderValues.Single, Size = 4 },
                            new LeftBorder { Val = BorderValues.Single, Size = 4 },
                            new RightBorder { Val = BorderValues.Single, Size = 4 },
                            new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
                            new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 }));
                    table.AppendChild(props);

                    foreach (var (label, value, unit) in rows)
                    {
                        var tr = new TableRow();
                        tr.AppendChild(new TableCell(new Paragraph(new Run(new Text(label)))));
                        tr.AppendChild(new TableCell(new Paragraph(new Run(new RunProperties(new Bold()), new Text(value)))));
                        tr.AppendChild(new TableCell(new Paragraph(new Run(new Text(unit)))));
                        table.AppendChild(tr);
                    }
                    body.AppendChild(table);
                    body.AppendChild(new Paragraph());
                }

                Heading("AxialFlow Designer — Design Report #" + result.Id, 32);
                body.AppendChild(new Paragraph(new Run(new Text(
                    $"Project: {di.Project?.Name}    Status: {result.Status.ToUpperInvariant()}    " +
                    $"Calculated: {result.CalculatedAt:dd MMM yyyy HH:mm}"))));
                body.AppendChild(new Paragraph());

                Heading("Input Parameters");
                KeyValueTable(
                    ("Media Type", di.MediaType, ""),
                    ("Flow Rate", di.FlowRateM3s.ToString("F3"), "m3/s"),
                    ("Total Pressure", di.TotalPressurePa.ToString("F0"), "Pa"),
                    ("Fan Speed", di.SpeedRpm.ToString(), "RPM"),
                    ("Blade Count", di.BladeCount.ToString(), ""),
                    ("Tip Diameter", di.TipDiameterMm.ToString("F0"), "mm"),
                    ("Hub Ratio", di.HubRatio.ToString("F2"), ""),
                    ("Blade Angle", di.BladeAngleDeg.ToString("F1"), "deg"),
                    ("Target Efficiency", di.TargetEfficiencyPct.ToString("F1"), "%"),
                    ("Motor Power", di.MotorPowerKw.ToString("F2"), "kW"));

                Heading("Aerodynamic Results");
                KeyValueTable(
                    ("Specific Speed", result.SpecificSpeed.ToString("F4"), ""),
                    ("Tip Speed", result.TipSpeedMs.ToString("F2"), "m/s"),
                    ("Hub Diameter", result.HubDiameterMm.ToString("F1"), "mm"),
                    ("Blade Span", result.BladeSpanMm.ToString("F1"), "mm"),
                    ("Chord Length", result.ChordLengthMm.ToString("F1"), "mm"),
                    ("Flow Coefficient", result.FlowCoefficient.ToString("F4"), ""),
                    ("Pressure Coefficient", result.PressureCoefficient.ToString("F4"), ""),
                    ("Overall Efficiency", result.OverallEfficiencyPct.ToString("F2"), "%"),
                    ("Shaft Power", result.ShaftPowerKw.ToString("F3"), "kW"));

                Heading("Structural Results");
                KeyValueTable(
                   ("Material", result.MaterialUsed, ""),
                 ("Yield Strength", result.YieldStrengthMpa.ToString("F0"), "MPa"),
                    ("Tip Clearance", result.TipClearanceMm.ToString("F1"), "mm"),
                    ("Blade Stress", result.BladeStressMpa.ToString("F2"), "MPa"),
                    ("Safety Factor", result.SafetyFactor.ToString("F2"),
                        result.SafetyFactor >= 2.0 ? "PASS" : "FAIL"));

                Heading("Acoustic Results");
                KeyValueTable(
                    ("Overall Noise (Lp)", $"{result.OverallNoiseDbA:F1}", "dB(A)"),
                    ("Sound Power Level (Lw)", $"{result.SoundPowerLevelDb:F1}", "dB"),
                    ("Blade Passing Frequency", $"{result.BladePassingFrequencyHz:F1}", "Hz"),
                    ("Tip Mach Number", $"{result.TipMachNumber:F3}", ""),
                    ("Noise Rating (NR)", $"{result.NoiseRatingValue:F1} ({result.NoiseRating})", ""));


                body.AppendChild(new Paragraph(
                    new Run(new RunProperties(new Italic()),
                        new Text("Generated by AxialFlow Designer — for engineering reference only."))));

                mainPart.Document.Save();
            }

            await LogExport(di.ProjectId, "docx");
            return File(ms.ToArray(),
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                $"AxialFan_Report_{result.Id}_{result.CalculatedAt:yyyyMMdd}.docx");
        }
        catch (Exception ex)
        {
            _exceptionHandlerRepository.SaveException(
                nameof(ExportController),
                nameof(Word),
                ex.ToString());

            return RedirectToAction("Index", "Home");
        }
    }

    // ── GET /Export/Drawings?resultId=5 ───────────────────────────
    // Serves SVG drawings as an HTML page (unchanged from original)
    public async Task<IActionResult> Drawings(int resultId)
    {
        try
        {
            var result = await LoadResult(resultId);
            if (result == null) return NotFound();

            if (!result.Drawings.Any())
            {
                var drawings = DrawingService.GenerateAll(result);
                _db.drawings.AddRange(drawings);
                await _db.SaveChangesAsync();
                result.Drawings = drawings;
            }

            await LogExport(result.DesignInput.ProjectId, "svg");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'/>");
            sb.AppendLine("<title>Engineering Drawings</title>");
            sb.AppendLine(@"<style>
            body { font-family: Arial, sans-serif; margin: 20px; }
            h1   { color: #1864ab; border-bottom: 2px solid #1864ab; padding-bottom: 8px; }
            h3   { color: #495057; margin-top: 30px; }
            svg  { max-width: 100%; height: auto; display: block; margin: 10px 0; }
            hr   { border: 1px solid #dee2e6; margin: 20px 0; }
        </style>");
            sb.AppendLine("</head><body>");
            sb.AppendLine($"<h1>⚙ Engineering Drawings — Result #{resultId}</h1>");
            sb.AppendLine($"<p style='color:#868e96'>Generated: {DateTime.Now:dd MMM yyyy HH:mm}</p>");

            foreach (var drw in result.Drawings)
            {
                sb.AppendLine($"<h3>{drw.DrawingType.Replace("_", " ").ToUpperInvariant()}</h3>");
                sb.AppendLine(drw.SvgData ?? "<p>SVG not available</p>");
                sb.AppendLine("<hr/>");
            }

            sb.AppendLine("</body></html>");

            return File(
                System.Text.Encoding.UTF8.GetBytes(sb.ToString()),
                "text/html",
                $"AxialFan_Drawings_{resultId}_{DateTime.Now:yyyyMMdd}.html");
        }
        catch (Exception ex)
        {
            _exceptionHandlerRepository.SaveException(
                nameof(ExportController),
                nameof(Drawings),
                ex.ToString());

            return RedirectToAction("Index", "Home");
        }
    }

    // ══════════════════════════════════════════════════════════════
    // DWG exports — Devdept Eyeshot 2026
    // ══════════════════════════════════════════════════════════════

    // ── GET /Export/DwgFront?resultId=5 ───────────────────────────
    // Returns a DXF file (opens in AutoCAD, LibreCAD, FreeCAD, etc.)
    // Uses netDXF — no WPF/WinForms dependency needed
    public async Task<IActionResult> DwgFront(int resultId)
    {
        try
        {
            var result = await LoadResult(resultId);
            if (result == null) return NotFound();

            var dxfBytes = AxialFanDrawingService.FrontElevationDxf(
                result.DesignInput, result);

            await SaveDwgPath(result, "front_elevation",
                $"DWG001_FrontElev_{resultId}.dxf", dxfBytes);

            await LogExport(result.DesignInput.ProjectId, "dxf");

            return File(
                dxfBytes,
                "application/dxf",
                $"AxialFan_FrontElevation_{resultId}.dxf");
        }
        catch (Exception ex)
        {
            _exceptionHandlerRepository.SaveException(
                nameof(ExportController),
                nameof(DwgFront),
                ex.ToString());

            return RedirectToAction("Index", "Home");
        }
    }

    // ── GET /Export/DwgSection?resultId=5 ─────────────────────────
    public async Task<IActionResult> DwgSection(int resultId)
    {
        try
        {
            var result = await LoadResult(resultId);
            if (result == null) return NotFound();

            var dxfBytes = AxialFanDrawingService.CrossSectionDxf(
                result.DesignInput, result);

            await SaveDwgPath(result, "cross_section",
                $"DWG002_CrossSection_{resultId}.dxf", dxfBytes);

            await LogExport(result.DesignInput.ProjectId, "dxf");

            return File(
                dxfBytes,
                "application/dxf",
                $"AxialFan_CrossSection_{resultId}.dxf");
        }
        catch (Exception ex)
        {
            _exceptionHandlerRepository.SaveException(
                nameof(ExportController),
                nameof(DwgSection),
                ex.ToString());

            return RedirectToAction("Index", "Home");
        }
    }

    // ── GET /Export/DwgBlade?resultId=5 ───────────────────────────
    public async Task<IActionResult> DwgBlade(int resultId)
    {
        try
        {
            var result = await LoadResult(resultId);
            if (result == null) return NotFound();

            var dxfBytes = AxialFanDrawingService.BladeProfileDxf(
                result.DesignInput, result);

            await SaveDwgPath(result, "blade_profile",
                $"DWG003_BladeProfile_{resultId}.dxf", dxfBytes);

            await LogExport(result.DesignInput.ProjectId, "dxf");

            return File(
                dxfBytes,
                "application/dxf",
                $"AxialFan_BladeProfile_{resultId}.dxf");
        }
        catch (Exception ex)
        {
            _exceptionHandlerRepository.SaveException(
                nameof(ExportController),
                nameof(DwgBlade),
                ex.ToString());

            return RedirectToAction("Index", "Home");
        }
    }

    // ── GET /Export/PrintView?resultId=5 ──────────────────────────
    public async Task<IActionResult> PrintView(int resultId)
    {
        try
        {
            var result = await LoadResult(resultId);
            if (result == null) return NotFound();

            var warnings = result.WarningMessages != null
                ? System.Text.Json.JsonSerializer
                    .Deserialize<List<string>>(result.WarningMessages) ?? new()
                : new List<string>();

            var di = result.DesignInput;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($@"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""utf-8""/>
<title>AxialFlow Report #{result.Id}</title>
<style>
  body  {{ font-family: Arial, sans-serif; font-size: 11pt;
           margin: 15mm; color: #222; }}
  h1    {{ font-size: 18pt; color: #1864ab;
           border-bottom: 2px solid #1864ab; padding-bottom: 6px; }}
  h2    {{ font-size: 13pt; color: #495057; margin-top: 20px;
           border-left: 4px solid #1864ab; padding-left: 8px; }}
  table {{ border-collapse: collapse; width: 100%; margin: 10px 0; }}
  th    {{ background: #1864ab; color: white; padding: 6px 10px;
           text-align: left; font-size: 10pt; }}
  td    {{ padding: 5px 10px; border-bottom: 1px solid #dee2e6;
           font-size: 10pt; }}
  tr:nth-child(even) td {{ background: #f8f9fa; }}
  .warn {{ background: #fff3cd; border-left: 4px solid #ffc107;
           padding: 8px 12px; margin: 10px 0; border-radius: 4px; }}
  .ok      {{ color: #2f9e44; font-weight: bold; }}
  .warning {{ color: #e67700; font-weight: bold; }}
  .meta    {{ color: #868e96; font-size: 9pt; }}
  @media print {{ body {{ margin: 10mm; }} }}
</style>
</head>
<body>
<div style=""margin-bottom:20px;"">
  <button onclick=""window.print()""
          style=""padding:8px 20px;background:#1864ab;color:white;
                 border:none;border-radius:4px;cursor:pointer;font-size:12pt;"">
    🖨 Print / Save as PDF
  </button>
  <button onclick=""window.close()""
          style=""padding:8px 20px;background:#868e96;color:white;
                 border:none;border-radius:4px;cursor:pointer;
                 font-size:12pt;margin-left:8px;"">
    ✕ Close
  </button>
</div>

<h1>⚙ AxialFlow Designer — Design Report</h1>
<p class=""meta"">
  Project: <strong>{di.Project?.Name}</strong> &nbsp;|&nbsp;
  Result ID: #{result.Id} &nbsp;|&nbsp;
  Calculated: {result.CalculatedAt:dd MMM yyyy HH:mm} &nbsp;|&nbsp;
  Status: <span class=""{result.Status}"">{result.Status.ToUpper()}</span>
</p>");

            if (warnings.Any())
            {
                sb.AppendLine("<h2>⚠ Design Warnings</h2>");
                foreach (var w in warnings)
                    sb.AppendLine($@"<div class=""warn"">⚠ {w}</div>");
            }

            sb.AppendLine(@"<h2>Input Parameters</h2>
<table>
<tr><th>Parameter</th><th>Value</th><th>Unit</th></tr>");

            void Row(string label, string val, string unit = "") =>
                sb.AppendLine($"<tr><td>{label}</td>" +
                              $"<td><strong>{val}</strong></td>" +
                              $"<td>{unit}</td></tr>");

            Row("Media Type", di.MediaType);
            Row("Temperature", $"{di.TemperatureCelsius:F1}", "°C");
            Row("Inlet Pressure", $"{di.InletPressurePa:F0}", "Pa");
            Row("Air Density", $"{di.DensityKgM3:F3}", "kg/m³");
            Row("Flow Rate", $"{di.FlowRateM3s:F3}", "m³/s");
            Row("Total Pressure Rise", $"{di.TotalPressurePa:F0}", "Pa");
            Row("Static Pressure", $"{di.StaticPressurePa:F0}", "Pa");
            Row("Fan Speed", $"{di.SpeedRpm}", "RPM");
            Row("Motor Configuration", di.MotorPoles);
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
            Row("Material", result.MaterialUsed);
            Row("Yield Strength", $"{result.YieldStrengthMpa:F0}", "MPa");
            Row("Tip Clearance", $"{result.TipClearanceMm:F1}", "mm");
            Row("Blade Stress", $"{result.BladeStressMpa:F2}", "MPa");
            Row("Safety Factor", $"{result.SafetyFactor:F2}",
                result.SafetyFactor >= 2.0 ? "✔ PASS (min 2.0)" : "✖ FAIL (min 2.0)");
            sb.AppendLine("</table>");

            var curve = result.PerformanceCurves?.FirstOrDefault();
            if (curve != null)
            {
                sb.AppendLine($@"<h2>Performance Curve
                (β={curve.BladeAngleDeg}°, N={curve.SpeedRpm} RPM)</h2>
<table>
<tr><th>Q (m³/s)</th><th>ΔP (Pa)</th><th>η (%)</th>
    <th>Power (kW)</th></tr>");
                var qArr = curve.QValues.Split(',');
                var dpArr = curve.DpValues.Split(',');
                var etaArr = curve.EtaValues.Split(',');
                var kwArr = curve.KwValues.Split(',');
                for (int i = 0; i < qArr.Length; i++)
                    sb.AppendLine($"<tr><td>{qArr[i]}</td><td>{dpArr[i]}</td>" +
                                  $"<td>{etaArr[i]}</td><td>{kwArr[i]}</td></tr>");
                sb.AppendLine("</table>");
            }

            sb.AppendLine(@"<p class=""meta"" style=""margin-top:30px;
            border-top:1px solid #dee2e6;padding-top:8px;"">
Generated by AxialFlow Designer.
For engineering reference only.
Always verify with physical testing.
</p>
</body></html>");

            return Content(sb.ToString(), "text/html", System.Text.Encoding.UTF8);
        }
        catch (Exception ex)
        {
            _exceptionHandlerRepository.SaveException(
                nameof(ExportController),
                nameof(PrintView),
                ex.ToString());

            return RedirectToAction("Index", "Home");
        }
    }

    // ══════════════════════════════════════════════════════════════
    // Private helpers
    // ══════════════════════════════════════════════════════════════

    private async Task<DesignResult?> LoadResult(int resultId) =>
        await _db.design_results
            .Include(r => r.DesignInput)
                .ThenInclude(di => di.Project)
            .Include(r => r.DesignInput)
                .ThenInclude(di => di.BladeProfile)
            .Include(r => r.PerformanceCurves)
            .Include(r => r.Drawings)
            .FirstOrDefaultAsync(r => r.Id == resultId &&
                r.DesignInput.Project.UserId == CurrentUserId);

    /// <summary>
    /// Writes the DWG bytes to disk and saves the file path in the
    /// Drawing.DwgPath column (uses your existing DxfPath column for now).
    /// </summary>
    private async Task SaveDwgPath(
        DesignResult result,
        string drawingType,
        string fileName,
        byte[] bytes)
    {
        string fullPath = Path.Combine(_exportDir, fileName);
        await System.IO.File.WriteAllBytesAsync(fullPath, bytes);

        var drawing = result.Drawings
            .FirstOrDefault(d => d.DrawingType == drawingType);

        if (drawing != null)
        {
            // Reusing DxfPath column for DWG path
            // (rename to DwgPath in a future migration if preferred)
            drawing.DxfPath = fullPath;
            await _db.SaveChangesAsync();
        }
    }

    private async Task LogExport(int projectId, string format)
    {
        _db.export_logs.Add(new ExportLog
        {
            ProjectId = projectId,
            UserId = CurrentUserId,
            Format = format
        });
        await _db.SaveChangesAsync();
    }

    // ── GET /Export/EyeshotPdf?resultId=5 ────────────────────────
    // Produces a genuine PDF (via QuestPDF) of the full design summary —
    // Input Parameters / Aerodynamic Results / Structural Results — the
    // same field set as Word/Excel/ExportHtmlReport. This is the PDF used
    // from list views (e.g. the Reports page) where there's no rendered
    // chart to snapshot, so it intentionally has no charts — the Result
    // page's "Download PDF Summary" button (PdfWithCharts) is the one
    // that includes charts.
    public async Task<IActionResult> EyeshotPdf(int resultId)
    {
        try
        {
            var result = await LoadResult(resultId);
            if (result == null) return NotFound();

            var di = result.DesignInput;
            var warnings = result.WarningMessages != null
                ? System.Text.Json.JsonSerializer.Deserialize<List<string>>(result.WarningMessages) ?? new()
                : new List<string>();

            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

            var pdfBytes = QuestPDF.Fluent.Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(30);
                    page.Size(QuestPDF.Helpers.PageSizes.A4);
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Header().Text($"AxialFlow Designer — Design Report #{result.Id}")
                        .FontSize(16).Bold();

                    page.Content().Column(col =>
                    {
                        col.Item().PaddingTop(10).Text($"Project: {di.Project?.Name}   |   Status: {result.Status.ToUpper()}   |   Calculated: {result.CalculatedAt:dd MMM yyyy HH:mm}");

                        if (warnings.Any())
                        {
                            col.Item().PaddingTop(10).Text("Warnings").Bold();
                            foreach (var w in warnings)
                                col.Item().Text($"⚠ {w}");
                        }

                        col.Item().PaddingTop(15).Text("Input Parameters").Bold();
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });
                            void Row(string label, string val)
                            {
                                table.Cell().Text(label);
                                table.Cell().Text(val);
                            }
                            Row("Media Type", di.MediaType);
                            Row("Flow Rate", $"{di.FlowRateM3s:F3} m³/s");
                            Row("Total Pressure", $"{di.TotalPressurePa:F0} Pa");
                            Row("Fan Speed", $"{di.SpeedRpm} RPM");
                            Row("Blade Count", $"{di.BladeCount}");
                            Row("Tip Diameter", $"{di.TipDiameterMm:F0} mm");
                            Row("Hub Ratio", $"{di.HubRatio:F2}");
                            Row("Blade Angle", $"{di.BladeAngleDeg:F1} °");
                            Row("Blade Profile", di.BladeProfile?.Name ?? "—");
                            Row("Target Efficiency", $"{di.TargetEfficiencyPct:F1} %");
                            Row("Motor Power", $"{di.MotorPowerKw:F2} kW");
                        });

                        col.Item().PaddingTop(15).Text("Aerodynamic Results").Bold();
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });
                            void Row(string label, string val)
                            {
                                table.Cell().Text(label);
                                table.Cell().Text(val);
                            }
                            Row("Specific Speed (Ωs)", $"{result.SpecificSpeed:F4}");
                            Row("Tip Speed", $"{result.TipSpeedMs:F2} m/s");
                            Row("Hub Diameter", $"{result.HubDiameterMm:F1} mm");
                            Row("Blade Span", $"{result.BladeSpanMm:F1} mm");
                            Row("Chord Length", $"{result.ChordLengthMm:F1} mm");
                            Row("Flow Coefficient (Φ)", $"{result.FlowCoefficient:F4}");
                            Row("Pressure Coefficient (Ψ)", $"{result.PressureCoefficient:F4}");
                            Row("Overall Efficiency", $"{result.OverallEfficiencyPct:F2} %");
                            Row("Shaft Power", $"{result.ShaftPowerKw:F3} kW");
                        });

                        col.Item().PaddingTop(15).Text("Structural Results").Bold();
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });
                            void Row(string label, string val)
                            {
                                table.Cell().Text(label);
                                table.Cell().Text(val);
                            }
                            Row("Material", result.MaterialUsed);
                            Row("Yield Strength", $"{result.YieldStrengthMpa:F0} MPa");
                            Row("Tip Clearance", $"{result.TipClearanceMm:F1} mm");
                            Row("Blade Stress", $"{result.BladeStressMpa:F2} MPa");
                            Row("Safety Factor", $"{result.SafetyFactor:F2} ({(result.SafetyFactor >= 2.0 ? "PASS" : "FAIL")})");
                        });
                    });

                    page.Footer().AlignCenter().Text("Generated by AxialFlow Designer — for engineering reference only.");
                });
            }).GeneratePdf();

            await LogExport(di.ProjectId, "pdf");
            return File(pdfBytes, "application/pdf", $"AxialFan_Report_{resultId}.pdf");
        }
        catch (Exception ex)
        {
            _exceptionHandlerRepository.SaveException(
                nameof(ExportController),
                nameof(EyeshotPdf),
                ex.ToString());

            return RedirectToAction("Index", "Home");
        }
    }

    [HttpPost]
    public async Task<IActionResult> EyeshotPdf(
        [FromBody] ExportPdfRequest request)
    {
        try
        {
            var result = await LoadResult(request.ResultId);
            if (result == null)
                return NotFound();

            var di = result.DesignInput;

            var warnings = result.WarningMessages != null
                ? System.Text.Json.JsonSerializer.Deserialize<List<string>>(result.WarningMessages) ?? new()
                : new List<string>();

            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

            byte[]? qdpChart = null;
            byte[]? etaChart = null;

            if (!string.IsNullOrWhiteSpace(request.QdpChart))
            {
                var base64 = request.QdpChart.Substring(request.QdpChart.IndexOf(',') + 1);
                qdpChart = Convert.FromBase64String(base64);
            }

            if (!string.IsNullOrWhiteSpace(request.EtaChart))
            {
                var base64 = request.EtaChart.Substring(request.EtaChart.IndexOf(',') + 1);
                etaChart = Convert.FromBase64String(base64);
            }

            var pdfBytes = QuestPDF.Fluent.Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(30);
                    page.Size(QuestPDF.Helpers.PageSizes.A4);
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Header()
                        .Text($"AxialFlow Designer — Design Report #{result.Id}")
                        .FontSize(16)
                        .Bold();

                    page.Content().Column(col =>
                    {
                        col.Item().Text(
                            $"Project: {di.Project?.Name}   |   Status: {result.Status.ToUpper()}   |   Calculated: {result.CalculatedAt:dd MMM yyyy HH:mm}");

                        if (warnings.Any())
                        {
                            col.Item().PaddingTop(10).Text("Warnings").Bold();

                            foreach (var w in warnings)
                                col.Item().Text($"⚠ {w}");
                        }

                        col.Item().PaddingTop(15).Text("Input Parameters").Bold();

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn();
                                c.RelativeColumn();
                            });

                            void Row(string label, string value)
                            {
                                table.Cell().Text(label);
                                table.Cell().Text(value);
                            }

                            Row("Media Type", di.MediaType);
                            Row("Flow Rate", $"{di.FlowRateM3s:F3} m³/s");
                            Row("Total Pressure", $"{di.TotalPressurePa:F0} Pa");
                            Row("Fan Speed", $"{di.SpeedRpm} RPM");
                            Row("Blade Count", $"{di.BladeCount}");
                            Row("Tip Diameter", $"{di.TipDiameterMm:F0} mm");
                            Row("Blade Angle", $"{di.BladeAngleDeg:F1}°");
                            Row("Motor Power", $"{di.MotorPowerKw:F2} kW");
                        });

                        col.Item().PaddingTop(20).Text("Structural Results").Bold();

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn();
                                c.RelativeColumn();
                            });

                            void Row(string label, string value)
                            {
                                table.Cell().Text(label);
                                table.Cell().Text(value);
                            }

                            Row("Material", result.MaterialUsed);
                            Row("Yield Strength", $"{result.YieldStrengthMpa:F0} MPa");
                            Row("Tip Clearance", $"{result.TipClearanceMm:F1} mm");
                            Row("Blade Stress", $"{result.BladeStressMpa:F2} MPa");
                            Row("Safety Factor", $"{result.SafetyFactor:F2}");
                        });

                        if (qdpChart != null)
                        {
                            col.Item().PageBreak();

                            col.Item().Text("Pressure Curve")
                                .Bold()
                                .FontSize(13);

                            col.Item()
                                .Height(240)
                                .Image(qdpChart);
                        }

                        if (etaChart != null)
                        {
                            col.Item().PaddingTop(20);

                            col.Item().Text("Efficiency Curve")
                                .Bold()
                                .FontSize(13);

                            col.Item()
                                .Height(240)
                                .Image(etaChart);
                        }
                    });

                    page.Footer()
                        .AlignCenter()
                        .Text("Generated by AxialFlow Designer — for engineering reference only.");
                });
            }).GeneratePdf();

            await LogExport(di.ProjectId, "pdf");

            return File(
                pdfBytes,
                "application/pdf",
                $"AxialFan_Report_{request.ResultId}.pdf");
        }
        catch (Exception ex)
        {
            _exceptionHandlerRepository.SaveException(
                nameof(ExportController),
                nameof(EyeshotPdf),
                ex.ToString());

            return StatusCode(500, "An unexpected error occurred while generating the PDF.");
        }
    }

    public async Task<IActionResult> EyeshotDwg(int resultId, string drawing = "front_elevation")
    {
        try
        {
            var result = await LoadResult(resultId);
            if (result == null) return NotFound();

            var di = result.DesignInput;

            byte[] bytes = drawing switch
            {
                "front_elevation" => AxialFanDrawingService.FrontElevationDxf(di, result),
                "cross_section" => AxialFanDrawingService.CrossSectionDxf(di, result),
                "blade_profile" => AxialFanDrawingService.BladeProfileDxf(di, result),
                "blade_angle" => AxialFanDrawingService.BladeAngleDxf(di, result),
                "hub_detail" => AxialFanDrawingService.HubDetailDxf(di, result),
                "casing_detail" => AxialFanDrawingService.CasingDetailDxf(di, result),
                "general_arrangement" => AxialFanDrawingService.GeneralArrangementDxf(di, result),
                _ => throw new ArgumentException($"Unknown drawing type: {drawing}")
            };

            await SaveDwgPath(result, drawing, $"{drawing}_{resultId}.dxf", bytes);
            await LogExport(di.ProjectId, "dxf");

            return File(bytes, "application/dxf", $"AxialFan_{drawing}_{resultId}.dxf");
        }
        catch (Exception ex)
        {
            _exceptionHandlerRepository.SaveException(
                nameof(ExportController),
                nameof(EyeshotDwg),
                ex.ToString());

            return RedirectToAction("Index", "Home");
        }
    }

    // ── POST /Export/ChartsOnlyPdfWithSnapshot ────────────────────────
    [HttpPost("Export/ChartsOnlyPdfWithSnapshot"), ValidateAntiForgeryToken]
    public async Task<IActionResult> ChartsOnlyPdfWithSnapshot([FromBody] PdfWithChartsRequest req)
    {
        try
        {
            var result = await LoadResult(req.ResultId);
            if (result == null) return NotFound();

            await LogExport(result.DesignInput.ProjectId, "pdf");

            byte[] qdpBytes = DecodeDataUrl(req.QdpImageBase64);
            byte[] etaBytes = DecodeDataUrl(req.EtaImageBase64);

            var (bytes, fileName) = _exportSvc.ExportChartsOnlyPdf(
                result, qdpBytes, etaBytes, req.Curves ?? new List<CurveComparisonRow>());

            return File(bytes, "application/pdf", fileName);
        }
        catch (Exception ex)
        {
            _exceptionHandlerRepository.SaveException(
                nameof(ExportController),
                nameof(ChartsOnlyPdfWithSnapshot),
                ex.ToString());

            return StatusCode(500, "An unexpected error occurred while generating the PDF.");
        }
    }

    private static byte[] DecodeDataUrl(string dataUrl)
    {
        // dataUrl looks like "data:image/png;base64,iVBORw0KG..."
        var comma = dataUrl.IndexOf(',');
        var base64 = comma >= 0 ? dataUrl[(comma + 1)..] : dataUrl;
        return Convert.FromBase64String(base64);
    }

    // ── GET /Export/FullReportPdf?resultId=15 ─────────────────────────
    // The one canonical, complete PDF report — Input, Aerodynamic,
    // Structural, AND Acoustic sections, plus server-rendered ΔP/η
    // charts. Fixes the two gaps every prior PDF path had (missing
    // Acoustic section; charts only present in some paths, and even
    // those needed a client-side snapshot). No request body needed —
    // charts are rendered server-side via SkiaSharp, same as ChartsOnlyPdf.
    [HttpGet]
    public async Task<IActionResult> FullReportPdf(int resultId)
    {
        try
        {
            var result = await LoadResult(resultId);
            if (result == null) return NotFound();

            await LogExport(result.DesignInput.ProjectId, "pdf");
            var (bytes, fileName) = _exportSvc.ExportFullPdfReport(result);
            return File(bytes, "application/pdf", fileName);
        }
        catch (Exception ex)
        {
            _exceptionHandlerRepository.SaveException(
                nameof(ExportController),
                nameof(FullReportPdf),
                ex.ToString());

            return RedirectToAction("Index", "Home");
        }
    }

    // ── GET /Export/ChartsOnlyPdf?resultId=15 ─────────────────────────
    [HttpGet]
    public async Task<IActionResult> ChartsOnlyPdf(int resultId)
    {
        try
        {
            var result = await LoadResult(resultId);
            if (result == null) return NotFound();

            await LogExport(result.DesignInput.ProjectId, "pdf");
            var (bytes, fileName) = _exportSvc.ExportChartsOnlyPdf(result);
            return File(bytes, "application/pdf", fileName);
        }
        catch (Exception ex)
        {
            _exceptionHandlerRepository.SaveException(
                nameof(ExportController),
                nameof(ChartsOnlyPdf),
                ex.ToString());

            return RedirectToAction("Index", "Home");
        }
    }

    public class PdfWithChartsRequest
    {
        public int ResultId { get; set; }
        public string QdpImageBase64 { get; set; } = "";
        public string EtaImageBase64 { get; set; } = "";
        public List<CurveComparisonRow>? Curves { get; set; }
    }

    public class ExportPdfRequest
    {
        public int ResultId { get; set; }

        public string QdpChart { get; set; } = string.Empty;

        public string EtaChart { get; set; } = string.Empty;
    }
}
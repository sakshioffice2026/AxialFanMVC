using AxialFanMVC.Database;
using AxialFanMVC.Repositories.Inteface;
using AxialFanMVC.ViewModels;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Security.Claims;

namespace AxialFanMVC.Controllers
{
    [Authorize]
    public class ReportsController : Controller
    {
        private readonly AxialFanDbContext _db;
        private readonly IExceptionHandlerRepository _exceptionHandlerRepository;
       public ReportsController(
       AxialFanDbContext db,
       IExceptionHandlerRepository exceptionHandlerRepository)
        {
            _db = db;
            _exceptionHandlerRepository = exceptionHandlerRepository;
        }

        private int CurrentUserId =>
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // GET /Reports?projectId=5&status=warning
        public async Task<IActionResult> Index(int? projectId = null, string? status = null)
        {
            try
            {
                if (projectId.HasValue)
                {
                    var owns = await _db.Projects
                        .AnyAsync(p => p.Id == projectId.Value && p.UserId == CurrentUserId);

                    if (!owns)
                        return NotFound();
                }

                var (rows, total, ok, warning, pending) = await LoadDesigns(projectId, status);

                var projectFilterOptions = await _db.Projects
                    .Where(p => p.UserId == CurrentUserId)
                    .OrderBy(p => p.Name)
                    .Select(p => new ProjectFilterOption
                    {
                        Id = p.Id,
                        Name = p.Name
                    })
                    .ToListAsync();

                var vm = new ReportsViewModel
                {
                    SelectedProjectId = projectId,
                    SelectedStatus = status,
                    ProjectFilterOptions = projectFilterOptions,
                    TotalDesigns = total,
                    CalculatedCount = ok,
                    WarningCount = warning,
                    PendingCount = pending,
                    Designs = rows
                };

                return View(vm);
            }
            catch (Exception ex)
            {
                _exceptionHandlerRepository.SaveException(
                    nameof(ReportsController),
                    nameof(Index),
                    ex.ToString());

                TempData["Error"] = "Unable to load reports.";

                return RedirectToAction("Index", "Projects");
            }
        }

        // GET /Reports/ExportPdf?projectId=5&status=warning
        public async Task<IActionResult> ExportPdf(int? projectId = null, string? status = null)
        {
            try
            {
                if (projectId.HasValue)
                {
                    var owns = await _db.Projects
                        .AnyAsync(p => p.Id == projectId.Value && p.UserId == CurrentUserId);

                    if (!owns)
                        return NotFound();
                }

                var (rows, total, ok, warning, pending) = await LoadDesigns(projectId, status);

                QuestPDF.Settings.License = LicenseType.Community;

                var pdfBytes = Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Margin(24);
                        page.Size(PageSizes.A4.Landscape());
                        page.DefaultTextStyle(x => x.FontSize(8));

                        page.Header().Column(col =>
                        {
                            col.Item().Text("AxialFlow Designer — Design Report").FontSize(15).Bold();
                            col.Item().PaddingTop(2).Text(
                                $"Generated {DateTime.Now:dd MMM yyyy HH:mm}   |   " +
                                $"{total} design(s)   |   {ok} OK   |   {warning} warning   |   {pending} pending");
                        });

                        page.Content().PaddingTop(10).Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn(2);
                                c.RelativeColumn(2);
                                c.RelativeColumn(1);
                                c.RelativeColumn(1);
                                c.RelativeColumn(1);
                                c.RelativeColumn(1);
                                c.RelativeColumn(1);
                                c.RelativeColumn(1);
                                c.RelativeColumn(1);
                                c.RelativeColumn(1);
                                c.RelativeColumn(1);
                                c.RelativeColumn(1);
                                c.RelativeColumn(1);
                            });

                            table.Header(header =>
                            {
                                void H(string text) => header.Cell()
                                    .Background("#1864ab")
                                    .Padding(3)
                                    .Text(text)
                                    .FontColor("#ffffff")
                                    .Bold();

                                H("Project");
                                H("Application");
                                H("Media");
                                H("Flow (m³/s)");
                                H("ΔP (Pa)");
                                H("Speed (RPM)");
                                H("Blades");
                                H("Tip Dia (mm)");
                                H("Eff (%)");
                                H("Shaft (kW)");
                                H("SF");
                                H("Status");
                                H("Created");
                            });

                            foreach (var d in rows)
                            {
                                void C(string text) => table.Cell()
                                    .BorderBottom(1)
                                    .BorderColor("#dee2e6")
                                    .Padding(3)
                                    .Text(text);

                                C(d.ProjectName);
                                C(d.Application ?? "-");
                                C(d.MediaType);
                                C(d.FlowRateM3s.ToString("F2"));
                                C(d.TotalPressurePa.ToString("F0"));
                                C(d.SpeedRpm.ToString());
                                C(d.BladeCount.ToString());
                                C(d.TipDiameterMm.ToString("F0"));
                                C(d.OverallEfficiencyPct.HasValue
                                    ? d.OverallEfficiencyPct.Value.ToString("F1")
                                    : "-");
                                C(d.ShaftPowerKw.HasValue
                                    ? d.ShaftPowerKw.Value.ToString("F2")
                                    : "-");
                                C(d.SafetyFactor.HasValue
                                    ? d.SafetyFactor.Value.ToString("F2")
                                    : "-");
                                C(d.Status.ToUpperInvariant());
                                C(d.CreatedAt.ToString("dd MMM yyyy"));
                            }
                        });

                        page.Footer()
                            .AlignCenter()
                            .Text("Generated by AxialFlow Designer — for engineering reference only.");
                    });
                }).GeneratePdf();

                await LogExportForRows(rows, "pdf");

                return File(
                    pdfBytes,
                    "application/pdf",
                    $"AxialFan_Designs_Report_{DateTime.Now:yyyyMMdd_HHmm}.pdf");
            }
            catch (Exception ex)
            {
                _exceptionHandlerRepository.SaveException(
                    nameof(ReportsController),
                    nameof(ExportPdf),
                    ex.ToString());

                TempData["Error"] = "Unable to generate PDF report.";

                return RedirectToAction(nameof(Index));
            }
        }

        // GET /Reports/ExportExcel?projectId=5&status=warning
        public async Task<IActionResult> ExportExcel(int? projectId = null, string? status = null)
        {
            try
            {
                if (projectId.HasValue)
                {
                    var owns = await _db.Projects
                        .AnyAsync(p => p.Id == projectId.Value && p.UserId == CurrentUserId);

                    if (!owns)
                        return NotFound();
                }

                var (rows, _, _, _, _) = await LoadDesigns(projectId, status);

                using var wb = new XLWorkbook();
                var ws = wb.Worksheets.Add("Designs");

                string[] headers =
                {
            "Project", "Client", "Application", "Engineer", "Media Type",
            "Flow Rate (m3/s)", "Total Pressure (Pa)", "Fan Speed (RPM)",
            "Blade Count", "Tip Diameter (mm)", "Overall Efficiency (%)",
            "Shaft Power (kW)", "Safety Factor", "Status", "Created"
        };

                for (int i = 0; i < headers.Length; i++)
                    ws.Cell(1, i + 1).Value = headers[i];

                ws.Row(1).Style.Font.Bold = true;
                ws.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1864ab");
                ws.Row(1).Style.Font.FontColor = XLColor.White;

                int r = 2;

                foreach (var d in rows)
                {
                    ws.Cell(r, 1).Value = d.ProjectName;
                    ws.Cell(r, 2).Value = d.Client ?? "";
                    ws.Cell(r, 3).Value = d.Application ?? "";
                    ws.Cell(r, 4).Value = d.Engineer ?? "";
                    ws.Cell(r, 5).Value = d.MediaType;
                    ws.Cell(r, 6).Value = d.FlowRateM3s;
                    ws.Cell(r, 7).Value = d.TotalPressurePa;
                    ws.Cell(r, 8).Value = d.SpeedRpm;
                    ws.Cell(r, 9).Value = d.BladeCount;
                    ws.Cell(r, 10).Value = d.TipDiameterMm;
                    ws.Cell(r, 11).Value = d.OverallEfficiencyPct;
                    ws.Cell(r, 12).Value = d.ShaftPowerKw;
                    ws.Cell(r, 13).Value = d.SafetyFactor;
                    ws.Cell(r, 14).Value = d.Status.ToUpperInvariant();
                    ws.Cell(r, 15).Value = d.CreatedAt;
                    ws.Cell(r, 15).Style.DateFormat.Format = "dd MMM yyyy";
                    r++;
                }

                ws.Columns().AdjustToContents();

                using var ms = new MemoryStream();
                wb.SaveAs(ms);

                await LogExportForRows(rows, "xlsx");

                return File(
                    ms.ToArray(),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"AxialFan_Designs_Report_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
            }
            catch (Exception ex)
            {
                _exceptionHandlerRepository.SaveException(
                    nameof(ReportsController),
                    nameof(ExportExcel),
                    ex.ToString());

                TempData["Error"] = "Unable to generate Excel report.";

                return RedirectToAction(nameof(Index));
            }
        }

        // ── Shared query used by Index / ExportPdf / ExportExcel ────────
        private async Task<(List<DesignReportRow> Rows, int Total, int Ok, int Warning, int Pending)>
            LoadDesigns(int? projectId, string? status)
        {
            var userProjectIds = await _db.Projects
                .Where(p => p.UserId == CurrentUserId)
                .Select(p => p.Id)
                .ToListAsync();

            var scopedProjectIds = projectId.HasValue
                ? new List<int> { projectId.Value }
                : userProjectIds;

            var query = _db.design_inputs
                .Include(d => d.Project)
                .Include(d => d.DesignResult)
                .Where(d => scopedProjectIds.Contains(d.ProjectId));

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = status == "pending"
                    ? query.Where(d => d.DesignResult == null)
                    : query.Where(d => d.DesignResult != null && d.DesignResult.Status == status);
            }

            var rows = await query
                .OrderByDescending(d => d.CreatedAt)
                .Select(d => new DesignReportRow
                {
                    DesignInputId = d.Id,
                    ResultId = d.DesignResult != null ? d.DesignResult.Id : (int?)null,
                    ProjectName = d.Project.Name,
                    Client = d.Project.Client,
                    Application = d.Project.Application,
                    Engineer = d.Project.Engineer,
                    MediaType = d.MediaType,
                    FlowRateM3s = d.FlowRateM3s,
                    TotalPressurePa = d.TotalPressurePa,
                    SpeedRpm = d.SpeedRpm,
                    BladeCount = d.BladeCount,
                    TipDiameterMm = d.TipDiameterMm,
                    OverallEfficiencyPct = d.DesignResult != null ? d.DesignResult.OverallEfficiencyPct : (double?)null,
                    ShaftPowerKw = d.DesignResult != null ? d.DesignResult.ShaftPowerKw : (double?)null,
                    SafetyFactor = d.DesignResult != null ? d.DesignResult.SafetyFactor : (double?)null,
                    Status = d.DesignResult != null ? d.DesignResult.Status : "pending",
                    CreatedAt = d.CreatedAt
                })
                .ToListAsync();

            var total = rows.Count;
            var ok = rows.Count(r => r.Status == "ok");
            var warning = rows.Count(r => r.Status == "warning");
            var pending = rows.Count(r => r.Status == "pending");

            return (rows, total, ok, warning, pending);
        }

        private async Task LogExportForRows(List<DesignReportRow> rows, string format)
        {
            var userProjectIds = await _db.Projects
                .Where(p => p.UserId == CurrentUserId)
                .Select(p => p.Id)
                .ToListAsync();

            // Log once per distinct project actually represented in the export,
            // falling back to a single log entry against the user's first project
            // if the filtered result set happened to be empty.
            var distinctProjectIds = rows
                .Select(r => r.DesignInputId)
                .Any()
                ? (await _db.design_inputs
                    .Where(d => rows.Select(r => r.DesignInputId).Contains(d.Id))
                    .Select(d => d.ProjectId)
                    .Distinct()
                    .ToListAsync())
                : new List<int>();

            if (!distinctProjectIds.Any() && userProjectIds.Any())
                distinctProjectIds.Add(userProjectIds.First());

            foreach (var pid in distinctProjectIds)
            {
                _db.export_logs.Add(new ExportLog
                {
                    ProjectId = pid,
                    UserId = CurrentUserId,
                    Format = format
                });
            }

            if (distinctProjectIds.Any())
                await _db.SaveChangesAsync();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AxialFanMVC.Database;
using AxialFanMVC.Models;
using AxialFanMVC.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AxialFan.Web.Controllers
{
    // ─────────────────────────────────────────────────────────────────────────
    // BladeProfilesController
    //
    // ASP.NET Core MVC (not API) version of the blade-profile feature.
    // Routes use conventional MVC routing; views are Razor (.cshtml).
    //
    // Route summary
    // ─────────────────────────────────────────────────────────────────────────
    //  GET  /BladeProfiles                        → Index  (list all profiles)
    //  GET  /BladeProfiles/Detail/{id}            → Detail (existing DB profile)
    //  GET  /BladeProfiles/Naca/{designation}     → Naca   (generate on-the-fly)
    //  GET  /BladeProfiles/Compare                → Compare (overlay two profiles)
    //  GET  /BladeProfiles/Upload                 → Upload form
    //  POST /BladeProfiles/Upload                 → Upload submit
    //  GET  /BladeProfiles/Svg/{id}               → SVG file download (DWG-003)
    //  GET  /BladeProfiles/StationSvg/{id}        → SVG file download (DWG-004)
    //  GET  /BladeProfiles/Coordinates/{id}       → Coordinate export (JSON / CSV)
    //  GET  /BladeProfiles/PreviewPartial?profileId={id}&chord={mm}
    //                                             → Partial view (AJAX from Wizard)
    // ─────────────────────────────────────────────────────────────────────────
    [Authorize]
    public class BladeProfilesController : Controller
    {
        private readonly AxialFanDbContext _db;

        public BladeProfilesController(AxialFanDbContext db) => _db = db;

        // ── INDEX ─────────────────────────────────────────────────────────────
        /// <summary>Lists all blade profiles stored in the database.</summary>
        public async Task<IActionResult> Index()
        {
            var profiles = await _db.blade_profiles.OrderBy(b => b.Id).ToListAsync();

            var vm = new BladeProfileListViewModel
            {
                Profiles = profiles.Select(MapToSummary).ToList()
            };
            return View(vm);
        }

        // ── DETAIL ────────────────────────────────────────────────────────────
        /// <summary>Shows full detail for a profile stored in the database.</summary>
        /// <param name="id">Database ID</param>
        /// <param name="chord">Chord length in mm (default 148.3)</param>
        /// <param name="points">Number of coordinate points (default 100)</param>
        public async Task<IActionResult> Detail(int id,
            double chord = 148.3, int points = 100)
        {
            var bp = await _db.blade_profiles.FindAsync(id);
            if (bp == null) return NotFound();

            var data = GenerateProfileData(bp, chord, points);
            if (data == null)
            {
                return View("Error", "Could not generate profile data for this entry.");
            }

            var vm = MapToDetailViewModel(bp, data, chord, points);
            return View(vm);
        }

        // ── NACA (on-the-fly) ─────────────────────────────────────────────────
        /// <summary>
        /// Generates a NACA profile on the fly from the designation string.
        /// Supports 4-digit (e.g. "4412") and 5-digit (e.g. "23012").
        /// The profile is NOT stored in the database.
        /// </summary>
        public IActionResult Naca(string designation,
            double chord = 148.3, int points = 100)
        {
            designation = designation
                .Replace("NACA", "", StringComparison.OrdinalIgnoreCase)
                .Replace(" ", "").Trim().ToUpper();

            BladeProfileData? data = null;
            string type;

            try
            {
                if (designation.Length == 4)
                {
                    data = BladeProfileEngine.GenerateNaca4("NACA " + designation, chord, points);
                    type = "NACA 4-digit";
                }
                else if (designation.Length == 5)
                {
                    data = BladeProfileEngine.GenerateNaca5("NACA " + designation, chord, points);
                    type = "NACA 5-digit";
                }
                else
                {
                    TempData["Error"] = "Provide a 4- or 5-digit NACA designation (e.g. 4412 or 23012).";
                    return RedirectToAction(nameof(Index));
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Invalid NACA designation: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }

            var synthetic = new BladeProfile
            {
                Id          = 0,
                Name        = $"NACA {designation}",
                Type        = type,
                Description = "Generated on demand — not stored in database."
            };

            var vm = MapToDetailViewModel(synthetic, data, chord, points);
            return View("Detail", vm);
        }

        // ── COMPARE ───────────────────────────────────────────────────────────
        /// <summary>Side-by-side comparison of two profiles with overlay SVG.</summary>
        [HttpGet]
        public async Task<IActionResult> Compare(
            int? profile1Id, int? profile2Id, double chord = 148.3)
        {
            var allProfiles = await _db.blade_profiles.OrderBy(b => b.Id).ToListAsync();

            var vm = new CompareViewModel
            {
                ChordMm     = chord,
                AllProfiles = allProfiles.Select(MapToSummary).ToList(),
                Profile1Id  = profile1Id ?? 0,
                Profile2Id  = profile2Id ?? 0,
            };

            if (profile1Id.HasValue && profile2Id.HasValue)
            {
                var bp1 = allProfiles.FirstOrDefault(b => b.Id == profile1Id.Value);
                var bp2 = allProfiles.FirstOrDefault(b => b.Id == profile2Id.Value);

                if (bp1 == null || bp2 == null)
                {
                    vm.ErrorMessage = "One or both profiles were not found.";
                    return View(vm);
                }

                var d1 = GenerateProfileData(bp1, chord, 100);
                var d2 = GenerateProfileData(bp2, chord, 100);

                if (d1 == null || d2 == null)
                {
                    vm.ErrorMessage = "Could not generate profile data for one of the selected profiles.";
                    return View(vm);
                }

                vm.Profile1    = MapToDetailViewModel(bp1, d1, chord, 100);
                vm.Profile2    = MapToDetailViewModel(bp2, d2, chord, 100);
                vm.OverlaySvg  = BuildOverlaySvg(d1, d2);
            }

            return View(vm);
        }

        // ── UPLOAD (GET) ──────────────────────────────────────────────────────
        [HttpGet]
        public IActionResult Upload()
        {
            return View(new UploadCustomProfileViewModel());
        }

        // ── UPLOAD (POST) ─────────────────────────────────────────────────────
        /// <summary>Accepts a custom profile upload and persists it to the database.</summary>
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Upload(UploadCustomProfileViewModel form)
        {
            if (!ModelState.IsValid) return View(form);

            // Parse coordinate JSON
            List<double[]>? rawArrays;
            try
            {
                rawArrays = JsonSerializer.Deserialize<List<double[]>>(form.CoordinateJson);
                if (rawArrays == null || rawArrays.Count < 10)
                {
                    ModelState.AddModelError(nameof(form.CoordinateJson),
                        "At least 10 coordinate triplets required.");
                    return View(form);
                }
                if (rawArrays.Any(r => r.Length < 3))
                {
                    ModelState.AddModelError(nameof(form.CoordinateJson),
                        "Each entry must have 3 values: [x, y_upper, y_lower].");
                    return View(form);
                }
            }
            catch
            {
                ModelState.AddModelError(nameof(form.CoordinateJson),
                    "Invalid JSON. Expected array of [x, y_upper, y_lower] triplets.");
                return View(form);
            }

            // Normalise x to 0–1 if supplied as percentage (0–100)
            bool isPercent = rawArrays.Max(r => r[0]) > 1.5;
            var coords = rawArrays
                .Select(r => (
                    x:  isPercent ? r[0] / 100.0 : r[0],
                    yU: r[1],
                    yL: r[2]))
                .ToList();

            // Generate and validate profile data
            BladeProfileData data;
            try
            {
                data = BladeProfileEngine.GenerateCustom(form.Name, form.ChordMm, coords);
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, $"Profile generation failed: {ex.Message}");
                return View(form);
            }

            // Persist to database
            var bp = new BladeProfile
            {
                Name           = form.Name,
                Type           = "custom",
                Description    = form.Description,
                CoordinateData = form.CoordinateJson
            };
            _db.blade_profiles.Add(bp);
            await _db.SaveChangesAsync();

            TempData["Success"] = $"Profile \"{form.Name}\" uploaded successfully.";
            return RedirectToAction(nameof(Detail), new { id = bp.Id, chord = form.ChordMm });
        }

        // ── SVG DOWNLOAD (DWG-003) ────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Svg(int id, double chord = 148.3)
        {
            var bp   = await _db.blade_profiles.FindAsync(id);
            if (bp == null) return NotFound();
            var data = GenerateProfileData(bp, chord, 100);
            if (data == null) return NotFound();

            var svg = BladeProfileDrawingService.DrawAerofoilProfile(data);
            return File(
                System.Text.Encoding.UTF8.GetBytes(svg),
                "image/svg+xml",
                $"{bp.Name.Replace(" ", "_")}_profile.svg");
        }

        // ── STATION SVG DOWNLOAD (DWG-004) ────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> StationSvg(int id, double chord = 148.3)
        {
            var bp   = await _db.blade_profiles.FindAsync(id);
            if (bp == null) return NotFound();
            var data = GenerateProfileData(bp, chord, 100);
            if (data == null) return NotFound();

            var svg = BladeProfileDrawingService.DrawStationDiagram(data);
            return File(
                System.Text.Encoding.UTF8.GetBytes(svg),
                "image/svg+xml",
                $"{bp.Name.Replace(" ", "_")}_stations.svg");
        }

        // ── COORDINATE EXPORT ────────────────────────────────────────────────
        /// <summary>
        /// Returns raw coordinates in JSON or CSV format.
        /// Query param: format = normalised | mm | csv  (default: normalised)
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Coordinates(
            int id, double chord = 148.3, string format = "normalised")
        {
            var bp   = await _db.blade_profiles.FindAsync(id);
            if (bp == null) return NotFound();
            var data = GenerateProfileData(bp, chord, 200); // more points for CAD
            if (data == null) return NotFound();

            if (format == "csv")
            {
                var csv = new System.Text.StringBuilder();
                csv.AppendLine($"# {data.Designation}  Chord={chord}mm");
                csv.AppendLine("# Upper surface (x_mm, y_mm)");
                foreach (var p in data.UpperSurface)
                    csv.AppendLine($"{p.X * chord:F4},{p.Y * chord:F4}");
                csv.AppendLine("# Lower surface (x_mm, y_mm)");
                foreach (var p in data.LowerSurface)
                    csv.AppendLine($"{p.X * chord:F4},{p.Y * chord:F4}");
                csv.AppendLine("# Camber line (x_mm, y_mm)");
                foreach (var p in data.CamberLine)
                    csv.AppendLine($"{p.X * chord:F4},{p.Y * chord:F4}");

                return File(
                    System.Text.Encoding.UTF8.GetBytes(csv.ToString()),
                    "text/csv",
                    $"{data.Designation.Replace(" ", "_")}_coords.csv");
            }

            double scale = format == "mm" ? chord : 1.0;
            var json = new
            {
                designation = data.Designation,
                chordMm     = chord,
                format      = format,
                upper  = data.UpperSurface.Select(p => new { x = Math.Round(p.X * scale, 4), y = Math.Round(p.Y * scale, 4) }),
                lower  = data.LowerSurface.Select(p => new { x = Math.Round(p.X * scale, 4), y = Math.Round(p.Y * scale, 4) }),
                camber = data.CamberLine.Select(p =>   new { x = Math.Round(p.X * scale, 4), y = Math.Round(p.Y * scale, 4) }),
            };
            return Json(json);
        }

        // ── PREVIEW PARTIAL (AJAX — called by Wizard dropdown) ────────────────
        /// <summary>
        /// Returns a partial view with the SVG drawing + key dimensions
        /// for the selected blade profile.
        ///
        /// Called via fetch() in Wizard.cshtml when the user changes the
        /// Blade Profile dropdown:
        ///   GET /BladeProfiles/PreviewPartial?profileId=7&amp;chord=400
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> PreviewPartial(int profileId, double chord = 148.3)
        {
            var bp = await _db.blade_profiles.FindAsync(profileId);
            if (bp == null)
                return Content("<div class='alert alert-danger small mb-0'>Profile not found.</div>");

            var data = GenerateProfileData(bp, chord, points: 100);
            if (data == null)
                return Content("<div class='alert alert-danger small mb-0'>Could not generate profile data.</div>");

            var vm = MapToDetailViewModel(bp, data, chord, points: 100);
            return PartialView("_BladeProfilePreview", vm);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Private helpers
        // ─────────────────────────────────────────────────────────────────────

        private static BladeProfileData? GenerateProfileData(
            BladeProfile bp, double chord, int points)
        {
            try
            {
                if (bp.Type == "custom" && bp.CoordinateData != null)
                {
                    var rawArrays = JsonSerializer.Deserialize<List<double[]>>(bp.CoordinateData)!;
                    bool isPct    = rawArrays.Max(r => r[0]) > 1.5;
                    var coords    = rawArrays
                        .Select(r => (x: isPct ? r[0] / 100.0 : r[0], yU: r[1], yL: r[2]))
                        .ToList();
                    return BladeProfileEngine.GenerateCustom(bp.Name, chord, coords);
                }

                string desig = bp.Name
                    .Replace("NACA", "", StringComparison.OrdinalIgnoreCase)
                    .Replace(" ", "").Trim();

                return desig.Length == 4
                    ? BladeProfileEngine.GenerateNaca4("NACA " + desig, chord, points)
                    : BladeProfileEngine.GenerateNaca5("NACA " + desig, chord, points);
            }
            catch { return null; }
        }

        private static BladeProfileDetailViewModel MapToDetailViewModel(
            BladeProfile bp, BladeProfileData data, double chord, int points) => new()
        {
            Id              = bp.Id,
            Name            = bp.Name,
            Type            = bp.Type,
            Description     = bp.Description,
            ChordMm         = chord,
            Points          = points,
            MaxCamberPct    = data.MaxCamberPct,
            MaxCamberPos    = data.MaxCamberPos,
            MaxThicknessPct = data.MaxThicknessPct,
            HasCoordinates  = true,
            Dimensions      = MapDimensions(data.Dimensions),
            AeroParams      = MapAeroParams(data.AeroParams),
            StationTable    = data.StationTable.Select(MapStation).ToList(),
            SvgProfile      = BladeProfileDrawingService.DrawAerofoilProfile(data),
            SvgStationTable = BladeProfileDrawingService.DrawStationDiagram(data),
            UpperNormalised = data.UpperSurface.Select(p => new[] { p.X, p.Y }).ToList(),
            LowerNormalised = data.LowerSurface.Select(p => new[] { p.X, p.Y }).ToList(),
            CamberNormalised= data.CamberLine.Select(p => new[] { p.X, p.Y }).ToList(),
            UpperMm         = data.UpperSurface.Select(p => new[] { p.X * chord, p.Y * chord }).ToList(),
            LowerMm         = data.LowerSurface.Select(p => new[] { p.X * chord, p.Y * chord }).ToList(),
        };

        private static BladeProfileSummary MapToSummary(BladeProfile bp)
        {
            double m = 0, p = 0, t = 0.12;
            try
            {
                string d = bp.Name
                    .Replace("NACA", "", StringComparison.OrdinalIgnoreCase)
                    .Replace(" ", "").Trim();
                if (d.Length == 4 && int.TryParse(d, out _))
                {
                    m = int.Parse(d[0].ToString()) / 100.0;
                    p = int.Parse(d[1].ToString()) / 10.0;
                    t = int.Parse(d.Substring(2)) / 100.0;
                }
            }
            catch { }

            return new BladeProfileSummary
            {
                Id              = bp.Id,
                Name            = bp.Name,
                Type            = bp.Type,
                Description     = bp.Description,
                MaxCamberPct    = Math.Round(m * 100, 1),
                MaxCamberPos    = Math.Round(p * 10, 0),
                MaxThicknessPct = Math.Round(t * 100, 1),
                HasCoordinates  = bp.CoordinateData != null || bp.Type == "NACA"
            };
        }

        private static string BuildOverlaySvg(BladeProfileData p1, BladeProfileData p2)
        {
            const double sc = 600;
            const double cx = 80, cy = 200;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(@"<svg xmlns=""http://www.w3.org/2000/svg"" width=""720"" height=""280"" viewBox=""0 0 720 280"" font-family=""sans-serif"">");
            sb.AppendLine(@"<rect width=""100%"" height=""100%"" fill=""white""/>");
            sb.AppendLine($@"<line x1=""{cx}"" y1=""{cy}"" x2=""{cx + sc}"" y2=""{cy}"" stroke=""#ccc"" stroke-width=""0.5"" stroke-dasharray=""6 3""/>");

            void DrawProfile(BladeProfileData p, string color, string dash = "none")
            {
                string up = "M " + string.Join(" L ", p.UpperSurface.Select(pt => $"{cx + pt.X * sc:F1},{cy - pt.Y * sc:F1}"));
                string lo = "M " + string.Join(" L ", p.LowerSurface.Select(pt => $"{cx + pt.X * sc:F1},{cy - pt.Y * sc:F1}"));
                string dd = dash == "none" ? "" : $@" stroke-dasharray=""{dash}""";
                sb.AppendLine($@"<path d=""{up}"" fill=""none"" stroke=""{color}"" stroke-width=""1.8""{dd}/>");
                sb.AppendLine($@"<path d=""{lo}"" fill=""none"" stroke=""{color}"" stroke-width=""1.8""{dd}/>");
            }

            DrawProfile(p1, "#185FA5");
            DrawProfile(p2, "#c00", "5 3");

            sb.AppendLine($@"<line x1=""20"" y1=""240"" x2=""50"" y2=""240"" stroke=""#185FA5"" stroke-width=""2""/><text x=""55"" y=""244"" font-size=""10"" fill=""#185FA5"">{p1.Designation}</text>");
            sb.AppendLine($@"<line x1=""160"" y1=""240"" x2=""190"" y2=""240"" stroke=""#c00"" stroke-width=""2"" stroke-dasharray=""5 3""/><text x=""195"" y=""244"" font-size=""10"" fill=""#c00"">{p2.Designation}</text>");
            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        private static ProfileDimensionsViewModel MapDimensions(ProfileDimensions d) => new()
        {
            ChordMm              = d.ChordMm,
            MaxThicknessMm       = d.MaxThicknessMm,
            MaxThicknessPct      = d.MaxThicknessPct,
            MaxThicknessXPct     = d.MaxThicknessXPct,
            MaxThicknessXMm      = d.MaxThicknessXMm,
            MaxCamberMm          = d.MaxCamberMm,
            MaxCamberPct         = d.MaxCamberPct,
            MaxCamberXPct        = d.MaxCamberXPct,
            MaxCamberXMm         = d.MaxCamberXMm,
            LeadingEdgeRadiusMm  = d.LeadingEdgeRadiusMm,
            LeadingEdgeRadiusPct = d.LeadingEdgeRadiusPct,
            TrailingEdgeThickMm  = d.TrailingEdgeThickMm,
            MeanLineAngle        = d.MeanLineAngle
        };

        private static AeroParamsViewModel MapAeroParams(AeroParameters a) => new()
        {
            DesignLiftCoeff      = a.DesignLiftCoeff,
            ThicknessRatio       = a.ThicknessRatio,
            MaxCamberLocation    = a.MaxCamberLocation,
            LeadingEdgeRadius    = a.LeadingEdgeRadius,
            LeadingEdgeRadiusPct = a.LeadingEdgeRadiusPct,
            TrailingEdgeAngle    = a.TrailingEdgeAngle,
            LiftCurveSlope       = a.LiftCurveSlope,
            ApproxZeroLiftAngle  = a.ApproxZeroLiftAngle,
            ApproxStallAngle     = a.ApproxStallAngle,
            ApproxMaxCl          = a.ApproxMaxCl,
            ApproxMinDrag        = a.ApproxMinDrag,
            ReynoldsRange        = a.ReynoldsRange,
            Note                 = a.Note
        };

        private static StationRowViewModel MapStation(StationRow r) => new()
        {
            XPct        = r.XPct,
            XMm         = r.XMm,
            YUpperMm    = r.YUpperMm,
            YLowerMm    = r.YLowerMm,
            YCamberMm   = r.YCamberMm,
            ThicknessMm = r.ThicknessMm,
            ThicknessPct= r.ThicknessPct
        };
    }
}

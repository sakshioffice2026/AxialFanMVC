using AxialFanMVC.Database;
using AxialFanMVC.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AxialFanMVC.Controllers
{
    [Authorize]
    public class ProjectsController : Controller
    {
        private readonly AxialFanDbContext _db;
        public ProjectsController(AxialFanDbContext db) => _db = db;

        private int CurrentUserId =>
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // GET /Projects
        public async Task<IActionResult> Index(int page = 1, int pageSize = 20)
        {
            var query = _db.Projects.Where(p => p.UserId == CurrentUserId);
            int total = await query.CountAsync();

            var projects = await query
                .OrderByDescending(p => p.UpdatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new ProjectSummaryViewModel
                {
                    Id = p.Id,
                    Name = p.Name,
                    Description = p.Description,
                    Status = p.Status,
                    CreatedAt = p.CreatedAt,
                    UpdatedAt = p.UpdatedAt,
                    DesignCount = p.DesignInputs.Count
                })
                .ToListAsync();

            return View(new ProjectListViewModel
            {
                Projects = projects,
                Page = page,
                PageSize = pageSize,
                Total = total
            });
        }

        // GET /Projects/Create
        public IActionResult Create() => View(new CreateProjectViewModel());

        // POST /Projects/Create
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateProjectViewModel vm)
        {
            if (!ModelState.IsValid) return View(vm);

            var project = new Project
            {
                UserId = CurrentUserId,
                Name = vm.Name,
                Description = vm.Description
            };
            _db.Projects.Add(project);
            await _db.SaveChangesAsync();

            TempData["Success"] = "Project created successfully.";
            return RedirectToAction(nameof(Index));
        }

        // GET /Projects/Edit/5
        public async Task<IActionResult> Edit(int id)
        {
            var project = await _db.Projects
                .FirstOrDefaultAsync(p => p.Id == id && p.UserId == CurrentUserId);
            if (project == null) return NotFound();

            return View(new EditProjectViewModel
            {
                Id = project.Id,
                Name = project.Name,
                Description = project.Description,
                Status = project.Status
            });
        }

        // POST /Projects/Edit/5
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, EditProjectViewModel vm)
        {
            if (!ModelState.IsValid) return View(vm);

            var project = await _db.Projects
                .FirstOrDefaultAsync(p => p.Id == id && p.UserId == CurrentUserId);
            if (project == null) return NotFound();

            project.Name = vm.Name;
            project.Description = vm.Description;
            project.Status = vm.Status;
            project.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            TempData["Success"] = "Project updated.";
            return RedirectToAction(nameof(Index));
        }

        // POST /Projects/Delete/5
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var project = await _db.Projects
                .FirstOrDefaultAsync(p => p.Id == id && p.UserId == CurrentUserId);
            if (project == null) return NotFound();

            _db.Projects.Remove(project);
            await _db.SaveChangesAsync();

            TempData["Success"] = "Project deleted.";
            return RedirectToAction(nameof(Index));
        }
    }
}

using Microsoft.AspNetCore.Mvc;

namespace AxialFanMVC.Controllers
{
    // Temporary controller for previewing sidebar sections with dummy data.
    // Safe to delete once the real layout/sections are approved and built.
    public class SectionPreviewController : Controller
    {
        public IActionResult Dashboard() => View();
        public IActionResult DesignSeries() => View();
        public IActionResult MyDrawings() => View();
        public IActionResult Reports() => View();
        public IActionResult BomCosting() => View();
        public IActionResult Settings() => View();
        public IActionResult Help() => View();

        // 8-step UI-only wizard preview, dummy data, mirrors the real
        // DesignController.Wizard step pattern but does not touch it.
        public IActionResult DesignWizard(int step = 1)
        {
            var vm = new AxialFanMVC.ViewModels.DesignWizardViewModel
            {
                ProjectId = 0,
                ProjectName = "Preview Project",
                CurrentStep = Math.Clamp(step, 1, 8),
                BladeProfiles = new()
            };
            return View(vm);
        }

        public IActionResult FanDatabase() => View();
        public IActionResult WizardAirInput() => View();
        public IActionResult WizardFanConstraints() => View();
        public IActionResult WizardDesignResults() => View();
        public IActionResult WizardPerformance() => View();
        public IActionResult WizardMotor() => View();
        public IActionResult WizardAccessories() => View();
        public IActionResult WizardOutputSave() => View();
    }
}

using AxialFanMVC.Database;
using AxialFanMVC.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AxialFanMVC.Controllers
{
    public class AccountController : Controller
    {
        private readonly AxialFanDbContext _db;
        public AccountController(AxialFanDbContext db) => _db = db;

        // GET /Account/Register
        [HttpGet]
        public IActionResult Register() => View(new RegisterViewModel());

        // POST /Account/Register
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel vm)
        {
            if (!ModelState.IsValid) return View(vm);

            if (await _db.Users.AnyAsync(u => u.Email == vm.Email))
            {
                ModelState.AddModelError(nameof(vm.Email), "Email address is already registered.");
                return View(vm);
            }

            var user = new User
            {
                Name = vm.Name,
                Email = vm.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(vm.Password)
            };
            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            await SignInUser(user);
            return RedirectToAction("Index", "Projects");
        }

        // GET /Account/Login
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            ViewBag.ReturnUrl = returnUrl;
            return View(new LoginViewModel());
        }

        // POST /Account/Login
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel vm, string? returnUrl = null)
        {
            if (!ModelState.IsValid) return View(vm);

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == vm.Email);
            if (user == null || !BCrypt.Net.BCrypt.Verify(vm.Password, user.PasswordHash))
            {
                ModelState.AddModelError(string.Empty, "Invalid email or password.");
                return View(vm);
            }

            await SignInUser(user, vm.RememberMe);

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction("Index", "Projects");
        }

        // POST /Account/Logout
        [HttpPost, ValidateAntiForgeryToken, Authorize]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Index", "Home");
        }

        private async Task SignInUser(User user, bool persistent = false)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Name,           user.Name),
                new(ClaimTypes.Email,          user.Email),
                new(ClaimTypes.Role,           user.Role)
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);
            var props = new AuthenticationProperties { IsPersistent = persistent };

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, props);
        }
    }
}

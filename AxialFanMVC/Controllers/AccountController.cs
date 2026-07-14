using AxialFanMVC.Database;
using AxialFanMVC.Repositories.Inteface;
using AxialFanMVC.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using System.Security.Claims;

namespace AxialFanMVC.Controllers
{
    public class AccountController : Controller
    {
        private readonly AxialFanDbContext _db;
        private readonly IExceptionHandlerRepository _exceptionHandlerRepository;
        public AccountController(AxialFanDbContext db, IExceptionHandlerRepository exceptionHandlerRepository)
        {
            _db = db;
            _exceptionHandlerRepository = exceptionHandlerRepository;
        }

        // GET /Account/Register
        [HttpGet]
        public IActionResult Register()
        {
            try
            {
                return View(new RegisterViewModel());
            }
            catch (Exception ex)
            {
                _exceptionHandlerRepository.SaveException(
                    nameof(AccountController),
                    nameof(Register),
                    ex.ToString());

                return RedirectToAction("Index", "Home");
            }
        }

        // POST /Account/Register
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel vm)
        {
            try
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
            catch (Exception ex)
            {
                _exceptionHandlerRepository.SaveException(
                    nameof(AccountController),
                    nameof(Register),
                    ex.ToString());

                ModelState.AddModelError(string.Empty,
                    "An unexpected error occurred while registering your account.");

                return View(vm);
            }
        }

        // GET /Account/Login
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            try
            {
                ViewBag.ReturnUrl = returnUrl;
                return View(new LoginViewModel());
            }
            catch (Exception ex)
            {
                _exceptionHandlerRepository.SaveException(
                    nameof(AccountController),
                    nameof(Login),
                    ex.ToString());

                return RedirectToAction("Index", "Home");
            }
        }

        // POST /Account/Login
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel vm, string? returnUrl = null)
        {
            try
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
            catch (Exception ex)
            {
                _exceptionHandlerRepository.SaveException(
                    nameof(AccountController),
                    nameof(Login),
                    ex.ToString());

                ModelState.AddModelError(string.Empty,
                    "An unexpected error occurred while logging in.");

                return View(vm);
            }
        }

        // POST /Account/Logout
        [HttpPost, ValidateAntiForgeryToken, Authorize]
        public async Task<IActionResult> Logout()
        {
            try
            {
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return RedirectToAction("Index", "Home");
            }
            catch (Exception ex)
            {
                _exceptionHandlerRepository.SaveException(
                    nameof(AccountController),
                    nameof(Logout),
                    ex.ToString());

                return RedirectToAction("Index", "Home");
            }
        }

        // Private helper — left unchanged per convention
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
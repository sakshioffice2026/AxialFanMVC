using AxialFanMVC.Database;
using AxialFanMVC.Repositories;
using AxialFanMVC.Repositories.Inteface;
using AxialFanMVC.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using System;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

var connStr = builder.Configuration.GetConnectionString("DefaultConnection")
              ?? throw new InvalidOperationException("Connection string not found.");

builder.Services.AddDbContext<AxialFanDbContext>(options =>
    options.UseMySql(connStr, ServerVersion.AutoDetect(connStr)));

builder.Services.AddScoped<IExceptionHandlerRepository, ExceptionHandlerRepository>();

builder.Services.AddScoped<IDesignResultRepository, DesignResultRepository>();
builder.Services.AddScoped<IPhysicsValidationEngine, PhysicsValidationEngine>();
builder.Services.AddScoped<ICurveGeneration, CurveGeneration>();
builder.Services.AddScoped<ICalibrationCaseRepository, CalibrationCaseRepository>();

builder.Services.AddScoped<IHandbookChunkRepository, HandbookChunkRepository>();

// Ollama chat client — base URL configurable via appsettings ("Ollama:BaseUrl")
builder.Services.AddHttpClient<IOllamaChatRepository, OllamaChatRepository>(client =>
{
    var baseUrl = builder.Configuration["Ollama:BaseUrl"] ?? "http://localhost:11434";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(180);
});

// HandbookChunkRepository now calls Ollama directly (for embeddings), so it
// needs an HttpClient the same way OllamaChatRepository does — same base URL,
// same config key, just a different endpoint (/api/embed vs /api/chat).
builder.Services.AddHttpClient<IHandbookChunkRepository, HandbookChunkRepository>(client =>
{
    var baseUrl = builder.Configuration["Ollama:BaseUrl"] ?? "http://localhost:11434";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(180);
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/Login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });

builder.Services.AddAuthorization();
// THIS LINE IS REQUIRED — registers ExportService
builder.Services.AddScoped<ExportService>();

var app = builder.Build(); 

CurveCorrectionService.Initialize(Path.Combine(builder.Environment.ContentRootPath, "MLModels", "efficiency_correction.onnx"),
    app.Logger);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AxialFanDbContext>();
    await ValidationFlagsBackfill.RunAsync(db);
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AxialFanDbContext>();
    //db.Database.Migrate();
}

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");

app.Run();
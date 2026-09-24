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
AxialFanMVC.Services.CfdVtkRenderer.PythonExe = builder.Configuration["CfdRender:PythonExe"] ?? AxialFanMVC.Services.CfdVtkRenderer.PythonExe;
AxialFanMVC.Services.CfdVtkRenderer.ScriptPath = builder.Configuration["CfdRender:ScriptPath"] ?? AxialFanMVC.Services.CfdVtkRenderer.ScriptPath;
// Scheduled Task + IPC settings for the interactive-desktop render
// workaround - see CfdVtkRenderer.cs for why this can no longer just
// shell out to python.exe directly from the app pool.
AxialFanMVC.Services.CfdVtkRenderer.TaskName = builder.Configuration["CfdRender:TaskName"] ?? AxialFanMVC.Services.CfdVtkRenderer.TaskName;
AxialFanMVC.Services.CfdVtkRenderer.IpcDirectory = builder.Configuration["CfdRender:IpcDirectory"] ?? AxialFanMVC.Services.CfdVtkRenderer.IpcDirectory;
if (int.TryParse(builder.Configuration["CfdRender:TimeoutSeconds"], out var cfdRenderTimeoutSeconds))
    AxialFanMVC.Services.CfdVtkRenderer.TimeoutSeconds = cfdRenderTimeoutSeconds;
builder.Services.AddScoped<IHandbookChunkRepository, HandbookChunkRepository>();

// Ollama chat client - base URL configurable via appsettings ("Ollama:BaseUrl")
//builder.Services.AddHttpClient<IOllamaChatRepository, OllamaChatRepository>(client =>
//{
//    var baseUrl = builder.Configuration["Ollama:BaseUrl"] ?? "http://localhost:11434";
//    client.BaseAddress = new Uri(baseUrl);
//    client.Timeout = TimeSpan.FromSeconds(180);
//});



// HandbookChunkRepository now calls Ollama directly (for embeddings), so it
// needs an HttpClient the same way OllamaChatRepository does - same base URL,
// same config key, just a different endpoint (/api/embed vs /api/chat).
builder.Services.AddHttpClient<IHandbookChunkRepository, HandbookChunkRepository>(client =>
{
    var baseUrl = builder.Configuration["Ollama:BaseUrl"] ?? "http://localhost:11434";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(180);
});

// LLamaSharp in-process local LLM - single shared model provider (loads .gguf
// weights once), with per-native-context SemaphoreSlim locks (see
// LlamaModelProvider) to keep concurrent HTTP requests from corrupting the
// underlying llama.cpp native context.
builder.Services.AddSingleton<ILlamaModelProvider, LlamaModelProvider>();
builder.Services.AddScoped<ILlamaSharpChatService, LlamaSharpChatService>();
builder.Services.AddScoped<ILlamaSharpEmbeddingService, LlamaSharpEmbeddingService>();
builder.Services.AddSingleton<IQdrantHandbookVectorService, QdrantHandbookVectorService>();
builder.Services.AddScoped<IHandbookVectorSyncService, HandbookVectorSyncService>();

// Semantic Kernel orchestration layer - Kernel per-request (via factory),
// app-data function-calling plugin, semantic retrieval over Qdrant, and the
// top-level RAG chat orchestrator that ChatController/DesignAssistantController
// depend on. These were previously missing from DI, which would have failed
// at first request with "Unable to resolve service for type IRagChatOrchestrator".
builder.Services.AddScoped<IAppDataQueryService, AppDataQueryService>();
builder.Services.AddScoped<IKernelFactory, KernelFactory>();
builder.Services.AddScoped<IRetrievalService, RetrievalService>();
builder.Services.AddScoped<IRetrievalPlugin, RetrievalPlugin>();
builder.Services.AddScoped<IRagChatOrchestrator, RagChatOrchestrator>();

// Full-app agent: pending-action store is a singleton (staged actions must
// survive across requests until the user confirms), the agent service and the
// executor are per-request. The executor lives in the web project because it
// needs ICfdJobSignal / IOptimizationJobSignal.
builder.Services.AddSingleton<IAgentPendingActionStore, AgentPendingActionStore>();
builder.Services.AddScoped<IAppAgentService, AppAgentService>();
builder.Services.AddScoped<IAgentActionExecutor, AgentActionExecutor>();

// Loads both .gguf models and runs a tiny inference in the background at startup,
// so the first user question does not pay the model-load cost (CPU-only machine).
builder.Services.AddHostedService<ModelWarmupService>();

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
// THIS LINE IS REQUIRED - registers ExportService
builder.Services.AddScoped<ExportService>();

// "Optimize for me" - DB-backed job queue processed by a background
// worker that calls out to the Python optimizer service (FastAPI).
// OptimizationJobChannel is registered as its own singleton (not just
// via the interface) because OptimizationBackgroundService needs the
// concrete type's Reader, while OptimizationController only needs the
// IOptimizationJobSignal.NotifyJobQueued side.
builder.Services.AddSingleton<OptimizationJobChannel>();
builder.Services.AddSingleton<IOptimizationJobSignal>(sp => sp.GetRequiredService<OptimizationJobChannel>());
builder.Services.AddHttpClient(nameof(OptimizationBackgroundService));
builder.Services.AddHostedService<OptimizationBackgroundService>();

// CFD pressure slice - same DB-backed job queue shape as "Optimize for
// me" above, but the worker calls the local OpenFOAM pipeline
// (LocalCfdOrchestrator) directly rather than an HTTP service.
builder.Services.AddSingleton<CfdJobChannel>();
builder.Services.AddSingleton<ICfdJobSignal>(sp => sp.GetRequiredService<CfdJobChannel>());
builder.Services.AddHostedService<CfdBackgroundService>();

var app = builder.Build();

CurveCorrectionService.Initialize(Path.Combine(builder.Environment.ContentRootPath, "MLModels", "efficiency_correction.onnx"),
    app.Logger);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
// .vtp isn't in ASP.NET Core's default recognized file-extension list, so
// plain UseStaticFiles() 404s on it even when the file exists on disk -
// that's what was causing "Couldn't download - No file" on the CFD
// results page despite pressure_slice.vtp being right there in wwwroot.
var cfdContentTypes = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
cfdContentTypes.Mappings[".vtp"] = "application/octet-stream";
app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = cfdContentTypes });
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AxialFanDbContext>();
    await ValidationFlagsBackfill.RunAsync(db);
    await CostRateSeeder.RunAsync(db);
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
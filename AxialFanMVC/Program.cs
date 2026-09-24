using AxialFanMVC.Database;
using AxialFanMVC.Repositories;
using AxialFanMVC.Repositories.Inteface;
using AxialFanMVC.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

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
AxialFanMVC.Services.CfdVtkRenderer.TaskName = builder.Configuration["CfdRender:TaskName"] ?? AxialFanMVC.Services.CfdVtkRenderer.TaskName;
AxialFanMVC.Services.CfdVtkRenderer.IpcDirectory = builder.Configuration["CfdRender:IpcDirectory"] ?? AxialFanMVC.Services.CfdVtkRenderer.IpcDirectory;
if (int.TryParse(builder.Configuration["CfdRender:TimeoutSeconds"], out var cfdRenderTimeoutSeconds))
    AxialFanMVC.Services.CfdVtkRenderer.TimeoutSeconds = cfdRenderTimeoutSeconds;

// LLamaSharp in-process local LLM — singleton model provider loads both
// .gguf weights once; per-request services hold SemaphoreSlim-guarded
// contexts. No Ollama HttpClient is registered or needed.
builder.Services.AddSingleton<ILlamaModelProvider, LlamaModelProvider>();
builder.Services.AddScoped<ILlamaSharpChatService, LlamaSharpChatService>();
builder.Services.AddScoped<ILlamaSharpEmbeddingService, LlamaSharpEmbeddingService>();

// HandbookChunkRepository: FULLTEXT fallback only — no Ollama HttpClient.
// Semantic search goes through IRetrievalService ? Qdrant.
builder.Services.AddScoped<IHandbookChunkRepository, HandbookChunkRepository>();

// Qdrant vector store for handbook semantic search.
builder.Services.AddSingleton<IQdrantHandbookVectorService, QdrantHandbookVectorService>();
builder.Services.AddScoped<IHandbookVectorSyncService, HandbookVectorSyncService>();

// Semantic Kernel orchestration layer.
builder.Services.AddScoped<IAppDataQueryService, AppDataQueryService>();
builder.Services.AddScoped<IKernelFactory, KernelFactory>();
builder.Services.AddScoped<IRetrievalService, RetrievalService>();
builder.Services.AddScoped<IRetrievalPlugin, RetrievalPlugin>();
builder.Services.AddScoped<IRagChatOrchestrator, RagChatOrchestrator>();

// Full-app agent.
builder.Services.AddSingleton<IAgentPendingActionStore, AgentPendingActionStore>();
builder.Services.AddScoped<IAppAgentService, AppAgentService>();
builder.Services.AddScoped<IAgentActionExecutor, AgentActionExecutor>();

// Warms up both .gguf models at startup so the first user request is fast.
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
builder.Services.AddScoped<ExportService>();

builder.Services.AddSingleton<OptimizationJobChannel>();
builder.Services.AddSingleton<IOptimizationJobSignal>(sp => sp.GetRequiredService<OptimizationJobChannel>());
builder.Services.AddHttpClient(nameof(OptimizationBackgroundService));
builder.Services.AddHostedService<OptimizationBackgroundService>();

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
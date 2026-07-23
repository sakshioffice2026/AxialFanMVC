using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using AxialFanMVC.Database;
using AxialFanMVC.Models;
using Microsoft.EntityFrameworkCore;

namespace AxialFanMVC.Services
{
    // Wakes the background worker immediately when a job is enqueued —
    // pure signaling, holds no state that survives a restart.
    public interface IOptimizationJobSignal
    {
        void NotifyJobQueued(int jobId);
    }

    public class OptimizationJobChannel : IOptimizationJobSignal
    {
        private readonly Channel<int> _channel = Channel.CreateUnbounded<int>();
        public ChannelReader<int> Reader => _channel.Reader;
        public void NotifyJobQueued(int jobId) => _channel.Writer.TryWrite(jobId);
    }

    // ═══════════════════════════════════════════════════════════════
    // OptimizationBackgroundService — Part 3's C# side.
    //
    // Durability model: the DB row (OptimizationJob, Status) is the
    // source of truth, not the channel. The channel only exists so a
    // freshly queued job starts within milliseconds instead of waiting
    // for the next poll interval. On startup, and every 30s as a safety
    // net, this also re-scans the DB for any Status="Queued" row that
    // never got picked up (e.g. the app restarted between enqueue and
    // pickup) — so a job can never get silently lost.
    // ═══════════════════════════════════════════════════════════════
    public class OptimizationBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly OptimizationJobChannel _channel;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<OptimizationBackgroundService> _logger;
        private readonly string _optimizerBaseUrl;

        public OptimizationBackgroundService(
            IServiceScopeFactory scopeFactory,
            OptimizationJobChannel channel,
            IHttpClientFactory httpClientFactory,
            IConfiguration config,
            ILogger<OptimizationBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _channel = channel;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _optimizerBaseUrl = config["OptimizerService:BaseUrl"] ?? "http://localhost:8088";
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var sweepTimer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            _ = SweepLoopAsync(sweepTimer, stoppingToken);

            await foreach (var jobId in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                await ProcessJobAsync(jobId, stoppingToken);
            }
        }

        private async Task SweepLoopAsync(PeriodicTimer timer, CancellationToken stoppingToken)
        {
            // Catch-up sweep on startup, then every tick — picks up any
            // job left in "Queued" by a crash/restart between enqueue and
            // the channel signal being read.
            do
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AxialFanDbContext>();
                    var stuck = await db.optimization_jobs
                        .Where(j => j.Status == "Queued")
                        .Select(j => j.Id)
                        .ToListAsync(stoppingToken);

                    foreach (var id in stuck)
                        await ProcessJobAsync(id, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Optimization job sweep failed.");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }

        private async Task ProcessJobAsync(int jobId, CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AxialFanDbContext>();

            // Re-fetch and re-check status inside a fresh scope — the sweep
            // and the channel path can both target the same job if timing
            // is unlucky; this makes picking a job up idempotent instead of
            // running it twice.
            var job = await db.optimization_jobs.FirstOrDefaultAsync(j => j.Id == jobId, stoppingToken);
            if (job is null || job.Status != "Queued") return;

            job.Status = "Running";
            job.StartedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(stoppingToken);

            try
            {
                var request = JsonSerializer.Deserialize<OptimizeRequestDto>(job.RequestJson)
                              ?? throw new InvalidOperationException("Stored request JSON was empty/invalid.");

                var client = _httpClientFactory.CreateClient(nameof(OptimizationBackgroundService));

                // NOTE: StringContent has two 3-arg overloads — (string, Encoding, string)
                // and (string, Encoding, MediaTypeHeaderValue). Passing the media type as a
                // MediaTypeHeaderValue explicitly sidesteps any overload-resolution ambiguity
                // between the two, instead of relying on the compiler picking the string one.
                var payload = new StringContent(
                    JsonSerializer.Serialize(request),
                    Encoding.UTF8,
                    new MediaTypeHeaderValue("application/json"));

                var response = await client.PostAsync($"{_optimizerBaseUrl}/optimize", payload, stoppingToken);

                var body = await response.Content.ReadAsStringAsync(stoppingToken);

                if (!response.IsSuccessStatusCode)
                {
                    // 409 from the optimizer means "no feasible design" —
                    // a legitimate outcome, not a system failure. Surface
                    // it as the job's error message so the UI can show
                    // "loosen your constraints" instead of a generic error.
                    job.Status = "Failed";
                    job.ErrorMessage = $"Optimizer returned {(int)response.StatusCode}: {body}";
                }
                else
                {
                    var candidates = JsonSerializer.Deserialize<List<OptimizeCandidateDto>>(body);
                    job.ResultJson = JsonSerializer.Serialize(candidates);
                    job.Status = "Completed";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Optimization job {JobId} failed.", jobId);
                job.Status = "Failed";
                job.ErrorMessage = ex.Message;
            }
            finally
            {
                job.CompletedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(stoppingToken);
            }
        }
    }
}

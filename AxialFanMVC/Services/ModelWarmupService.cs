using AxialFanMVC.Repositories.Inteface;

namespace AxialFanMVC.Services
{
    public sealed class ModelWarmupService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ModelWarmupService> _logger;

        public ModelWarmupService(
            IServiceScopeFactory scopeFactory,
            ILogger<ModelWarmupService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Let the host finish starting before doing heavy work.
            await Task.Yield();

            try
            {
                var started = DateTime.UtcNow;

                using var scope = _scopeFactory.CreateScope();

                // Resolving these builds the singleton LlamaModelProvider, which loads both .gguf files.
                var chat = scope.ServiceProvider.GetRequiredService<ILlamaSharpChatService>();
                var embedding = scope.ServiceProvider.GetRequiredService<ILlamaSharpEmbeddingService>();

                await Task.Run(async () =>
                {
                    await embedding.EmbedAsync("warm up");
                    await chat.ChatAsync("You are a test.", "Hi", 4);
                }, stoppingToken);

                _logger.LogInformation(
                    "LLamaSharp models warmed up in {Seconds:F1}s.",
                    (DateTime.UtcNow - started).TotalSeconds);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LLamaSharp warm-up failed. Models will load on the first request instead.");
            }
        }
    }
}
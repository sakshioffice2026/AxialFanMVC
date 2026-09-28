using Microsoft.Extensions.DependencyInjection;

namespace AxialFanMVC.Services.AeroAi
{
    public static class AeroAiServiceExtensions
    {
        // Program.cs: builder.Services.AddAeroAiOptimizeFlow();
        public static IServiceCollection AddAeroAiOptimizeFlow(this IServiceCollection services)
        {
            services.AddScoped<AeroAiOptimizeFlow>();
            return services;
        }
    }
}
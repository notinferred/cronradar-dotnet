using System;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CronRadar.Extensions
{
    /// <summary>
    /// Extension methods for registering CronRadar services.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds CronRadar monitoring services to the service collection.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddCronRadar(this IServiceCollection services)
        {
            return services.AddCronRadar(options => { });
        }

        /// <summary>
        /// Adds CronRadar monitoring services to the service collection with configuration.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configure">Action to configure CronRadar options.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddCronRadar(this IServiceCollection services, Action<CronRadarOptions> configure)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (configure == null) throw new ArgumentNullException(nameof(configure));

            // Configure options
            services.Configure(configure);

            // Register HttpClient if not already registered
            services.AddHttpClient<ICronRadarClient, CronRadarClient>((serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<CronRadarOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl);
                client.Timeout = options.Timeout;

                // Set up Basic Auth if API key is available
                var apiKey = options.ApiKey ?? Environment.GetEnvironmentVariable("CRONRADAR_API_KEY");
                if (!string.IsNullOrEmpty(apiKey))
                {
                    var authValue = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{apiKey}:"));
                    client.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authValue);
                }
            });

            // Register the client as singleton
            services.TryAddSingleton<ICronRadarClient>(serviceProvider =>
            {
                var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
                var httpClient = httpClientFactory.CreateClient(nameof(ICronRadarClient));
                var options = serviceProvider.GetRequiredService<IOptions<CronRadarOptions>>().Value;
                var logger = serviceProvider.GetService<ILogger<CronRadarClient>>();

                options.Logger = logger;
                return new CronRadarClient(httpClient, options.ApiKey, options);
            });

            // Register hosted service for auto-sync if enabled
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, CronRadarHostedService>());

            return services;
        }

        /// <summary>
        /// Adds CronRadar monitoring services to the service collection with API key.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="apiKey">The API key for authentication.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddCronRadar(this IServiceCollection services, string apiKey)
        {
            return services.AddCronRadar(options => options.ApiKey = apiKey);
        }

        /// <summary>
        /// Adds CronRadar monitoring services with monitors to sync.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="apiKey">The API key for authentication.</param>
        /// <param name="monitors">The monitors to sync on startup.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddCronRadar(this IServiceCollection services, string apiKey, params Monitor[] monitors)
        {
            return services.AddCronRadar(options =>
            {
                options.ApiKey = apiKey;
                options.Monitors = monitors;
                options.AutoSync = true;
            });
        }
    }
}
using System.Data;
using Microsoft.Extensions.DependencyInjection;
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace SlySoft.ClacksNet;

public class ClacksOutboxConfig {
    public Func<IServiceProvider, IDbConnection>? GetConnection { get; set; }
    public Type? Sender { get; set; }
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromMinutes(1);
}

public static class Register {
    public static IServiceCollection AddClacksOutbox(this IServiceCollection services, Action<ClacksOutboxConfig> configClacksOut) {
        var config = new ClacksOutboxConfig();
        configClacksOut(config);
        if (config.GetConnection == null) {
            throw new ArgumentNullException(nameof(config.GetConnection), "Clacks.NET: GetConnection must be set.");
        }
        
        services
            .AddSingleton(config)
            .AddHostedService<ClacksOutInitializer>()
            .AddSingleton(new DbConnectionProvider(config.GetConnection))
            .AddTransient<IOutbox, Outbox>();
        
        if (config.Sender != null) {
            services
                .AddTransient(typeof(IOutboxMessageSender), config.Sender)
                .AddHostedService<OutboxProcessor>();
        }

        return services;
    }
}

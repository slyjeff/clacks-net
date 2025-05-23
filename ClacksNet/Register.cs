using System.Data;
using Microsoft.Extensions.DependencyInjection;

namespace SlySoft.ClacksNet;

public class ClacksOutConfig {
    public Func<IServiceProvider, IDbConnection>? GetConnection { get; set; }
    public Type? Sender { get; set; }
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromMinutes(1);
}

public static class Register {
    public static IServiceCollection AddClacksOut(this IServiceCollection services, Action<ClacksOutConfig> configClacksOut = null) {
        var config = new ClacksOutConfig();
        configClacksOut(config);
        if (config.GetConnection == null) {
            throw new ArgumentNullException(nameof(config.GetConnection), "ClacksOut: GetConnection must be set.");
        }
        
        services
            .AddSingleton(config)
            .AddHostedService<ClacksOutInitializer>()
            .AddSingleton(new DbConnectionProvider(config.GetConnection))
            .AddTransient<IClacksOut, ClacksOut>();
        
        if (config.Sender != null) {
            services
                .AddTransient(typeof(IClacksOutSender), config.Sender)
                .AddHostedService<ClacksOutProcessor>();
        }

        return services;
    }
}

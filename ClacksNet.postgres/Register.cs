using Microsoft.Extensions.DependencyInjection;

namespace SlySoft.ClacksNet.Postgres;

public static class Register {
    public static IServiceCollection EnablePostgresOutboxTrigger(this IServiceCollection services) {
        return services.AddTransient<IOutboxListener, PostgresOutboxListener>();
    }
}

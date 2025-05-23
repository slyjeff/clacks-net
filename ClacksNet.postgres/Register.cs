using ClacksNet.postgres;
using Microsoft.Extensions.DependencyInjection;

namespace SlySoft.ClacksNet.postgres;

public static class Register {
    public static IServiceCollection AddPostgresClacksOutListener(this IServiceCollection services) {
        return services.AddTransient<IClacksOutListener, PostgresClacksOutListener>();
    }
}

using System.Data;

namespace SlySoft.ClacksNet;

internal sealed class DbConnectionProvider(Func<IServiceProvider, IDbConnection> getConnection) {
    public IDbConnection GetConnection(IServiceProvider services) {
        return getConnection(services);
    }
}

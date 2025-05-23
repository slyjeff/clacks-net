using System.Data;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SlySoft.ClacksNet;

internal sealed class ClacksOutInitializer(IServiceProvider services, DbConnectionProvider connectionProvider, ILogger<ClacksOutInitializer> logger) : IHostedService {
    public async Task StartAsync(CancellationToken cancellationToken) {
        try {
            var connection = connectionProvider.GetConnection(services);
            await connection.OpenAsync(cancellationToken);

            if (connection.TableExists("clacks_out")) {
                return;
            }

            CreateOutbox(connection);
            logger.LogInformation("Created clacks_out table. Send the message on!");
        } catch (Exception e) {
            logger.LogError(e, "Failed to create clacks_out table");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken) {
        await Task.CompletedTask;
    }

   private static void CreateOutbox(IDbConnection connection) {
        using (var command = connection.CreateCommand()) {
            command.CommandText = GetCreateOutboxSql(connection);
            command.ExecuteNonQuery();
        }

        var typeName = connection.GetType().Name;
        if (!typeName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)) {
            return;
        }
        
        //for postgres, create a trigger/function to notify on insert
        using (var command = connection.CreateCommand()) {
            command.CommandText = CreatePostgresNotification;
            command.ExecuteNonQuery();
        }
        
        using (var command = connection.CreateCommand()) {
            command.CommandText = CreatePostgresInsertTrigger;
            command.ExecuteNonQuery();
        }
   }

    private const string CreateOutboxSqlPostgres =
        """
        CREATE TABLE clacks_out (
            id             UUID      PRIMARY KEY DEFAULT gen_random_uuid(),
            topic          TEXT      NOT NULL,
            message        TEXT      NOT NULL,
            created_at     TIMESTAMP DEFAULT NOW(),
            next_send_time TIMESTAMP NULL,
            send_count     INT       DEFAULT 0,
            sent_at        TIMESTAMP NULL
        )
        """;

    private const string CreatePostgresNotification =
        """
        CREATE FUNCTION notify_new_clacks_out_message()
        RETURNS TRIGGER AS $$
        BEGIN
            PERFORM pg_notify('clacks_out_channel', NEW.Id::text);
            RETURN NEW;
        END;
        $$ LANGUAGE plpgsql
        """;

    private const string CreatePostgresInsertTrigger =
        """
        CREATE TRIGGER clacks_out_message_insert_trigger
        AFTER INSERT ON clacks_out
        FOR EACH ROW
        EXECUTE FUNCTION notify_new_clacks_out_message();
        """;

    private const string CreateOutboxSqlSqlServer =
        """
        CREATE TABLE clacks_out (
            id             UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
            topic          NVARCHAR(MAX)    NOT NULL,
            message        NVARCHAR(MAX)    NOT NULL,
            created_at     DATETIME2        DEFAULT SYSUTCDATETIME(),
            next_send_time DATETIME2        NULL,
            send_count     INT              DEFAULT 0,
            sent_at        DATETIME2        NULL                                
        );
        """;

    private const string CreateOutboxSqlMySql =
        """
        CREATE TABLE clacks_out (
            id             CHAR(36)     PRIMARY KEY DEFAULT (UUID()),
            topic          TEXT         NOT NULL,
            message        TEXT         NOT NULL,
            created_at     DATETIME     DEFAULT CURRENT_TIMESTAMP,
            next_send_time DATETIME     NULL,
            send_count     INT          DEFAULT 0,
            sent_at        DATETIME     NULL                                
        );
        """;

    private const string CreateOutboxSqlSqlite =
        """
        CREATE TABLE clacks_out (
            id             TEXT         PRIMARY KEY DEFAULT (lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)),2) || '-' || substr('89ab',abs(random()) % 4 + 1,1) || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6)))),
            topic          TEXT         NOT NULL,
            message        TEXT         NOT NULL,
            created_at     DATETIME     DEFAULT CURRENT_TIMESTAMP,
            next_send_time DATETIME     NULL,
            send_count     INTEGER      DEFAULT 0,
            sent_at        DATETIME     NULL                                
        );
        """;

    private const string CreateOutboxSqlOracle =
        """
        CREATE TABLE clacks_out (
            id             RAW(16)      DEFAULT SYS_GUID() PRIMARY KEY,
            topic          CLOB         NOT NULL,
            message        CLOB         NOT NULL,
            created_at     TIMESTAMP    DEFAULT CURRENT_TIMESTAMP,
            next_send_time TIMESTAMP    NULL,
            send_count     NUMBER       DEFAULT 0,
            sent_at        TIMESTAMP    NULL
        )
        """;

    private const string CreateOutboxSqlDb2 =
        """
        CREATE TABLE clacks_out (
            id             CHAR(36)     PRIMARY KEY DEFAULT SYS_GUID(),
            topic          CLOB         NOT NULL,
            message        CLOB         NOT NULL,
            created_at     TIMESTAMP    DEFAULT CURRENT_TIMESTAMP,
            next_send_time TIMESTAMP    NULL,
            send_count     INTEGER      DEFAULT 0,
            sent_at        TIMESTAMP    NULL
        )
        """;

    private static string GetCreateOutboxSql(IDbConnection connection) {
        var typeName = connection.GetType().Name;
        if (typeName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)) {
            return CreateOutboxSqlPostgres;
        }
        if (typeName.Contains("MySql", StringComparison.OrdinalIgnoreCase) || typeName.Contains("MariaDb", StringComparison.OrdinalIgnoreCase)) {
            return CreateOutboxSqlMySql;
        }
        if (typeName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)) {
            return CreateOutboxSqlSqlite;
        }
        if (typeName.Contains("Oracle", StringComparison.OrdinalIgnoreCase)) {
            return CreateOutboxSqlOracle;
        }
        if (typeName.Contains("DB2", StringComparison.OrdinalIgnoreCase)) {
            return CreateOutboxSqlDb2;
        }
        if (typeName.Contains("SqlConnection", StringComparison.OrdinalIgnoreCase)) {
            return CreateOutboxSqlSqlServer;
        }  
        throw new NotSupportedException("Unsupported database type");
    }
}
        
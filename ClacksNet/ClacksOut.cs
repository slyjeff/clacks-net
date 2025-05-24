using System.Data;
using System.Text.Json;

namespace SlySoft.ClacksNet;

public interface IOutbox {
    Task InsertAsync(string topic, object message, CancellationToken cancellationToken = default);
    void Insert(string topic, object message);
}

internal sealed class Outbox(IServiceProvider services, DbConnectionProvider connectionProvider) : IOutbox {
    public async Task InsertAsync(string topic, object message, CancellationToken cancellationToken = default) {
        using var connection = connectionProvider.GetConnection(services);
        await connection.OpenAsync(cancellationToken);

        using var command = CreateSendCommand(connection, topic, message);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public void Insert(string topic, object message) {
        using var connection = connectionProvider.GetConnection(services);
        connection.Open();
        
        using var command = CreateSendCommand(connection, topic, message);
        command.ExecuteNonQuery();
    }
    
    private const string InsertSql = 
        """
         INSERT INTO clacks_outbox (topic, message)
         VALUES (@topic, @message)
        """;

    private static IDbCommand CreateSendCommand(IDbConnection connection, string topic, object message) {
        var command = connection.CreateCommand();
        command.CommandText = InsertSql;
        var topicParam = command.CreateParameter();
        topicParam.ParameterName = "@topic";
        topicParam.Value = topic;
        command.Parameters.Add(topicParam);

        var messageParam = command.CreateParameter();
        messageParam.ParameterName = "@message";
        messageParam.Value = JsonSerializer.Serialize(message);
        command.Parameters.Add(messageParam);

        return command;
    }
}

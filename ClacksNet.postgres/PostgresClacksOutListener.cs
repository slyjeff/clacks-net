using System.Data;
using Microsoft.Extensions.Logging;
using Npgsql;
using SlySoft.ClacksNet;

namespace SlySoft.ClacksNet.Postgres;

internal sealed class PostgresClacksOutListener(ILogger<PostgresClacksOutListener> logger) : IClacksOutListener {
    private CancellationTokenSource? _cancellationTokenSource;
    private NpgsqlConnection? _connection = null;
    private string _connectionString = null!;
    private Task? _listenerTask;
    
    public async Task Register(IDbConnection connection, Func<CancellationToken, Task> processClacksOutMessages, CancellationToken cancellationToken) {
        if (connection is not NpgsqlConnection npgsqlConnection) {
            logger.LogInformation("Connection is not to PostgreSQL. Cannot register listener.");
            return;
        }
        
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        //use a separate connection for notifications, since we'll hold this open
        _connectionString = npgsqlConnection.ConnectionString;

        await OpenConnection();
        logger.LogInformation("Listening for notifications on 'clacks_out_channel'...");
        
        // Start the background task that waits for notifications
        _listenerTask = Task.Run(async () => {
            while (!_cancellationTokenSource.Token.IsCancellationRequested && _connection != null) {
                try {
                    await _connection.WaitAsync(_cancellationTokenSource.Token);
                    
                    if (_cancellationTokenSource.Token.IsCancellationRequested || _connection == null) {
                        break;
                    }
                    
                    await processClacksOutMessages(_cancellationTokenSource.Token);
                } catch (NpgsqlException ex) when (ex.SqlState is "08006" or "08003") {
                    logger.LogInformation("Connection lost: {ExMessage}. Attempting to reconnect...", ex.Message);
                    await ReconnectAsync();
                } catch (OperationCanceledException)  {
                    logger.LogInformation("Listener task cancelled.");
                    break;
                } catch (Exception ex)  {
                    logger.LogInformation("Error in listener loop: {ExMessage}", ex.Message);
                    await Task.Delay(TimeSpan.FromSeconds(5), _cancellationTokenSource.Token);
                }
            }
        }, _cancellationTokenSource.Token);
    }
    
    public async ValueTask DisposeAsync() {
        await CloseConnection();

        if (_listenerTask != null) {
            await _listenerTask;
            _listenerTask = null;
        }
    }
    
    private void OnNotification(object sender, NpgsqlNotificationEventArgs e) {
        logger.LogInformation("Received notification on channel '{EChannel}'. Payload: '{EPayload}'.", e.Channel, e.Payload);
        // The WaitAsync call in the listener task will wake up and trigger ProcessOutboxMessagesAsync.
        // We don't need to explicitly call ProcessOutboxMessagesAsync here to avoid potential race conditions
        // if the notification arrives *before* the transaction is fully committed and visible to the
        // querying connection. WaitAsync ensures the listener loop resumes and then queries.
    }
    
    private async Task ReconnectAsync() {
        await CloseConnection();
        
        if (_cancellationTokenSource == null) {
            return;
        }

        var retryAttempts = 0;
        var delay = TimeSpan.FromSeconds(1);
        while (!_cancellationTokenSource.Token.IsCancellationRequested) { 
            try {
                await OpenConnection();
                
                logger.LogInformation("Reconnected and re-listening for notifications on 'outbox_channel'.");
                return;
            } catch (Exception ex) {
                logger.LogInformation("Reconnect attempt {I} failed: {ExMessage}. Retrying in {DelayTotalSeconds}s...", ++retryAttempts, ex.Message, delay.TotalSeconds);
                await Task.Delay(delay, _cancellationTokenSource.Token);
                delay = TimeSpan.FromSeconds(Math.Min(60, delay.TotalSeconds * 2)); //max delay of 1 minute
            }
        }
    }
    
    private async Task OpenConnection() {
        if (_cancellationTokenSource == null) {
            return;
        }    
        
        _connection = new NpgsqlConnection(_connectionString);

        _connection.Notification += OnNotification;
        await _connection.OpenAsync(_cancellationTokenSource.Token);

        await using (var cmd = new NpgsqlCommand("LISTEN clacks_out_channel", _connection)) {
            await cmd.ExecuteNonQueryAsync(_cancellationTokenSource.Token);
        }
    }

    private async Task CloseConnection() {
        if (_cancellationTokenSource is { IsCancellationRequested: false }) {
            await _cancellationTokenSource.CancelAsync();    
        }

        if (_connection == null) {
            return;
        }
        
        _connection.Notification -= OnNotification;
        await _connection.CloseAsync();
        _connection.Dispose();
        _connection = null;
    }
}

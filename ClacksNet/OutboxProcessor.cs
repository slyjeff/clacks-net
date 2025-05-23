﻿using System.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SlySoft.ClacksNet;

public interface IOutboxListener : IAsyncDisposable {
    Task Register(IDbConnection connection, Func<CancellationToken, Task> processClacksOutMessages, CancellationToken cancellationToken);
}

public sealed record OutboxMessage(Guid Id, string Topic, string Message) { }

public interface IOutboxMessageSender {
    Task<bool> SendMessage(OutboxMessage message, CancellationToken cancellationToken = default);    
}

internal sealed class OutboxProcessor(IServiceProvider services, DbConnectionProvider connectionProvider, ILogger<OutboxProcessor> logger, ClacksOutboxConfig config) : IHostedService, IAsyncDisposable {
    private CancellationTokenSource? _cancellationTokenSource;
    private CancellationToken _cancellationToken = CancellationToken.None;
    private IOutboxMessageSender _sender = null!;
    private List<IOutboxListener> _listeners = [];
    private System.Timers.Timer? _pollingTimer;
    private readonly TimeSpan _pollingInterval = config.PollingInterval;

    public async Task StartAsync(CancellationToken cancellationToken) {
        var sender = services.GetService<IOutboxMessageSender>();
        if (sender == null) {
            logger.LogInformation("IOutboxListener not registered. OutboxProcessor will not start.");
            return;
        }
        _sender = sender;
        
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cancellationToken = _cancellationTokenSource.Token;

        try {
            // grab any messages that need to be processed on startup
            await ProcessOutboxMessages(cancellationToken);
            logger.LogInformation("Completed initial outbox scan on startup.");

            await RegisterListeners();

            StartPolling();
        } catch (Exception ex) {
            logger.LogError(ex, "Failed to start Outbox Processor Service");
            await DisposeAsync();
        }
    }

    private async Task RegisterListeners() {
        _listeners = services.GetServices<IOutboxListener>().ToList();
        if (_listeners.Count > 0) {
            using var connection = connectionProvider.GetConnection(services);
            foreach (var listener in _listeners) {
                await listener.Register(connection, ProcessOutboxMessages, _cancellationToken);
            }
        }        
    }
    
    private void StartPolling() {
        _pollingTimer = new System.Timers.Timer(_pollingInterval.TotalMilliseconds);
        _pollingTimer.Elapsed += async (_, _) => await OnPollingTimerElapsedAsync(_cancellationToken);
        _pollingTimer.AutoReset = true;
        _pollingTimer.Start();
        logger.LogInformation("Started periodic polling every {PollingIntervalTotalMinutes} minutes.", _pollingInterval.TotalMinutes);
    }

    private async Task OnPollingTimerElapsedAsync(CancellationToken cancellationToken) {
        if (cancellationToken.IsCancellationRequested) {
            return;
        }

        logger.LogInformation("Polling for outbox messages.");
        await ProcessOutboxMessages(cancellationToken);
    }

    private async Task ProcessOutboxMessages(CancellationToken cancellationToken) {
        logger.LogInformation("Scanning for outbox messages...");
        using var connection = connectionProvider.GetConnection(services);
        await connection.OpenAsync(cancellationToken);

        while (true) {
            var message = await GetNextMessage(connection, cancellationToken);
            if (message == null) {
                logger.LogInformation("Finished processing outbox messages.");
                return;
            }
            
            try {
                logger.LogInformation("Processing message {MessageId} (Topic: {MessageTopic}, Message: {MessageMessage})", message.Id, message.Topic, message.Message);
                var sent = await _sender.SendMessage(message, cancellationToken);

                if (!sent) {
                    continue;
                }

                await UpdateMessageSent(connection, message, cancellationToken);
                logger.LogInformation("Message {Id} processed and marked as sent.", message.Id);
            } catch (Exception e) {
                logger.LogError(e, "Error processing message {MessageId}", message.Id);
            }
        }
    }

    private async Task<OutboxMessage?> GetNextMessage(IDbConnection connection, CancellationToken cancellationToken) {
        try {
            using var transaction = connection.BeginTransaction();

            var message = await SelectNextMessage(connection, transaction, cancellationToken);
            if (message == null)  {
                return null;
            }

            await UpdateMessageSendCount(connection, transaction, message.Id, cancellationToken);

            transaction.Commit();

            return message;
        } catch (Exception e) {
            logger.LogError(e, "Error getting next message from outbox");
            return null;
        }
    }

    private const string SelectNextMessageSql = 
        """
        SELECT id, topic, message, created_at, next_send_time, send_count
          FROM clacks_outbox 
         WHERE (next_send_time <= @CurrentTime OR next_send_time IS NULL)
           AND sent_at is null
         ORDER BY next_send_time
         LIMIT 1
        """;

    private static async Task<OutboxMessage?> SelectNextMessage(IDbConnection connection, IDbTransaction transaction, CancellationToken cancellationToken = default) {
        using var command = connection.CreateCommand();

        var currentTimeParam = command.CreateParameter();
        currentTimeParam.ParameterName = "@CurrentTime";
        currentTimeParam.Value = DateTime.UtcNow;
        command.Parameters.Add(currentTimeParam);

        command.CommandText = SelectNextMessageSql;

        command.Transaction = transaction;

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) {
            return null;
        }

        return new OutboxMessage(reader.GetGuid(0), reader.GetString(1), reader.GetString(2));
    } 
    
    private const string UpdateMessageSendCountSql = 
        """
        UPDATE clacks_outbox 
         SET send_count = send_count + 1, 
             next_send_time = @NextSendTime
        WHERE ID=@ID
        """;    

    private static async Task UpdateMessageSendCount(IDbConnection connection, IDbTransaction transaction, Guid id, CancellationToken cancellationToken = default) {
        using var command = connection.CreateCommand();
        command.CommandText = UpdateMessageSendCountSql;
        command.Transaction = transaction;

        var idParam = command.CreateParameter();
        idParam.ParameterName = "@ID";
        idParam.Value = id;
        command.Parameters.Add(idParam);

        var nextSendTimeParam = command.CreateParameter();
        nextSendTimeParam.ParameterName = "@NextSendTime";
        nextSendTimeParam.Value = DateTime.UtcNow.AddMinutes(1);
        command.Parameters.Add(nextSendTimeParam);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string UpdateMessageSentSql = 
        """
        UPDATE clacks_outbox 
          SET sent_at = @SentAt 
        WHERE ID=@ID
        """;    

    private static async Task UpdateMessageSent(IDbConnection connection, OutboxMessage message, CancellationToken cancellationToken = default) {
        using var command = connection.CreateCommand();
        command.CommandText = UpdateMessageSentSql;

        var idParam = command.CreateParameter();
        idParam.ParameterName = "@ID";
        idParam.Value = message.Id;
        command.Parameters.Add(idParam);

        var sentAtParam = command.CreateParameter();
        sentAtParam.ParameterName = "@SentAt";
        sentAtParam.Value = DateTime.UtcNow;
        command.Parameters.Add(sentAtParam);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }    
    
    public async Task StopAsync(CancellationToken cancellationToken)  {
        logger.LogInformation("Outbox Processor stopping...");
        _pollingTimer?.Stop();

        if (_cancellationTokenSource != null) {
            await _cancellationTokenSource.CancelAsync();
        }
        
        await DisposeAsync();
        logger.LogInformation("Outbox Processor stopped.");
    }

    public async ValueTask DisposeAsync() {
        foreach (var listener in _listeners) {
            await listener.DisposeAsync();
        }        

        _pollingTimer?.Dispose();
        _cancellationTokenSource?.Dispose();
    }
}
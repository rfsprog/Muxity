using RabbitMQ.Client;
using System.Text;
using System.Text.Json;

namespace Muxity.Api.Services.Messaging;

public class RabbitMqSettings
{
    public string Host           { get; set; } = "localhost";
    public int    Port           { get; set; } = 5672;
    public string Username       { get; set; } = "guest";
    public string Password       { get; set; } = "guest";
    public string TranscodeQueue { get; set; } = "transcode_jobs";
}

/// <summary>
/// Publishes messages to RabbitMQ as durable JSON.
/// The connection and channel are created lazily and reused for the lifetime of the service.
/// </summary>
public class RabbitMqPublisher : IAsyncDisposable
{
    private readonly RabbitMqSettings _settings;
    private readonly ILogger<RabbitMqPublisher> _logger;
    private IConnection? _connection;
    private IChannel? _channel;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public RabbitMqPublisher(IConfiguration config, ILogger<RabbitMqPublisher> logger)
    {
        _settings = config.GetSection("RabbitMQ").Get<RabbitMqSettings>() ?? new RabbitMqSettings();
        _logger   = logger;
    }

    public async Task PublishAsync<T>(string queue, T message, CancellationToken ct = default)
    {
        var channel = await GetChannelAsync(ct);

        await channel.QueueDeclareAsync(
            queue:      queue,
            durable:    true,
            exclusive:  false,
            autoDelete: false,
            arguments:  new Dictionary<string, object?>
            {
                // Dead-letter exchange for failed/rejected messages
                ["x-dead-letter-exchange"]    = "",
                ["x-dead-letter-routing-key"] = $"{queue}.dlq",
            },
            cancellationToken: ct);

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));

        var props = new BasicProperties { Persistent = true };

        await channel.BasicPublishAsync(
            exchange:   string.Empty,
            routingKey: queue,
            mandatory:  false,
            basicProperties: props,
            body:       body,
            cancellationToken: ct);

        _logger.LogDebug("Published message to queue {Queue}", queue);
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true })
            return _channel;

        await _lock.WaitAsync(ct);
        try
        {
            if (_channel is { IsOpen: true })
                return _channel;

            var factory = new ConnectionFactory
            {
                HostName = _settings.Host,
                Port     = _settings.Port,
                UserName = _settings.Username,
                Password = _settings.Password,
            };

            _connection = await factory.CreateConnectionAsync(ct);
            _channel    = await _connection.CreateChannelAsync(cancellationToken: ct);

            // Publisher confirms — ensure messages reach the broker
            await _channel.ConfirmSelectAsync(cancellationToken: ct);

            _logger.LogInformation("Connected to RabbitMQ at {Host}:{Port}", _settings.Host, _settings.Port);
            return _channel;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null) await _channel.CloseAsync();
        if (_connection is not null) await _connection.CloseAsync();
        _lock.Dispose();
    }
}

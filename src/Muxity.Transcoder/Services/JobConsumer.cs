using Microsoft.Extensions.Options;
using Muxity.Shared.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace Muxity.Transcoder.Services;

public class RabbitMqConsumerSettings
{
    public string Host           { get; set; } = "localhost";
    public int    Port           { get; set; } = 5672;
    public string Username       { get; set; } = "guest";
    public string Password       { get; set; } = "guest";
    public string TranscodeQueue { get; set; } = "transcode_jobs";
}

/// <summary>
/// Hosted service that connects to RabbitMQ, declares the queue, and
/// exposes an observable stream of <see cref="TranscodeJobMessage"/> to the
/// <see cref="TranscoderWorker"/>.
/// Messages are only ack'd after the worker confirms successful processing.
/// </summary>
public class JobConsumer : IHostedService, IAsyncDisposable
{
    private readonly RabbitMqConsumerSettings _settings;
    private readonly ILogger<JobConsumer> _logger;

    private IConnection? _connection;
    private IChannel?    _channel;

    // The worker reads from this channel; capacity=1 provides backpressure
    private readonly System.Threading.Channels.Channel<(TranscodeJobMessage Msg, ulong DeliveryTag)> _inbox =
        System.Threading.Channels.Channel.CreateBounded<(TranscodeJobMessage, ulong)>(1);

    public System.Threading.Channels.ChannelReader<(TranscodeJobMessage Msg, ulong DeliveryTag)> Reader
        => _inbox.Reader;

    public JobConsumer(IConfiguration config, ILogger<JobConsumer> logger)
    {
        _settings = config.GetSection("RabbitMQ").Get<RabbitMqConsumerSettings>()
                    ?? new RabbitMqConsumerSettings();
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        var factory = new ConnectionFactory
        {
            HostName = _settings.Host,
            Port     = _settings.Port,
            UserName = _settings.Username,
            Password = _settings.Password,
        };

        _connection = await factory.CreateConnectionAsync(ct);
        _channel    = await _connection.CreateChannelAsync(cancellationToken: ct);

        await _channel.QueueDeclareAsync(
            queue:      _settings.TranscodeQueue,
            durable:    true,
            exclusive:  false,
            autoDelete: false,
            arguments:  new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"]    = "",
                ["x-dead-letter-routing-key"] = $"{_settings.TranscodeQueue}.dlq",
            },
            cancellationToken: ct);

        // Prefetch 1 — only pull the next message once the current one is ack'd
        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: ct);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += OnMessageReceivedAsync;

        await _channel.BasicConsumeAsync(
            queue:       _settings.TranscodeQueue,
            autoAck:     false,
            consumer:    consumer,
            cancellationToken: ct);

        _logger.LogInformation("Connected to RabbitMQ, consuming queue '{Queue}'", _settings.TranscodeQueue);
    }

    public Task StopAsync(CancellationToken ct)
    {
        _inbox.Writer.TryComplete();
        return Task.CompletedTask;
    }

    /// <summary>Ack a successfully processed message.</summary>
    public async Task AckAsync(ulong deliveryTag, CancellationToken ct = default)
    {
        if (_channel is not null)
            await _channel.BasicAckAsync(deliveryTag, multiple: false, cancellationToken: ct);
    }

    /// <summary>Nack a failed message — sends it to the DLQ (no requeue).</summary>
    public async Task NackAsync(ulong deliveryTag, CancellationToken ct = default)
    {
        if (_channel is not null)
            await _channel.BasicNackAsync(deliveryTag, multiple: false, requeue: false, cancellationToken: ct);
    }

    private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        TranscodeJobMessage? message;
        try
        {
            var body = Encoding.UTF8.GetString(ea.Body.Span);
            message  = JsonSerializer.Deserialize<TranscodeJobMessage>(body);
            if (message is null) throw new InvalidOperationException("Null deserialization result.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize RabbitMQ message — sending to DLQ");
            if (_channel is not null)
                await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
            return;
        }

        // Block until the worker is ready to accept the next job
        await _inbox.Writer.WriteAsync((message, ea.DeliveryTag));
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel    is not null) await _channel.CloseAsync();
        if (_connection is not null) await _connection.CloseAsync();
    }
}

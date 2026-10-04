using Confluent.Kafka;

namespace CinemaAbyss.EventsService.Kafka;

/// <summary>
/// MVP consumer: subscribes to all three event topics and logs every message it reads back,
/// proving the round-trip (producer -> Kafka -> consumer) works end-to-end within this service.
/// </summary>
public class EventConsumer : BackgroundService
{
    private readonly ILogger<EventConsumer> _logger;
    private readonly IConsumer<string, string> _consumer;

    public EventConsumer(IConfiguration configuration, ILogger<EventConsumer> logger)
    {
        _logger = logger;

        var config = new ConsumerConfig
        {
            BootstrapServers = configuration["KAFKA_BROKERS"] ?? "localhost:9092",
            GroupId = "events-service-consumer",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true
        };

        _consumer = new ConsumerBuilder<string, string>(config).Build();
        _consumer.Subscribe(KafkaTopics.All);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() =>
        {
            _logger.LogInformation("Consumer subscribed to topics: {Topics}", string.Join(", ", KafkaTopics.All));

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var result = _consumer.Consume(stoppingToken);
                    if (result is { IsPartitionEOF: false })
                    {
                        _logger.LogInformation(
                            "Consumed event from topic {Topic} [partition {Partition}, offset {Offset}]: {Value}",
                            result.Topic, result.Partition.Value, result.Offset.Value, result.Message.Value);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "Error consuming message from Kafka");
                }
            }
        }, stoppingToken);

    public override void Dispose()
    {
        _consumer.Close();
        _consumer.Dispose();
        base.Dispose();
    }
}

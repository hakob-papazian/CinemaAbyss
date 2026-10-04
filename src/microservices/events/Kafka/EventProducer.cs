using System.Text.Json;
using CinemaAbyss.EventsService.Models;
using Confluent.Kafka;

namespace CinemaAbyss.EventsService.Kafka;

/// <summary>
/// Thin wrapper around the Confluent Kafka producer. One producer instance is shared
/// (thread-safe) for the whole app, as recommended by the Confluent client docs.
/// </summary>
public class EventProducer : IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<EventProducer> _logger;

    public EventProducer(IConfiguration configuration, ILogger<EventProducer> logger)
    {
        _logger = logger;

        var config = new ProducerConfig
        {
            BootstrapServers = configuration["KAFKA_BROKERS"] ?? "localhost:9092"
        };

        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    public async Task<DeliveryResult<string, string>> PublishAsync(string topic, Event domainEvent)
    {
        var json = JsonSerializer.Serialize(domainEvent);
        var message = new Message<string, string> { Key = domainEvent.Id, Value = json };

        var result = await _producer.ProduceAsync(topic, message);

        _logger.LogInformation(
            "Produced event {EventId} ({Type}) to topic {Topic} [partition {Partition}, offset {Offset}]",
            domainEvent.Id, domainEvent.Type, topic, result.Partition.Value, result.Offset.Value);

        return result;
    }

    public void Dispose() => _producer.Dispose();
}

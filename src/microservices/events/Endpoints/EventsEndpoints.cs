using CinemaAbyss.EventsService.Kafka;
using CinemaAbyss.EventsService.Models;

namespace CinemaAbyss.EventsService.Endpoints;

public static class EventsEndpoints
{
    public static void MapEventsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/events/health", () => Results.Ok(new { status = true }));

        app.MapPost("/api/events/movie", async (MovieEvent input, EventProducer producer) =>
        {
            var domainEvent = new Event
            {
                Id = $"movie-{input.MovieId}-{input.Action}-{Guid.NewGuid():N}",
                Type = "movie",
                Timestamp = DateTime.UtcNow,
                Payload = input
            };

            var result = await producer.PublishAsync(KafkaTopics.Movie, domainEvent);
            return Results.Created($"/api/events/movie/{domainEvent.Id}", ToResponse(domainEvent, result));
        });

        app.MapPost("/api/events/user", async (UserEvent input, EventProducer producer) =>
        {
            var domainEvent = new Event
            {
                Id = $"user-{input.UserId}-{input.Action}-{Guid.NewGuid():N}",
                Type = "user",
                Timestamp = input.Timestamp == default ? DateTime.UtcNow : input.Timestamp,
                Payload = input
            };

            var result = await producer.PublishAsync(KafkaTopics.User, domainEvent);
            return Results.Created($"/api/events/user/{domainEvent.Id}", ToResponse(domainEvent, result));
        });

        app.MapPost("/api/events/payment", async (PaymentEvent input, EventProducer producer) =>
        {
            var domainEvent = new Event
            {
                Id = $"payment-{input.PaymentId}-{input.Status}-{Guid.NewGuid():N}",
                Type = "payment",
                Timestamp = input.Timestamp == default ? DateTime.UtcNow : input.Timestamp,
                Payload = input
            };

            var result = await producer.PublishAsync(KafkaTopics.Payment, domainEvent);
            return Results.Created($"/api/events/payment/{domainEvent.Id}", ToResponse(domainEvent, result));
        });
    }

    private static EventResponse ToResponse(Event domainEvent, Confluent.Kafka.DeliveryResult<string, string> result) => new()
    {
        Status = "success",
        Partition = result.Partition.Value,
        Offset = result.Offset.Value,
        Event = domainEvent
    };
}

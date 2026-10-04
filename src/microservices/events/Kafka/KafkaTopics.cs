namespace CinemaAbyss.EventsService.Kafka;

public static class KafkaTopics
{
    public const string Movie = "movie-events";
    public const string User = "user-events";
    public const string Payment = "payment-events";

    public static readonly string[] All = { Movie, User, Payment };
}

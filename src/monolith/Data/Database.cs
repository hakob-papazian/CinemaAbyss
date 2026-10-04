using Npgsql;

namespace CinemaAbyss.Monolith.Data;

public static class Database
{
    public static void AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = ConnectionStringResolver.Resolve(configuration["DB_CONNECTION_STRING"]);
        services.AddNpgsqlDataSource(connectionString);
    }

    /// <summary>
    /// Waits for the database to accept connections, the way the service cannot start without it.
    /// </summary>
    public static async Task WaitForDatabaseAsync(this IHost host, int attempts = 30, int delaySeconds = 2)
    {
        var dataSource = host.Services.GetRequiredService<NpgsqlDataSource>();
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Database");

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var connection = await dataSource.OpenConnectionAsync();
                logger.LogInformation("Successfully connected to database");
                return;
            }
            catch (Exception ex) when (attempt < attempts)
            {
                logger.LogWarning("Database is not ready yet ({Attempt}/{Attempts}): {Message}", attempt, attempts, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
            }
        }
    }

    public static DateTime AsUtc(this DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}

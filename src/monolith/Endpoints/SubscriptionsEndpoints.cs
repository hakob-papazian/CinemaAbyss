using CinemaAbyss.Monolith.Data;
using CinemaAbyss.Monolith.Models;
using Npgsql;

namespace CinemaAbyss.Monolith.Endpoints;

public static class SubscriptionsEndpoints
{
    private const string SelectColumns = "SELECT id, user_id, plan_type, start_date, end_date FROM subscriptions";

    public static void MapSubscriptionsEndpoints(this IEndpointRouteBuilder app)
    {
        var subscriptions = app.MapGroup("/api/subscriptions");

        subscriptions.MapGet("", async (int? id, int? user_id, NpgsqlDataSource dataSource) =>
        {
            if (id.HasValue)
            {
                return await GetByIdAsync(id.Value, dataSource);
            }

            return user_id.HasValue
                ? Results.Ok(await QueryAsync(dataSource, $"{SelectColumns} WHERE user_id = $1", user_id.Value))
                : Results.Ok(await QueryAsync(dataSource, SelectColumns));
        });

        subscriptions.MapPost("", async (Subscription subscription, NpgsqlDataSource dataSource) =>
        {
            await using var command = dataSource.CreateCommand(
                "INSERT INTO subscriptions (user_id, plan_type, start_date, end_date) VALUES ($1, $2, $3, $4) RETURNING id");
            command.Parameters.AddWithValue(subscription.UserId);
            command.Parameters.AddWithValue(subscription.PlanType);
            command.Parameters.AddWithValue(subscription.StartDate.AsUtc());
            command.Parameters.AddWithValue(subscription.EndDate.AsUtc());

            subscription.Id = (int)(await command.ExecuteScalarAsync())!;
            return Results.Created($"/api/subscriptions?id={subscription.Id}", subscription);
        });
    }

    private static async Task<IResult> GetByIdAsync(int id, NpgsqlDataSource dataSource)
    {
        var subscriptions = await QueryAsync(dataSource, $"{SelectColumns} WHERE id = $1", id);
        return subscriptions.Count > 0 ? Results.Ok(subscriptions[0]) : Results.NotFound();
    }

    private static async Task<List<Subscription>> QueryAsync(NpgsqlDataSource dataSource, string sql, params object[] parameters)
    {
        await using var command = dataSource.CreateCommand(sql);
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter);
        }

        await using var reader = await command.ExecuteReaderAsync();

        var subscriptions = new List<Subscription>();
        while (await reader.ReadAsync())
        {
            subscriptions.Add(new Subscription
            {
                Id = reader.GetInt32(0),
                UserId = reader.GetInt32(1),
                PlanType = reader.GetString(2),
                StartDate = reader.GetDateTime(3),
                EndDate = reader.GetDateTime(4)
            });
        }

        return subscriptions;
    }
}

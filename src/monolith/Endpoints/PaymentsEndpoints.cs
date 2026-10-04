using CinemaAbyss.Monolith.Models;
using Npgsql;

namespace CinemaAbyss.Monolith.Endpoints;

public static class PaymentsEndpoints
{
    public static void MapPaymentsEndpoints(this IEndpointRouteBuilder app)
    {
        var payments = app.MapGroup("/api/payments");

        payments.MapGet("", async (int? id, int? user_id, NpgsqlDataSource dataSource) =>
        {
            if (id.HasValue)
            {
                return await GetByIdAsync(id.Value, dataSource);
            }

            return user_id.HasValue
                ? Results.Ok(await GetByUserIdAsync(user_id.Value, dataSource))
                : Results.Ok(await GetAllAsync(dataSource));
        });

        payments.MapPost("", async (Payment payment, NpgsqlDataSource dataSource) =>
        {
            payment.Timestamp = DateTime.UtcNow;

            await using var command = dataSource.CreateCommand(
                "INSERT INTO payments (user_id, amount, timestamp) VALUES ($1, $2, $3) RETURNING id");
            command.Parameters.AddWithValue(payment.UserId);
            command.Parameters.AddWithValue(payment.Amount);
            command.Parameters.AddWithValue(payment.Timestamp);

            payment.Id = (int)(await command.ExecuteScalarAsync())!;
            return Results.Created($"/api/payments?id={payment.Id}", payment);
        });
    }

    private static Task<List<Payment>> GetAllAsync(NpgsqlDataSource dataSource) =>
        QueryAsync(dataSource, "SELECT id, user_id, amount, timestamp FROM payments");

    private static Task<List<Payment>> GetByUserIdAsync(int userId, NpgsqlDataSource dataSource) =>
        QueryAsync(dataSource, "SELECT id, user_id, amount, timestamp FROM payments WHERE user_id = $1", userId);

    private static async Task<IResult> GetByIdAsync(int id, NpgsqlDataSource dataSource)
    {
        var payments = await QueryAsync(
            dataSource, "SELECT id, user_id, amount, timestamp FROM payments WHERE id = $1", id);

        return payments.Count > 0 ? Results.Ok(payments[0]) : Results.NotFound();
    }

    private static async Task<List<Payment>> QueryAsync(NpgsqlDataSource dataSource, string sql, params object[] parameters)
    {
        await using var command = dataSource.CreateCommand(sql);
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter);
        }

        await using var reader = await command.ExecuteReaderAsync();

        var payments = new List<Payment>();
        while (await reader.ReadAsync())
        {
            payments.Add(new Payment
            {
                Id = reader.GetInt32(0),
                UserId = reader.GetInt32(1),
                Amount = (double)reader.GetDecimal(2),
                Timestamp = reader.GetDateTime(3)
            });
        }

        return payments;
    }
}

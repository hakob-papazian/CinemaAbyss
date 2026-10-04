using CinemaAbyss.Monolith.Models;
using Npgsql;

namespace CinemaAbyss.Monolith.Endpoints;

public static class UsersEndpoints
{
    public static void MapUsersEndpoints(this IEndpointRouteBuilder app)
    {
        var users = app.MapGroup("/api/users");

        users.MapGet("", async (int? id, NpgsqlDataSource dataSource) =>
            id.HasValue
                ? await GetByIdAsync(id.Value, dataSource)
                : Results.Ok(await GetAllAsync(dataSource)));

        users.MapPost("", async (User user, NpgsqlDataSource dataSource) =>
        {
            await using var command = dataSource.CreateCommand(
                "INSERT INTO users (username, email) VALUES ($1, $2) RETURNING id");
            command.Parameters.AddWithValue(user.Username);
            command.Parameters.AddWithValue(user.Email);

            user.Id = (int)(await command.ExecuteScalarAsync())!;
            return Results.Created($"/api/users?id={user.Id}", user);
        });
    }

    private static async Task<List<User>> GetAllAsync(NpgsqlDataSource dataSource)
    {
        await using var command = dataSource.CreateCommand("SELECT id, username, email FROM users");
        await using var reader = await command.ExecuteReaderAsync();

        var users = new List<User>();
        while (await reader.ReadAsync())
        {
            users.Add(Read(reader));
        }

        return users;
    }

    private static async Task<IResult> GetByIdAsync(int id, NpgsqlDataSource dataSource)
    {
        await using var command = dataSource.CreateCommand("SELECT id, username, email FROM users WHERE id = $1");
        command.Parameters.AddWithValue(id);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Results.Ok(Read(reader)) : Results.NotFound();
    }

    private static User Read(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        Username = reader.GetString(1),
        Email = reader.GetString(2)
    };
}

using CinemaAbyss.MoviesService.Models;
using Npgsql;

namespace CinemaAbyss.MoviesService.Endpoints;

public static class MoviesEndpoints
{
    public static void MapMoviesEndpoints(this IEndpointRouteBuilder app)
    {
        var movies = app.MapGroup("/api/movies");

        movies.MapGet("", async (int? id, NpgsqlDataSource dataSource, ILoggerFactory loggerFactory) =>
        {
            loggerFactory.CreateLogger("Movies").LogInformation("get movies from movies");

            return id.HasValue
                ? await GetByIdAsync(id.Value, dataSource)
                : Results.Ok(await GetAllAsync(dataSource));
        });

        movies.MapPost("", async (Movie movie, NpgsqlDataSource dataSource) =>
        {
            await using var connection = await dataSource.OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            await using (var command = new NpgsqlCommand(
                "INSERT INTO movies (title, description, rating) VALUES ($1, $2, $3) RETURNING id",
                connection, transaction))
            {
                command.Parameters.AddWithValue(movie.Title);
                command.Parameters.AddWithValue(movie.Description);
                command.Parameters.AddWithValue(movie.Rating);
                movie.Id = (int)(await command.ExecuteScalarAsync())!;
            }

            foreach (var genre in movie.Genres)
            {
                await using var command = new NpgsqlCommand(
                    "INSERT INTO movie_genres (movie_id, genre) VALUES ($1, $2)", connection, transaction);
                command.Parameters.AddWithValue(movie.Id);
                command.Parameters.AddWithValue(genre);
                await command.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
            return Results.Created($"/api/movies?id={movie.Id}", movie);
        });
    }

    private static async Task<List<Movie>> GetAllAsync(NpgsqlDataSource dataSource)
    {
        var movies = new List<Movie>();

        await using (var command = dataSource.CreateCommand("SELECT id, title, description, rating FROM movies"))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                movies.Add(Read(reader));
            }
        }

        foreach (var movie in movies)
        {
            movie.Genres = await GetGenresAsync(movie.Id, dataSource);
        }

        return movies;
    }

    private static async Task<IResult> GetByIdAsync(int id, NpgsqlDataSource dataSource)
    {
        Movie movie;

        await using (var command = dataSource.CreateCommand(
            "SELECT id, title, description, rating FROM movies WHERE id = $1"))
        {
            command.Parameters.AddWithValue(id);
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return Results.NotFound();
            }

            movie = Read(reader);
        }

        movie.Genres = await GetGenresAsync(movie.Id, dataSource);
        return Results.Ok(movie);
    }

    private static async Task<List<string>> GetGenresAsync(int movieId, NpgsqlDataSource dataSource)
    {
        await using var command = dataSource.CreateCommand("SELECT genre FROM movie_genres WHERE movie_id = $1");
        command.Parameters.AddWithValue(movieId);

        await using var reader = await command.ExecuteReaderAsync();

        var genres = new List<string>();
        while (await reader.ReadAsync())
        {
            genres.Add(reader.GetString(0));
        }

        return genres;
    }

    private static Movie Read(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        Title = reader.GetString(1),
        Description = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
        Rating = reader.IsDBNull(3) ? 0 : (double)reader.GetDecimal(3)
    };
}

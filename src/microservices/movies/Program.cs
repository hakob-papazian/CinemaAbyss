using CinemaAbyss.MoviesService.Data;
using CinemaAbyss.MoviesService.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables();

// Note: using a different port than the monolith
var port = builder.Configuration["PORT"] ?? "8081";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

builder.Services.AddDatabase(builder.Configuration);
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();

app.MapGet("/api/movies/health", () => Results.Ok(new { status = true }));
app.MapMoviesEndpoints();

await app.WaitForDatabaseAsync();

app.Logger.LogInformation("Starting movies microservice on port {Port}", port);
app.Run();

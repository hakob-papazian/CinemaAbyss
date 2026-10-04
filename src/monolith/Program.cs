using CinemaAbyss.Monolith.Data;
using CinemaAbyss.Monolith.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables();
builder.WebHost.UseUrls($"http://0.0.0.0:{builder.Configuration["PORT"] ?? "8080"}");

builder.Services.AddDatabase(builder.Configuration);
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();

app.MapGet("/health", () => Results.Ok(new { status = true }));
app.MapUsersEndpoints();
app.MapMoviesEndpoints();
app.MapPaymentsEndpoints();
app.MapSubscriptionsEndpoints();

await app.WaitForDatabaseAsync();

app.Logger.LogInformation("Starting server on port {Port}", builder.Configuration["PORT"] ?? "8080");
app.Run();

using CinemaAbyss.EventsService.Endpoints;
using CinemaAbyss.EventsService.Kafka;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables();

var port = builder.Configuration["PORT"] ?? "8082";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

builder.Services.AddSingleton<EventProducer>();
builder.Services.AddHostedService<EventConsumer>();

var app = builder.Build();

app.MapEventsEndpoints();

app.Logger.LogInformation("Starting events microservice on port {Port}", port);
app.Run();

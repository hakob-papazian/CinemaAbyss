FROM mcr.microsoft.com/dotnet/sdk:8.0 AS builder

WORKDIR /src

# Copy the project file and restore dependencies
COPY CinemaAbyss.EventsService.csproj ./
RUN dotnet restore

# Copy the source code
COPY . .

# Build and publish the application
RUN dotnet publish CinemaAbyss.EventsService.csproj -c Release -o /app/publish --no-restore

# Use a smaller image for the final application
FROM mcr.microsoft.com/dotnet/aspnet:8.0

WORKDIR /app

# Copy the published output from builder
COPY --from=builder /app/publish .

# Expose the application port
EXPOSE 8082

# Command to run the application
ENTRYPOINT ["dotnet", "CinemaAbyss.EventsService.dll"]

namespace CinemaAbyss.ProxyService.Routing;

/// <summary>
/// Feature flag controlling the Strangler Fig migration of the "movies" domain
/// from the monolith to the dedicated movies-service.
///
/// GRADUAL_MIGRATION=false -> every request stays on the monolith (pre-migration / rollback state).
/// GRADUAL_MIGRATION=true  -> MOVIES_MIGRATION_PERCENT % of requests are routed to movies-service,
///                            the rest keep hitting the monolith, so traffic can be shifted
///                            gradually (0 -> 100) without a deployment or downtime.
/// </summary>
public class MigrationFlag
{
    public bool GradualMigrationEnabled { get; private set; }
    public int MoviesMigrationPercent { get; private set; }

    public MigrationFlag(IConfiguration configuration)
    {
        Reload(configuration);
    }

    public void Reload(IConfiguration configuration)
    {
        GradualMigrationEnabled = bool.TryParse(configuration["GRADUAL_MIGRATION"], out var enabled) && enabled;

        var percent = int.TryParse(configuration["MOVIES_MIGRATION_PERCENT"], out var value) ? value : 0;
        MoviesMigrationPercent = Math.Clamp(percent, 0, 100);
    }

    /// <summary>
    /// Decides, for a single incoming request, whether it should be routed to movies-service.
    /// </summary>
    public bool ShouldRouteMoviesToMicroservice()
    {
        if (!GradualMigrationEnabled)
        {
            return false;
        }

        if (MoviesMigrationPercent >= 100)
        {
            return true;
        }

        if (MoviesMigrationPercent <= 0)
        {
            return false;
        }

        return Random.Shared.Next(0, 100) < MoviesMigrationPercent;
    }
}

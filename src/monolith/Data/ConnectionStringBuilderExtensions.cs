using Npgsql;

namespace CinemaAbyss.Monolith.Data;

/// <summary>
/// Accepts both the URL form used by the deployment configuration
/// (postgres://user:password@host:port/database?sslmode=disable)
/// and the plain keyword form understood by Npgsql.
/// </summary>
public static class ConnectionStringResolver
{
    public const string Default = "postgres://postgres:postgres@localhost/cinemaabyss?sslmode=disable";

    public static string Resolve(string? value)
    {
        var connectionString = string.IsNullOrWhiteSpace(value) ? Default : value.Trim();

        if (!connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return connectionString;
        }

        var uri = new Uri(connectionString);
        var userInfo = uri.UserInfo.Split(':', 2);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = uri.AbsolutePath.Trim('/'),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null
        };

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            var key = Uri.UnescapeDataString(parts[0]);
            var val = Uri.UnescapeDataString(parts[1]);

            if (key.Equals("sslmode", StringComparison.OrdinalIgnoreCase))
            {
                builder.SslMode = Enum.Parse<SslMode>(val, ignoreCase: true);
            }
            else
            {
                builder[key] = val;
            }
        }

        return builder.ConnectionString;
    }
}

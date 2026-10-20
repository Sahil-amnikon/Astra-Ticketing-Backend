using Npgsql;

internal static class SourceConnection
{
    public static string? Read()
    {
        var configured = Environment.GetEnvironmentVariable("POSTGRES_MIGRATION_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        var url = Environment.GetEnvironmentVariable("POSTGRES_MIGRATION_URL");
        if (string.IsNullOrWhiteSpace(url)) return null;
        var uri = new Uri(url); if (uri.Scheme is not ("postgres" or "postgresql")) throw new InvalidOperationException("Unsupported source database.");
        var credentials = uri.UserInfo.Split(':', 2);
        return new NpgsqlConnectionStringBuilder { Host = "127.0.0.1", Port = 15432, Database = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/')), Username = Uri.UnescapeDataString(credentials[0]), Password = Uri.UnescapeDataString(credentials[1]), ApplicationName = "ASTRA read-only migration", Timeout = 10, CommandTimeout = 120 }.ConnectionString;
    }
}

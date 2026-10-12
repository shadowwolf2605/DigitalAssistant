using Npgsql;

namespace DigitalAssistant.Data;

// Ако е зададена връзка към PostgreSQL (например безплатната база Neon), системата пази данните там.
// Иначе ползва файла SQLite в App_Data, както при стартиране от Visual Studio.
public static class PostgresSettings
{
    // Връзката се взима от ConnectionStrings:Postgres или от DATABASE_URL.
    // Приема се и видът, който дава Neon: postgresql://потребител:парола@адрес/база?sslmode=require
    public static string? ConnectionString(IConfiguration config)
    {
        var value = config.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(value)) value = config["DATABASE_URL"];
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        return value.StartsWith("postgres://") || value.StartsWith("postgresql://") ? FromUrl(value) : value;
    }

    private static string FromUrl(string url)
    {
        var uri = new Uri(url);
        var user = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(user[0]),
            Password = user.Length > 1 ? Uri.UnescapeDataString(user[1]) : null,
            SslMode = SslMode.Require,
        };
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            var name = Uri.UnescapeDataString(pair[0]).ToLowerInvariant();
            var setting = pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : "";
            if (name == "sslmode" && Enum.TryParse<SslMode>(setting.Replace("-", ""), true, out var ssl)) builder.SslMode = ssl;
            else if (name == "channel_binding" && Enum.TryParse<ChannelBinding>(setting, true, out var binding)) builder.ChannelBinding = binding;
        }
        return builder.ConnectionString;
    }
}

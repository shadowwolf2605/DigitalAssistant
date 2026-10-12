using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Npgsql;

namespace DigitalAssistant.Services;

// Тайните ключове на системата: ключовете за шифроване (ЕГН, бисквитки за вход) и ключът за отпечатъка на ЕГН.
// Без тях шифрованите ЕГН-та не могат да се прочетат, затова трябва да се пазят толкова сигурно, колкото и базата.
public interface IKeyStore
{
    byte[] GetOrCreate(string name);
}

// На компютъра ключовете са файлове в App_Data/Keys
public class FileKeyStore : IKeyStore
{
    private readonly string _folder;

    public FileKeyStore(string folder) => _folder = folder;

    public byte[] GetOrCreate(string name)
    {
        var file = Path.Combine(_folder, name + ".key");
        Directory.CreateDirectory(_folder);
        if (!File.Exists(file))
            File.WriteAllBytes(file, RandomNumberGenerator.GetBytes(32));
        return File.ReadAllBytes(file);
    }
}

// В интернет (Render) няма постоянен диск, затова ключовете се пазят в базата данни, в таблица AppKeys.
// Там те са шифровани с паролата от настройката Keys:Secret, която е само в Render.
// Така човек, който види само базата, не може да прочете ЕГН-тата.
public class DatabaseKeyStore : IKeyStore, IXmlRepository
{
    private const string KeyPrefix = "dp:";
    private readonly string _connectionString;
    private readonly byte[]? _secret;

    public DatabaseKeyStore(string connectionString, string? secret)
    {
        _connectionString = connectionString;
        _secret = string.IsNullOrEmpty(secret) ? null : SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }

    public bool HasSecret => _secret != null;

    // Създава таблицата, ако я няма. Вика се след като EF е създал останалите таблици.
    public void EnsureTable()
    {
        using var connection = Open();
        using var command = new NpgsqlCommand(
            """CREATE TABLE IF NOT EXISTS "AppKeys" ("Name" text PRIMARY KEY, "Value" text NOT NULL)""", connection);
        command.ExecuteNonQuery();
    }

    public byte[] GetOrCreate(string name)
    {
        using var connection = Open();
        // Ако два процеса го създадат едновременно, остава първият
        using (var insert = new NpgsqlCommand(
            """INSERT INTO "AppKeys" ("Name", "Value") VALUES (@name, @value) ON CONFLICT ("Name") DO NOTHING""", connection))
        {
            insert.Parameters.AddWithValue("name", name);
            insert.Parameters.AddWithValue("value", Encrypt(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));
            insert.ExecuteNonQuery();
        }
        return Convert.FromBase64String(Decrypt(Read(connection, name)!));
    }

    // Версията на таблиците (за SQLite тя се пази в самия файл)
    public long? GetVersion()
    {
        using var connection = Open();
        var value = Read(connection, "schema-version");
        return value == null ? null : long.Parse(value);
    }

    public void SetVersion(long version)
    {
        using var connection = Open();
        using var command = new NpgsqlCommand(
            """INSERT INTO "AppKeys" ("Name", "Value") VALUES ('schema-version', @value) ON CONFLICT ("Name") DO UPDATE SET "Value" = @value""", connection);
        command.Parameters.AddWithValue("value", version.ToString());
        command.ExecuteNonQuery();
    }

    // Ключовете за шифроване (ползват се от ASP.NET Core Data Protection)
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var connection = Open();
        using var command = new NpgsqlCommand("""SELECT "Value" FROM "AppKeys" WHERE "Name" LIKE 'dp:%'""", connection);
        using var reader = command.ExecuteReader();
        var list = new List<XElement>();
        while (reader.Read())
            list.Add(XElement.Parse(Decrypt(reader.GetString(0))));
        return list;
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        using var connection = Open();
        using var command = new NpgsqlCommand(
            """INSERT INTO "AppKeys" ("Name", "Value") VALUES (@name, @value) ON CONFLICT ("Name") DO NOTHING""", connection);
        command.Parameters.AddWithValue("name", KeyPrefix + friendlyName);
        command.Parameters.AddWithValue("value", Encrypt(element.ToString(SaveOptions.DisableFormatting)));
        command.ExecuteNonQuery();
    }

    private NpgsqlConnection Open()
    {
        var connection = new NpgsqlConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static string? Read(NpgsqlConnection connection, string name)
    {
        using var command = new NpgsqlCommand("""SELECT "Value" FROM "AppKeys" WHERE "Name" = @name""", connection);
        command.Parameters.AddWithValue("name", name);
        return command.ExecuteScalar() as string;
    }

    // AES-GCM: 12 байта случайно число, 16 байта проверка и шифрованият текст, всичко в base64
    private string Encrypt(string text)
    {
        if (_secret == null) return "plain:" + text;
        var data = Encoding.UTF8.GetBytes(text);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[data.Length];
        using (var aes = new AesGcm(_secret, 16)) aes.Encrypt(nonce, data, cipher, tag);
        return Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
    }

    private string Decrypt(string value)
    {
        if (value.StartsWith("plain:")) return value["plain:".Length..];
        if (_secret == null)
            throw new InvalidOperationException("Ключовете в базата са шифровани, а настройката Keys:Secret липсва.");
        var bytes = Convert.FromBase64String(value);
        var cipher = bytes[28..];
        var text = new byte[cipher.Length];
        using (var aes = new AesGcm(_secret, 16)) aes.Decrypt(bytes[..12], cipher, bytes[12..28], text);
        return Encoding.UTF8.GetString(text);
    }
}

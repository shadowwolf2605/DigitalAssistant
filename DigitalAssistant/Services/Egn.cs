using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;

namespace DigitalAssistant.Services;

// Проверка на ЕГН (10 цифри, валидна дата на раждане и контролна цифра)
public static class Egn
{
    private static readonly int[] Weights = { 2, 4, 8, 5, 10, 9, 7, 3, 6 };

    public static string Clean(string? egn) => Regex.Replace(egn ?? "", @"\s+", "");

    // Връща текст на грешката или null, ако ЕГН е валидно
    public static string? Validate(string? value)
    {
        var egn = Clean(value);
        if (egn.Length == 0) return "Въведете ЕГН.";
        if (!Regex.IsMatch(egn, @"^[0-9]{10}$")) return "ЕГН трябва да е точно 10 цифри.";
        if (BirthDate(egn) is not DateTime born || born > DateTime.Today) return "ЕГН не е валидно. Проверете датата на раждане в него.";

        int sum = 0;
        for (int i = 0; i < 9; i++) sum += (egn[i] - '0') * Weights[i];
        int check = sum % 11;
        if (check == 10) check = 0;
        if (check != egn[9] - '0') return "ЕГН не е валидно. Проверете цифрите.";
        return null;
    }

    // Датата на раждане от ЕГН. Към месеца се добавя 20 за родените през 1800-те и 40 за родените след 2000 г.
    public static DateTime? BirthDate(string egn)
    {
        int year = int.Parse(egn[..2]), month = int.Parse(egn.Substring(2, 2)), day = int.Parse(egn.Substring(4, 2));
        if (month > 40) { month -= 40; year += 2000; }
        else if (month > 20) { month -= 20; year += 1800; }
        else year += 1900;
        if (month < 1 || month > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)) return null;
        return new DateTime(year, month, day);
    }

    // Показва само първите 6 цифри (датата), напр. 850314****
    public static string Mask(string egn) => egn.Length == 10 ? egn[..6] + "****" : "";
}

// Шифроване на ЕГН в базата (т. 3.2.5). Ключовете са в папка App_Data/Keys.
public class PersonalDataProtector
{
    private readonly IDataProtector _protector;
    private readonly byte[] _hashKey;

    public PersonalDataProtector(IDataProtectionProvider provider, IKeyStore keys)
    {
        _protector = provider.CreateProtector("DigitalAssistant.Egn");

        // Отделен таен ключ за отпечатъка (хеша) на ЕГН. Създава се при първото стартиране
        // (файлът App_Data/Keys/egn-hash.key или ред в базата, ако тя е PostgreSQL).
        _hashKey = keys.GetOrCreate("egn-hash");
    }

    public string Protect(string egn) => _protector.Protect(Egn.Clean(egn));

    public string Unprotect(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        try { return _protector.Unprotect(value); }
        catch (CryptographicException) { return ""; }
    }

    // Един и същ отпечатък за едно и също ЕГН. По него се проверява дали вече има профил и се търси.
    public string Hash(string egn) =>
        Convert.ToHexString(HMACSHA256.HashData(_hashKey, Encoding.UTF8.GetBytes(Egn.Clean(egn))));
}

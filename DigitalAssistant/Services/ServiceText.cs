using System.Text.RegularExpressions;

namespace DigitalAssistant.Services;

// Документите и стъпките на услугата се пишат като обикновен текст.
// Тук се разделят на отделни точки за показване.
public static class ServiceText
{
    // Документите се разделят с „;“ или с нов ред
    public static List<string> Documents(string? text) =>
        (text ?? "").Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => Regex.Replace(d, @"^(?:[-•*]|\d+[.)])\s*", "").Trim())
            .Where(d => d.Length > 0).ToList();

    // Възможните отговори на уточняващия въпрос на услугата
    public record Situation(string Answer, List<string> Documents, string Note);

    public static List<Situation> Situations(string? text) =>
        (text ?? "").Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('|', 3, StringSplitOptions.TrimEntries))
            .Where(p => p[0].Length > 0)
            .Select(p => new Situation(p[0], p.Length > 1 ? Documents(p[1]) : new List<string>(), p.Length > 2 ? p[2] : ""))
            .ToList();

    // Стъпките са на отделни редове или са номерирани в един ред: „1. ... 2. ...“
    public static List<string> Steps(string? text)
    {
        var lines = (text ?? "").Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var parts = lines.Length > 1 ? lines : Regex.Split(text ?? "", @"(?:^|\s)\d+\.\s");
        return parts.Select(s => Regex.Replace(s, @"^(?:[-•*]|\d+[.)])\s*", "").Trim()).Where(s => s.Length > 0).ToList();
    }
}

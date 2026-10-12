using DigitalAssistant.Models;

namespace DigitalAssistant.Services;

// Правила за прикачените документи към онлайн заявленията (т. 3.1.3)
public static class ApplicationFileRules
{
    public const int MaxFiles = 5;
    public const long MaxFileSize = 5 * 1024 * 1024;

    public static readonly Dictionary<string, string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
    };

    // Връща списък с грешки (празен, ако всичко е наред)
    public static List<string> Check(IList<IFormFile> files, bool required)
    {
        var errors = new List<string>();
        var real = files.Where(f => f != null && f.Length > 0).ToList();
        if (required && real.Count == 0)
            errors.Add("Прикачете поне един документ (например попълненото заявление).");
        if (real.Count > MaxFiles)
            errors.Add($"Може да прикачите най-много {MaxFiles} файла.");
        foreach (var f in real)
        {
            var name = Path.GetFileName(f.FileName);
            if (!Allowed.ContainsKey(Path.GetExtension(name)))
                errors.Add($"„{name}“: разрешени са само файлове .pdf, .jpg, .png, .doc и .docx.");
            else if (f.Length > MaxFileSize)
                errors.Add($"„{name}“ е по-голям от 5 MB.");
        }
        return errors;
    }

    public static async Task<List<ApplicationFile>> ReadAsync(IEnumerable<IFormFile> files)
    {
        var result = new List<ApplicationFile>();
        foreach (var f in files.Where(f => f != null && f.Length > 0))
        {
            using var ms = new MemoryStream();
            await f.CopyToAsync(ms);
            var name = Path.GetFileName(f.FileName);
            result.Add(new ApplicationFile
            {
                FileName = name,
                ContentType = Allowed[Path.GetExtension(name)],
                Size = f.Length,
                Data = ms.ToArray()
            });
        }
        return result;
    }

    public static string Size(long bytes) =>
        bytes >= 1024 * 1024 ? $"{bytes / 1024.0 / 1024.0:0.0} MB" : $"{Math.Max(1, bytes / 1024)} KB";

    // Иконка според вида на файла
    public static string Icon(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".pdf" => "bi-file-earmark-pdf",
        ".jpg" or ".jpeg" or ".png" => "bi-file-earmark-image",
        _ => "bi-file-earmark-text"
    };
}

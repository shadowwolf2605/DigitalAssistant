using System.Globalization;
using DigitalAssistant.Services;

namespace DigitalAssistant.Models;

// Отговорите, които човекът е дал до момента в помощника (т. 3.1.2).
// Пазят се в адреса на страницата, затова бутонът „Назад“ на браузъра работи и упътването може да се отвори отново.
public class GuideState
{
    public int? C { get; set; }          // категория
    public int? S { get; set; }          // услуга
    public int? O { get; set; }          // отговор на уточняващия въпрос на услугата (номер)
    public string? How { get; set; }     // online или place
    public string? Speed { get; set; }   // normal или express
    public bool D { get; set; }          // отговорил е за документите
    public string? H { get; set; }       // номерата на документите, които вече има, разделени със запетая
    public List<int> Have { get; set; } = new(); // същото, но от отметките във формуляра
    public string? City { get; set; }
    public double? Lat { get; set; }
    public double? Lng { get; set; }
    public string? Q { get; set; }       // въпрос с думи, ако е търсил така

    public const string Online = "online";
    public const string Place = "place";
    public const string Express = "express";
    public const string Normal = "normal";

    public bool IsOnline => How == Online;
    public bool IsExpress => Speed == Express;
    public bool HasLocation => !string.IsNullOrWhiteSpace(City) || (Lat != null && Lng != null);

    public HashSet<int> HaveSet =>
        Have.Count > 0
            ? Have.ToHashSet()
            : (H ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => int.TryParse(x, out var n) ? n : -1).Where(n => n >= 0).ToHashSet();

    // Параметрите за връзка към следваща стъпка. Изброените ключове се пропускат (за „Назад“ и „Промени“).
    public Dictionary<string, string> Route(params string[] without)
    {
        var r = new Dictionary<string, string>();
        void Add(string key, string? value)
        {
            if (!string.IsNullOrEmpty(value) && !without.Contains(key)) r[key] = value;
        }
        Add("q", Q);
        Add("c", C?.ToString());
        Add("s", S?.ToString());
        Add("o", O?.ToString());
        Add("how", How);
        Add("speed", Speed);
        if (D && !without.Contains("d")) r["d"] = "true";
        // Отметнатите документи се пазят и при „Назад“, за да не се отбелязват отново
        if (HaveSet.Count > 0 && !without.Contains("h")) r["h"] = string.Join(",", HaveSet.OrderBy(x => x));
        if (!without.Contains("loc"))
        {
            Add("city", City);
            Add("lat", Lat?.ToString("0.###", CultureInfo.InvariantCulture));
            Add("lng", Lng?.ToString("0.###", CultureInfo.InvariantCulture));
        }
        return r;
    }
}

// Една стъпка на помощника: въпрос и възможни отговори
public enum GuideStep { Category, Service, Search, Situation, How, Speed, Documents, Location, Guide }

public class GuideOption
{
    public string Title { get; set; } = "";
    public string? Text { get; set; }
    public string Icon { get; set; } = "bi-chevron-right";
    public string? Tone { get; set; }
    public Dictionary<string, string> Route { get; set; } = new();
    public List<string> Badges { get; set; } = new();
}

public class GuideViewModel
{
    public GuideState State { get; set; } = new();
    public GuideStep Step { get; set; }
    public string Question { get; set; } = "";
    public string? Hint { get; set; }
    public List<GuideOption> Options { get; set; } = new();

    // Номер на стъпката и общ брой стъпки (за лентата с напредъка)
    public int Number { get; set; }
    public int Total { get; set; }

    // Връзка за „Назад“ (null на първата стъпка)
    public Dictionary<string, string>? Back { get; set; }

    public Category? Category { get; set; }
    public Service? Service { get; set; }
    public List<string> Documents { get; set; } = new();

    // Уточняващият въпрос на услугата и избраният отговор
    public List<ServiceText.Situation> Situations { get; set; } = new();
    public ServiceText.Situation? Situation { get; set; }
    public string? Error { get; set; }

    // Търсене с думи
    public string? AiAnswer { get; set; }
    public string? ClaudeUrl { get; set; }

    // Мястото, ако човекът е споделил местоположението си
    public GuideCities.City? NearestCity { get; set; }
    public string? CityName => string.IsNullOrWhiteSpace(State.City) ? NearestCity?.Name : State.City;

    // Офисът на институцията в този град, ако администраторът го е добавил
    public Office? Office { get; set; }
}

using System.Text.RegularExpressions;
using DigitalAssistant.Data;
using DigitalAssistant.Models;
using Microsoft.EntityFrameworkCore;

namespace DigitalAssistant.Services;

// Дигитален помощник (т. 3.1.2): търси думите от въпроса в имената и ключовите думи на активните услуги.
// Ако не намери услуга, въпросът се изпраща на изкуствения интелект (AiAssistant).
public class AssistantService
{
    public const int MaxQuestionLength = 300;

    // Думи, които не помагат при търсенето
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "а", "аз", "ако", "би", "бих", "в", "във", "да", "до", "е", "за", "и", "или", "им", "има", "имам",
        "искам", "иска", "как", "какво", "какви", "какъв", "каква", "кога", "колко", "който", "която", "което",
        "кой", "коя", "кое", "къде", "ли", "ме", "мен", "ми", "мога", "може", "моля", "моят", "моя", "мой",
        "мои", "на", "не", "нещо", "ние", "но", "нужно", "нужни", "нужна", "нужен", "необходимо",
        "необходими", "от", "по", "при", "с", "са", "се", "си", "сме", "със", "съм", "т", "та", "ти", "то",
        "това", "този", "тази", "тези", "трябва", "трябват", "у", "че", "ще", "един", "една", "едно",
        "документ", "документи", "документа", "документите", "издаване", "издава", "издаде", "изваждане",
        "извадя", "вадя", "вади", "получа", "получавам", "направя", "правя", "услуга", "услугата", "услуги",
        "здравей", "здравейте", "благодаря", "вие", "те", "той", "тя", "нас", "вас", "ги", "го", "я",
        "моето", "моите", "своя", "своята", "свой", "нова", "нов", "ново", "нови", "дали", "вече", "още", "сега",
        "много", "само", "където", "тук", "там", "някой", "някоя", "някакъв", "някаква", "нашия", "наша", "наш", "ваш",
        "бъде", "бъда", "беше", "бях", "можете", "искаме", "нужно", "какъвто", "кои", "откъде", "колкото"
    };

    private readonly AppDbContext _db;

    public AssistantService(AppDbContext db) => _db = db;

    public static List<string> Words(string text) =>
        Regex.Split(text.ToLowerInvariant(), @"[^\p{L}\p{Nd}]+")
            .Where(w => w.Length >= 2 && !StopWords.Contains(w))
            .Distinct()
            .ToList();

    // Много проста основа на думата, за да съвпадат "адрес" и "адресна", "карта" и "карти"
    public static string Stem(string word) =>
        word.Length >= 6 ? word[..5] : word.Length == 5 ? word[..4] : word;

    private static bool Same(string a, string b)
    {
        if (a.Length < 4 || b.Length < 4) return a == b;
        return a.StartsWith(b) || b.StartsWith(a);
    }

    // Оценка колко подходяща е услугата за въпроса.
    // Ключова фраза (напр. "лична карта") се брои само ако всичките ѝ думи са във въпроса.
    // Така "членска карта" не намира "лична карта".
    public int Score(Service service, List<string> questionStems)
    {
        bool InQuestion(string stem) => questionStems.Any(q => Same(stem, q));

        // Еднакви фрази (напр. "пенсия" и "пенсионер" дават една и съща основа) се броят веднъж
        var fullPhrases = service.KeywordList
            .Select(k => Words(k).Select(Stem).Distinct().OrderBy(x => x).ToList())
            .Where(p => p.Count > 0 && p.All(InQuestion))
            .DistinctBy(p => string.Join(" ", p))
            .ToList();
        int nameMatches = Words(service.Name).Select(Stem).Distinct().Count(InQuestion);

        if (fullPhrases.Count == 0 && nameMatches < 2) return 0;
        return fullPhrases.Sum(p => 3 * p.Count) + nameMatches;
    }

    // Връща подходящите услуги, подредени от най-подходящата
    public async Task<List<Service>> FindAsync(string question)
    {
        var stems = Words(question).Select(Stem).Distinct().ToList();
        if (stems.Count == 0) return new List<Service>();

        var services = await _db.Services
            .Include(s => s.Institution)
            .Include(s => s.Category)
            .Where(s => s.IsActive)
            .ToListAsync();

        var scored = services
            .Select(s => new { Service = s, Score = Score(s, stems) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Service.Name)
            .ToList();

        if (scored.Count == 0) return new List<Service>();

        int top = scored[0].Score;
        return scored
            .Where(x => x.Score >= Math.Ceiling(top * 0.75))
            .Take(5)
            .Select(x => x.Service)
            .ToList();
    }

    // Записва въпроса с дата, час и намерената услуга. ЕГН, телефони и имейли във въпроса се скриват.
    public async Task LogAsync(string question, List<Service> found, AiAnswer? ai = null)
    {
        _db.Questions.Add(new Question
        {
            Text = AiAssistant.RemovePersonalData(question),
            AskedAt = DateTime.Now,
            ServiceId = found.FirstOrDefault()?.Id,
            FoundCount = found.Count,
            AiAnswer = ai?.Text,
            AiServiceIds = ai == null ? "" : string.Join(",", ai.ServiceIds),
            // Поздрав или само емоджи няма какво да се преглежда, затова не влиза в новите въпроси
            Reviewed = Words(question).Count == 0
        });
        await _db.SaveChangesAsync();
    }
}

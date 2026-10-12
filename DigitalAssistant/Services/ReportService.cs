using DigitalAssistant.Data;
using DigitalAssistant.Models;
using Microsoft.EntityFrameworkCore;

namespace DigitalAssistant.Services;

public record ReportColumn(string Key, string Title);

public record ReportDefinition(string Key, string Title, bool NeedsPeriod, List<ReportColumn> Columns);

// Готова справка в табличен вид
public class ReportTable
{
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public List<string> Headers { get; set; } = new();
    public List<List<string>> Rows { get; set; } = new();
    public string? Footer { get; set; }
}

public class ReportRequest
{
    public string Type { get; set; } = "questions";
    public DateTime From { get; set; } = DateTime.Today.AddDays(-30);
    public DateTime To { get; set; } = DateTime.Today;
    public List<string> Cols { get; set; } = new();
    public int? CategoryId { get; set; }
    public int? InstitutionId { get; set; }
}

// Справки (т. 3.1.4)
public class ReportService
{
    public static readonly List<ReportDefinition> Definitions = new()
    {
        new("questions", "Брой зададени въпроси за период", true, new()
        {
            new("date", "Дата"), new("total", "Брой въпроси"), new("answered", "С отговор"), new("unanswered", "Без отговор")
        }),
        new("top", "Най-търсени услуги", true, new()
        {
            new("rank", "№"), new("service", "Услуга"), new("institution", "Институция"), new("guides", "Показани упътвания"), new("count", "Брой въпроси")
        }),
        new("unanswered", "Въпроси без отговор", true, new()
        {
            new("date", "Дата и час"), new("text", "Въпрос"), new("reviewed", "Прегледан")
        }),
        new("services", "Услуги по категория и по институция", false, new()
        {
            new("category", "Категория"), new("institution", "Институция"), new("service", "Услуга"),
            new("fee", "Такса (€)"), new("deadline", "Срок (работни дни)"), new("status", "Статус")
        }),
        new("changes", "Промени в данните и действия на администраторите", true, new()
        {
            new("date", "Дата и час"), new("admin", "Администратор"), new("action", "Действие"), new("details", "Подробности")
        }),
        new("applications", "Онлайн заявления за период", true, new()
        {
            new("number", "Номер"), new("date", "Подадено на"), new("citizen", "Гражданин"), new("service", "Услуга"),
            new("status", "Статус"), new("updated", "Последна промяна")
        }),
    };

    private readonly AppDbContext _db;

    public ReportService(AppDbContext db) => _db = db;

    public static ReportDefinition Find(string type) =>
        Definitions.FirstOrDefault(d => d.Key == type) ?? Definitions[0];

    public async Task<ReportTable> BuildAsync(ReportRequest req)
    {
        var def = Find(req.Type);
        var cols = def.Columns.Where(c => req.Cols.Count == 0 || req.Cols.Contains(c.Key)).ToList();
        if (cols.Count == 0) cols = def.Columns;

        var from = req.From.Date;
        var toExclusive = req.To.Date.AddDays(1);

        var table = new ReportTable
        {
            Title = def.Title,
            Subtitle = def.NeedsPeriod
                ? $"Период: {from:dd.MM.yyyy} – {req.To:dd.MM.yyyy}"
                : $"Към дата: {DateTime.Today:dd.MM.yyyy}",
            Headers = cols.Select(c => c.Title).ToList()
        };

        // Всеки ред се пази като речник "колона -> стойност", после се взимат само избраните колони
        var rows = new List<Dictionary<string, string>>();

        switch (def.Key)
        {
            case "questions":
            {
                var questions = await _db.Questions
                    .Where(q => q.AskedAt >= from && q.AskedAt < toExclusive)
                    .Select(q => new { q.AskedAt, q.FoundCount })
                    .ToListAsync();
                foreach (var g in questions.GroupBy(q => q.AskedAt.Date).OrderBy(g => g.Key))
                {
                    rows.Add(new()
                    {
                        ["date"] = g.Key.ToString("dd.MM.yyyy"),
                        ["total"] = g.Count().ToString(),
                        ["answered"] = g.Count(q => q.FoundCount > 0).ToString(),
                        ["unanswered"] = g.Count(q => q.FoundCount == 0).ToString()
                    });
                }
                table.Footer = $"Общо въпроси: {questions.Count}, с отговор: {questions.Count(q => q.FoundCount > 0)}, " +
                               $"без отговор: {questions.Count(q => q.FoundCount == 0)}";
                break;
            }
            case "top":
            {
                // Броят се и упътванията от помощника с въпроси, и намерените услуги при търсене с думи
                var questions = await _db.Questions
                    .Where(q => q.AskedAt >= from && q.AskedAt < toExclusive && q.ServiceId != null)
                    .GroupBy(q => q.ServiceId!.Value)
                    .Select(g => new { ServiceId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.ServiceId, x => x.Count);
                var guides = await _db.GuideViews
                    .Where(g => g.ShownAt >= from && g.ShownAt < toExclusive)
                    .GroupBy(g => g.ServiceId)
                    .Select(g => new { ServiceId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.ServiceId, x => x.Count);
                var services = await _db.Services.Include(s => s.Institution).ToDictionaryAsync(s => s.Id);
                int rank = 1;
                foreach (var id in questions.Keys.Union(guides.Keys)
                             .OrderByDescending(id => guides.GetValueOrDefault(id) + questions.GetValueOrDefault(id)).Take(20))
                {
                    var s = services[id];
                    rows.Add(new()
                    {
                        ["rank"] = (rank++).ToString(),
                        ["service"] = s.Name,
                        ["institution"] = s.Institution?.Name ?? "",
                        ["guides"] = guides.GetValueOrDefault(id).ToString(),
                        ["count"] = questions.GetValueOrDefault(id).ToString()
                    });
                }
                break;
            }
            case "unanswered":
            {
                var data = await _db.Questions
                    .Where(q => q.AskedAt >= from && q.AskedAt < toExclusive && q.FoundCount == 0)
                    .OrderByDescending(q => q.AskedAt)
                    .ToListAsync();
                foreach (var q in data)
                {
                    rows.Add(new()
                    {
                        ["date"] = q.AskedAt.ToString("dd.MM.yyyy HH:mm"),
                        ["text"] = q.Text,
                        ["reviewed"] = q.Reviewed ? "да" : "не"
                    });
                }
                table.Footer = $"Общо: {data.Count}";
                break;
            }
            case "services":
            {
                var query = _db.Services.Include(s => s.Category).Include(s => s.Institution).AsQueryable();
                if (req.CategoryId != null) query = query.Where(s => s.CategoryId == req.CategoryId);
                if (req.InstitutionId != null) query = query.Where(s => s.InstitutionId == req.InstitutionId);
                var data = await query.ToListAsync();
                foreach (var s in data.OrderBy(s => s.Category!.Name).ThenBy(s => s.Institution!.Name).ThenBy(s => s.Name))
                {
                    rows.Add(new()
                    {
                        ["category"] = s.Category!.Name,
                        ["institution"] = s.Institution!.Name,
                        ["service"] = s.Name,
                        ["fee"] = s.Fee.ToString("0.00"),
                        ["deadline"] = s.DeadlineDays.ToString(),
                        ["status"] = s.IsActive ? "активна" : "неактивна"
                    });
                }
                table.Footer = $"Общо услуги: {data.Count}";
                break;
            }
            case "changes":
            {
                var data = await _db.AuditEntries
                    .Where(a => a.At >= from && a.At < toExclusive)
                    .OrderByDescending(a => a.At)
                    .ToListAsync();
                foreach (var a in data)
                {
                    rows.Add(new()
                    {
                        ["date"] = a.At.ToString("dd.MM.yyyy HH:mm"),
                        ["admin"] = a.AdminUsername,
                        ["action"] = a.Action,
                        ["details"] = a.Details
                    });
                }
                table.Footer = $"Общо промени: {data.Count}";
                break;
            }
            case "applications":
            {
                var data = await _db.Applications.Include(a => a.Citizen).Include(a => a.Service)
                    .Where(a => a.CreatedAt >= from && a.CreatedAt < toExclusive)
                    .OrderByDescending(a => a.CreatedAt)
                    .ToListAsync();
                foreach (var a in data)
                {
                    rows.Add(new()
                    {
                        ["number"] = a.Number,
                        ["date"] = a.CreatedAt.ToString("dd.MM.yyyy HH:mm"),
                        ["citizen"] = a.Citizen!.FullName,
                        ["service"] = a.Service!.Name,
                        ["status"] = ApplicationStatuses.Name(a.Status),
                        ["updated"] = a.UpdatedAt.ToString("dd.MM.yyyy HH:mm")
                    });
                }
                var byStatus = ApplicationStatuses.All
                    .Select(st => $"{ApplicationStatuses.Name(st).ToLower()}: {data.Count(a => a.Status == st)}");
                table.Footer = $"Общо заявления: {data.Count} ({string.Join(", ", byStatus)})";
                break;
            }
        }

        table.Rows = rows.Select(r => cols.Select(c => r.GetValueOrDefault(c.Key, "")).ToList()).ToList();
        return table;
    }
}

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using DigitalAssistant.Data;
using DigitalAssistant.Models;
using Microsoft.EntityFrameworkCore;

namespace DigitalAssistant.Services;

// Настройки за изкуствения интелект (раздел "Ai" в appsettings.json).
// Ключът не се пише в appsettings.json, а в User Secrets или в променливата ANTHROPIC_API_KEY.
public class AiOptions
{
    public bool Enabled { get; set; } = true;
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "claude-opus-5-5";
    public string? BaseUrl { get; set; }
    public int TimeoutSeconds { get; set; } = 20;
    // Бутон „Попитай Claude“, когато изкуственият интелект не е включен в сайта (няма ключ)
    public bool ClaudeLink { get; set; } = true;
}

// Отговор на изкуствения интелект: текст и номерата на подходящите услуги
public record AiAnswer(string Text, List<int> ServiceIds);

// Отговаря на въпроси, за които помощникът не е намерил услуга по ключовите думи (т. 3.1.2).
// Изпраща въпроса заедно със списъка с активните услуги. Лични данни (ЕГН, телефон, имейл) се махат преди това.
public class AiAssistant
{
    private readonly AiOptions _options;
    private readonly ILogger<AiAssistant> _logger;
    private readonly AnthropicClient? _client;

    public AiAssistant(AiOptions options, ILogger<AiAssistant> logger)
    {
        _options = options;
        _logger = logger;
        var key = string.IsNullOrWhiteSpace(options.ApiKey) ? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") : options.ApiKey;
        if (options.Enabled && !string.IsNullOrWhiteSpace(key))
        {
            _client = string.IsNullOrWhiteSpace(options.BaseUrl)
                ? new AnthropicClient { ApiKey = key, Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds), MaxRetries = 1 }
                : new AnthropicClient { ApiKey = key, Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds), MaxRetries = 1, BaseUrl = options.BaseUrl };
        }
    }

    // Има ли ключ, т.е. работи ли изкуственият интелект
    public bool IsAvailable => _client != null;

    public string Model => _options.Model;

    // Защо е изключен: изрично е изключен в настройките или няма ключ
    public bool TurnedOff => !_options.Enabled;
    public bool ClaudeLinkOn => _options.ClaudeLink;

    // Връзка, която отваря claude.ai с вече написан въпрос. Гражданинът получава отговора там,
    // със своя профил в claude.ai, затова сайтът не плаща нищо. Личните данни се махат и тук.
    public string? ClaudeLinkFor(string question)
    {
        if (!_options.ClaudeLink) return null;
        var text = "Въпрос на гражданин за административна услуга в България (гр. Русе). " +
                   "Отговори кратко, на български и с прости думи: " + RemovePersonalData(question);
        return "https://claude.ai/new?q=" + Uri.EscapeDataString(text);
    }

    // Маха от текста неща, които приличат на лични данни
    public static string RemovePersonalData(string text)
    {
        text = Regex.Replace(text, @"[\w.+-]+@[\w-]+(\.[\w-]+)+", "[имейл]");
        // Телефон: +359, 00359 или 0 и още 8–9 цифри, може с интервали, тирета, скоби или наклонена черта
        text = Regex.Replace(text, @"(?:(?:\+|00) ?359|\(?(?<![0-9])0)(?:[ /()-]{0,2}[0-9]){8,9}(?![0-9])", "[телефон]");
        // ЕГН: 10 цифри, може с интервали или тирета между тях и залепени за думи (напр. „ЕГН9204153172“)
        text = Regex.Replace(text, @"(?<![0-9])[0-9](?:[ -]?[0-9]){9}(?![0-9])", "[ЕГН]");
        return text;
    }

    public async Task<AiAnswer?> AskAsync(string question, List<Service> services, CancellationToken token = default)
    {
        if (_client == null) return null;

        var parameters = new MessageCreateParams
        {
            Model = _options.Model,
            MaxTokens = 4000,
            Betas = ["server-side-fallback-2026-07-01"],
            // При отказ по съображения за сигурност API-то само опитва с подходящ друг модел
            Fallbacks = new Default(),
            // Списъкът с услугите се пази в кеша на API-то и не се плаща изцяло при всеки въпрос
            System = new List<BetaTextBlockParam>
            {
                new() { Text = SystemPrompt(services), CacheControl = new BetaCacheControlEphemeral() }
            },
            OutputConfig = new BetaOutputConfig
            {
                Effort = Effort.Low,
                Format = new BetaJsonOutputFormat
                {
                    Schema = new Dictionary<string, JsonElement>
                    {
                        ["type"] = JsonSerializer.SerializeToElement("object"),
                        ["properties"] = JsonSerializer.SerializeToElement(new
                        {
                            answer = new { type = "string", description = "Отговорът до гражданина" },
                            service_ids = new { type = "array", items = new { type = "integer" }, description = "Номерата на подходящите услуги от списъка (най-много 3)" }
                        }),
                        ["required"] = JsonSerializer.SerializeToElement(new[] { "answer", "service_ids" }),
                        ["additionalProperties"] = JsonSerializer.SerializeToElement(false)
                    }
                }
            },
            Messages = [new() { Role = Role.User, Content = RemovePersonalData(question) }]
        };

        // Гражданинът не чака повече от TimeoutSeconds секунди, дори ако заявката се повтори
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        try
        {
            var response = await _client.Beta.Messages.Create(parameters, limit.Token);
            if (response.StopReason != "end_turn")
            {
                _logger.LogWarning("Изкуственият интелект не отговори докрай (stop_reason: {Reason})", response.StopReason);
                return null;
            }
            var text = string.Concat(response.Content.Select(b => b.Value).OfType<BetaTextBlock>().Select(t => t.Text));
            using var json = JsonDocument.Parse(text);
            var answer = json.RootElement.GetProperty("answer").GetString()?.Trim() ?? "";
            var known = services.Select(s => s.Id).ToHashSet();
            var ids = json.RootElement.GetProperty("service_ids").EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out _))
                .Select(e => e.GetInt32()).Where(known.Contains).Distinct().Take(3).ToList();
            if (answer.Length == 0) return null;
            _logger.LogInformation("Изкуственият интелект отговори (вход {In} токена, от кеша {Cached}, изход {Out})",
                response.Usage.InputTokens, response.Usage.CacheReadInputTokens, response.Usage.OutputTokens);
            return new AiAnswer(answer, ids);
        }
        catch (AnthropicRateLimitException ex)
        {
            _logger.LogWarning(ex, "Изкуственият интелект е претоварен (твърде много заявки).");
        }
        catch (AnthropicApiException ex)
        {
            _logger.LogError(ex, "Изкуственият интелект върна грешка.");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Отговорът на изкуствения интелект не е в очаквания вид.");
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or AnthropicException)
        {
            _logger.LogError(ex, "Няма връзка с изкуствения интелект.");
        }
        return null;
    }

    // Указанията и списъкът с услугите. Подреден е по номер, за да е еднакъв при всеки въпрос (заради кеша).
    private static string SystemPrompt(List<Service> services)
    {
        var sb = new StringBuilder();
        sb.AppendLine("""
            Ти си дигиталният помощник на сайта „Дигитален помощник за административни услуги“ на
            „Административен информационен център“ ЕООД в Русе. Гражданите питат за административни услуги в България.
            Тези въпроси са стигнали до теб, защото търсенето по ключови думи в сайта не е намерило услуга.

            Как да отговаряш:
            - Пиши на български, учтиво, с прости думи и кратко – до 5 изречения. Без звездички, заглавия и друго форматиране.
            - Първо търси в списъка с услуги по-долу. Ако въпросът е за услуга от списъка, кажи накратко какво трябва
              и сложи номера ѝ в service_ids. Сложи най-много 3 номера, най-подходящият да е първи.
            - Такси, срокове, адреси, телефони и работно време казвай само ако ги има в списъка. Не ги измисляй.
            - Ако темата е административна, но я няма в списъка, дай кратък общ отговор, кажи коя институция се занимава
              с това и посъветвай гражданина да провери там. Тогава service_ids е празен.
            - Ако въпросът не е за административни услуги, кажи учтиво, че помагаш само за административни услуги. service_ids е празен.
            - Не искай лични данни (ЕГН, адрес, телефон). Текст като [ЕГН] или [телефон] значи, че системата е скрила лични данни.
            - Онлайн заявление може да се подаде през сайта само за услугите с „Онлайн заявление: да“.

            Списък с услугите:
            """);
        foreach (var s in services.OrderBy(s => s.Id))
        {
            sb.AppendLine($"[{s.Id}] {s.Name} – {s.Institution?.Name} (категория: {s.Category?.Name})");
            if (!string.IsNullOrWhiteSpace(s.Description)) sb.AppendLine($"Описание: {s.Description}");
            sb.AppendLine($"Документи: {s.RequiredDocuments}");
            if (s.OfficialDocuments.Length > 0) sb.AppendLine($"Не се носят (служителят ги взима служебно): {s.OfficialDocuments}");
            sb.AppendLine($"Такса: {(s.Fee == 0 ? "безплатно" : $"{s.Fee:0.00} €")}" +
                          (s.ExpressFee != null ? $"; бърза услуга: {s.ExpressFee:0.00} €" : ""));
            sb.AppendLine($"Срок: {WorkingDays.Text(s.DeadlineDays)}" + (s.ExpressDeadlineDays != null ? $"; бърза услуга: {WorkingDays.Text(s.ExpressDeadlineDays.Value)}" : ""));
            sb.AppendLine($"Място: {s.Address}; {s.WorkingHours}; тел. {s.Phone}");
            sb.AppendLine($"Онлайн заявление: {(s.AllowOnline ? "да" : "не")}");
            sb.AppendLine();
        }
        return sb.ToString();
    }
}

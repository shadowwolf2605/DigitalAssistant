using System.Net;
using System.Net.Mail;
using System.Text;

namespace DigitalAssistant.Services;

// Настройки за изпращане на имейли (раздел "Email" в appsettings.json)
public class EmailOptions
{
    // Ако SmtpHost е празен, писмата не се изпращат, а се записват в папка App_Data/Emails
    // (за тестване на компютъра). Отварят се от страницата „Тестова поща“.
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string From { get; set; } = "no-reply@uslugi-ruse.bg";
    public string FromName { get; set; } = "Дигитален помощник – АИЦ Русе";
}

// Изпращане на известия до гражданите по имейл
public class EmailService
{
    private readonly EmailOptions _options;
    private readonly ILogger<EmailService> _logger;
    private readonly string _folder;
    private readonly bool _isDevelopment;

    public EmailService(EmailOptions options, ILogger<EmailService> logger, IWebHostEnvironment env)
    {
        _options = options;
        _logger = logger;
        _folder = Path.Combine(env.ContentRootPath, "App_Data", "Emails");
        _isDevelopment = env.IsDevelopment();
    }

    public bool UsesSmtp => !string.IsNullOrWhiteSpace(_options.SmtpHost);

    // Писмата стигат до човека: или има пощенски сървър, или е тест на компютъра с „Тестова поща“.
    // Ако сайтът е качен в интернет без пощенски сървър, писма не стигат – тогава имейлът не се потвърждава
    // и „Забравена парола“ не работи.
    public bool CanDeliver => UsesSmtp || _isDevelopment;

    public string Folder => _folder;

    // paragraphs – редове от текста; link – бутон с връзка (по желание). Връща false, ако писмото не е изпратено.
    public async Task<bool> SendAsync(string to, string subject, IEnumerable<string> paragraphs, string? linkText = null, string? linkUrl = null)
    {
        var html = Build(subject, paragraphs, linkText, linkUrl);
        try
        {
            if (UsesSmtp)
            {
                using var message = new MailMessage
                {
                    From = new MailAddress(_options.From, _options.FromName, Encoding.UTF8),
                    Subject = subject,
                    SubjectEncoding = Encoding.UTF8,
                    Body = html,
                    BodyEncoding = Encoding.UTF8,
                    IsBodyHtml = true
                };
                message.To.Add(to);
                using var client = new SmtpClient(_options.SmtpHost, _options.SmtpPort) { EnableSsl = _options.UseSsl };
                if (!string.IsNullOrEmpty(_options.Username))
                    client.Credentials = new NetworkCredential(_options.Username, _options.Password);
                await client.SendMailAsync(message);
            }
            else
            {
                Directory.CreateDirectory(_folder);
                var name = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}_{Safe(to)}.html";
                var page = $"<!-- to: {WebUtility.HtmlEncode(to)} -->\n<!-- subject: {WebUtility.HtmlEncode(subject)} -->\n{html}";
                await File.WriteAllTextAsync(Path.Combine(_folder, name), page, Encoding.UTF8);
            }
            // В дневника не се записва адресът – личните данни са само в базата (т. 3.2.4)
            _logger.LogInformation("Изпратен имейл „{Subject}“", subject);
            return true;
        }
        catch (Exception ex)
        {
            // Грешка при имейла не спира работата на системата
            _logger.LogError(ex, "Имейлът „{Subject}“ не беше изпратен", subject);
            return false;
        }
    }

    private static string Safe(string text) =>
        new string(text.Select(c => char.IsLetterOrDigit(c) || c is '.' or '@' or '-' ? c : '_').ToArray());

    private string Build(string subject, IEnumerable<string> paragraphs, string? linkText, string? linkUrl)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"bg\"><head><meta charset=\"utf-8\"><title>")
          .Append(WebUtility.HtmlEncode(subject))
          .Append("</title></head><body style=\"margin:0;background:#f7f8fa;font-family:Arial,sans-serif;color:#101828\">")
          .Append("<div style=\"max-width:560px;margin:24px auto;background:#fff;border-radius:12px;overflow:hidden;border:1px solid #e3e7f3\">")
          .Append("<div style=\"background:#2453d4;color:#fff;padding:18px 24px;font-size:18px;font-weight:bold\">")
          .Append(WebUtility.HtmlEncode(_options.FromName)).Append("</div><div style=\"padding:24px\">")
          .Append("<h1 style=\"font-size:20px;margin:0 0 16px\">").Append(WebUtility.HtmlEncode(subject)).Append("</h1>");
        foreach (var p in paragraphs)
            sb.Append("<p style=\"font-size:15px;line-height:1.5;margin:0 0 12px\">").Append(WebUtility.HtmlEncode(p)).Append("</p>");
        if (linkText != null && linkUrl != null)
            sb.Append("<p style=\"margin:20px 0\"><a href=\"").Append(WebUtility.HtmlEncode(linkUrl))
              .Append("\" style=\"background:#2453d4;color:#fff;text-decoration:none;padding:12px 20px;border-radius:8px;font-weight:bold;display:inline-block\">")
              .Append(WebUtility.HtmlEncode(linkText)).Append("</a></p>")
              .Append("<p style=\"font-size:12px;color:#6b7390;word-break:break-all\">Ако бутонът не работи, копирайте адреса: ")
              .Append(WebUtility.HtmlEncode(linkUrl)).Append("</p>");
        sb.Append("<p style=\"font-size:12px;color:#6b7390;margin-top:24px\">Това писмо е изпратено автоматично. Моля, не отговаряйте на него.</p>")
          .Append("</div></div></body></html>");
        return sb.ToString();
    }
}

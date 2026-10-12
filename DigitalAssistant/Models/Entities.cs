using System.ComponentModel.DataAnnotations;

namespace DigitalAssistant.Models;

// Категория на услугата (лични документи, адресна регистрация и т.н.)
public class Category
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Името е задължително.")]
    [StringLength(100, ErrorMessage = "Името трябва да е до 100 знака.")]
    [Display(Name = "Име")]
    public string Name { get; set; } = "";

    public List<Service> Services { get; set; } = new();
}

// Институция, която извършва услугата (община, НАП, МВР и т.н.)
public class Institution
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Името е задължително.")]
    [StringLength(150, ErrorMessage = "Името трябва да е до 150 знака.")]
    [Display(Name = "Име")]
    public string Name { get; set; } = "";

    public List<Service> Services { get; set; } = new();
}

// Офис на институция в даден град: точен адрес, телефон и работно време.
// Помощникът го показва, когато човекът е в този град. Ако за града няма офис, показва търсене в картата.
public class Office
{
    public int Id { get; set; }

    [Display(Name = "Институция")]
    public int InstitutionId { get; set; }
    public Institution? Institution { get; set; }

    [Display(Name = "Град")]
    [Required(ErrorMessage = "Изберете град.")]
    public string City { get; set; } = "";

    [Display(Name = "Адрес")]
    [Required(ErrorMessage = "Напишете адреса.")]
    [StringLength(200, ErrorMessage = "Адресът трябва да е до 200 знака.")]
    public string Address { get; set; } = "";

    [Display(Name = "Телефон")]
    [StringLength(50, ErrorMessage = "Телефонът трябва да е до 50 знака.")]
    public string Phone { get; set; } = "";

    [Display(Name = "Работно време")]
    [StringLength(100, ErrorMessage = "Работното време трябва да е до 100 знака.")]
    public string WorkingHours { get; set; } = "";
}

// Административна услуга (т. 3.1.1 от спецификацията)
public class Service
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    public int CategoryId { get; set; }
    public Category? Category { get; set; }

    public int InstitutionId { get; set; }
    public Institution? Institution { get; set; }

    public string RequiredDocuments { get; set; } = "";

    // Документи, които гражданинът не носи, защото служителят ги взима служебно от другата институция
    // през „Служебни справки“ (напр. „удостоверение за раждане (от общината)“). Разделени с „;“ или нов ред.
    public string OfficialDocuments { get; set; } = "";

    public string Steps { get; set; } = "";

    // Уточняващ въпрос, от чийто отговор зависят документите (напр. „Защо вадите нова лична карта?“).
    // Отговорите са по един на ред: „отговор | допълнителни документи, разделени с ; | бележка“.
    public string Question { get; set; } = "";
    public string QuestionOptions { get; set; } = "";

    // Място на подаване
    public string Address { get; set; } = "";
    public string WorkingHours { get; set; } = "";
    public string Phone { get; set; } = "";

    // Какво да се търси в картата, за да се намери най-близкото място за подаване
    // (напр. „Сектор Пътна полиция“). Празно, ако услугата не се заявява на място.
    public string PlaceSearch { get; set; } = "";

    // Такса в евро
    public decimal Fee { get; set; }
    public decimal? ExpressFee { get; set; }

    // Срок в работни дни
    public int DeadlineDays { get; set; }
    public int? ExpressDeadlineDays { get; set; }

    public string ResultDelivery { get; set; } = "";

    // Образец на заявлението (.pdf или .doc). Самият файл се зарежда само при изтегляне.
    public string? TemplateFileName { get; set; }
    public string? TemplateContentType { get; set; }
    public ServiceTemplate? Template { get; set; }

    // Ключови думи, разделени със запетая
    public string Keywords { get; set; } = "";

    // Гражданите могат да подадат заявление за услугата през сайта (т. 3.1.3)
    public bool AllowOnline { get; set; } = true;

    public bool IsActive { get; set; } = true;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public IEnumerable<string> KeywordList =>
        Keywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

// Зададен въпрос към помощника. Не се пазят лични данни.
public class Question
{
    public int Id { get; set; }
    public string Text { get; set; } = "";
    public DateTime AskedAt { get; set; } = DateTime.Now;

    // Намерената услуга (null, ако помощникът не е намерил отговор)
    public int? ServiceId { get; set; }
    public Service? Service { get; set; }

    // Брой намерени услуги
    public int FoundCount { get; set; }

    // Администраторът е прегледал въпроса без отговор
    public bool Reviewed { get; set; }

    // Отговор от изкуствения интелект, когато помощникът не е намерил услуга по ключовите думи
    public string? AiAnswer { get; set; }

    // Номерата на услугите, които изкуственият интелект е предложил, разделени със запетая
    public string AiServiceIds { get; set; } = "";

    public bool AnsweredByAi => AiAnswer != null;

    public IEnumerable<int> AiServiceIdList =>
        AiServiceIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse);
}

// Показано упътване от помощника (т. 3.1.2). Пази се само услугата, градът и начинът на подаване,
// без точното местоположение на човека.
public class GuideView
{
    public int Id { get; set; }
    public int ServiceId { get; set; }
    public Service? Service { get; set; }
    public string City { get; set; } = "";
    public bool Online { get; set; }
    public DateTime ShownAt { get; set; } = DateTime.Now;
}

// Запис в журнала на промените
public class AuditEntry
{
    public int Id { get; set; }
    public DateTime At { get; set; } = DateTime.Now;
    public string AdminUsername { get; set; } = "";
    public string Action { get; set; } = "";
    public string Details { get; set; } = "";
}

// Администратор (служител на центъра)
public class AdminUser
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string FullName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public int FailedAttempts { get; set; }
    public DateTime? LockedUntil { get; set; }

    // Главният администратор добавя и деактивира служители
    public bool IsMain { get; set; }
    public bool IsActive { get; set; } = true;

    // При първия вход служителят трябва да смени временната парола
    public bool MustChangePassword { get; set; }

    public DateTime? LastLoginAt { get; set; }

    // Институцията, в която работи служителят. Служебните справки до нея се показват на него.
    // Празно – служител на центъра, който вижда справките до всички институции.
    public int? InstitutionId { get; set; }
    public Institution? Institution { get; set; }

    // Градът на служителя: вижда само заявленията и справките от този град. Празно – всички градове.
    public string? City { get; set; }
}

// Служебна справка: служител, който обработва заявление, иска информация или документ от друга институция.
// Така гражданинът не обикаля институциите, а информацията идва в системата (т. 3.1.3).
public class InfoRequest
{
    public int Id { get; set; }
    public int ApplicationId { get; set; }
    public Application? Application { get; set; }

    // До коя институция е справката
    public int ToInstitutionId { get; set; }
    public Institution? ToInstitution { get; set; }

    // Какво се иска
    public string Question { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public string FromInstitution { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // Отговорът на другата институция (текст и по желание един файл)
    public string? Answer { get; set; }
    public string? AnsweredBy { get; set; }
    public DateTime? AnsweredAt { get; set; }
    public string? AnswerFileName { get; set; }
    public string? AnswerContentType { get; set; }
    public byte[]? AnswerFile { get; set; }

    public bool IsAnswered => AnsweredAt != null;
}

// Файлът с образеца на заявлението. Пази се в таблицата Services (колона TemplateFile),
// но е отделен обект, за да не се чете от базата при всяко търсене на услуги.
public class ServiceTemplate
{
    public int Id { get; set; }
    public byte[] File { get; set; } = Array.Empty<byte>();
}

// Регистриран гражданин, който подава заявления онлайн (т. 3.1.3)
public class Citizen
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";

    // ЕГН се пази шифровано. За проверката дали вече има профил с това ЕГН се пази и отпечатък (хеш).
    public string EgnProtected { get; set; } = "";
    public string EgnHash { get; set; } = "";

    public string PasswordHash { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int FailedAttempts { get; set; }
    public DateTime? LockedUntil { get; set; }

    // Гражданинът е потвърдил имейла си от връзката в писмото
    public bool EmailConfirmed { get; set; }

    // Получава писмо при всяка промяна по заявленията си
    public bool EmailNotifications { get; set; } = true;

    public List<Application> Applications { get; set; } = new();
}

// Еднократна връзка от имейл: потвърждение на имейла или нова парола.
// В базата се пази само отпечатъкът (хешът) на връзката, а не самата връзка.
public class CitizenToken
{
    public int Id { get; set; }
    public int CitizenId { get; set; }
    public Citizen? Citizen { get; set; }
    public string Purpose { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }

    public const string ConfirmEmail = "confirm";
    public const string ResetPassword = "reset";
}

// Статус на заявлението
public enum ApplicationStatus
{
    Submitted = 0,      // Подадено
    InProgress = 1,     // В обработка
    NeedsDocuments = 2, // Нужни са още документи
    Completed = 3,      // Готово
    Rejected = 4        // Отказано
}

// Заявление, подадено онлайн от гражданин
public class Application
{
    public int Id { get; set; }

    // Номер, който гражданинът вижда, напр. 2026-00012
    public string Number { get; set; } = "";

    public int CitizenId { get; set; }
    public Citizen? Citizen { get; set; }

    public int ServiceId { get; set; }
    public Service? Service { get; set; }

    // Какво иска гражданинът
    public string Details { get; set; } = "";

    // Как иска да получи резултата и адрес (при получаване по пощата)
    public string ResultDelivery { get; set; } = "";
    public string DeliveryAddress { get; set; } = "";

    // Таксата и срокът към деня на подаване. Ако после услугата се промени, за това заявление важат старите.
    public decimal Fee { get; set; }
    public int DeadlineDays { get; set; }

    // Заявена е бърза услуга
    public bool Express { get; set; }

    // Градът, в който се обработва заявлението (областният град, избран от гражданина)
    public string City { get; set; } = "";

    public ApplicationStatus Status { get; set; } = ApplicationStatus.Submitted;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    // Кога гражданинът и служителят за последно са отворили заявлението.
    // По тях се разбира дали има нови съобщения.
    public DateTime? CitizenSeenAt { get; set; }
    public DateTime? AdminSeenAt { get; set; }

    public List<ApplicationFile> Files { get; set; } = new();
    public List<ApplicationEvent> Events { get; set; } = new();
}

// Прикачен документ към заявлението. Пази се в базата, а не в публична папка.
public class ApplicationFile
{
    public int Id { get; set; }
    public int ApplicationId { get; set; }
    public Application? Application { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Size { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public DateTime UploadedAt { get; set; } = DateTime.Now;

    // Файл, изпратен от служителя (например готовият документ), а не качен от гражданина
    public bool ByStaff { get; set; }

    // Съобщението на служителя, към което е прикачен файлът
    public int? EventId { get; set; }
}

// Промяна по заявлението: смяна на статуса и съобщение до гражданина
public class ApplicationEvent
{
    public int Id { get; set; }
    public int ApplicationId { get; set; }
    public Application? Application { get; set; }
    public DateTime At { get; set; } = DateTime.Now;
    public ApplicationStatus Status { get; set; }
    public string Comment { get; set; } = "";
    public string Author { get; set; } = "";

    // Съобщението е от гражданина (а не от служителя)
    public bool ByCitizen { get; set; }
}

// Текстове и цветове за статусите
public static class ApplicationStatuses
{
    public static string Name(ApplicationStatus s) => s switch
    {
        ApplicationStatus.Submitted => "Подадено",
        ApplicationStatus.InProgress => "В обработка",
        ApplicationStatus.NeedsDocuments => "Нужни са още документи",
        ApplicationStatus.Completed => "Готово",
        ApplicationStatus.Rejected => "Отказано",
        _ => s.ToString()
    };

    public static string Css(ApplicationStatus s) => s switch
    {
        ApplicationStatus.Submitted => "status-new",
        ApplicationStatus.InProgress => "status-progress",
        ApplicationStatus.NeedsDocuments => "status-docs",
        ApplicationStatus.Completed => "status-done",
        _ => "status-rejected"
    };

    public static string Icon(ApplicationStatus s) => s switch
    {
        ApplicationStatus.Submitted => "bi-send",
        ApplicationStatus.InProgress => "bi-hourglass-split",
        ApplicationStatus.NeedsDocuments => "bi-exclamation-circle",
        ApplicationStatus.Completed => "bi-check-circle",
        _ => "bi-x-circle"
    };

    public static IEnumerable<ApplicationStatus> All => Enum.GetValues<ApplicationStatus>();

    // Очакваната дата, до която заявлението ще е готово: срокът на услугата в работни дни
    // от последното подаване (или от качването на поисканите документи). Не се брои при готови и отказани заявления.
    public static DateTime? ExpectedDate(Application app)
    {
        if (app.Status is ApplicationStatus.Completed or ApplicationStatus.Rejected or ApplicationStatus.NeedsDocuments)
            return null;
        // Брои се само преминаването към „Подадено“, а не съобщенията, писани докато заявлението е подадено
        var start = app.CreatedAt;
        ApplicationStatus? previous = null;
        foreach (var e in app.Events.OrderBy(e => e.At).ThenBy(e => e.Id))
        {
            if (e.Status == ApplicationStatus.Submitted && previous != ApplicationStatus.Submitted) start = e.At;
            previous = e.Status;
        }
        return DigitalAssistant.Services.WorkingDays.AddWorkingDays(start, app.DeadlineDays);
    }

    // Кога заявлението е получило сегашния си статус (съобщенията след това не се броят)
    public static DateTime StatusSince(Application app)
    {
        var since = app.UpdatedAt;
        ApplicationStatus? previous = null;
        foreach (var e in app.Events.OrderBy(e => e.At).ThenBy(e => e.Id))
        {
            if (e.Status == app.Status && previous != app.Status) since = e.At;
            previous = e.Status;
        }
        return since;
    }

    // Статусът, който заявлението е имало в даден момент (по историята му)
    public static ApplicationStatus StatusAt(Application app, DateTime at) =>
        app.Events.Where(e => e.At <= at).OrderBy(e => e.At).ThenBy(e => e.Id)
            .Select(e => (ApplicationStatus?)e.Status).LastOrDefault() ?? app.Status;
}

public class ErrorViewModel
{
    public string? RequestId { get; set; }
    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
}

// Отговор на служителя по заявление, насрочен за избрани дата и час.
// Докато не дойде часът, гражданинът не го вижда, а служителят може да го отмени.
public class ScheduledReply
{
    public int Id { get; set; }
    public int ApplicationId { get; set; }
    public Application? Application { get; set; }
    public ApplicationStatus Status { get; set; }
    public string Comment { get; set; } = "";
    public DateTime SendAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<ScheduledReplyFile> Files { get; set; } = new();
}

// Файл към насрочения отговор. Когато отговорът се изпрати, файлът отива при документите на заявлението.
public class ScheduledReplyFile
{
    public int Id { get; set; }
    public int ScheduledReplyId { get; set; }
    public ScheduledReply? Reply { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Size { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
}

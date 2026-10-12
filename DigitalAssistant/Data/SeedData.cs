using DigitalAssistant.Models;
using DigitalAssistant.Services;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using QuestPDF.Fluent;
using QuestPDF.Helpers;

namespace DigitalAssistant.Data;

// Примерни данни, за да може системата да се тества веднага.
// Таксите и сроковете са примерни и трябва да се сменят с данните от клиента.
public static class SeedData
{
    public const string DefaultAdminUsername = "admin";
    public const string DefaultAdminPassword = "Admin123!";

    // Примерните услуги, категории и институции са във файла Data/seed-data.json,
    // за да може лесно да се добавят нови, без да се променя кодът.
    public static async Task InitializeAsync(AppDbContext db, string contentRoot, PersonalDataProtector protector, string? adminPassword = null)
    {
        if (db.Categories.Any()) return;

        var json = await File.ReadAllTextAsync(Path.Combine(contentRoot, "Data", "seed-data.json"));
        var data = JsonSerializer.Deserialize<SeedFile>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var categories = data.Categories.ToDictionary(n => n, n => new Category { Name = n });
        var institutions = data.Institutions.ToDictionary(n => n, n => new Institution { Name = n });
        db.Categories.AddRange(categories.Values);
        db.Institutions.AddRange(institutions.Values);

        var services = data.Services.Select(s => new Service
        {
            Name = s.Name,
            Description = s.Description,
            Category = categories[s.Category],
            Institution = institutions[s.Institution],
            RequiredDocuments = s.RequiredDocuments,
            OfficialDocuments = s.Official,
            Question = s.Question,
            QuestionOptions = s.Situations,
            Steps = s.Steps,
            Address = s.Address,
            PlaceSearch = s.Place,
            WorkingHours = s.WorkingHours,
            Phone = s.Phone,
            Fee = s.Fee,
            ExpressFee = s.ExpressFee,
            DeadlineDays = s.DeadlineDays,
            ExpressDeadlineDays = s.ExpressDeadlineDays,
            ResultDelivery = s.ResultDelivery,
            Keywords = s.Keywords,
            AllowOnline = s.AllowOnline,
            // Образец на заявление за услугите, при които се попълва искане
            Template = s.Template ? new ServiceTemplate { File = SampleTemplate(s.Name) } : null,
            TemplateFileName = s.Template ? "obrazec-zayavlenie.pdf" : null,
            TemplateContentType = s.Template ? "application/pdf" : null
        }).ToList();
        db.Services.AddRange(services);

        // Примерните офиси са само в Русе. За другите градове ги добавя администраторът,
        // а дотогава помощникът показва най-близкото място в картата.
        db.Offices.AddRange(data.Offices.Select(o => new Office
        {
            Institution = institutions[o.Institution], City = o.City, Address = o.Address, Phone = o.Phone, WorkingHours = o.WorkingHours
        }));

        // Главният администратор. При първия вход системата иска да смени началната парола.
        var admin = new AdminUser { Username = DefaultAdminUsername, FullName = "Главен администратор", IsMain = true, MustChangePassword = true };
        admin.PasswordHash = new PasswordHasher<AdminUser>().HashPassword(admin, string.IsNullOrWhiteSpace(adminPassword) ? DefaultAdminPassword : adminPassword);
        db.AdminUsers.Add(admin);

        AuditService.Log(db, "система", "Начални данни", $"Добавени са {services.Count} примерни услуги.");
        await db.SaveChangesAsync();

        await AddSampleQuestionsAsync(db, data.SampleQuestions, data.UnansweredQuestions);
        if (data.DemoCitizen != null)
            await AddSampleApplicationsAsync(db, data.DemoCitizen, data.SampleApplications, protector);
    }

    // Примерен гражданин с няколко заявления в различен статус
    private static async Task AddSampleApplicationsAsync(AppDbContext db, SeedCitizen demo, List<SeedApplication> list, PersonalDataProtector protector)
    {
        var citizen = new Citizen
        {
            FullName = demo.FullName, Email = demo.Email, Phone = demo.Phone, CreatedAt = DateTime.Now.AddDays(-10),
            EgnProtected = protector.Protect(demo.Egn), EgnHash = protector.Hash(demo.Egn), EmailConfirmed = true
        };
        citizen.PasswordHash = new PasswordHasher<Citizen>().HashPassword(citizen, demo.Password);
        db.Citizens.Add(citizen);

        var apps = new List<Application>();
        foreach (var a in list)
        {
            var service = db.Services.Local.First(s => s.Name == a.Service);
            var created = DateTime.Now.AddDays(-a.DaysAgo).AddHours(a.DaysAgo == 0 ? -1 : -3);
            var app = new Application
            {
                Citizen = citizen,
                Service = service,
                Details = a.Details,
                ResultDelivery = a.Delivery,
                DeliveryAddress = a.Address ?? "",
                Fee = service.Fee,
                DeadlineDays = service.DeadlineDays,
                City = a.City ?? "Русе",
                CreatedAt = created,
                Files = a.Files.Select(f => new ApplicationFile
                {
                    FileName = f, ContentType = "application/pdf", UploadedAt = created,
                    Data = SampleDocument(f, service.Name)
                }).ToList()
            };
            foreach (var f in app.Files) f.Size = f.Data.Length;
            for (int i = 0; i < a.Events.Count; i++)
            {
                var e = a.Events[i];
                var at = i == 0 ? created : created.AddDays(i).AddHours(i);
                if (at > DateTime.Now) at = DateTime.Now;
                app.Events.Add(new ApplicationEvent
                {
                    At = at,
                    Status = Enum.Parse<ApplicationStatus>(e[0]),
                    Comment = e[1],
                    Author = e[2] == "citizen" ? citizen.FullName : DefaultAdminUsername,
                    ByCitizen = e[2] == "citizen"
                });
            }
            app.Status = app.Events.Last().Status;
            app.UpdatedAt = app.Events.Last().At;
            // Последното съобщение (искането на документи) още не е прочетено от гражданина
            app.CitizenSeenAt = app.Status == ApplicationStatus.NeedsDocuments ? app.Events[^2].At : app.UpdatedAt;
            app.AdminSeenAt = app.UpdatedAt;
            apps.Add(app);
        }
        db.Applications.AddRange(apps);
        await db.SaveChangesAsync();
        foreach (var app in apps) app.Number = $"{app.CreatedAt:yyyy}-{app.Id:D5}";

        // Примерна служебна справка: за удостоверението за наследници служителят проверява в районния съд
        // дали има отказ от наследство, вместо гражданинът да ходи там за справка
        var heirs = apps.FirstOrDefault(a => a.Service!.Name == "Удостоверение за наследници");
        var court = db.Institutions.Local.FirstOrDefault(i => i.Name == "Районен съд");
        if (heirs != null && court != null)
        {
            var asked = heirs.CreatedAt.AddMinutes(20);
            db.InfoRequests.Add(new InfoRequest
            {
                Application = heirs, ToInstitution = court, CreatedBy = DefaultAdminUsername, FromInstitution = InfoRequests.CenterName,
                Question = "Има ли вписан отказ от наследство след Петър Иванов Петров, починал през 2025 г.?", CreatedAt = asked,
                Answer = "Няма вписан отказ от наследство. Прилагаме справката.",
                AnsweredBy = "служител на районния съд", AnsweredAt = asked.AddMinutes(25) < DateTime.Now ? asked.AddMinutes(25) : DateTime.Now,
                AnswerFileName = "spravka-otkaz-nasledstvo.pdf", AnswerContentType = "application/pdf",
                AnswerFile = SampleDocument("spravka-otkaz-nasledstvo.pdf", heirs.Service!.Name)
            });
        }

        await db.SaveChangesAsync();
    }

    private static byte[] SampleDocument(string fileName, string serviceName)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(50);
                page.Content().Column(col =>
                {
                    col.Spacing(12);
                    col.Item().Text("ПРИМЕРЕН ДОКУМЕНТ").FontSize(18).Bold();
                    col.Item().Text($"Файл: {fileName}");
                    col.Item().Text($"Към заявление за услуга „{serviceName}“");
                });
            });
        }).GeneratePdf();
    }

    // Примерни въпроси за последните дни, за да има данни в справките
    private static async Task AddSampleQuestionsAsync(AppDbContext db, List<string> texts, List<string> unanswered)
    {
        var assistant = new AssistantService(db);
        var random = new Random(7);
        for (int day = 20; day >= 1; day--)
        {
            int count = random.Next(2, 7);
            for (int i = 0; i < count; i++)
            {
                var text = texts[random.Next(texts.Count)];
                var found = await assistant.FindAsync(text);
                db.Questions.Add(new Question
                {
                    Text = text,
                    AskedAt = DateTime.Today.AddDays(-day).AddHours(8 + random.Next(9)).AddMinutes(random.Next(60)),
                    ServiceId = found.FirstOrDefault()?.Id,
                    FoundCount = found.Count
                });
            }
        }
        for (int i = 0; i < unanswered.Count; i++)
        {
            db.Questions.Add(new Question
            {
                Text = unanswered[i],
                AskedAt = DateTime.Today.AddDays(-i * 3 - 1).AddHours(10 + i),
                FoundCount = 0
            });
        }
        await db.SaveChangesAsync();
    }

    private static byte[] SampleTemplate(string serviceName)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(50);
                page.DefaultTextStyle(x => x.FontSize(12));
                page.Content().Column(col =>
                {
                    col.Spacing(12);
                    col.Item().AlignRight().Text("ОБРАЗЕЦ").Bold().FontColor(Colors.Grey.Darken1);
                    col.Item().AlignCenter().Text("ЗАЯВЛЕНИЕ").FontSize(18).Bold();
                    col.Item().AlignCenter().Text($"за услуга „{serviceName}“");
                    col.Item().PaddingTop(20).Text("Име, презиме и фамилия: ............................................................................");
                    col.Item().Text("ЕГН: ...........................................");
                    col.Item().Text("Адрес: ...........................................................................................................");
                    col.Item().Text("Телефон: ...........................................");
                    col.Item().PaddingTop(10).Text("Желая да ми бъде извършена горната услуга.");
                    col.Item().Text("Желая да получа резултата:  □ на място   □ по пощата   □ по електронен път");
                    col.Item().PaddingTop(30).Row(r =>
                    {
                        r.RelativeItem().Text("Дата: ..................");
                        r.RelativeItem().AlignRight().Text("Подпис: ..................");
                    });
                });
            });
        }).GeneratePdf();
    }

    // Структура на файла seed-data.json
    private class SeedFile
    {
        public List<string> Categories { get; set; } = new();
        public List<string> Institutions { get; set; } = new();
        public List<SeedService> Services { get; set; } = new();
        public List<SeedOffice> Offices { get; set; } = new();
        public List<string> SampleQuestions { get; set; } = new();
        public List<string> UnansweredQuestions { get; set; } = new();
        public SeedCitizen? DemoCitizen { get; set; }
        public List<SeedApplication> SampleApplications { get; set; } = new();
    }

    private class SeedCitizen
    {
        public string FullName { get; set; } = "";
        public string Email { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Egn { get; set; } = "";
        public string Password { get; set; } = "";
    }

    private class SeedOffice
    {
        public string Institution { get; set; } = "";
        public string City { get; set; } = "";
        public string Address { get; set; } = "";
        public string Phone { get; set; } = "";
        public string WorkingHours { get; set; } = "";
    }

    private class SeedApplication
    {
        public string? City { get; set; }
        public string Service { get; set; } = "";
        public int DaysAgo { get; set; }
        public string Delivery { get; set; } = "";
        public string? Address { get; set; }
        public string Details { get; set; } = "";
        public List<string> Files { get; set; } = new();
        public List<List<string>> Events { get; set; } = new();
    }

    private class SeedService
    {
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public string Institution { get; set; } = "";
        public string Address { get; set; } = "";
        public string Place { get; set; } = "";
        public string WorkingHours { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Description { get; set; } = "";
        public string RequiredDocuments { get; set; } = "";
        public string Official { get; set; } = "";
        public string Question { get; set; } = "";
        public string Situations { get; set; } = "";
        public string Steps { get; set; } = "";
        public decimal Fee { get; set; }
        public decimal? ExpressFee { get; set; }
        public int DeadlineDays { get; set; }
        public int? ExpressDeadlineDays { get; set; }
        public string ResultDelivery { get; set; } = "";
        public string Keywords { get; set; } = "";
        public bool Template { get; set; }
        public bool AllowOnline { get; set; } = true;
    }
}

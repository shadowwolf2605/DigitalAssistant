using System.Text.RegularExpressions;
using DigitalAssistant.Data;
using DigitalAssistant.Models;
using DigitalAssistant.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DigitalAssistant.Controllers;

// Дигитален помощник (т. 3.1.2). Задава въпроси един след друг и накрая показва подробно упътване:
// какви документи да подготви човекът, къде да отиде, как да стигне, какво да направи на място и кога е готово.
// Въпросите и отговорите се взимат от данните за услугите в базата, затова нова услуга се появява веднага.
public class AssistantController : Controller
{
    private readonly AssistantService _assistant;
    private readonly AiAssistant _ai;
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;

    public AssistantController(AssistantService assistant, AiAssistant ai, AppDbContext db, IMemoryCache cache)
    {
        _cache = cache;
        _assistant = assistant;
        _ai = ai;
        _db = db;
    }

    [EnableRateLimiting("guide")]
    public async Task<IActionResult> Index(GuideState state, bool partial = false)
    {
        // Ако е търсил с думи и още не е избрал услуга, се показват резултатите от търсенето
        if (!string.IsNullOrWhiteSpace(state.Q) && state.S == null)
        {
            var route = state.Route();
            route["again"] = "true";
            if (partial) route["partial"] = "true";
            return RedirectToAction(nameof(Search), route);
        }

        var model = await BuildAsync(state);
        if (model == null)
        {
            TempData["Error"] = "Тази услуга вече не е налична. Започнете отначало.";
            return partial ? RedirectToAction(nameof(Index), new { partial = true }) : RedirectToAction(nameof(Index));
        }
        // Упътването се брои веднъж на човек за 30 минути (не при всяко презареждане или „Назад“)
        var viewKey = $"guide:{HttpContext.Connection.RemoteIpAddress}:{model.Service?.Id}:{model.CityName}:{state.IsOnline}";
        if (model.Step == GuideStep.Guide && !_cache.TryGetValue(viewKey, out _))
        {
            _cache.Set(viewKey, true, TimeSpan.FromMinutes(30));
            _db.GuideViews.Add(new GuideView { ServiceId = model.Service!.Id, City = model.CityName ?? "", Online = state.IsOnline });
            await _db.SaveChangesAsync();
        }
        return Show(model, partial);
    }

    // Търсене с думи – за хората, които не знаят в коя тема е услугата
    [EnableRateLimiting("assistant")]
    public async Task<IActionResult> Search(string? q, bool again = false, bool partial = false)
    {
        var question = (q ?? "").Trim();
        if (question.Length == 0) return RedirectToAction(nameof(Index));

        var model = new GuideViewModel
        {
            Step = GuideStep.Search,
            State = new GuideState { Q = question },
            Number = 1,
            Total = 5,
            Back = new Dictionary<string, string>(),
            Question = $"Резултати за „{question}“"
        };
        if (question.Length > AssistantService.MaxQuestionLength)
        {
            model.State.Q = question[..AssistantService.MaxQuestionLength];
            model.Question = "Търсене с думи";
            model.Error = $"Въпросът трябва да е до {AssistantService.MaxQuestionLength} знака.";
            return Show(model, partial);
        }

        var found = await _assistant.FindAsync(question);
        var byKeywords = found;
        AiAnswer? ai = null;
        if (found.Count == 0 && _ai.IsAvailable)
        {
            var services = await _db.Services.Include(s => s.Institution).Include(s => s.Category)
                .Where(s => s.IsActive).ToListAsync();
            ai = await _ai.AskAsync(question, services, HttpContext.RequestAborted);
            if (ai != null)
            {
                model.AiAnswer = ai.Text;
                found = ai.ServiceIds.Select(id => services.First(s => s.Id == id)).ToList();
            }
        }
        // При връщане назад въпросът вече е записан
        if (!again) await _assistant.LogAsync(question, byKeywords, ai);

        if (found.Count == 0)
        {
            model.Hint = "Не намерих такава услуга. Опитайте с други думи или изберете тема.";
            model.ClaudeUrl = _ai.ClaudeLinkFor(question);
        }
        else
        {
            model.Hint = found.Count == 1 ? "Това ли търсите?" : "Изберете услугата, която ви трябва:";
            model.Options = found.Select(s => ServiceOption(s, model.State)).ToList();
        }
        return Show(model, partial);
    }

    private IActionResult Show(GuideViewModel model, bool partial)
    {
        Response.Headers.Vary = "X-Guide";
        if (partial || Request.Headers.ContainsKey("X-Guide"))
        {
            Response.Headers.CacheControl = "no-store";
            return PartialView("_Guide", model);
        }
        return View("Index", model);
    }

    // Определя коя е следващата стъпка според отговорите дотук
    private async Task<GuideViewModel?> BuildAsync(GuideState st)
    {
        var model = new GuideViewModel { State = st };
        if (st.How is not (GuideState.Online or GuideState.Place)) st.How = null;
        if (st.Speed is not (GuideState.Express or GuideState.Normal)) st.Speed = null;
        if (st.Have.Count > 0) st.D = true;
        CheckLocation(st, model);

        Service? service = null;
        if (st.S != null)
        {
            service = await _db.Services.Include(s => s.Category).Include(s => s.Institution)
                .FirstOrDefaultAsync(s => s.Id == st.S && s.IsActive);
            if (service == null) return null;
            if (string.IsNullOrEmpty(st.Q)) st.C = service.CategoryId;
            model.Service = service;
            model.Category = service.Category;
            model.Documents = ServiceText.Documents(service.RequiredDocuments);
            model.Situations = string.IsNullOrWhiteSpace(service.Question) ? new() : ServiceText.Situations(service.QuestionOptions);
            if (model.Situations.Count < 2) model.Situations.Clear();
            if (st.O != null && (st.O < 0 || st.O >= model.Situations.Count)) st.O = null;
            if (st.O != null)
            {
                // Документите според отговора се добавят към общите
                model.Situation = model.Situations[st.O.Value];
                model.Documents.AddRange(model.Situation.Documents.Where(d => !model.Documents.Contains(d)));
            }
            if (!HasExpress(service)) st.Speed = null;
            if (!service.AllowOnline) st.How = null;
        }
        else if (st.C != null)
        {
            st.O = null;
            model.Category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == st.C);
            if (model.Category == null) st.C = null;
        }

        // Стъпките, които важат за тази услуга, и параметърът, с който се отговаря на всяка
        var steps = new List<(GuideStep Step, string Param)>();
        if (!string.IsNullOrEmpty(st.Q)) steps.Add((GuideStep.Search, "s"));
        else { steps.Add((GuideStep.Category, "c")); steps.Add((GuideStep.Service, "s")); }
        if (model.Situations.Count > 0) steps.Add((GuideStep.Situation, "o"));
        if (service == null || service.AllowOnline) steps.Add((GuideStep.How, "how"));
        if (service == null || HasExpress(service)) steps.Add((GuideStep.Speed, "speed"));
        if (service == null || model.Documents.Count > 0) steps.Add((GuideStep.Documents, "d"));
        if (service == null || (!string.IsNullOrWhiteSpace(service.PlaceSearch) && !st.IsOnline)) steps.Add((GuideStep.Location, "loc"));

        bool Answered(GuideStep step) => step switch
        {
            GuideStep.Category => st.C != null,
            GuideStep.Service or GuideStep.Search => service != null,
            GuideStep.Situation => st.O != null,
            GuideStep.How => st.How != null,
            GuideStep.Speed => st.Speed != null,
            GuideStep.Documents => st.D,
            GuideStep.Location => st.HasLocation,
            _ => false
        };

        var index = steps.FindIndex(x => !Answered(x.Step));
        model.Step = index < 0 ? GuideStep.Guide : steps[index].Step;
        model.Total = steps.Count;
        model.Number = index < 0 ? steps.Count : index + 1;

        // „Назад“ премахва отговора на предишния въпрос
        var previous = index < 0 ? steps.Count - 1 : index - 1;
        if (previous >= 0) model.Back = st.Route(steps[previous].Param);
        if (previous == 0 && steps[0].Step == GuideStep.Search)
        {
            model.Back = st.Route("s", "o", "how", "speed", "d", "h", "loc");
            model.Back["again"] = "true";
        }

        switch (model.Step)
        {
            case GuideStep.Category:
                await CategoryStepAsync(model);
                break;
            case GuideStep.Service:
                await ServiceStepAsync(model);
                break;
            case GuideStep.Situation:
                SituationStep(model, service!);
                break;
            case GuideStep.How:
                HowStep(model, service!);
                break;
            case GuideStep.Speed:
                SpeedStep(model, service!);
                break;
            case GuideStep.Documents:
                model.Question = "Кои от тези документи вече имате?";
                model.Hint = "Отбележете ги. В упътването ще ви напомним за тези, които ви липсват.";
                break;
            case GuideStep.Location:
                model.Question = "Къде се намирате?";
                model.Hint = "Ще ви покажем най-близкото място за услугата и как да стигнете до него.";
                break;
        }
        return model;
    }

    private static bool HasExpress(Service s) => s.ExpressDeadlineDays != null || s.ExpressFee != null;

    private async Task CategoryStepAsync(GuideViewModel model)
    {
        model.Question = "За какво ви е нужна помощ?";
        model.Hint = "Изберете темата. След това ще ви зададем още няколко кратки въпроса.";
        var categories = await _db.Categories
            .Select(c => new { c.Id, c.Name, Count = c.Services.Count(s => s.IsActive) })
            .Where(c => c.Count > 0).OrderBy(c => c.Name).ToListAsync();
        model.Options = categories.Select(c => new GuideOption
        {
            Title = c.Name,
            Text = $"{c.Count} {(c.Count == 1 ? "услуга" : "услуги")}",
            Icon = CategoryStyle.Icon(c.Name),
            Tone = CategoryStyle.Tone(c.Id),
            Route = new Dictionary<string, string> { ["c"] = c.Id.ToString() }
        }).ToList();
    }

    private async Task ServiceStepAsync(GuideViewModel model)
    {
        model.Question = "Какво точно ви трябва?";
        model.Hint = model.Category!.Name;
        var services = await _db.Services.Include(s => s.Institution)
            .Where(s => s.IsActive && s.CategoryId == model.Category.Id).OrderBy(s => s.Name).ToListAsync();
        model.Options = services.Select(s => ServiceOption(s, model.State)).ToList();
    }

    private static GuideOption ServiceOption(Service s, GuideState st)
    {
        var route = st.Route("o", "how", "speed", "d", "h", "loc");
        route["s"] = s.Id.ToString();
        var option = new GuideOption
        {
            Title = s.Name,
            Text = FirstSentence(s.Description),
            Icon = "bi-file-earmark-text",
            Route = route
        };
        option.Badges.Add(s.Fee == 0 ? "безплатно" : $"{s.Fee:0.00} €");
        option.Badges.Add(WorkingDays.Text(s.DeadlineDays));
        if (s.AllowOnline) option.Badges.Add("и онлайн");
        return option;
    }

    private static void SituationStep(GuideViewModel model, Service s)
    {
        model.Question = s.Question;
        model.Hint = "От отговора зависи какви документи ще ви трябват.";
        model.Options = model.Situations.Select((x, i) =>
        {
            var route = model.State.Route("d", "h");
            route["o"] = i.ToString();
            var text = x.Documents.Count > 0 ? "Ще ви трябва още: " + string.Join(", ", x.Documents) + "." : null;
            return new GuideOption { Title = x.Answer, Text = text, Icon = "bi-signpost-split", Route = route };
        }).ToList();
    }

    private static void HowStep(GuideViewModel model, Service s)
    {
        model.Question = "Как искате да заявите услугата?";
        model.Hint = s.Name;
        var online = model.State.Route();
        online["how"] = GuideState.Online;
        var place = model.State.Route();
        place["how"] = GuideState.Place;
        model.Options = new()
        {
            new GuideOption { Title = "Онлайн, през този сайт", Icon = "bi-laptop", Route = online,
                Text = "Без опашка. Нужен е профил в сайта. Прикачвате документите като снимки или PDF и следите какво става със заявлението." },
            new GuideOption { Title = "Лично, на гишето", Icon = "bi-person-walking", Route = place,
                Text = $"Отивате с документите в {s.Institution?.Name ?? "институцията"}. Ще ви покажем къде е и как да стигнете." }
        };
    }

    private static void SpeedStep(GuideViewModel model, Service s)
    {
        model.Question = "Колко бързо ви трябва?";
        model.Hint = s.Name;
        var today = DateTime.Today;
        GuideOption Option(string title, string speed, decimal fee, int days, string icon)
        {
            var route = model.State.Route();
            route["speed"] = speed;
            return new GuideOption
            {
                Title = title,
                Icon = icon,
                Route = route,
                Text = $"{(fee == 0 ? "Безплатно" : $"Такса {fee:0.00} €")} · {WorkingDays.Text(days)} · " +
                       $"готово около {WorkingDays.AddWorkingDays(today, days):dd.MM.yyyy}"
            };
        }
        model.Options = new()
        {
            Option("Обикновена услуга", GuideState.Normal, s.Fee, s.DeadlineDays, "bi-hourglass-split"),
            Option("Бърза услуга", GuideState.Express, s.ExpressFee ?? s.Fee, s.ExpressDeadlineDays ?? s.DeadlineDays, "bi-lightning-charge")
        };
    }

    // Проверява града или координатите, които човекът е дал
    private static void CheckLocation(GuideState st, GuideViewModel model)
    {
        st.City = st.City?.Trim();
        if (string.IsNullOrEmpty(st.City)) st.City = null;
        if (st.City != null && (st.City.Length > 60 || !Regex.IsMatch(st.City, @"^\p{L}[\p{L} .\-]*$")))
        {
            model.Error = "Въведете името на населеното място с букви, например „Пловдив“.";
            st.City = null;
        }
        if (st.City != null) st.City = GuideCities.Normalize(st.City);
        if (st.Lat != null && st.Lng != null)
        {
            if (!GuideCities.InBulgaria(st.Lat.Value, st.Lng.Value))
            {
                model.Error = "Местоположението ви изглежда извън България. Изберете град от списъка.";
                st.Lat = st.Lng = null;
            }
            else
            {
                // Точното място не се пази – закръгля се до около 100 метра
                st.Lat = Math.Round(st.Lat.Value, 3);
                st.Lng = Math.Round(st.Lng.Value, 3);
                model.NearestCity = GuideCities.Nearest(st.Lat.Value, st.Lng.Value);
            }
        }
        else st.Lat = st.Lng = null;
    }

    private static string FirstSentence(string? text)
    {
        var match = Regex.Match(text ?? "", @"^.*?[.!?](?=\s|$)");
        var sentence = match.Success ? match.Value : text ?? "";
        return sentence.Length > 160 ? sentence[..157] + "…" : sentence;
    }
}

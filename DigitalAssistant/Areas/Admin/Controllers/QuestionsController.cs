using DigitalAssistant.Data;
using DigitalAssistant.Models;
using DigitalAssistant.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace DigitalAssistant.Areas.Admin.Controllers;

// Преглед на въпросите без отговор и добавяне на ключови думи (т. 3.1.2)
public class QuestionsController : AdminController
{
    private readonly AppDbContext _db;

    public QuestionsController(AppDbContext db) => _db = db;

    public async Task<IActionResult> Index(bool all = false)
    {
        var query = _db.Questions.Where(q => q.FoundCount == 0);
        if (!all) query = query.Where(q => !q.Reviewed);
        ViewBag.All = all;
        return View(await query.OrderByDescending(q => q.AskedAt).ToListAsync());
    }

    public async Task<IActionResult> Review(int id)
    {
        var question = await _db.Questions.FindAsync(id);
        if (question == null) return NotFound();

        // Предлагаме думите от въпроса като нови ключови думи
        var model = new QuestionReviewModel
        {
            QuestionId = id,
            Keywords = string.Join(", ", AssistantService.Words(question.Text))
        };
        // Ако изкуственият интелект е предложил услуга, тя е избрана предварително
        model.ServiceId = question.AiServiceIdList.Cast<int?>().FirstOrDefault();
        await FillAsync(question, model.ServiceId);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(QuestionReviewModel model)
    {
        var question = await _db.Questions.FindAsync(model.QuestionId);
        if (question == null) return NotFound();

        var service = model.ServiceId == null ? null : await _db.Services.FindAsync(model.ServiceId);
        if (model.ServiceId != null && service == null)
            ModelState.AddModelError(nameof(model.ServiceId), "Изберете услуга.");

        var newWords = (model.Keywords ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        if (!string.IsNullOrWhiteSpace(model.Keywords) && newWords.Count == 0)
            ModelState.AddModelError(nameof(model.Keywords), "Въведете поне една ключова дума.");

        if (!ModelState.IsValid)
        {
            await FillAsync(question, model.ServiceId);
            return View(model);
        }

        var existing = service!.KeywordList.ToList();
        var added = newWords.Where(w => !existing.Contains(w, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        service.Keywords = string.Join(", ", existing.Concat(added));
        service.UpdatedAt = DateTime.Now;
        question.Reviewed = true;

        AuditService.Log(_db, AdminName, "Добавени ключови думи",
            $"{service.Name}: {(added.Count > 0 ? string.Join(", ", added) : "няма нови думи")} (от въпрос „{question.Text}“)");
        await _db.SaveChangesAsync();

        Success(added.Count > 0
            ? $"Към „{service.Name}“ са добавени ключовите думи: {string.Join(", ", added)}."
            : $"Всички думи вече бяха ключови думи на „{service.Name}“. Въпросът е отбелязан като прегледан.");
        return RedirectToAction(nameof(Index));
    }

    // Въпросът няма връзка с услуга (напр. не е за административна услуга)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Dismiss(int id)
    {
        var question = await _db.Questions.FindAsync(id);
        if (question == null) return NotFound();
        question.Reviewed = true;
        AuditService.Log(_db, AdminName, "Прегледан въпрос", $"„{question.Text}“");
        await _db.SaveChangesAsync();
        Success("Въпросът е отбелязан като прегледан.");
        return RedirectToAction(nameof(Index));
    }

    private async Task FillAsync(Question question, int? serviceId)
    {
        ViewBag.Question = question;
        var services = await _db.Services.Include(s => s.Institution).Where(s => s.IsActive)
            .OrderBy(s => s.Name).ToListAsync();
        ViewBag.Services = new SelectList(services.Select(s => new { s.Id, Name = $"{s.Name} ({s.Institution!.Name})" }),
            "Id", "Name", serviceId);
    }
}

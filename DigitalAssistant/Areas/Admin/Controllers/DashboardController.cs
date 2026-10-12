using DigitalAssistant.Data;
using DigitalAssistant.Models;
using DigitalAssistant.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DigitalAssistant.Areas.Admin.Controllers;

// Табло: след вход администраторът вижда новите заявления и въпросите без отговор (т. 3.1.5)
public class DashboardController : AdminController
{
    private readonly AppDbContext _db;

    public DashboardController(AppDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        ViewBag.NewQuestions = await _db.Questions
            .Where(q => q.FoundCount == 0 && !q.Reviewed)
            .OrderByDescending(q => q.AskedAt)
            .ToListAsync();
        ViewBag.NewApplications = await _db.Applications.Include(a => a.Citizen).Include(a => a.Service)
            .Where(a => a.Status == ApplicationStatus.Submitted)
            .OrderByDescending(a => a.UpdatedAt)
            .ToListAsync();
        ViewBag.WaitingDocuments = await _db.Applications.CountAsync(a => a.Status == ApplicationStatus.NeedsDocuments);
        ViewBag.NewMessages = await UnreadMessages.ForStaff(_db).Include(a => a.Citizen).OrderByDescending(a => a.UpdatedAt).ToListAsync();
        ViewBag.AiAnswered = await _db.Questions.CountAsync(q => q.AiAnswer != null && q.AskedAt >= DateTime.Today.AddDays(-30));
        // Заявления, чийто очакван срок вече е минал
        var open = await _db.Applications.Include(a => a.Service).Include(a => a.Events)
            .Where(a => a.Status == ApplicationStatus.Submitted || a.Status == ApplicationStatus.InProgress).ToListAsync();
        ViewBag.Overdue = open.Count(a => ApplicationStatuses.ExpectedDate(a) < DateTime.Today);
        ViewBag.ActiveServices = await _db.Services.CountAsync(s => s.IsActive);
        ViewBag.InactiveServices = await _db.Services.CountAsync(s => !s.IsActive);
        ViewBag.QuestionsToday = await _db.Questions.CountAsync(q => q.AskedAt >= DateTime.Today);
        ViewBag.QuestionsMonth = await _db.Questions.CountAsync(q => q.AskedAt >= DateTime.Today.AddDays(-30));

        // Брой въпроси по дни за последните 14 дни (за графиката)
        var from = DateTime.Today.AddDays(-13);
        var dates = await _db.Questions.Where(q => q.AskedAt >= from).Select(q => q.AskedAt).ToListAsync();
        ViewBag.Chart = Enumerable.Range(0, 14)
            .Select(i => from.AddDays(i))
            .Select(d => (Day: d, Count: dates.Count(x => x.Date == d)))
            .ToList();
        return View();
    }
}

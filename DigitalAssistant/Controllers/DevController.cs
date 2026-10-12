using DigitalAssistant.Services;
using Microsoft.AspNetCore.Mvc;

namespace DigitalAssistant.Controllers;

// „Тестова поща“ – само при тестване на компютъра (Development), когато няма настроен пощенски сървър.
// Показва писмата, които системата би изпратила, за да може да се отвори връзката за потвърждение и др.
public class DevController : Controller
{
    private readonly EmailService _email;
    private readonly IWebHostEnvironment _env;

    public DevController(EmailService email, IWebHostEnvironment env)
    {
        _email = email;
        _env = env;
    }

    private bool Enabled => _env.IsDevelopment() && !_email.UsesSmtp;

    [HttpGet("/Dev/Mail")]
    public IActionResult Mail()
    {
        if (!Enabled) return NotFound();
        var files = Directory.Exists(_email.Folder)
            ? new DirectoryInfo(_email.Folder).GetFiles("*.html").OrderByDescending(f => f.Name).Take(100).ToList()
            : new List<FileInfo>();
        var list = files.Select(f =>
        {
            var lines = System.IO.File.ReadLines(f.FullName).Take(2).ToList();
            string Header(int i, string name) => lines.Count > i ? System.Net.WebUtility.HtmlDecode(lines[i].Replace($"<!-- {name}: ", "").Replace(" -->", "")) : "";
            return (Name: f.Name, To: Header(0, "to"), Subject: Header(1, "subject"), At: f.CreationTime);
        }).ToList();
        return View(list);
    }

    [HttpGet("/Dev/Mail/{name}")]
    public IActionResult Message(string name)
    {
        if (!Enabled) return NotFound();
        var file = Path.Combine(_email.Folder, Path.GetFileName(name));
        if (!name.EndsWith(".html") || !System.IO.File.Exists(file)) return NotFound();
        return PhysicalFile(file, "text/html; charset=utf-8");
    }
}

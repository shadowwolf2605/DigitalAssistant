using DigitalAssistant.Data;
using Microsoft.EntityFrameworkCore;

namespace DigitalAssistant.Services;

// Всеки ден автоматично прави резервно копие на базата данни (т. 3.2.4).
// Копията се пазят в папка App_Data/Backups, като остават последните 14. Копират се и ключовете за шифроване.
public class BackupService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<BackupService> _logger;
    private readonly string _folder;

    public BackupService(IServiceProvider services, ILogger<BackupService> logger, IWebHostEnvironment env)
    {
        _services = services;
        _logger = logger;
        _folder = Path.Combine(env.ContentRootPath, "App_Data", "Backups");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await BackupIfNeededAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Резервното копие не беше направено.");
            }
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    private async Task BackupIfNeededAsync()
    {
        using (var check = _services.CreateScope())
        {
            // PostgreSQL (напр. Neon) прави резервните копия сам
            if (!check.ServiceProvider.GetRequiredService<AppDbContext>().Database.IsSqlite()) return;
        }
        Directory.CreateDirectory(_folder);
        var file = Path.Combine(_folder, $"digital-assistant-{DateTime.Today:yyyy-MM-dd}.db");
        if (File.Exists(file)) return;

        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // VACUUM INTO прави цяло и правилно копие, дори докато системата работи
        await db.Database.ExecuteSqlRawAsync("VACUUM INTO {0}", file);
        _logger.LogInformation("Направено е резервно копие: {File}", file);

        foreach (var old in Directory.GetFiles(_folder, "digital-assistant-*.db").OrderByDescending(f => f).Skip(14))
            File.Delete(old);

        // Без ключовете шифрованите данни (ЕГН) в копието не могат да се прочетат, затова се копират и те
        var keys = Path.Combine(Path.GetDirectoryName(_folder)!, "Keys");
        if (Directory.Exists(keys))
        {
            var target = Path.Combine(_folder, "Keys");
            Directory.CreateDirectory(target);
            foreach (var key in Directory.GetFiles(keys))
                File.Copy(key, Path.Combine(target, Path.GetFileName(key)), overwrite: true);
        }
    }
}

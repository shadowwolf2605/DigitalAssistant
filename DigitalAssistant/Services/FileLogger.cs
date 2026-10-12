using System.Collections.Concurrent;
using System.Text;

namespace DigitalAssistant.Services;

// Записва съобщенията и грешките на системата във файл за всеки ден: App_Data/Logs/log-гггг-мм-дд.txt.
// Пазят се файловете от последните 30 дни.
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _folder;
    private readonly object _lock = new();
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();

    public FileLoggerProvider(string folder)
    {
        _folder = folder;
        Directory.CreateDirectory(folder);
        foreach (var old in Directory.GetFiles(folder, "log-*.txt").OrderByDescending(f => f).Skip(30))
            File.Delete(old);
    }

    public ILogger CreateLogger(string categoryName) => _loggers.GetOrAdd(categoryName, name => new FileLogger(this, name));

    internal void Write(string line)
    {
        lock (_lock)
        {
            File.AppendAllText(Path.Combine(_folder, $"log-{DateTime.Today:yyyy-MM-dd}.txt"), line, Encoding.UTF8);
        }
    }

    public void Dispose() { }

    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _provider;
        private readonly string _category;

        public FileLogger(FileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        // Във файла се пишат предупрежденията и грешките, както и съобщенията на самото приложение
        public bool IsEnabled(LogLevel level) =>
            level >= LogLevel.Warning || (level >= LogLevel.Information && _category.StartsWith("DigitalAssistant"));

        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            var sb = new StringBuilder();
            sb.Append($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {_category}: {formatter(state, exception)}");
            if (exception != null) sb.Append(Environment.NewLine).Append(exception);
            sb.Append(Environment.NewLine);
            try { _provider.Write(sb.ToString()); } catch (IOException) { }
        }
    }
}

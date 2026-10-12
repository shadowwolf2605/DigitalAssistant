using Microsoft.Extensions.Caching.Memory;

namespace DigitalAssistant.Services;

// Грешни опити за вход с имейл или потребителско име, които ги няма в базата (т. 3.2.5).
// Броят се в паметта точно както при истински профил, за да не може по съобщението
// да се разбере дали такъв профил съществува.
public static class UnknownLogins
{
    private sealed class Counter
    {
        public int FailedAttempts;
        public DateTime? LockedUntil;
    }

    // Отбелязва грешен опит и връща текста на грешката
    public static string Fail(IMemoryCache cache, string key, int maxAttempts, TimeSpan lockTime, string wrongText)
    {
        // При истински профил броячът се нулира чак при успешен вход
        var counter = cache.GetOrCreate("login:" + key, e =>
        {
            e.SlidingExpiration = TimeSpan.FromDays(1);
            return new Counter();
        })!;
        lock (counter)
        {
            if (counter.LockedUntil > DateTime.Now)
            {
                var left = (int)Math.Ceiling((counter.LockedUntil.Value - DateTime.Now).TotalMinutes);
                return $"Профилът е заключен след {maxAttempts} грешни опита. Опитайте отново след {left} мин.";
            }
            if (++counter.FailedAttempts >= maxAttempts)
            {
                counter.LockedUntil = DateTime.Now.Add(lockTime);
                counter.FailedAttempts = 0;
                return $"Профилът е заключен за {lockTime.TotalMinutes} минути след {maxAttempts} грешни опита.";
            }
            return $"{wrongText} {AttemptsLeft(maxAttempts - counter.FailedAttempts)}";
        }
    }

    public static string AttemptsLeft(int n) => n == 1 ? "Остава 1 опит." : $"Остават {n} опита.";
}

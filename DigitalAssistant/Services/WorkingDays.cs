namespace DigitalAssistant.Services;

// Работни дни в България: без събота, неделя и официалните празници.
// Използва се за очаквания срок на заявленията.
public static class WorkingDays
{
    // „1 работен ден“, „5 работни дни“
    public static string Text(int days) => $"{days} {(days == 1 ? "работен ден" : "работни дни")}";

    // Празници с постоянна дата (ден, месец)
    private static readonly (int Day, int Month)[] Fixed =
    {
        (1, 1), (3, 3), (1, 5), (6, 5), (24, 5), (6, 9), (22, 9), (24, 12), (25, 12), (26, 12)
    };

    private static readonly Dictionary<int, HashSet<DateTime>> Cache = new();

    public static HashSet<DateTime> Holidays(int year)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(year, out var cached)) return cached;

            var days = new HashSet<DateTime>();
            // Великден: от Разпети петък до понеделник след Великден
            var easter = OrthodoxEaster(year);
            for (int i = -2; i <= 1; i++) days.Add(easter.AddDays(i));

            // Ако празник с постоянна дата се падне в събота или неделя, следващият работен ден е почивен
            foreach (var (day, month) in Fixed) days.Add(new DateTime(year, month, day));
            foreach (var (day, month) in Fixed)
            {
                var d = new DateTime(year, month, day);
                if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                {
                    var next = d.AddDays(1);
                    while (next.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || days.Contains(next)) next = next.AddDays(1);
                    days.Add(next);
                }
            }
            Cache[year] = days;
            return days;
        }
    }

    public static bool IsWorkingDay(DateTime date) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !Holidays(date.Year).Contains(date.Date);

    // Датата след даден брой работни дни (броенето започва от следващия ден)
    public static DateTime AddWorkingDays(DateTime start, int count)
    {
        var date = start.Date;
        while (count > 0)
        {
            date = date.AddDays(1);
            if (IsWorkingDay(date)) count--;
        }
        return date;
    }

    // Православен Великден (по юлианския календар + 13 дни)
    public static DateTime OrthodoxEaster(int year)
    {
        int a = year % 4, b = year % 7, c = year % 19;
        int d = (19 * c + 15) % 30;
        int e = (2 * a + 4 * b - d + 34) % 7;
        int month = (d + e + 114) / 31;
        int day = (d + e + 114) % 31 + 1;
        return new DateTime(year, month, day).AddDays(13);
    }
}

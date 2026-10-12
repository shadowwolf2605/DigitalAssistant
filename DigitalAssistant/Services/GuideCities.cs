using System.Globalization;

namespace DigitalAssistant.Services;

// Областните градове с координати. Помощникът ги предлага при въпроса „Къде се намирате?“,
// а ако човекът сподели местоположението си, намира най-близкия от тях.
public static class GuideCities
{
    public record City(string Name, double Lat, double Lng);

    public static readonly City[] All =
    {
        new("Благоевград", 42.0209, 23.0943), new("Бургас", 42.5048, 27.4626), new("Варна", 43.2141, 27.9147),
        new("Велико Търново", 43.0757, 25.6172), new("Видин", 43.9962, 22.8679), new("Враца", 43.2102, 23.5529),
        new("Габрово", 42.8742, 25.3187), new("Добрич", 43.5726, 27.8273), new("Кърджали", 41.6338, 25.3777),
        new("Кюстендил", 42.2839, 22.6911), new("Ловеч", 43.1370, 24.7142), new("Монтана", 43.4085, 23.2257),
        new("Пазарджик", 42.1928, 24.3336), new("Перник", 42.6052, 23.0378), new("Плевен", 43.4170, 24.6067),
        new("Пловдив", 42.1354, 24.7453), new("Разград", 43.5333, 26.5167), new("Русе", 43.8356, 25.9657),
        new("Силистра", 44.1171, 27.2606), new("Сливен", 42.6817, 26.3229), new("Смолян", 41.5774, 24.7011),
        new("София", 42.6977, 23.3219), new("Стара Загора", 42.4258, 25.6345), new("Търговище", 43.2512, 26.5722),
        new("Хасково", 41.9344, 25.5554), new("Шумен", 43.2712, 26.9361), new("Ямбол", 42.4842, 26.5035)
    };

    // Градът на центъра (клиента). Телефоните на услугите в базата са на офисите в този град.
    public const string HomeCity = "Русе";

    // „в Русе“, но „във Варна“ и „във Враца“ (пред „в“ и „ф“)
    public static string In(string city) =>
        (city.Length > 0 && "ВвФф".Contains(city[0]) ? "във " : "в ") + city;

    // Населеното място, както е написано в списъка, или поне с главна буква
    public static string Normalize(string city)
    {
        city = System.Text.RegularExpressions.Regex.Replace(city.Trim(), @"\s+", " ");
        var known = All.FirstOrDefault(c => string.Equals(c.Name, city, StringComparison.OrdinalIgnoreCase));
        return known?.Name ?? (city.Length > 0 ? char.ToUpper(city[0]) + city[1..] : city);
    }

    // Приблизително дали точката е в България (с малко резерв около границата)
    public static bool InBulgaria(double lat, double lng) => lat is >= 41.0 and <= 44.4 && lng is >= 22.2 and <= 28.8;

    public static City Nearest(double lat, double lng) =>
        All.MinBy(c => Distance(lat, lng, c.Lat, c.Lng))!;

    // Разстояние в километри между две точки
    public static double Distance(double lat1, double lng1, double lat2, double lng2)
    {
        const double r = 6371;
        double Rad(double d) => d * Math.PI / 180;
        var dLat = Rad(lat2 - lat1);
        var dLng = Rad(lng2 - lng1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return 2 * r * Math.Asin(Math.Sqrt(a));
    }

    // Връзки към Google Maps. Не е нужен ключ – страницата на картата се отваря в нов прозорец.
    public static string MapSearch(string place, string? city, double? lat, double? lng)
    {
        if (lat != null && lng != null)
            return "https://www.google.com/maps/search/" + Uri.EscapeDataString(place) + "/@" +
                   lat.Value.ToString("0.###", CultureInfo.InvariantCulture) + "," +
                   lng.Value.ToString("0.###", CultureInfo.InvariantCulture) + ",14z";
        return "https://www.google.com/maps/search/?api=1&query=" + Uri.EscapeDataString(Join(place, city));
    }

    // Маршрут до мястото: пеша, с кола или с градски транспорт
    public static string Route(string place, string? city, double? lat, double? lng, string mode)
    {
        var url = "https://www.google.com/maps/dir/?api=1&destination=" + Uri.EscapeDataString(Join(place, city)) +
                  "&travelmode=" + mode;
        if (lat != null && lng != null)
            url += "&origin=" + lat.Value.ToString("0.###", CultureInfo.InvariantCulture) + "," +
                   lng.Value.ToString("0.###", CultureInfo.InvariantCulture);
        return url;
    }

    private static string Join(string place, string? city) =>
        string.IsNullOrWhiteSpace(city) ? place : $"{place}, {city}";
}

namespace DigitalAssistant.Models;

// Икона и цвят за категорията, които се показват на страниците.
// Иконата се избира по името на категорията. Нова категория без позната дума получава обща икона.
public static class CategoryStyle
{
    private static readonly (string Part, string Icon)[] Icons =
    {
        ("Лични", "bi-person-vcard"), ("Шофьор", "bi-car-front"), ("автомоб", "bi-car-front"),
        ("Граждан", "bi-people"), ("Адрес", "bi-house-door"), ("Местни", "bi-receipt"),
        ("Строител", "bi-buildings"), ("Социал", "bi-heart"), ("Пенси", "bi-cash-coin"),
        ("Работа", "bi-briefcase"), ("Здрав", "bi-heart-pulse"), ("Образ", "bi-mortarboard"),
        ("Фирм", "bi-shop"), ("бизнес", "bi-shop"), ("НАП", "bi-bank"), ("Данъ", "bi-bank"),
        ("Избор", "bi-check2-square"), ("Транспорт", "bi-bus-front")
    };

    public static string Icon(string name) =>
        Icons.FirstOrDefault(i => name.Contains(i.Part, StringComparison.OrdinalIgnoreCase)).Icon ?? "bi-folder2-open";

    // Един от 7 цвята, според номера на категорията
    public static string Tone(int id) => $"tone-{id % 7}";
}

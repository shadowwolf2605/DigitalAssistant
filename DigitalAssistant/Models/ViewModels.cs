using System.ComponentModel.DataAnnotations;

namespace DigitalAssistant.Models;

// Формуляр за добавяне и промяна на услуга (т. 3.1.1)
public class ServiceFormModel
{
    public int Id { get; set; }

    [Display(Name = "Име на услугата")]
    [Required(ErrorMessage = "Името на услугата е задължително.")]
    [StringLength(200, ErrorMessage = "Името трябва да е до 200 знака.")]
    public string Name { get; set; } = "";

    [Display(Name = "Кратко описание")]
    [StringLength(1000, ErrorMessage = "Описанието трябва да е до 1000 знака.")]
    public string? Description { get; set; }

    [Display(Name = "Категория")]
    [Required(ErrorMessage = "Изберете категория.")]
    public int? CategoryId { get; set; }

    [Display(Name = "Институция")]
    [Required(ErrorMessage = "Изберете институция.")]
    public int? InstitutionId { get; set; }

    [Display(Name = "Необходими документи")]
    [Required(ErrorMessage = "Необходимите документи са задължителни.")]
    [StringLength(2000, ErrorMessage = "Текстът трябва да е до 2000 знака.")]
    public string RequiredDocuments { get; set; } = "";

    [Display(Name = "Взимат се служебно от други институции")]
    [StringLength(1000, ErrorMessage = "Текстът трябва да е до 1000 знака.")]
    public string? OfficialDocuments { get; set; }

    [Display(Name = "Уточняващ въпрос")]
    [StringLength(150, ErrorMessage = "Въпросът трябва да е до 150 знака.")]
    public string? Question { get; set; }

    [Display(Name = "Отговори на въпроса")]
    [StringLength(2000, ErrorMessage = "Текстът трябва да е до 2000 знака.")]
    public string? QuestionOptions { get; set; }

    [Display(Name = "Стъпки за гражданина")]
    [StringLength(2000, ErrorMessage = "Текстът трябва да е до 2000 знака.")]
    public string? Steps { get; set; }

    [Display(Name = "Място на подаване (адрес)")]
    [Required(ErrorMessage = "Мястото на подаване е задължително.")]
    [StringLength(300, ErrorMessage = "Адресът трябва да е до 300 знака.")]
    public string Address { get; set; } = "";

    [Display(Name = "Какво да се търси в картата")]
    [StringLength(150, ErrorMessage = "Текстът трябва да е до 150 знака.")]
    public string? PlaceSearch { get; set; }

    [Display(Name = "Работно време")]
    [StringLength(200)]
    public string? WorkingHours { get; set; }

    [Display(Name = "Телефон")]
    [StringLength(50)]
    public string? Phone { get; set; }

    [Display(Name = "Такса (€)")]
    [Required(ErrorMessage = "Таксата е задължителна. Въведете 0, ако услугата е безплатна.")]
    [Range(0, 100000, ErrorMessage = "Таксата трябва да е число, по-голямо или равно на 0.")]
    public decimal? Fee { get; set; }

    [Display(Name = "Такса за бърза услуга (€)")]
    [Range(0, 100000, ErrorMessage = "Таксата трябва да е число, по-голямо или равно на 0.")]
    public decimal? ExpressFee { get; set; }

    [Display(Name = "Срок (работни дни)")]
    [Required(ErrorMessage = "Срокът е задължителен.")]
    [Range(1, 3650, ErrorMessage = "Срокът трябва да е цяло число, по-голямо от 0.")]
    public int? DeadlineDays { get; set; }

    [Display(Name = "Срок за бърза услуга (работни дни)")]
    [Range(1, 3650, ErrorMessage = "Срокът трябва да е цяло число, по-голямо от 0.")]
    public int? ExpressDeadlineDays { get; set; }

    [Display(Name = "Начин на получаване на резултата")]
    public List<string> ResultDelivery { get; set; } = new();

    [Display(Name = "Ключови думи (разделени със запетая)")]
    [Required(ErrorMessage = "Въведете поне една ключова дума.")]
    [StringLength(1000, ErrorMessage = "Ключовите думи трябва да са до 1000 знака.")]
    public string Keywords { get; set; } = "";

    [Display(Name = "Образец на заявлението (.pdf или .doc, до 5 MB)")]
    public IFormFile? TemplateUpload { get; set; }

    [Display(Name = "Премахни сегашния образец")]
    public bool RemoveTemplate { get; set; }

    public string? ExistingTemplateName { get; set; }

    [Display(Name = "Може да се заяви онлайн")]
    public bool AllowOnline { get; set; } = true;

    public static readonly string[] DeliveryOptions = { "на място", "по пощата", "по електронен път" };
}

public class LoginModel
{
    [Display(Name = "Потребителско име")]
    [Required(ErrorMessage = "Въведете потребителско име.")]
    public string Username { get; set; } = "";

    [Display(Name = "Парола")]
    [Required(ErrorMessage = "Въведете парола.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";

    public string? ReturnUrl { get; set; }
}

public class ChangePasswordModel
{
    [Display(Name = "Сегашна парола")]
    [Required(ErrorMessage = "Въведете сегашната парола.")]
    [DataType(DataType.Password)]
    public string CurrentPassword { get; set; } = "";

    [Display(Name = "Нова парола")]
    [Required(ErrorMessage = "Въведете нова парола.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Паролата трябва да е поне 8 знака.")]
    [DataType(DataType.Password)]
    public string NewPassword { get; set; } = "";

    [Display(Name = "Повторете новата парола")]
    [Compare(nameof(NewPassword), ErrorMessage = "Двете пароли не съвпадат.")]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = "";
}

public class ServiceFilterModel
{
    public string? Name { get; set; }
    public int? CategoryId { get; set; }
    public int? InstitutionId { get; set; }
    public string? Status { get; set; }
}

public class QuestionReviewModel
{
    public int QuestionId { get; set; }

    [Display(Name = "Услуга")]
    [Required(ErrorMessage = "Изберете услуга.")]
    public int? ServiceId { get; set; }

    [Display(Name = "Нови ключови думи (разделени със запетая)")]
    [Required(ErrorMessage = "Въведете поне една ключова дума.")]
    public string Keywords { get; set; } = "";
}

// Регистрация на гражданин (т. 3.1.3)
public class RegisterModel
{
    [Display(Name = "Име и фамилия")]
    [Required(ErrorMessage = "Въведете име и фамилия.")]
    [StringLength(100, ErrorMessage = "Името трябва да е до 100 знака.")]
    public string FullName { get; set; } = "";

    [Display(Name = "Имейл")]
    [Required(ErrorMessage = "Въведете имейл.")]
    [EmailAddress(ErrorMessage = "Имейлът не е валиден.")]
    [StringLength(150, ErrorMessage = "Имейлът трябва да е до 150 знака.")]
    public string Email { get; set; } = "";

    [Display(Name = "ЕГН")]
    [Required(ErrorMessage = "Въведете ЕГН.")]
    public string Egn { get; set; } = "";

    [Display(Name = "Телефон")]
    [Required(ErrorMessage = "Въведете телефон.")]
    [RegularExpression(@"^\+?[0-9 ]{6,20}$", ErrorMessage = "Телефонът може да съдържа само цифри, интервали и + в началото.")]
    public string Phone { get; set; } = "";

    [Display(Name = "Парола")]
    [Required(ErrorMessage = "Въведете парола.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Паролата трябва да е поне 8 знака.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [Display(Name = "Повторете паролата")]
    [Required(ErrorMessage = "Повторете паролата.")]
    [Compare(nameof(Password), ErrorMessage = "Двете пароли не съвпадат.")]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = "";

    [Display(Name = "Съгласен/съгласна съм личните ми данни да се използват за обработка на заявленията")]
    [Required(ErrorMessage = "Трябва да се съгласите личните ви данни да се използват за заявленията.")]
    public bool Consent { get; set; }

    public string? ReturnUrl { get; set; }
}

public class CitizenLoginModel
{
    [Display(Name = "Имейл")]
    [Required(ErrorMessage = "Въведете имейл.")]
    public string Email { get; set; } = "";

    [Display(Name = "Парола")]
    [Required(ErrorMessage = "Въведете парола.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";

    public string? ReturnUrl { get; set; }
}

public class ForgotPasswordModel
{
    [Display(Name = "Имейл")]
    [Required(ErrorMessage = "Въведете имейл.")]
    [EmailAddress(ErrorMessage = "Имейлът не е валиден.")]
    public string Email { get; set; } = "";
}

public class ResetPasswordModel
{
    public string Token { get; set; } = "";

    [Display(Name = "Нова парола")]
    [Required(ErrorMessage = "Въведете нова парола.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Паролата трябва да е поне 8 знака.")]
    [DataType(DataType.Password)]
    public string NewPassword { get; set; } = "";

    [Display(Name = "Повторете новата парола")]
    [Required(ErrorMessage = "Повторете паролата.")]
    [Compare(nameof(NewPassword), ErrorMessage = "Двете пароли не съвпадат.")]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = "";
}

// Профил на гражданина: данни, които може да промени сам
public class ProfileModel
{
    [Display(Name = "Телефон")]
    [Required(ErrorMessage = "Въведете телефон.")]
    [RegularExpression(@"^\+?[0-9 ]{6,20}$", ErrorMessage = "Телефонът може да съдържа само цифри, интервали и + в началото.")]
    public string Phone { get; set; } = "";

    [Display(Name = "Искам да получавам имейл при промяна по заявленията ми")]
    public bool EmailNotifications { get; set; } = true;
}

// Съобщение от гражданина до служителя по заявление
public class CitizenMessageModel
{
    [Display(Name = "Съобщение до служителя")]
    [Required(ErrorMessage = "Напишете съобщение.")]
    [StringLength(1000, ErrorMessage = "Съобщението трябва да е до 1000 знака.")]
    public string Text { get; set; } = "";
}

// Нов служител (добавя го главният администратор)
public class StaffFormModel
{
    [Display(Name = "Потребителско име")]
    [Required(ErrorMessage = "Въведете потребителско име.")]
    [RegularExpression(@"^[a-zA-Z0-9._-]{3,30}$", ErrorMessage = "Потребителското име трябва да е от 3 до 30 латински букви, цифри, точка, тире или долна черта.")]
    public string Username { get; set; } = "";

    [Display(Name = "Име и фамилия")]
    [Required(ErrorMessage = "Въведете име и фамилия.")]
    [StringLength(100, ErrorMessage = "Името трябва да е до 100 знака.")]
    public string FullName { get; set; } = "";

    [Display(Name = "Временна парола")]
    [Required(ErrorMessage = "Въведете временна парола.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Паролата трябва да е поне 8 знака.")]
    public string Password { get; set; } = "";

    [Display(Name = "Институция")]
    public int? InstitutionId { get; set; }

    // Градът на служителя (празно – всички градове)
    [Display(Name = "Град")]
    public string? City { get; set; }
}

// Формуляр за онлайн заявление (т. 3.1.3)
public class ApplicationFormModel
{
    public int ServiceId { get; set; }

    [Display(Name = "Какво заявявате")]
    [Required(ErrorMessage = "Опишете какво заявявате.")]
    [StringLength(1000, ErrorMessage = "Текстът трябва да е до 1000 знака.")]
    public string Details { get; set; } = "";

    [Display(Name = "Как да получите резултата")]
    [Required(ErrorMessage = "Изберете как да получите резултата.")]
    public string? ResultDelivery { get; set; }

    [Display(Name = "Адрес за получаване по пощата")]
    [StringLength(300, ErrorMessage = "Адресът трябва да е до 300 знака.")]
    public string? DeliveryAddress { get; set; }

    [Display(Name = "Документи")]
    public List<IFormFile> Files { get; set; } = new();

    // Бърза услуга (ако услугата има такава)
    public bool Express { get; set; }

    [Display(Name = "Град, в който да се обработи заявлението")]
    [Required(ErrorMessage = "Изберете град.")]
    public string? City { get; set; }
}

// Смяна на статуса на заявление от администратора
public class StatusChangeModel
{
    public int ApplicationId { get; set; }

    [Display(Name = "Нов статус")]
    public ApplicationStatus Status { get; set; }

    // Статусът, който служителят е виждал на страницата. Ако междувременно е сменен, отговорът не го връща назад.
    public ApplicationStatus? CurrentStatus { get; set; }

    [Display(Name = "Съобщение до гражданина")]
    [StringLength(1000, ErrorMessage = "Съобщението трябва да е до 1000 знака.")]
    public string? Comment { get; set; }

    [Display(Name = "Файлове към гражданина")]
    public List<IFormFile> Files { get; set; } = new();

    // false – веднага; true – в избраните дата и час
    public bool SendLater { get; set; }

    [Display(Name = "Дата и час на изпращане")]
    public DateTime? SendAt { get; set; }

    // Заявлението, което се показва на страницата
    public Application? Application { get; set; }

    public StatusChangeModel WithApplication(Application app) { Application = app; return this; }
}

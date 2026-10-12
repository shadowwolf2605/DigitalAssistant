using Microsoft.AspNetCore.Mvc;

namespace DigitalAssistant.Controllers;

// Упътване за гражданите и ръководство за администратора (т. 3.2.7)
public class HelpController : Controller
{
    public IActionResult Index() => View();
}

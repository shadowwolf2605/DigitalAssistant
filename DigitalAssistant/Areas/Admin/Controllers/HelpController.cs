using Microsoft.AspNetCore.Mvc;

namespace DigitalAssistant.Areas.Admin.Controllers;

// Ръководство за администратора (т. 3.2.7). Вижда се само в администрацията.
public class HelpController : AdminController
{
    protected override bool ForInstitutionStaff => true;

    public IActionResult Index() => View();
}

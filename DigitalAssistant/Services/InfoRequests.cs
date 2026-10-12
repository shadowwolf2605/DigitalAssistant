using DigitalAssistant.Data;
using DigitalAssistant.Models;

namespace DigitalAssistant.Services;

// Служебни справки между институциите (т. 3.1.3)
public static class InfoRequests
{
    // Името на центъра, когато справката е от служител, който не е към конкретна институция
    public const string CenterName = "Административен информационен център";

    // Справките, на които този служител може да отговори: до неговата институция,
    // а служител на центъра (без институция) вижда справките до всички институции
    public static IQueryable<InfoRequest> ForStaff(AppDbContext db, AdminUser? me) =>
        me?.InstitutionId == null
            ? db.InfoRequests
            : db.InfoRequests.Where(r => r.ToInstitutionId == me.InstitutionId);

    public static bool CanAnswer(AdminUser? me, InfoRequest r) =>
        me != null && (me.InstitutionId == null || me.InstitutionId == r.ToInstitutionId);
}

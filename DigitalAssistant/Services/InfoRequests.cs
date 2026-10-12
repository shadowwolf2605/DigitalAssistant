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
    // Служител с избран град вижда само справките по заявления от този град.
    public static IQueryable<InfoRequest> ForStaff(AppDbContext db, AdminUser? me)
    {
        var query = me?.InstitutionId == null
            ? db.InfoRequests
            : db.InfoRequests.Where(r => r.ToInstitutionId == me.InstitutionId);
        var city = string.IsNullOrEmpty(me?.City) ? null : me.City;
        return city == null ? query : query.Where(r => r.Application!.City == city);
    }

    // r.Application трябва да е заредено, за да се провери градът
    public static bool CanAnswer(AdminUser? me, InfoRequest r) =>
        me != null && (me.InstitutionId == null || me.InstitutionId == r.ToInstitutionId)
                   && (string.IsNullOrEmpty(me.City) || r.Application?.City == me.City);
}

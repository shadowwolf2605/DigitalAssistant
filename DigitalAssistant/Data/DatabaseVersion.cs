using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DigitalAssistant.Data;

// Версията на таблиците се пази в самия файл на базата (PRAGMA user_version).
// Ако системата е обновена, базата се допълва с новите колони и данните се запазват.
// Ако е от много стара версия, която не може да се допълни, тя се премества в App_Data/Backups
// и при стартирането се създава нова с примерните данни.
public static class DatabaseVersion
{
    // Промени, с които база от предишната версия става като новата
    private static readonly Dictionary<long, string> Upgrades = new()
    {
        // Версия 4: служителят може да прикача файлове към съобщението си
        [3] = """
              ALTER TABLE "ApplicationFiles" ADD COLUMN "ByStaff" INTEGER NOT NULL DEFAULT 0;
              ALTER TABLE "ApplicationFiles" ADD COLUMN "EventId" INTEGER NULL;
              """,
        // Версия 5 имаше отделна страница за имейли, която е премахната – таблицата ѝ се трие във версия 6
        [4] = "",
        // Версия 6: насрочени отговори по заявленията
        [5] = """
              DROP TABLE IF EXISTS "ScheduledEmails";
              CREATE TABLE "ScheduledReplies" (
                  "Id" INTEGER NOT NULL CONSTRAINT "PK_ScheduledReplies" PRIMARY KEY AUTOINCREMENT,
                  "ApplicationId" INTEGER NOT NULL,
                  "Status" INTEGER NOT NULL,
                  "Comment" TEXT NOT NULL,
                  "SendAt" TEXT NOT NULL,
                  "CreatedBy" TEXT NOT NULL,
                  "CreatedAt" TEXT NOT NULL,
                  CONSTRAINT "FK_ScheduledReplies_Applications_ApplicationId" FOREIGN KEY ("ApplicationId") REFERENCES "Applications" ("Id") ON DELETE CASCADE
              );
              CREATE INDEX "IX_ScheduledReplies_ApplicationId" ON "ScheduledReplies" ("ApplicationId");
              CREATE INDEX "IX_ScheduledReplies_SendAt" ON "ScheduledReplies" ("SendAt");
              CREATE TABLE "ScheduledReplyFiles" (
                  "Id" INTEGER NOT NULL CONSTRAINT "PK_ScheduledReplyFiles" PRIMARY KEY AUTOINCREMENT,
                  "ScheduledReplyId" INTEGER NOT NULL,
                  "FileName" TEXT NOT NULL,
                  "ContentType" TEXT NOT NULL,
                  "Size" INTEGER NOT NULL,
                  "Data" BLOB NOT NULL,
                  CONSTRAINT "FK_ScheduledReplyFiles_ScheduledReplies_ScheduledReplyId" FOREIGN KEY ("ScheduledReplyId") REFERENCES "ScheduledReplies" ("Id") ON DELETE CASCADE
              );
              CREATE INDEX "IX_ScheduledReplyFiles_ScheduledReplyId" ON "ScheduledReplyFiles" ("ScheduledReplyId");
              """,
        // Версия 7: таксата и срокът се записват в заявлението при подаването
        [6] = """
              ALTER TABLE "Applications" ADD COLUMN "Fee" REAL NOT NULL DEFAULT 0;
              ALTER TABLE "Applications" ADD COLUMN "DeadlineDays" INTEGER NOT NULL DEFAULT 0;
              UPDATE "Applications" SET
                  "Fee" = (SELECT "Fee" FROM "Services" WHERE "Services"."Id" = "Applications"."ServiceId"),
                  "DeadlineDays" = (SELECT "DeadlineDays" FROM "Services" WHERE "Services"."Id" = "Applications"."ServiceId");
              """,
        // Версия 8: помощникът с въпроси (място за търсене в картата, брой показани упътвания)
        // и връзка между институциите (служебни документи, служебни справки, институция на служителя)
        // Версия 9: уточняващ въпрос за услугите (от него зависят документите), бърза услуга при онлайн заявление
        [8] = """
              ALTER TABLE "Applications" ADD COLUMN "Express" INTEGER NOT NULL DEFAULT 0;
              ALTER TABLE "Services" ADD COLUMN "Question" TEXT NOT NULL DEFAULT '';
              ALTER TABLE "Services" ADD COLUMN "QuestionOptions" TEXT NOT NULL DEFAULT '';
              UPDATE "Services" SET "Question" = 'Защо вадите нова лична карта?', "QuestionOptions" = 'Изтича срокът или е повредена | старата лична карта
              Изгубена или открадната | декларация за загуба или кражба (попълва се на място) | Ако е открадната, първо съобщете в полицията. Старата карта спира да важи от момента на заявяването.
              Смяна на име или адрес | старата лична карта | Новото име или адрес трябва вече да са вписани в общината.
              Първа лична карта (на 14 години) |  | Детето идва лично с родител. Удостоверението за раждане се взима служебно.', "RequiredDocuments" = CASE WHEN "RequiredDocuments" = 'старата лична карта (ако имате такава); заявление (попълва се на място); при загуба или кражба – декларация (попълва се на място)' THEN 'заявление (попълва се на място)' ELSE "RequiredDocuments" END WHERE "Name" = 'Издаване на лична карта';
              UPDATE "Services" SET "Question" = 'За кого е паспортът?', "QuestionOptions" = 'За мен | лична карта; стар паспорт (ако имате такъв)
              За дете под 14 години | лични карти на двамата родители; нотариално заверено съгласие (ако идва само единият родител) | Детето идва лично, защото се снима на място.', "RequiredDocuments" = CASE WHEN "RequiredDocuments" = 'лична карта; стар паспорт (ако имате такъв); заявление (попълва се на място); за дете под 14 години – двамата родители или нотариално заверено съгласие на единия' THEN 'заявление (попълва се на място)' ELSE "RequiredDocuments" END WHERE "Name" = 'Издаване на паспорт';
              UPDATE "Services" SET "Question" = 'Защо сменяте книжката?', "QuestionOptions" = 'Изтича срокът | медицинско удостоверение за водач
              Смяна на име или адрес |  | Медицинско удостоверение не е нужно, ако срокът на книжката не е изтекъл.
              Повредена книжка', "RequiredDocuments" = CASE WHEN "RequiredDocuments" = 'лична карта; старата книжка; медицинско удостоверение за водач (при изтекъл срок); заявление (попълва се на място)' THEN 'лична карта; старата книжка; заявление (попълва се на място)' ELSE "RequiredDocuments" END WHERE "Name" = 'Подмяна на шофьорска книжка';
              UPDATE "Services" SET "Question" = 'Какъв е автомобилът?', "QuestionOptions" = 'Нов, купен в България
              Внесен от друга държава | талон от другата държава; документ за технически преглед (за употребяван автомобил) | Ако автомобилът е употребяван, първо направете технически преглед.', "RequiredDocuments" = CASE WHEN "RequiredDocuments" = 'лична карта; документ за собственост (фактура или договор); талон от другата държава (за внесен автомобил); застраховка „Гражданска отговорност“; документ за технически преглед (за внесен употребяван автомобил)' THEN 'лична карта; документ за собственост (фактура или договор); застраховка „Гражданска отговорност“' ELSE "RequiredDocuments" END WHERE "Name" = 'Регистрация на нов или внесен автомобил';
              UPDATE "Services" SET "Question" = 'Защо прекратявате регистрацията?', "QuestionOptions" = 'Бракуване | документ от площадката за разкомплектоване | Първо закарайте автомобила на площадка с разрешение. Там ще ви дадат документа.
              Продажба в друга държава | документ, че автомобилът се изнася (например договор)', "RequiredDocuments" = CASE WHEN "RequiredDocuments" = 'лична карта; голям и малък талон; регистрационните табели; документ от площадката за разкомплектоване (при бракуване)' THEN 'лична карта; голям и малък талон; регистрационните табели' ELSE "RequiredDocuments" END WHERE "Name" = 'Прекратяване на регистрацията (бракуване) на автомобил';
              UPDATE "Services" SET "Question" = 'За кого е удостоверението?', "QuestionOptions" = 'За мен
              За моето дете под 18 години |  | Родителят подава искането без пълномощно.
              За друг човек | нотариално заверено пълномощно' WHERE "Name" = 'Удостоверение за раждане – дубликат';
              UPDATE "Services" SET "Question" = 'За кого е удостоверението?', "QuestionOptions" = 'За мен
              За друг човек | нотариално заверено пълномощно' WHERE "Name" = 'Удостоверение за семейно положение';
              UPDATE "Services" SET "Question" = 'Кой подава искането?', "QuestionOptions" = 'Аз съм наследник
              Друг човек вместо наследника | нотариално заверено пълномощно' WHERE "Name" = 'Удостоверение за наследници';
              UPDATE "Services" SET "Question" = 'Чие е жилището?', "QuestionOptions" = 'Мое е | документ за собственост
              Под наем съм | договор за наем или съгласие на собственика
              На близки | съгласие на собственика | Собственикът може да дойде с вас и да подпише на място.', "RequiredDocuments" = CASE WHEN "RequiredDocuments" = 'документ за самоличност; адресна карта (попълва се на място); при наем – договор за наем или съгласие на собственика' THEN 'документ за самоличност; адресна карта (попълва се на място)' ELSE "RequiredDocuments" END WHERE "Name" = 'Адресна регистрация – смяна на настоящ адрес';
              UPDATE "Services" SET "Question" = 'Чие е жилището?', "QuestionOptions" = 'Мое е | документ за собственост
              На друг човек | нотариално заверено съгласие на собственика | Собственикът може и да дойде с вас и да подпише на място.', "RequiredDocuments" = CASE WHEN "RequiredDocuments" = 'лична карта; заявление за постоянен адрес (попълва се на място); документ за собственост или нотариално заверено съгласие на собственика' THEN 'лична карта; заявление за постоянен адрес (попълва се на място)' ELSE "RequiredDocuments" END WHERE "Name" = 'Смяна на постоянен адрес';
              UPDATE "Services" SET "Question" = 'С какво се отоплявате?', "QuestionOptions" = 'Ток |  | Помощта се приспада от сметката за ток.
              Парно |  | Помощта се превежда на топлофикацията.
              Дърва, въглища или газ |  | Помощта се изплаща в пари.' WHERE "Name" = 'Целева помощ за отопление';
              UPDATE "Services" SET "Question" = 'Целият ви стаж ли е записан в НОИ?', "QuestionOptions" = 'Да, работил съм само след 2000 г.
              Имам стаж преди 2000 г. | трудова книжка; документи за стажа и доходите (УП-2, УП-3) от работодателите | Ако фирмата вече не съществува, документите се търсят в архива, където са предадени.', "RequiredDocuments" = CASE WHEN "RequiredDocuments" = 'лична карта; заявление; документи за стажа (трудова книжка, УП-2, УП-3), ако не са в НОИ' THEN 'лична карта; заявление' ELSE "RequiredDocuments" END WHERE "Name" = 'Отпускане на пенсия за осигурителен стаж и възраст';
              UPDATE "Services" SET "Question" = 'Работили ли сте досега?', "QuestionOptions" = 'Да | трудова книжка | Регистрирайте се до 7 работни дни след края на работата, ако искате обезщетение.
              Не, търся първа работа', "RequiredDocuments" = CASE WHEN "RequiredDocuments" = 'лична карта; документ за завършено образование; трудова книжка (ако имате)' THEN 'лична карта; документ за завършено образование' ELSE "RequiredDocuments" END WHERE "Name" = 'Регистрация като безработен в Бюрото по труда';
              """,
        [7] = """
              ALTER TABLE "Services" ADD COLUMN "OfficialDocuments" TEXT NOT NULL DEFAULT '';
              ALTER TABLE "AdminUsers" ADD COLUMN "InstitutionId" INTEGER NULL
                  CONSTRAINT "FK_AdminUsers_Institutions_InstitutionId" REFERENCES "Institutions" ("Id") ON DELETE SET NULL;
              CREATE INDEX "IX_AdminUsers_InstitutionId" ON "AdminUsers" ("InstitutionId");
              CREATE TABLE "InfoRequests" (
                  "Id" INTEGER NOT NULL CONSTRAINT "PK_InfoRequests" PRIMARY KEY AUTOINCREMENT,
                  "ApplicationId" INTEGER NOT NULL,
                  "ToInstitutionId" INTEGER NOT NULL,
                  "Question" TEXT NOT NULL,
                  "CreatedBy" TEXT NOT NULL,
                  "FromInstitution" TEXT NOT NULL,
                  "CreatedAt" TEXT NOT NULL,
                  "Answer" TEXT NULL,
                  "AnsweredBy" TEXT NULL,
                  "AnsweredAt" TEXT NULL,
                  "AnswerFileName" TEXT NULL,
                  "AnswerContentType" TEXT NULL,
                  "AnswerFile" BLOB NULL,
                  CONSTRAINT "FK_InfoRequests_Applications_ApplicationId" FOREIGN KEY ("ApplicationId") REFERENCES "Applications" ("Id") ON DELETE CASCADE,
                  CONSTRAINT "FK_InfoRequests_Institutions_ToInstitutionId" FOREIGN KEY ("ToInstitutionId") REFERENCES "Institutions" ("Id") ON DELETE RESTRICT
              );
              CREATE INDEX "IX_InfoRequests_AnsweredAt" ON "InfoRequests" ("AnsweredAt");
              CREATE INDEX "IX_InfoRequests_ApplicationId" ON "InfoRequests" ("ApplicationId");
              CREATE INDEX "IX_InfoRequests_ToInstitutionId" ON "InfoRequests" ("ToInstitutionId");
              ALTER TABLE "Services" ADD COLUMN "PlaceSearch" TEXT NOT NULL DEFAULT '';
              UPDATE "Services" SET "PlaceSearch" = CASE "Address"
                      WHEN 'Звено „Български документи за самоличност“ към областната дирекция на МВР' THEN 'Български документи за самоличност МВР'
                      WHEN 'Бюро за съдимост към районния съд' THEN 'Бюро за съдимост Районен съд'
                      WHEN 'Център за административно обслужване на общината, гишета 1 – 6' THEN 'Център за административно обслужване Община'
                      WHEN 'Сектор „Пътна полиция“ към областната дирекция на МВР' THEN 'Сектор Пътна полиция'
                      WHEN 'Офис на СБА' THEN 'Съюз на българските автомобилисти СБА'
                      WHEN 'Общинско звено „Паркиране“' THEN 'Общинско предприятие Паркиране'
                      WHEN 'Областна администрация – звено за административно обслужване' THEN 'Областна администрация'
                      WHEN 'Районен съд – регистратура' THEN 'Районен съд'
                      WHEN 'Общината – отдел „Местни данъци и такси“' THEN 'Местни данъци и такси Община'
                      WHEN 'Общината – отдел „Устройство на територията“' THEN 'Община Устройство на територията'
                      WHEN 'Служба по геодезия, картография и кадастър' THEN 'Служба по геодезия картография и кадастър'
                      WHEN 'Служба по вписванията към районния съд' THEN 'Служба по вписванията'
                      WHEN 'Дирекция „Социално подпомагане“' THEN 'Дирекция Социално подпомагане'
                      WHEN 'Териториално поделение на НОИ' THEN 'Териториално поделение на НОИ'
                      WHEN 'Дирекция „Бюро по труда“' THEN 'Дирекция Бюро по труда'
                      WHEN 'При новия личен лекар' THEN 'Общопрактикуващ лекар'
                      WHEN 'Районна здравноосигурителна каса' THEN 'Районна здравноосигурителна каса РЗОК'
                      WHEN 'Офис на НАП' THEN 'НАП офис'
                      WHEN 'ТЕЛК към областната болница' THEN 'ТЕЛК'
                      WHEN 'Общината – отдел „Образование“' THEN 'Община'
                      WHEN 'Училището, издало дипломата (ако е закрито – РУО)' THEN 'Регионално управление на образованието'
                      WHEN 'Агенция по вписванията – териториална служба' THEN 'Агенция по вписванията'
                      ELSE "Address" END
                  WHERE "Address" NOT LIKE 'Онлайн%';
              CREATE TABLE "GuideViews" (
                  "Id" INTEGER NOT NULL CONSTRAINT "PK_GuideViews" PRIMARY KEY AUTOINCREMENT,
                  "ServiceId" INTEGER NOT NULL,
                  "City" TEXT NOT NULL,
                  "Online" INTEGER NOT NULL,
                  "ShownAt" TEXT NOT NULL,
                  CONSTRAINT "FK_GuideViews_Services_ServiceId" FOREIGN KEY ("ServiceId") REFERENCES "Services" ("Id") ON DELETE CASCADE
              );
              CREATE INDEX "IX_GuideViews_ServiceId" ON "GuideViews" ("ServiceId");
              CREATE INDEX "IX_GuideViews_ShownAt" ON "GuideViews" ("ShownAt");
              """,
        // Версия 10: системата е за всички градове – офиси на институциите по градове, град на заявлението
        // и на служителя. Досегашните заявления са в Русе, а телефоните от Русе (082) отиват в офисите в Русе.
        [9] = """
              CREATE TABLE "Offices" (
                  "Id" INTEGER NOT NULL CONSTRAINT "PK_Offices" PRIMARY KEY AUTOINCREMENT,
                  "InstitutionId" INTEGER NOT NULL,
                  "City" TEXT NOT NULL,
                  "Address" TEXT NOT NULL,
                  "Phone" TEXT NOT NULL,
                  "WorkingHours" TEXT NOT NULL,
                  CONSTRAINT "FK_Offices_Institutions_InstitutionId" FOREIGN KEY ("InstitutionId") REFERENCES "Institutions" ("Id") ON DELETE CASCADE
              );
              CREATE UNIQUE INDEX "IX_Offices_InstitutionId_City" ON "Offices" ("InstitutionId", "City");
              ALTER TABLE "Applications" ADD COLUMN "City" TEXT NOT NULL DEFAULT '';
              CREATE INDEX "IX_Applications_City" ON "Applications" ("City");
              ALTER TABLE "AdminUsers" ADD COLUMN "City" TEXT NULL;
              """ + CitySql,
    };

    // Същите промени за PostgreSQL (базата в интернет)
    public static readonly Dictionary<long, string> PostgresUpgrades = new()
    {
        [9] = """
              CREATE TABLE "Offices" (
                  "Id" integer GENERATED BY DEFAULT AS IDENTITY,
                  "InstitutionId" integer NOT NULL,
                  "City" text NOT NULL,
                  "Address" text NOT NULL,
                  "Phone" text NOT NULL,
                  "WorkingHours" text NOT NULL,
                  CONSTRAINT "PK_Offices" PRIMARY KEY ("Id"),
                  CONSTRAINT "FK_Offices_Institutions_InstitutionId" FOREIGN KEY ("InstitutionId") REFERENCES "Institutions" ("Id") ON DELETE CASCADE
              );
              CREATE UNIQUE INDEX "IX_Offices_InstitutionId_City" ON "Offices" ("InstitutionId", "City");
              ALTER TABLE "Applications" ADD COLUMN "City" text NOT NULL DEFAULT '';
              CREATE INDEX "IX_Applications_City" ON "Applications" ("City");
              ALTER TABLE "AdminUsers" ADD COLUMN "City" text NULL;
              """ + CitySql,
    };

    // Общо за двете бази: досегашните заявления са от Русе; за всяка институция с телефон от Русе (082)
    // се прави офис в Русе с телефона от първата ѝ услуга, а в услугите остават само общите телефони.
    private const string CitySql = """
        UPDATE "Applications" SET "City" = 'Русе';
        INSERT INTO "Offices" ("InstitutionId", "City", "Address", "Phone", "WorkingHours")
        SELECT s."InstitutionId", 'Русе', s."Address", s."Phone", s."WorkingHours" FROM "Services" s
        WHERE s."Phone" LIKE '082 %' AND s."Id" = (SELECT MIN(s2."Id") FROM "Services" s2
            WHERE s2."InstitutionId" = s."InstitutionId" AND s2."Phone" LIKE '082 %');
        UPDATE "Services" SET "Phone" = '' WHERE "Phone" LIKE '082 %';
        """;

    public static void Check(string dbFile, string backupFolder, ILogger logger)
    {
        if (!File.Exists(dbFile)) return;

        long version;
        using (var connection = new SqliteConnection($"Data Source={dbFile}"))
        {
            connection.Open();
            version = ReadVersion(connection);
            if (version != AppDbContext.SchemaVersion && CanUpgrade(version))
            {
                var from = version;
                using var transaction = connection.BeginTransaction();
                while (version < AppDbContext.SchemaVersion)
                {
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = Upgrades[version] + $"PRAGMA user_version = {version + 1};";
                    command.ExecuteNonQuery();
                    version++;
                }
                transaction.Commit();
                logger.LogInformation("Базата е обновена от версия {Old} до версия {New}. Данните са запазени.", from, version);
            }
        }
        SqliteConnection.ClearAllPools();
        if (version == AppDbContext.SchemaVersion) return;

        Directory.CreateDirectory(backupFolder);
        var target = Path.Combine(backupFolder, $"old-version-{version}-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.db");
        File.Move(dbFile, target);
        foreach (var extra in new[] { "-wal", "-shm" })
            if (File.Exists(dbFile + extra)) File.Move(dbFile + extra, target + extra);
        logger.LogWarning("Базата е от по-стара версия ({Old}). Преместена е в {File} и ще се създаде нова.", version, target);
    }

    private static long ReadVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version";
        return (long)command.ExecuteScalar()!;
    }

    private static bool CanUpgrade(long version)
    {
        if (version <= 0 || version > AppDbContext.SchemaVersion) return false;
        for (var v = version; v < AppDbContext.SchemaVersion; v++)
            if (!Upgrades.ContainsKey(v)) return false;
        return true;
    }

    public static void Set(AppDbContext db) =>
        db.Database.ExecuteSqlRaw($"PRAGMA user_version = {AppDbContext.SchemaVersion}");
}

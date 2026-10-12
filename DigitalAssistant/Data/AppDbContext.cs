using DigitalAssistant.Models;
using Microsoft.EntityFrameworkCore;

namespace DigitalAssistant.Data;

public class AppDbContext : DbContext
{
    // Версия на таблиците. Сменя се, когато се добавят нови таблици или колони.
    public const int SchemaVersion = 9;

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Institution> Institutions => Set<Institution>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<GuideView> GuideViews => Set<GuideView>();
    public DbSet<InfoRequest> InfoRequests => Set<InfoRequest>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<Citizen> Citizens => Set<Citizen>();
    public DbSet<Application> Applications => Set<Application>();
    public DbSet<ApplicationFile> ApplicationFiles => Set<ApplicationFile>();
    public DbSet<ApplicationEvent> ApplicationEvents => Set<ApplicationEvent>();
    public DbSet<CitizenToken> CitizenTokens => Set<CitizenToken>();
    public DbSet<ScheduledReply> ScheduledReplies => Set<ScheduledReply>();
    public DbSet<ScheduledReplyFile> ScheduledReplyFiles => Set<ScheduledReplyFile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Category>().HasIndex(c => c.Name).IsUnique();
        modelBuilder.Entity<Institution>().HasIndex(i => i.Name).IsUnique();
        modelBuilder.Entity<AdminUser>().HasIndex(a => a.Username).IsUnique();

        // Не може да има две услуги с едно и също име в една институция
        modelBuilder.Entity<Service>().HasIndex(s => new { s.InstitutionId, s.Name }).IsUnique();

        modelBuilder.Entity<Service>().HasOne(s => s.Template).WithOne()
            .HasForeignKey<ServiceTemplate>(t => t.Id);
        modelBuilder.Entity<ServiceTemplate>().ToTable("Services")
            .Property(t => t.File).HasColumnName("TemplateFile");
        modelBuilder.Entity<Service>().Property(s => s.Fee).HasConversion<double>();
        modelBuilder.Entity<Service>().Property(s => s.ExpressFee).HasConversion<double?>();
        modelBuilder.Entity<Application>().Property(a => a.Fee).HasConversion<double>();

        modelBuilder.Entity<Service>().HasOne(s => s.Category).WithMany(c => c.Services)
            .HasForeignKey(s => s.CategoryId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Service>().HasOne(s => s.Institution).WithMany(i => i.Services)
            .HasForeignKey(s => s.InstitutionId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Question>().HasOne(q => q.Service).WithMany()
            .HasForeignKey(q => q.ServiceId).OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<GuideView>().HasIndex(g => g.ShownAt);
        modelBuilder.Entity<AdminUser>().HasOne(a => a.Institution).WithMany()
            .HasForeignKey(a => a.InstitutionId).OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<InfoRequest>().HasOne(r => r.ToInstitution).WithMany()
            .HasForeignKey(r => r.ToInstitutionId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<InfoRequest>().HasIndex(r => r.AnsweredAt);

        // Заявления: един гражданин с един имейл; номерът на заявлението е уникален
        modelBuilder.Entity<Citizen>().HasIndex(c => c.Email).IsUnique();
        // Един профил на едно ЕГН
        modelBuilder.Entity<Citizen>().HasIndex(c => c.EgnHash).IsUnique().HasFilter("\"EgnHash\" <> ''");
        // Номерът се дава след първия запис (от поредния номер), затова празният номер не се брои
        modelBuilder.Entity<Application>().HasIndex(a => a.Number).IsUnique().HasFilter("\"Number\" <> ''");
        modelBuilder.Entity<Application>().HasOne(a => a.Citizen).WithMany(c => c.Applications)
            .HasForeignKey(a => a.CitizenId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Application>().HasOne(a => a.Service).WithMany()
            .HasForeignKey(a => a.ServiceId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ApplicationFile>().HasOne(f => f.Application).WithMany(a => a.Files)
            .HasForeignKey(f => f.ApplicationId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ApplicationEvent>().HasOne(e => e.Application).WithMany(a => a.Events)
            .HasForeignKey(e => e.ApplicationId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<CitizenToken>().HasIndex(t => t.TokenHash).IsUnique();
        modelBuilder.Entity<CitizenToken>().HasOne(t => t.Citizen).WithMany()
            .HasForeignKey(t => t.CitizenId).OnDelete(DeleteBehavior.Cascade);
        // Насрочените отговори се търсят по час на изпращане
        modelBuilder.Entity<ScheduledReply>().HasIndex(r => r.SendAt);
        modelBuilder.Entity<ScheduledReply>().HasOne(r => r.Application).WithMany()
            .HasForeignKey(r => r.ApplicationId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ScheduledReplyFile>().HasOne(f => f.Reply).WithMany(r => r.Files)
            .HasForeignKey(f => f.ScheduledReplyId).OnDelete(DeleteBehavior.Cascade);
    }
}

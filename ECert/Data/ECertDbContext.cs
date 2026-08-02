using ECert.Models;
using Microsoft.EntityFrameworkCore;

namespace ECert.Data;

public class ECertDbContext : DbContext
{
    public ECertDbContext(DbContextOptions<ECertDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<Instructor> Instructors => Set<Instructor>();
    public DbSet<Registration> Registrations => Set<Registration>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Certificate> Certificates => Set<Certificate>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<AppNotification> Notifications => Set<AppNotification>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<PhoneCountry> PhoneCountries => Set<PhoneCountry>();
    public DbSet<SiteSetting> SiteSettings => Set<SiteSetting>();
    public DbSet<HeroSlide> HeroSlides => Set<HeroSlide>();
    public DbSet<HeroAnimatedText> HeroAnimatedTexts => Set<HeroAnimatedText>();
    public DbSet<SocialLink> SocialLinks => Set<SocialLink>();
    public DbSet<ContactInfo> ContactInfos => Set<ContactInfo>();
    public DbSet<StatCard> StatCards => Set<StatCard>();
    public DbSet<HomepageSection> HomepageSections => Set<HomepageSection>();
    public DbSet<ThemeSetting> ThemeSettings => Set<ThemeSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Ignore computed properties
        modelBuilder.Entity<Course>().Ignore(c => c.AvailableSeats);
        modelBuilder.Entity<Course>().Ignore(c => c.FinalPrice);
        modelBuilder.Entity<Invoice>().Ignore(i => i.RemainingAmount);

        // User Roles
        modelBuilder.Entity<UserRole>()
            .HasOne(ur => ur.User)
            .WithMany(u => u.UserRoles)
            .HasForeignKey(ur => ur.UserId);

        modelBuilder.Entity<UserRole>()
            .HasOne(ur => ur.Role)
            .WithMany(r => r.UserRoles)
            .HasForeignKey(ur => ur.RoleId);

        // Role Permissions
        modelBuilder.Entity<RolePermission>()
            .HasOne(rp => rp.Role)
            .WithMany(r => r.RolePermissions)
            .HasForeignKey(rp => rp.RoleId);

        modelBuilder.Entity<RolePermission>()
            .HasOne(rp => rp.Permission)
            .WithMany(p => p.RolePermissions)
            .HasForeignKey(rp => rp.PermissionId);

        // Course relationships
        modelBuilder.Entity<Course>()
            .HasOne(c => c.Category)
            .WithMany(cat => cat.Courses)
            .HasForeignKey(c => c.CategoryId);

        modelBuilder.Entity<Course>()
            .HasOne(c => c.Instructor)
            .WithMany(i => i.Courses)
            .HasForeignKey(c => c.InstructorId);

        // Registration
        modelBuilder.Entity<Registration>()
            .HasOne(r => r.Course)
            .WithMany(c => c.Registrations)
            .HasForeignKey(r => r.CourseId);

        // Invoice
        modelBuilder.Entity<Invoice>()
            .HasOne(i => i.Registration)
            .WithOne(r => r.Invoice)
            .HasForeignKey<Invoice>(i => i.RegistrationId);

        // Payment
        modelBuilder.Entity<Payment>()
            .HasOne(p => p.Invoice)
            .WithMany(i => i.Payments)
            .HasForeignKey(p => p.InvoiceId);

        // Certificate
        modelBuilder.Entity<Certificate>()
            .HasOne(c => c.Registration)
            .WithOne(r => r.Certificate)
            .HasForeignKey<Certificate>(c => c.RegistrationId);

    }
}

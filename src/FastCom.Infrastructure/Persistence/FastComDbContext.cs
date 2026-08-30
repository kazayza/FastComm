using FastCom.Domain.Entities;              // 🔗 الـ 61 Entity المولّدة من الـ Scaffold
using FastCom.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Infrastructure.Persistence;

/// <summary>
/// الـ DbContext الرئيسي.
/// <para>
/// ⚠️ الـ Entities نفسها بتتولّد بأمر <c>dotnet ef dbcontext scaffold</c>
/// من قاعدة البيانات db65922 — مش مكتوبة يدوي.
/// شوف docs/SETUP.md
/// </para>
/// </summary>
/// <remarks>
/// ⚠️ <c>partial</c> — لأن <see cref="ScaffoldedDbContext"/> (الملف المولّد من الـ Scaffold)
/// هو الجزء التاني من نفس الكلاس. الـ DbSets والـ Fluent API Configurations
/// كلها جوه الجزء ده.
/// </remarks>
public partial class FastComDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, int>
{
    public FastComDbContext(DbContextOptions<FastComDbContext> options)
        : base(options)
    {
    }

    // ⬇️⬇️⬇️  DbSet properties هتتولّد تلقائيًا بعد الـ Scaffold  ⬇️⬇️⬇️
    // public DbSet<Booking> Bookings => Set<Booking>();
    // public DbSet<Operation> Operations => Set<Operation>();
    // ... إلخ

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ⚠️ مهم: Identity بتسمي الجداول AspNetUsers / AspNetRoles ...
        //         وإحنا عاملينهم كده فعلًا في الـ Schema، فمافيش تغيير مطلوب.

        // 🔗 هنا بنستدعي الجزء التاني من الـ partial class
        //    (اللي فيه الـ 61 DbSet + كل الـ Fluent API من الـ Scaffold)
        OnModelCreatingPartial(modelBuilder);
    }

    /// <summary>
    /// الجزء التاني من <see cref="OnModelCreating"/> —
    /// متنفّذ في <c>ScaffoldedDbContext.cs</c> (الملف المولّد).
    /// </summary>
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // 🔴 A8/A9: الـ Triggers في قاعدة البيانات بتزامن المجاميع،
        //          فمش محتاجين نحسبها هنا.
        //          بس هنضيف Audit Fields (CreatedAt/By, UpdatedAt/By) هنا.
        ApplyAuditFields();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        ApplyAuditFields();
        return base.SaveChanges();
    }

    private void ApplyAuditFields()
    {
        var now = DateTime.UtcNow;
        // TODO: نجيب الـ UserId من IHttpContextAccessor
        int? userId = null;

        foreach (var entry in ChangeTracker.Entries())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    TrySet(entry, "CreatedAt", now);
                    TrySet(entry, "CreatedBy", userId);
                    break;

                case EntityState.Modified:
                    TrySet(entry, "UpdatedAt", now);
                    TrySet(entry, "UpdatedBy", userId);
                    // منع تعديل الـ CreatedAt
                    if (entry.Properties.Any(p => p.Metadata.Name == "CreatedAt"))
                        entry.Property("CreatedAt").IsModified = false;
                    break;
            }
        }
    }

    private static void TrySet(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry,
                               string propName, object? value)
    {
        var prop = entry.Properties.FirstOrDefault(p => p.Metadata.Name == propName);
        if (prop is not null && !prop.IsModified)
            prop.CurrentValue = value;
    }
}

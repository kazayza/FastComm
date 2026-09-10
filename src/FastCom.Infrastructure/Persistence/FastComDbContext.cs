using System.Text.Json;
using FastCom.Domain.Entities;              // 🔗 الـ 61 Entity المولّدة من الـ Scaffold
using FastCom.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

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
    /// <summary>مصدر المستخدم الحالي لأغراض الـ Audit — <c>null</c> بره الطلب (DesignTime/Jobs).</summary>
    private readonly IUserContext? _userContext;

    public FastComDbContext(DbContextOptions<FastComDbContext> options,
                            IUserContext? userContext = null)
        : base(options)
    {
        _userContext = userContext;
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
        var userId = _userContext?.UserId;
        ApplyAuditFields(userId);
        WriteAuditLogs(userId, _userContext?.IpAddress, _userContext?.UserAgent);
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        var userId = _userContext?.UserId;
        ApplyAuditFields(userId);
        WriteAuditLogs(userId, _userContext?.IpAddress, _userContext?.UserAgent);
        return base.SaveChanges();
    }

    private void ApplyAuditFields(int? userId)
    {
        var now = DateTime.UtcNow;

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

    private static void TrySet(EntityEntry entry, string propName, object? value)
    {
        var prop = entry.Properties.FirstOrDefault(p => p.Metadata.Name == propName);
        if (prop is not null && !prop.IsModified)
            prop.CurrentValue = value;
    }

    // ═══════════════════════════════════════════════════════════════
    //  🔐 Audit Log — كتابة سجل في AuditLogs عند أي Create/Update/Delete
    //  (Create/Update/Delete فقط ضمن قيم CK_AuditLogs_Action المسموحة)
    // ═══════════════════════════════════════════════════════════════

    private void WriteAuditLogs(int? userId, string? ipAddress, string? userAgent)
    {
        // ── صورة ثابتة من التغييرات قبل ما نضيف سجلات الـ Audit ──
        var changes = ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(e => new
            {
                Entry  = e,
                Table  = e.Metadata.GetTableName(),
                State  = e.State,
                Entity = e.Entity
            })
            .Where(x => x.Table is not null &&
                        x.Table != "AuditLogs" &&
                        x.Table != "NumberSequences" &&
                        x.Table != "AspNetUserTokens" &&
                        x.Table != "AspNetUserLogins" &&
                        x.Table != "AspNetUserClaims" &&
                        x.Table != "AspNetRoleClaims")
            .ToArray();

        if (changes.Length == 0) return;

        var now = DateTime.UtcNow;

        foreach (var c in changes)
        {
            try
            {
                var action = ResolveAction(c.Entry, c.State, out var isSoftDelete);
                var key    = EntityKey(c.Entry);

                string? oldValues = null;
                string? newValues = null;

                if (c.State == EntityState.Deleted)
                {
                    oldValues = ToJson(ScalarValues(c.Entry, original: false));
                    if (isSoftDelete) newValues = "{\"IsDeleted\":true}";
                }
                else if (c.State == EntityState.Added)
                {
                    newValues = ToJson(ScalarValues(c.Entry, original: false));
                }
                else
                {
                    oldValues = ToJson(ScalarValues(c.Entry, original: true));
                    newValues = ToJson(ScalarValues(c.Entry, original: false));
                }

                AuditLogs.Add(new AuditLog
                {
                    UserId     = userId,
                    Action     = action,
                    EntityType = c.Entity.GetType().Name,
                    EntityId   = key,
                    OldValues  = oldValues,
                    NewValues  = newValues,
                    IpAddress  = Truncate(ipAddress, 64),
                    UserAgent  = Truncate(userAgent, 500),
                    CreatedAt  = now
                });
            }
            catch (Exception)
            {
                // Audit ممنوع يكسر العملية — سجل الفاشل يتجاهل
            }
        }
    }

    /// <summary>يحدد الـ Action المناسب — والـ Soft Delete بيتعامل معاه كـ Delete.</summary>
    private static string ResolveAction(EntityEntry entry, EntityState state, out bool isSoftDelete)
    {
        isSoftDelete = false;

        if (state == EntityState.Deleted) return "Delete";
        if (state == EntityState.Added)   return "Create";

        if (entry.Metadata.FindProperty("IsDeleted") is { } p)
        {
            var current  = entry.Property(p.Name).CurrentValue;
            var original = entry.Property(p.Name).OriginalValue;

            if (current is true && original is not true)
            {
                isSoftDelete = true;
                return "Delete";
            }
        }

        return "Update";
    }

    /// <summary>معرّف السجل (قيمة الـ Primary Key) — أو <c>null</c>.</summary>
    private static string? EntityKey(EntityEntry entry)
    {
        var keyProps = entry.Metadata.FindPrimaryKey()?.Properties;
        if (keyProps is null || keyProps.Count == 0) return null;

        var values = keyProps
            .Select(p => entry.Property(p.Name).CurrentValue?.ToString())
            .Where(v => !string.IsNullOrWhiteSpace(v));

        return string.Join("|", values);
    }

    /// <summary>قيم الأعمدة (سكيلار) للسجل — الحالية أو الأصلية (قبل التعديل).</summary>
    private static Dictionary<string, object?> ScalarValues(EntityEntry entry, bool original)
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var prop in entry.Properties)
        {
            if (original && !prop.IsModified) continue;

            var value = original ? prop.OriginalValue : prop.CurrentValue;
            dict[prop.Metadata.Name] = Normalize(value);
        }

        return dict;
    }

    /// <summary>بيقشّر القيم المعقدة ويتعامل مع الـ byte[] (الملفات) بصورة آمنة.</summary>
    private static object? Normalize(object? value) => value switch
    {
        null              => null,
        byte[] bytes      => bytes.Length > 0 ? $"[{bytes.Length} bytes]" : "[]",
        DateTime dt       => dt.ToString("o"),
        DateTimeOffset x  => x.ToString("o"),
        _                 => value
    };

    private static string? ToJson(object? value)
    {
        try
        {
            return value is null ? null : JsonSerializer.Serialize(value);
        }
        catch
        {
            return null;
        }
    }

    private static string? Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return null;
        return s.Length <= max ? s : s[..max];
    }
}

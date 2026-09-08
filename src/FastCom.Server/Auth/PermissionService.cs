using System.Security.Claims;
using Microsoft.Extensions.Caching.Memory;

namespace FastCom.Server.Auth;

/// <summary>
/// 🔐 خدمة الصلاحيات — الحقيقة الوحيدة هي قاعدة البيانات.
/// </summary>
/// <remarks>
/// <b>ليه مش بنحط الصلاحيات في الـ JWT؟</b>
/// 107 صلاحية × ~20 حرف = <b>4–5 KB</b> في كل HTTP Header.
/// <para>
/// فبدل كده: <b>الأدوار بس</b> في الـ JWT، والصلاحيات بتتجلب من هنا
/// مع <b>كاش 60 ثانية</b>.
/// </para>
/// </remarks>
public interface IPermissionService
{
    /// <summary>كل أكواد الصلاحيات بتاعة مستخدم.</summary>
    Task<List<string>> GetPermissionsAsync(int userId, CancellationToken ct = default);

    /// <summary>عنده الصلاحية دي؟</summary>
    Task<bool> HasAsync(int userId, string code, CancellationToken ct = default);

    /// <summary>🔧 بينضّف كاش مستخدم معيّن — بينادى عليها لما صلاحياته تتغير.</summary>
    void Invalidate(int userId);
}

/// <inheritdoc />
public class PermissionService : IPermissionService
{
    /// <summary>مدة الكاش — تغيير الصلاحيات بينفّذ خلال دقيقة من غير Logout.</summary>
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    private readonly TokenService _tokens;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PermissionService> _logger;

    public PermissionService(TokenService tokens, IMemoryCache cache, ILogger<PermissionService> logger)
    {
        _tokens = tokens;
        _cache  = cache;
        _logger = logger;
    }

    public async Task<List<string>> GetPermissionsAsync(int userId, CancellationToken ct = default)
    {
        var perms = await _cache.GetOrCreateAsync(
            Key(userId),
            entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = Ttl;
                return _tokens.GetPermissionsAsync(userId, ct);
            });

        return perms ?? new List<string>();
    }

    public async Task<bool> HasAsync(int userId, string code, CancellationToken ct = default)
    {
        var perms = await GetPermissionsAsync(userId, ct);
        return perms.Contains(code, StringComparer.OrdinalIgnoreCase);
    }

    public void Invalidate(int userId)
    {
        _cache.Remove(Key(userId));
        _logger.LogInformation("🔄 اتنضّف كاش صلاحيات المستخدم {UserId}", userId);
    }

    private static string Key(int userId) => $"fc:perms:{userId}";
}

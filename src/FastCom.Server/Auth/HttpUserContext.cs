using System.Security.Claims;
using FastCom.Infrastructure.Persistence;

namespace FastCom.Server.Auth;

/// <summary>
/// 🔐 تنفيذ <see cref="IUserContext"/> من الـ HTTP — بيقرا الـ UserId من الـ JWT claims.
/// </summary>
public sealed class HttpUserContext : IUserContext
{
    private readonly IHttpContextAccessor _accessor;

    public HttpUserContext(IHttpContextAccessor accessor) => _accessor = accessor;

    public int? UserId
    {
        get
        {
            var user = _accessor.HttpContext?.User;
            if (user is null) return null;

            var id = user.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? user.FindFirstValue("sub");

            return int.TryParse(id, out var i) ? i : null;
        }
    }

    public string? IpAddress =>
        _accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent =>
        _accessor.HttpContext?.Request.Headers.UserAgent.ToString();
}
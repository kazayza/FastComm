using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace FastCom.Infrastructure.Persistence;

/// <summary>
/// ⚙️  بيُستخدم بس وقت التصميم (Design Time) — يعني لأوامر dotnet ef.
///
/// 🔴  مهم: الأمر ده بيخلي الـ Scaffold يشتغل من غير ما نمرّر
///     الـ Connection String على سطر الأوامر (يعني مافيش كلمة سر في أي ملف script).
///
/// 📖  بيقرأ كلمة السر من:
///     1. Environment Variable:  FASTCOM_DB_CONNECTION
///     2. src/FastCom.Server/appsettings.json
///     3. src/FastCom.Server/bin/Debug/net8.0/appsettings.json
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<FastComDbContext>
{
    public FastComDbContext CreateDbContext(string[] args)
    {
        var connectionString = ResolveConnectionString();

        var options = new DbContextOptionsBuilder<FastComDbContext>()
            .UseSqlServer(connectionString, sql =>
            {
                sql.EnableRetryOnFailure(3);
            })
            .Options;

        return new FastComDbContext(options);
    }

    private static string ResolveConnectionString()
    {
        // ── 1) Environment Variable (الأنضف — مافيش كلمة سر في أي ملف) ──
        var fromEnv = Environment.GetEnvironmentVariable("FASTCOM_DB_CONNECTION");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv;

        // ── 2) ندوّر على appsettings.json ──
        var start = AppContext.BaseDirectory;   // bin/Debug/net8.0/
        var dir = new DirectoryInfo(start);

        // نطلع لفوق لحد ما نوصل لجذر المشروع (اللي فيه FastCom.sln)
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FastCom.sln")))
            dir = dir.Parent;

        var candidates = new List<string>();

        if (dir is not null)
        {
            var root = dir.FullName;
            candidates.Add(Path.Combine(root, "src", "FastCom.Server", "appsettings.json"));
            candidates.Add(Path.Combine(root, "src", "FastCom.Server", "bin", "Debug", "net8.0", "appsettings.json"));
        }

        candidates.Add(Path.Combine(start, "appsettings.json"));

        foreach (var path in candidates)
        {
            if (!File.Exists(path)) continue;

            var config = new ConfigurationBuilder()
                .AddJsonFile(path, optional: false)
                .Build();

            var cs = config.GetConnectionString("DefaultConnection");

            if (!string.IsNullOrWhiteSpace(cs) && !cs.Contains("PUT_YOUR_PASSWORD_HERE"))
                return cs;
        }

        throw new InvalidOperationException(
            "❌ مش لاقي Connection String صالح.\n\n" +
            "الحل — اختار واحد:\n\n" +
            "  (1) عدّل  src/FastCom.Server/appsettings.json\n" +
            "      وحط كلمة السر الحقيقية مكان PUT_YOUR_PASSWORD_HERE\n\n" +
            "  (2) أو اضبط Environment Variable:\n" +
            "      $env:FASTCOM_DB_CONNECTION = \"Server=...;Password=...;...\"\n");
    }
}

using System.Data;
using FastCom.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Server.Services;

/// <summary>ترقيم مركزي من usp_GetNextNumber — BOOKING/OPERATION/TRIP/... </summary>
public interface INumberingService
{
    Task<string> NextAsync(string documentType, CancellationToken ct = default);
}

public class NumberingService : INumberingService
{
    private readonly FastComDbContext _db;

    public NumberingService(FastComDbContext db) => _db = db;

    public async Task<string> NextAsync(string documentType, CancellationToken ct = default)
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        cmd.CommandType    = CommandType.StoredProcedure;
        cmd.CommandText    = "usp_GetNextNumber";
        cmd.CommandTimeout = 30;

        var pType = cmd.CreateParameter();
        pType.ParameterName = "@DocumentType";
        pType.Value = documentType;
        cmd.Parameters.Add(pType);

        var pBranch = cmd.CreateParameter();
        pBranch.ParameterName = "@BranchId";
        pBranch.Value = DBNull.Value;
        cmd.Parameters.Add(pBranch);

        var pOut = cmd.CreateParameter();
        pOut.ParameterName = "@NextNumber";
        pOut.Direction     = ParameterDirection.Output;
        pOut.Size          = 40;
        cmd.Parameters.Add(pOut);

        /* 🔴 الـ SP بترمي `THROW 50001` (قفل) و`THROW 50002` (سلسلة مش موجودة).
         *    لو سبناها SqlException هتطلع 500 غير مفهومة + stack trace في اللوج.
         *    بنحوّلها `InvalidOperationException` برسالة عربية واضحة — فأي Controller
         *    بيقبض عليها ويرجّع 400 برسالة تتعرض للمستخدم بدل ما السيرفر يقع.
         *    الإصلاح هنا بيحمي كل الـ 19 مكان اللي بينادوا NextAsync، مش واحد بس. */
        try
        {
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (SqlException ex) when (ex.Number is 50001 or 50002)
        {
            throw new InvalidOperationException(
                ex.Number == 50001
                    ? $"تعذّر الحصول على قفل الترقيم لنوع ({documentType}). حاول تاني."
                    : $"سلسلة الترقيم مش معرّفة لنوع ({documentType}) لسنة {DateTime.UtcNow.Year}. " +
                      $"ضيفها من شاشة «الترقيم» الأول.",
                ex);
        }

        return (string)pOut.Value!;
    }
}

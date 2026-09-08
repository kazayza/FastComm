using System.Data;
using FastCom.Infrastructure.Persistence;
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

        await cmd.ExecuteNonQueryAsync(ct);
        return (string)pOut.Value!;
    }
}

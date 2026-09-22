using FastCom.Domain.Entities;
using FastCom.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FastCom.Server.Services;

/// <summary>
/// 🔴 دفتر الخزينة — **المكان الوحيد** اللي بيكتب في <c>CashTransactions</c>
/// من خارج شاشة الخزينة نفسها.
/// </summary>
/// <remarks>
/// <para><b>ليه خدمة واحدة؟</b> المدفوعات والمصروفات والعهد كلها لازم تنعكس في الخزينة.
/// لو كل كنترولر كتب بنفسه، هتتكرر القواعد وهتحصل ثغرات (وأهمها العدّ المزدوج).</para>
///
/// <para><b>قاعدة منع العدّ المزدوج:</b></para>
/// <list type="bullet">
///   <item>العهدة وقت <b>الصرف</b> = فلوس خرجت من الخزينة → <c>Payment</c></item>
///   <item>المصروف <b>المربوط بعهدة</b> = خرج من العهدة <b>مش من الخزينة</b> → ⛔ مافيش حركة</item>
///   <item>المصروف <b>من غير عهدة</b> = خرج من الخزينة → <c>Payment</c></item>
///   <item>رد باقي العهدة = فلوس رجعت للخزينة → <c>Receipt</c></item>
/// </list>
///
/// <para><b>Idempotent:</b> كل حركة ليها <c>ReferenceNumber</c> فريد.
/// لو اتنادت مرتين بنفس المرجع مش بتعمل حاجة — فمافيش خطر تكرار.</para>
///
/// <para><b>Fail-open:</b> لو مافيش خزينة نشطة، بيرجّع <c>false</c> ويسجّل تحذير —
/// <b>مابيوقّعش العملية المالية</b>. الخزينة مكملة، مش شرط.</para>
/// </remarks>
public interface ICashBook
{
    /// <summary>فلوس دخلت الخزينة. <c>false</c> لو مافيش خزينة نشطة.</summary>
    Task<bool> ReceiptAsync(CashEntry e, CancellationToken ct = default);

    /// <summary>فلوس خرجت من الخزينة. <c>false</c> لو مافيش خزينة نشطة.</summary>
    Task<bool> PaymentAsync(CashEntry e, CancellationToken ct = default);

    /// <summary>
    /// بيلغي كل الحركات المرتبطة بمرجع (بالـ prefix).
    /// بيستخدم نمط المشروع: <c>Status = "Void"</c> — والـ trigger بيستثنيها من الرصيد.
    /// </summary>
    Task<int> VoidByReferenceAsync(string referencePrefix, CancellationToken ct = default);
}

/// <summary>بيانات حركة خزينة.</summary>
/// <param name="ReferenceNumber">معرّف فريد — أساس منع التكرار.</param>
public sealed record CashEntry(
    string  ReferenceNumber,
    decimal Amount,
    int     PaymentMethodId,
    int     CustomerId       = 0,
    int     SupplierId       = 0,
    long    OperationId      = 0,
    long    InvoiceId        = 0,
    long    CustodyId        = 0,
    string? Description      = null,
    DateOnly? Date           = null);

/// <inheritdoc cref="ICashBook"/>
public sealed class CashBook : ICashBook
{
    /* 🔴 المرحلة 2: كان ثابت "MAIN" — بقى من الإعداد `TREASURY.DEFAULT_BOX`.
       ولو الخزينة دي مقفولة/مش موجودة → أول خزينة مفتوحة. */
    private readonly FastComDbContext _db;
    private readonly ILogger<CashBook> _log;
    private readonly ISettingsService _settings;

    public CashBook(FastComDbContext db, ILogger<CashBook> log, ISettingsService settings)
    { _db = db; _log = log; _settings = settings; }

    // ═══════════════ Public ═══════════════

    public Task<bool> ReceiptAsync(CashEntry e, CancellationToken ct = default) =>
        AddAsync(e, "Receipt", ct);

    public Task<bool> PaymentAsync(CashEntry e, CancellationToken ct = default) =>
        AddAsync(e, "Payment", ct);

    public async Task<int> VoidByReferenceAsync(string referencePrefix, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(referencePrefix)) return 0;

        var rows = await _db.CashTransactions
            .Where(t => !t.IsDeleted && t.Status == "Posted"
                        && t.ReferenceNumber != null
                        && t.ReferenceNumber.StartsWith(referencePrefix))
            .ToListAsync(ct);

        if (rows.Count == 0) return 0;

        foreach (var t in rows) t.Status = "Void";   // الـ trigger بيحدّث الرصيد
        return rows.Count;
    }

    // ═══════════════ Core ═══════════════

    private async Task<bool> AddAsync(CashEntry e, string type, CancellationToken ct)
    {
        if (e is null) return false;
        if (e.Amount <= 0)
        {
            _log.LogWarning("CashBook: مبلغ غير صالح ({Amount}) للمرجع {Ref}", e.Amount, e.ReferenceNumber);
            return false;
        }

        /* 🔴 Idempotency — لو الحركة موجودة أصلًا (بنفس المرجع) مش بنعمل حاجة.
           ده بيحمي من أي إعادة نداء أو retry. */
        if (await _db.CashTransactions.AnyAsync(t =>
                !t.IsDeleted && t.ReferenceNumber == e.ReferenceNumber, ct))
        {
            _log.LogInformation("CashBook: الحركة {Ref} موجودة أصلًا — اتخطت", e.ReferenceNumber);
            return true;
        }

        var box = await ResolveBoxAsync(ct);
        if (box is null)
        {
            _log.LogWarning("CashBook: مافيش خزينة مفتوحة — الحركة {Ref} ما اتسجلتش",
                            e.ReferenceNumber);
            return false;
        }

        _db.CashTransactions.Add(new CashTransaction
        {
            BranchId        = box.BranchId,
            CashBoxId       = box.CashBoxId,
            TransactionDate = (e.Date ?? DateOnly.FromDateTime(DateTime.Today)).ToDateTime(TimeOnly.MinValue),
            TransactionType = type,
            Amount          = e.Amount,
            PaymentMethodId = e.PaymentMethodId,
            CustomerId      = e.CustomerId  == 0 ? null : e.CustomerId,
            SupplierId      = e.SupplierId  == 0 ? null : e.SupplierId,
            OperationId     = e.OperationId == 0 ? null : e.OperationId,
            InvoiceId       = e.InvoiceId   == 0 ? null : e.InvoiceId,
            CustodyId       = e.CustodyId   == 0 ? null : e.CustodyId,
            ReferenceNumber = e.ReferenceNumber,
            Description     = e.Description,
            Status          = "Posted",
            CreatedBy       = null
        });

        return true;
    }

    /// <summary>
    /// الخزينة الافتراضية: كودها من الإعداد <c>TREASURY.DEFAULT_BOX</c> (الافتراضي MAIN) —
    /// ولو مش موجودة أو مقفولة، أول خزينة نشطة ومفتوحة.
    /// </summary>
    private async Task<CashBox?> ResolveBoxAsync(CancellationToken ct)
    {
        var code = await _settings.GetStringAsync(SettingKeys.TreasuryDefaultBox,
                                                  SettingDefaults.TreasuryDefaultBox, ct);
        if (string.IsNullOrWhiteSpace(code)) code = SettingDefaults.TreasuryDefaultBox;

        var box = await _db.CashBoxes.AsNoTracking()
            .Where(b => !b.IsDeleted && b.IsActive && b.Status == "Open" && b.Code == code)
            .OrderBy(b => b.CashBoxId)
            .FirstOrDefaultAsync(ct);

        return box ?? await _db.CashBoxes.AsNoTracking()
            .Where(b => !b.IsDeleted && b.IsActive && b.Status == "Open")
            .OrderBy(b => b.CashBoxId)
            .FirstOrDefaultAsync(ct);
    }
}

<#
=====================================================================================
  FastCom - توليد الـ Entities من قاعدة البيانات db65922
=====================================================================================

  [OK] مافيش كلمة سر في الملف ده - بتتقرا من:
        src/FastCom.Server/appsettings.json

  طريقة التشغيل (من PowerShell):
        powershell -ExecutionPolicy Bypass -File .\scaffold.ps1

  الناتج:
        src/FastCom.Infrastructure/Persistence/Generated/*.cs   (61 Entity)
        src/FastCom.Infrastructure/Persistence/ScaffoldedDbContext.cs  (مؤقت)

  [!] ScaffoldedDbContext.cs مؤقت - هننقل الـ DbSets منه لـ FastComDbContext.cs
      وبعدين نمسحه. شوف docs/VSCode-Steps.md المرحلة 4.
=====================================================================================
#>

$ErrorActionPreference = "Stop"

# ── 1) نتأكد إننا في جذر المشروع ──────────────────────────────────────────────
if (-not (Test-Path "FastCom.sln")) {
    Write-Host ""
    Write-Host "  [X] لازم تشغّل السكربت من جذر المشروع (اللي فيه FastCom.sln)" -ForegroundColor Red
    Write-Host ""
    Write-Host "     cd E:\Transport\FastCom-project\FastCom" -ForegroundColor Yellow
    Write-Host "     powershell -ExecutionPolicy Bypass -File .\scaffold.ps1" -ForegroundColor Yellow
    Write-Host ""
    Read-Host "اضغط Enter للخروج"
    exit 1
}

# ── 2) نقرا الـ Connection String من appsettings.json ─────────────────────────
$appsettings = "src\FastCom.Server\appsettings.json"

if (-not (Test-Path $appsettings)) {
    Write-Host "  [X] مش لاقي $appsettings" -ForegroundColor Red
    Read-Host "اضغط Enter للخروج"
    exit 1
}

$config = Get-Content $appsettings -Raw | ConvertFrom-Json
$cs = $config.ConnectionStrings.DefaultConnection

if ([string]::IsNullOrWhiteSpace($cs)) {
    Write-Host "  [X] ConnectionStrings:DefaultConnection فاضي في appsettings.json" -ForegroundColor Red
    Read-Host "اضغط Enter للخروج"
    exit 1
}

if ($cs -match "PUT_YOUR_PASSWORD_HERE") {
    Write-Host ""
    Write-Host "  [X] لسه ما حطيتش كلمة السر الحقيقية" -ForegroundColor Red
    Write-Host ""
    Write-Host "     افتح: src\FastCom.Server\appsettings.json" -ForegroundColor Yellow
    Write-Host "     وغيّر  PUT_YOUR_PASSWORD_HERE  لكلمة السر" -ForegroundColor Yellow
    Write-Host ""
    Read-Host "اضغط Enter للخروج"
    exit 1
}

# ── 3) نخفي كلمة السر في العرض ────────────────────────────────────────────────
$masked = $cs -replace 'Password=[^;]*', 'Password=********'

Write-Host ""
Write-Host "  ==============================================================" -ForegroundColor Cyan
Write-Host "   FastCom - توليد الـ Entities" -ForegroundColor Cyan
Write-Host "  ==============================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Connection String:" -ForegroundColor Gray
Write-Host "    $masked" -ForegroundColor DarkGray
Write-Host ""

# ── 4) ننفذ الـ Scaffold ──────────────────────────────────────────────────────
Write-Host "  ... شغال (ممكن ياخد دقيقة أو تلاتة)" -ForegroundColor Yellow
Write-Host ""

& dotnet dotnet-ef dbcontext scaffold `
    $cs `
    Microsoft.EntityFrameworkCore.SqlServer `
    --project src/FastCom.Infrastructure `
    --startup-project src/FastCom.Server `
    --output-dir Persistence/Generated `
    --context-dir Persistence `
    --context ScaffoldedDbContext `
    --namespace FastCom.Domain.Entities `
    --context-namespace FastCom.Infrastructure.Persistence `
    --data-annotations `
    --no-onconfiguring `
    --force `
    --table Branches --table CompanyProfile --table Departments --table JobTitles --table SystemSettings --table DocumentTypes --table PaymentTerms --table PaymentMethods --table TaxRates --table Ports --table Destinations --table TripTypes --table Services --table ContainerTypes --table ExpenseTypes --table OperationStatuses --table Customers --table CustomerContacts --table Suppliers --table Employees --table Drivers --table Vehicles --table Trailers --table VehicleMaintenance --table PriceLists --table CustomerPriceRules --table Bookings --table Containers --table ContainerMovements --table BookingContainerLines --table BookingContainerDetails --table Operations --table Trips --table TripOperations --table TripCostAllocations --table OperationContainers --table OperationRevenueItems --table DriverCustodies --table CustodyTransactions --table Expenses --table Invoices --table InvoiceItems --table InvoiceOperations --table Payments --table PaymentAllocations --table InvoiceSendLogs --table SupplierInvoices --table SupplierInvoiceItems --table SupplierPayments --table SupplierInvoiceAllocations --table CashBoxes --table CashTransactions --table Documents --table Notifications --table CustomerPortalTokens --table PortalAccessLogs --table AppPermissions --table AppRolePermissions --table AppUserPermissions --table AuditLogs --table NumberSequences

$exitCode = $LASTEXITCODE

Write-Host ""
Write-Host "  ==============================================================" -ForegroundColor Cyan

if ($exitCode -eq 0) {
    $count = (Get-ChildItem "src\FastCom.Infrastructure\Persistence\Generated" -ErrorAction SilentlyContinue).Count
    Write-Host "   [OK] نجح" -ForegroundColor Green
    Write-Host ""
    Write-Host "   عدد الملفات في Generated: $count  (المفروض 61)" -ForegroundColor Green
    Write-Host ""
    Write-Host "   >> الخطوة الجاية: docs/VSCode-Steps.md  المرحلة 4" -ForegroundColor Yellow
} else {
    Write-Host "   [X] فشل (Exit Code: $exitCode)" -ForegroundColor Red
    Write-Host ""
    Write-Host "   ابعت الرسالة اللي فوق كاملة." -ForegroundColor Yellow
}

Write-Host "  ==============================================================" -ForegroundColor Cyan
Write-Host ""
Read-Host "اضغط Enter للخروج"

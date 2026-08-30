<#
=====================================================================================
  FastCom - Merge ScaffoldedDbContext into FastComDbContext   (v6 - FINAL)
=====================================================================================

  EF Core scaffold بيولّد (993 سطر):

      public partial class ScaffoldedDbContext : DbContext
      {
          public ScaffoldedDbContext(DbContextOptions<ScaffoldedDbContext> options)
              : base(options)
          {
          }

          public virtual DbSet<...> ... { get; set; }     x 61

          protected override void OnModelCreating(ModelBuilder modelBuilder)
          {
              modelBuilder.Entity<...>(entity => { ... });   x 61   (~850 سطر)
              ...
              OnModelCreatingPartial(modelBuilder);
          }

          partial void OnModelCreatingPartial(ModelBuilder modelBuilder);    <- فاضي
      }

  السكربت بيحوّله لـ:

      public partial class FastComDbContext
      {
          public virtual DbSet<...> ... { get; set; }     x 61

          partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
          {
              modelBuilder.Entity<...>(entity => { ... });   x 61
              ...
              modelBuilder.ApplySoftDeleteFilters();
          }
      }

  يعني 5 خطوات:
    [1] class name  ScaffoldedDbContext -> FastComDbContext  (+ شيل ': DbContext')
    [2] شيل الـ constructor
    [3] شيل سطر 'OnModelCreatingPartial(modelBuilder);'  (كان هيعمل infinite recursion)
    [4] protected override void OnModelCreating(...)  ->  partial void OnModelCreatingPartial(...)
    [5] شيل الـ declaration الفاضي + ضيف ApplySoftDeleteFilters()

  [OK] IDEMPOTENT

  Usage:
      powershell -ExecutionPolicy Bypass -File .\tools\merge-dbcontext.ps1
=====================================================================================
#>

$ErrorActionPreference = "Stop"
$target = "src\FastCom.Infrastructure\Persistence\ScaffoldedDbContext.cs"

function Fail([string]$m) {
    Write-Host ""
    Write-Host ("  [X] " + $m) -ForegroundColor Red
    Write-Host ""
    Read-Host "  Press Enter"
    exit 1
}

# ── بيلاقي } المتوازن لـ { معين ───────────────────────────────────────────────
function Find-BlockEnd([string]$src, [int]$openBrace) {
    $depth = 0
    $i = $openBrace
    while ($i -lt $src.Length) {
        $ch = $src[$i]
        if ($ch -eq '{') { $depth++ }
        elseif ($ch -eq '}') {
            $depth--
            if ($depth -eq 0) { return $i + 1 }
        }
        $i++
    }
    return -1
}

if (-not (Test-Path "FastCom.sln")) { Fail "Run this from the project root (where FastCom.sln is)." }
if (-not (Test-Path $target))       { Fail ("Not found: " + $target) }

$text    = Get-Content $target -Raw -Encoding UTF8
$changes = @()

Write-Host ""
Write-Host "  ============================================================== " -ForegroundColor Cyan
Write-Host "   FastCom - Merge DbContext  (v6)" -ForegroundColor Cyan
Write-Host "  ============================================================== " -ForegroundColor Cyan
Write-Host ""

# ══ [1] اسم الكلاس ════════════════════════════════════════════════════════════
$re1 = 'public\s+(?:partial\s+)?class\s+ScaffoldedDbContext\s*:\s*DbContext'
if ([regex]::IsMatch($text, $re1)) {
    $text = [regex]::Replace($text, $re1, 'public partial class FastComDbContext')
    $changes += "[1] class ScaffoldedDbContext : DbContext  ->  partial class FastComDbContext"
}
elseif ($text -match 'public\s+partial\s+class\s+FastComDbContext') {
    Write-Host "  [1] already done" -ForegroundColor DarkGray
}
else {
    Fail "Could not find the class declaration."
}

# ══ [2] الـ constructor ═══════════════════════════════════════════════════════
$reCtor = '(?m)^[ \t]*public\s+ScaffoldedDbContext\s*\('
$mCtor  = [regex]::Match($text, $reCtor)
if ($mCtor.Success) {
    $braceIdx = $text.IndexOf('{', $mCtor.Index)
    if ($braceIdx -lt 0) { Fail "Could not find the constructor's opening brace." }
    $end = Find-BlockEnd $text $braceIdx
    if ($end -lt 0) { Fail "Unbalanced braces in the constructor." }
    $text = $text.Remove($mCtor.Index, $end - $mCtor.Index)
    $changes += "[2] removed the ScaffoldedDbContext(DbContextOptions<...>) constructor"
}
else {
    Write-Host "  [2] no constructor to remove" -ForegroundColor DarkGray
}

# ══ [3] شيل نداء OnModelCreatingPartial (كان هيعمل infinite recursion) ════════
$reCall = '(?m)^[ \t]*OnModelCreatingPartial\s*\(\s*modelBuilder\s*\)\s*;'
if ([regex]::IsMatch($text, $reCall)) {
    $text = [regex]::Replace($text, $reCall, '// (removed - would recurse)')
    $changes += "[3] removed the 'OnModelCreatingPartial(modelBuilder);' call (would recurse after the rename)"
}
else {
    Write-Host "  [3] no recursive call to remove" -ForegroundColor DarkGray
}

# ══ [4] OnModelCreating -> OnModelCreatingPartial ═════════════════════════════
$reOmc = 'protected\s+override\s+void\s+OnModelCreating\s*\(\s*ModelBuilder\s+(\w+)\s*\)'
$mOmc  = [regex]::Match($text, $reOmc)
if ($mOmc.Success) {
    $pn   = $mOmc.Groups[1].Value
    $text = $text.Remove($mOmc.Index, $mOmc.Length)
    $text = $text.Insert($mOmc.Index, "partial void OnModelCreatingPartial(ModelBuilder $pn)")
    $changes += "[4] 'protected override void OnModelCreating(...)'  ->  'partial void OnModelCreatingPartial(...)'"
}
elseif ($text -match 'partial\s+void\s+OnModelCreatingPartial\s*\(\s*ModelBuilder\s+\w+\s*\)\s*\{') {
    Write-Host "  [4] already done" -ForegroundColor DarkGray
}
else {
    Fail "Could not find 'protected override void OnModelCreating(...)'"
}

# ══ [5] شيل الـ declaration الفاضي + ضيف ApplySoftDeleteFilters ═══════════════
$reDecl = '(?m)^[ \t]*partial\s+void\s+OnModelCreatingPartial\s*\(\s*ModelBuilder\s+\w+\s*\)\s*;'
if ([regex]::IsMatch($text, $reDecl)) {
    $text = [regex]::Replace($text, $reDecl, '')
    $changes += "[5] removed the empty 'partial void OnModelCreatingPartial(...);' declaration"
}

if ($text -notmatch 'ApplySoftDeleteFilters') {
    $reImpl = '(?m)^[ \t]*partial\s+void\s+OnModelCreatingPartial\s*\(\s*ModelBuilder\s+(\w+)\s*\)\s*\{'
    $mImpl  = [regex]::Match($text, $reImpl)
    if (-not $mImpl.Success) { Fail "Could not find 'partial void OnModelCreatingPartial(...) {'" }

    $pn2      = $mImpl.Groups[1].Value
    $braceIdx = $text.IndexOf('{', $mImpl.Index)
    $end      = Find-BlockEnd $text $braceIdx
    if ($end -lt 0) { Fail "Unbalanced braces in OnModelCreatingPartial." }

    $snippet = ""
    $snippet += "`r`n"
    $snippet += "        // ============================================================`r`n"
    $snippet += "        //  FastCom: Global Query Filter for soft-deleted rows`r`n"
    $snippet += "        //  (every query now ignores rows where IsDeleted = 1)`r`n"
    $snippet += "        // ============================================================`r`n"
    $snippet += "        " + $pn2 + ".ApplySoftDeleteFilters();"

    $text = $text.Insert($end - 1, $snippet + "`r`n    ")
    $changes += ("[6] + " + $pn2 + ".ApplySoftDeleteFilters();")
}
else {
    Write-Host "  [6] ApplySoftDeleteFilters already there" -ForegroundColor DarkGray
}

# ══ save ═════════════════════════════════════════════════════════════════════
if ($changes.Count -gt 0) {
    $enc  = New-Object System.Text.UTF8Encoding($true)
    $full = (Resolve-Path $target).Path
    [System.IO.File]::WriteAllText($full, $text, $enc)
}

Write-Host ""
foreach ($c in $changes) { Write-Host ("  " + $c) -ForegroundColor Green }

Write-Host ""
Write-Host "  -------------------------------------------------------------- " -ForegroundColor DarkGray
Write-Host ""

# ══ Self-check ════════════════════════════════════════════════════════════════
$open  = ([regex]::Matches($text, '\{')).Count
$close = ([regex]::Matches($text, '\}')).Count
$nDbs  = ([regex]::Matches($text, 'public\s+virtual\s+DbSet<')).Count
$nEnt  = ([regex]::Matches($text, 'modelBuilder\.Entity<')).Count

$ok1 = ($open -eq $close)
$ok2 = ($text -match 'public\s+partial\s+class\s+FastComDbContext')
$ok3 = ($text -notmatch 'ScaffoldedDbContext')
$ok4 = ($text -notmatch 'protected\s+override\s+void\s+OnModelCreating\s*\(')
$ok5 = ([regex]::Matches($text, 'partial\s+void\s+OnModelCreatingPartial\s*\(').Count -eq 1)
$ok6 = ($text -match 'ApplySoftDeleteFilters\(\);')
$ok7 = ($nDbs -eq 61)
$ok8 = ($nEnt -ge 55)

Write-Host "  Self-check:" -ForegroundColor Cyan
function Chk([string]$lbl, [bool]$ok, [string]$extra) {
    $col = "Red"; $st = "FAIL"
    if ($ok) { $col = "Green"; $st = "OK" }
    Write-Host ("    " + $lbl.PadRight(38) + ": " + $st + "  " + $extra) -ForegroundColor $col
}
Chk "braces balanced"            $ok1 ("{" + $open + "}/" + $close)
Chk "partial class FastComDbContext" $ok2 ""
Chk "ScaffoldedDbContext gone"   $ok3 ""
Chk "OnModelCreating gone"       $ok4 ""
Chk "OnModelCreatingPartial x1"  $ok5 ""
Chk "ApplySoftDeleteFilters"     $ok6 ""
Chk "DbSet count = 61"           $ok7 ("found " + $nDbs)
Chk "Entity config kept"         $ok8 ("found " + $nEnt)

Write-Host ""
if ($ok1 -and $ok2 -and $ok3 -and $ok4 -and $ok5 -and $ok6 -and $ok7 -and $ok8) {
    Write-Host "  [OK] Next:  dotnet build" -ForegroundColor Green
} else {
    Write-Host "  [X] Self-check FAILED - send me the output above" -ForegroundColor Red
}
Write-Host ""
Read-Host "  Press Enter"

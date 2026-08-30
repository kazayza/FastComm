# ==============================================================================
# FastCom — استخراج اللوجو من logo.pdf
# ------------------------------------------------------------------------------
# الـ PDF معمول من Photoshop: صورة CMYK JPEG (YCCK معكوسة) + قناة شفافية SMask
# مضغوطة بـ FlateDecode + PNG Predictor.
# الناتج: docs/brand/logo-true.png (الألوان الحقيقية) + logo-blue.png + logo-white.png
#         + favicon.png + wwwroot/img/logo.png
# الاستخدام:  powershell -File tools\extract-logo.ps1
# ==============================================================================
$ErrorActionPreference = 'Stop'

$root    = Split-Path -Parent $PSScriptRoot
$pdfPath = Join-Path $root 'logo.pdf'
$brandDir  = Join-Path $root 'docs\brand'
$imgDir    = Join-Path $root 'src\FastCom.Client\wwwroot\img'
New-Item -ItemType Directory -Force -Path $brandDir, $imgDir | Out-Null

# ── مساعد C# — فكّ PNG Predictor + التركيب ────────────────────────────────────
$src = @'
using System;
using System.IO;
public static class PdfTools {
    // فك PNG per-row filters (bpc=8)
    public static byte[] Unpredict(byte[] data, int width, int height, int components) {
        int bpp = components;
        int stride = width * bpp;
        byte[] res = new byte[height * stride];
        int pos = 0;
        for (int y = 0; y < height; y++) {
            if (pos >= data.Length) break;
            byte filter = data[pos++];
            int rowStart = y * stride;
            for (int x = 0; x < stride; x++) {
                if (pos + x >= data.Length) break;
                int raw = data[pos + x];
                int a = x >= bpp ? res[rowStart + x - bpp] : 0;
                int b = y > 0 ? res[rowStart - stride + x] : 0;
                int c = (x >= bpp && y > 0) ? res[rowStart - stride + x - bpp] : 0;
                int val;
                switch (filter) {
                    case 1: val = raw + a; break;
                    case 2: val = raw + b; break;
                    case 3: val = raw + ((a + b) >> 1); break;
                    case 4: {
                        int p = a + b - c;
                        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                        val = raw + (pa <= pb && pa <= pc ? a : (pb <= pc ? b : c));
                        break;
                    }
                    default: val = raw; break;
                }
                res[rowStart + x] = (byte)val;
            }
            pos += stride;
        }
        return res;
    }

    // CMYK (WPF Cmyk32: C,M,Y,255-K) + alpha mask -> BGRA
    // 🔴 الـ mask = باتش مستطيل (صورة اللوجو على صفحة شفافة في Photoshop)
    //    فبنشيل الأبيض الخارجي بـ flood-fill من حدود الباتش (مش chroma-key عام
    //    عشان منعملش ثقوب في أي أبيض داخل الرسمة نفسها)
    public static string Composite(byte[] cmyk, byte[] alpha, int w, int h,
                                   byte fr, byte fg, byte fb, string mode, string tmpPath) {
        var bgra = new byte[w * h * 4];
        for (int i = 0, p4 = 0; i < w * h; i++, p4 += 4) {
            double C = cmyk[p4], M = cmyk[p4 + 1], Y = cmyk[p4 + 2], K = 255 - cmyk[p4 + 3];
            int r = (int)Math.Round(255 * (1 - C / 255.0) * (1 - K / 255.0));
            int g = (int)Math.Round(255 * (1 - M / 255.0) * (1 - K / 255.0));
            int b = (int)Math.Round(255 * (1 - Y / 255.0) * (1 - K / 255.0));
            bgra[p4] = (byte)b; bgra[p4 + 1] = (byte)g; bgra[p4 + 2] = (byte)r; bgra[p4 + 3] = alpha[i];
        }

        // ── إزالة الأبيض الخارجي: بذور = أول وآخر صف معتم (حواف الباتش العلوية/السفلية) ──
        int keyed = 0, firstRow = -1, lastRow = -1;
        for (int y = 0; y < h && firstRow < 0; y++)
            for (int x = 0; x < w; x++)
                if (bgra[(y * w + x) * 4 + 3] > 0) { firstRow = y; break; }
        for (int y = h - 1; y >= 0 && lastRow < 0; y--)
            for (int x = 0; x < w; x++)
                if (bgra[(y * w + x) * 4 + 3] > 0) { lastRow = y; break; }
        if (firstRow >= 0) {
            var visited = new bool[w * h];
            var stack = new int[w * h];
            int sp = 0;
            byte thr = 236;
            for (int x = 0; x < w; x++) {
                Push(stack, ref sp, visited, bgra, w, h, x, firstRow, thr);
                Push(stack, ref sp, visited, bgra, w, h, x, lastRow, thr);
            }
            while (sp > 0) {
                int i2 = stack[--sp];
                bgra[i2 * 4 + 3] = 0;
                keyed++;
                int x = i2 % w, y = i2 / w;
                Push(stack, ref sp, visited, bgra, w, h, x + 1, y, thr);
                Push(stack, ref sp, visited, bgra, w, h, x - 1, y, thr);
                Push(stack, ref sp, visited, bgra, w, h, x, y + 1, thr);
                Push(stack, ref sp, visited, bgra, w, h, x, y - 1, thr);
            }
        }

        // ── fill mode: نلوّن البكسلات المتبقية (غير الأبيض الممسوح) بلون موحّد ──
        if (mode == "fill") {
            for (int p4 = 0; p4 < bgra.Length; p4 += 4)
                if (bgra[p4 + 3] > 0) { bgra[p4] = fb; bgra[p4 + 1] = fg; bgra[p4 + 2] = fr; }
        }

        // ── إحصائيات + BBOX على المحتوى الفعلي (غير الشفاف) ──
        var counts = new System.Collections.Generic.Dictionary<string, int>();
        int opaque = 0; long sR = 0, sG = 0, sB = 0;
        int minX = w, minY = h, maxX = 0, maxY = 0;
        for (int i = 0, p4 = 0; i < w * h; i++, p4 += 4) {
            if (bgra[p4 + 3] <= 64) continue;
            opaque++;
            int x = i % w, y = i / w;
            if (x < minX) minX = x; if (x > maxX) maxX = x;
            if (y < minY) minY = y; if (y > maxY) maxY = y;
            sR += bgra[p4 + 2]; sG += bgra[p4 + 1]; sB += bgra[p4];
            string key = string.Format("#{0:X2}{1:X2}{2:X2}", bgra[p4 + 2] & 0xF0, bgra[p4 + 1] & 0xF0, bgra[p4] & 0xF0);
            if (counts.ContainsKey(key)) counts[key]++; else counts[key] = 1;
        }
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("PATCH-OPAQUE=" + (w * h - keyed) + "  WHITE-KEYED=" + keyed);
        if (opaque > 0) {
            sb.AppendLine("CONTENT-OPAQUE=" + opaque + " (" + (100.0 * opaque / (w * h)).ToString("0.00") + "%)");
            sb.AppendLine("BBOX x=" + minX + "-" + maxX + "  y=" + minY + "-" + maxY);
            sb.AppendLine("AVG=#" + (sR / opaque).ToString("X2") + (sG / opaque).ToString("X2") + (sB / opaque).ToString("X2"));
            foreach (var kv in counts) { sb.AppendLine("COLOR " + kv.Key + " n=" + kv.Value); }
        }
        File.WriteAllBytes(tmpPath, bgra);
        return sb.ToString();
    }

    static bool IsNearWhite(byte[] bgra, int p4, byte thr) {
        return bgra[p4 + 2] >= thr && bgra[p4 + 1] >= thr && bgra[p4] >= thr;
    }

    // بذرة فلود: ندخل بس على البكسلات الأبيض القريبة من الأبيض وجوه الباتش
    static void Push(int[] stack, ref int sp, bool[] visited, byte[] bgra,
                     int w, int h, int x, int y, byte thr) {
        if (x < 0 || y < 0 || x >= w || y >= h) return;
        int i = y * w + x;
        if (visited[i]) return;
        visited[i] = true;
        if (bgra[i * 4 + 3] == 0) return;              // برا الباتش — ماننتشرش
        if (!IsNearWhite(bgra, i * 4, thr)) return;    // لون الرسمة — حدّ المحتوى
        stack[sp++] = i;
    }

    // قصّ جزء من صورة BGRA (بحدود + هامش) — عشان نطلع اللوجو من نص الصفحة
    public static byte[] Crop(byte[] src, int w, int h, int x0, int y0, int x1, int y1) {
        int nw = x1 - x0 + 1, nh = y1 - y0 + 1;
        byte[] dst = new byte[nw * nh * 4];
        for (int y = 0; y < nh; y++) {
            int srcRow = (y0 + y) * w * 4 + x0 * 4;
            int dstRow = y * nw * 4;
            Array.Copy(src, srcRow, dst, dstRow, nw * 4);
        }
        return dst;
    }

    // فكّ TIFF Predictor 2 (horizontal differencing) — المستخدم في قناة الشفافية:
    // كل byte = الفرق عن اللي قبله في نفس الصف، والقيمة الحقيقية = تجميع متسلسل.
    public static byte[] UnpredictTiff2(byte[] data, int width, int height) {
        byte[] res = new byte[data.Length];
        for (int y = 0; y < height; y++) {
            int row = y * width;
            byte prev = 0;
            for (int x = 0; x < width; x++) {
                prev = (byte)(prev + data[row + x]);
                res[row + x] = prev;
            }
        }
        return res;
    }
}
'@
Add-Type -TypeDefinition $src -Language CSharp
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName System.Drawing

# ── دالة حفظ BGRA كملف PNG — عبر WPF (موثوقة أكتر من GDI+ للذاكرة المباشرة) ──
function Save-Png([byte[]]$bgra, [int]$w, [int]$h, [string]$path) {
    $bsrc = [Windows.Media.Imaging.BitmapSource]::Create($w, $h, 96, 96, [Windows.Media.PixelFormats]::Bgra32, $null, $bgra, ($w * 4))
    $enc  = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bsrc))
    # ⚠️ كتابة عبر ملف مؤقت + إعادة محاولة — بعض برامج الحماية بتقفل الملف الجديد لحظةً
    $tmpPng = "$path.tmp"
    for ($try = 1; $try -le 5; $try++) {
        try {
            $fs = [IO.File]::Create($tmpPng)
            $enc.Save($fs); $fs.Dispose()
            break
        } catch {
            if ($try -eq 5) { throw }
            Start-Sleep -Milliseconds (250 * $try)
        }
    }
    for ($try = 1; $try -le 5; $try++) {
        try { Move-Item $tmpPng $path -Force; return } catch { Start-Sleep -Milliseconds (250 * $try) }
    }
    Write-Warning "الملف $path مقفول من برنامج تاني — النسخة اتحفظت كـ $tmpPng"
}

# ── معاينة ASCII لقناة الشفافية — نتحقق بصريًا إن الشكل لوجو حقيقي ─────────────
function Show-AsciiPreview([byte[]]$bgra, [int]$w, [int]$h, [int]$cols = 96) {
    $rows = [int]($cols * $h / $w / 2)   # ÷2 لأن الحرف أطول من عرضه
    if ($rows -gt 36) { $rows = 36 }
    $sb = New-Object Text.StringBuilder
    for ($ry = 0; $ry -lt $rows; $ry++) {
        $line = ""
        for ($rx = 0; $rx -lt $cols; $rx++) {
            $sx = [int]($rx * $w / $cols)
            $sy = [int]($ry * $h / $rows)
            if ($sy -ge $h) { $sy = $h - 1 }
            $o = ($sy * $w + $sx) * 4
            $a = $bgra[$o + 3]
            if     ($a -gt 200) { $line += [char]0x2588 }  # █
            elseif ($a -gt 128) { $line += [char]0x2593 }  # ▓
            elseif ($a -gt 64)  { $line += [char]0x2591 }  # ░
            else                { $line += [char]0x00B7 }  # ·
        }
        [void]$sb.AppendLine($line)
    }
    Write-Host $sb.ToString()
}

# ── 1) قراءة الـ PDF وتفكيك الـ streams ───────────────────────────────────────
$pdf   = [IO.File]::ReadAllBytes($pdfPath)
$ascii = [Text.Encoding]::ASCII.GetString($pdf)

function Get-PdfStream([string]$marker) {
    $i = $ascii.IndexOf($marker)
    if ($i -lt 0) { throw "marker not found: $marker" }
    $s = $ascii.IndexOf('stream', $i) + 6
    if ($ascii[$s] -eq [char]13) { $s += 2 } elseif ($ascii[$s] -eq [char]10) { $s += 1 }
    $e = $ascii.IndexOf('endstream', $s)
    $len = $e - $s
    $b = New-Object byte[] $len
    [Array]::Copy($pdf, $s, $b, 0, $len)
    return ,$b
}

# ── 2) فك ضغط قناة الشفافية (SMask) ───────────────────────────────────────────
$alphaStream = Get-PdfStream '10 0 obj'
$off = 2   # ⚠️ الـ FlateDecode في PDF دايمًا zlib wrapper (2 bytes) — هنا بتبدأ 0x48 0x89
$aLen = $alphaStream.Length - $off
$ms  = [IO.MemoryStream]::new($alphaStream, $off, $aLen)
$ds  = New-Object IO.Compression.DeflateStream($ms, [IO.Compression.CompressionMode]::Decompress)
$out = New-Object IO.MemoryStream
$ds.CopyTo($out)
$alpha = $out.ToArray()
Write-Host ("[1] SMask decoded: " + $alpha.Length + " bytes (expected 2480x3508 = " + (2480*3508) + ")")
$alpha = [PdfTools]::UnpredictTiff2($alpha, 2480, 3508)
Write-Host "[1b] TIFF Predictor 2 un-differencing applied"

# ── 3) تحميل صورة الـ CMYK JPEG عبر WPF (بتفك YCCK صح) ────────────────────────
$jpg = Get-PdfStream '11 0 obj'
$jms = New-Object IO.MemoryStream(,$jpg)
$dec = [Windows.Media.Imaging.BitmapDecoder]::Create($jms, [Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat, [Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
$frame = $dec.Frames[0]
$W = $frame.PixelWidth; $H = $frame.PixelHeight
Write-Host ("[2] CMYK image: $W x $H  fmt=" + $frame.Format.ToString())
if ($alpha.Length -ne $W * $H) { throw "alpha size mismatch: $($alpha.Length) vs $($W*$H)" }

# ننزل نسخة مصغرة (×0.5) عشان الذاكرة — كافية جدًا للويب
$scale = 0.5
$tb = New-Object Windows.Media.Imaging.TransformedBitmap
$tb.BeginInit(); $tb.Source = $frame; $tb.Transform = New-Object Windows.Media.MatrixTransform($scale, 0, 0, $scale, 0, 0); $tb.EndInit()
$SW = $tb.PixelWidth; $SH = $tb.PixelHeight
$cmykBuf = New-Object byte[] ($SW * $SH * 4)
$tb.CopyPixels($cmykBuf, $SW * 4, 0)
Write-Host "[3] scaled to $SW x $SH"

# ── 4) قناة الشفافية مصغرة — بنعمل resize يدوي (nearest-neighbor) ─────────────
$aScaled = New-Object byte[] ($SW * $SH)
for ($y = 0; $y -lt $SH; $y++) {
    $sy = [int][Math]::Floor($y / $scale)
    if ($sy -ge $H) { $sy = $H - 1 }
    $rowSrc = $sy * $W
    $rowDst = $y * $SW
    for ($x = 0; $x -lt $SW; $x++) {
        $sx = [int][Math]::Floor($x / $scale)
        if ($sx -ge $W) { $sx = $W - 1 }
        $aScaled[$rowDst + $x] = $alpha[$rowSrc + $sx]
    }
}
Write-Host "[4] alpha resized"

# ── 5) التركيب — 3 نسخ + قصّ حدود اللوجو ──────────────────────────────────────
$tmp = Join-Path $env:TEMP 'fastcom-composite.bin'
$pad = 16   # هامش حول اللوجو بالبكسل

function Save-Cropped([string]$mode, [byte]$fr, [byte]$fg, [byte]$fb, [string]$path, [string]$label) {
    $report = [PdfTools]::Composite($cmykBuf, $aScaled, $SW, $SH, $fr, $fg, $fb, $mode, $tmp)
    Write-Host "`n=== $label ==="; Write-Host $report
    if ($report -match 'BBOX x=(\d+)-(\d+)\s+y=(\d+)-(\d+)') {
        $x0 = [Math]::Max(0, [int]$Matches[1] - $pad)
        $x1 = [Math]::Min($SW - 1, [int]$Matches[2] + $pad)
        $y0 = [Math]::Max(0, [int]$Matches[3] - $pad)
        $y1 = [Math]::Min($SH - 1, [int]$Matches[4] + $pad)
        $bgra = [IO.File]::ReadAllBytes($tmp)
        $cropped = [PdfTools]::Crop($bgra, $SW, $SH, $x0, $y0, $x1, $y1)
        Save-Png $cropped ($x1 - $x0 + 1) ($y1 - $y0 + 1) $path
        Write-Host ("-> saved " + (Split-Path -Leaf $path) + "  (" + ($x1-$x0+1) + "x" + ($y1-$y0+1) + ")")
        Show-AsciiPreview $cropped ($x1 - $x0 + 1) ($y1 - $y0 + 1)
    } else {
        throw "BBOX not found in report — اللوجو فاضي؟"
    }
}

Save-Cropped "true" 0 0 0 (Join-Path $brandDir 'logo-true.png') "TRUE COLORS"
Save-Cropped "fill" 11 86 223 (Join-Path $brandDir 'logo-blue.png') "BLUE FILL"
Copy-Item (Join-Path $brandDir 'logo-blue.png') (Join-Path $imgDir 'logo.png') -Force
Save-Cropped "fill" 255 255 255 (Join-Path $brandDir 'logo-white.png') "WHITE FILL"

# ── 6) favicon 64x64 من النسخة الزرقاء المقصوصة ───────────────────────────────
$blueImg = [System.Drawing.Image]::FromFile((Join-Path $brandDir 'logo-blue.png'))
$fav = New-Object System.Drawing.Bitmap(64, 64)
$g = [System.Drawing.Graphics]::FromImage($fav)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.DrawImage($blueImg, 0, 0, 64, 64)
$fav.Save((Join-Path $brandDir 'favicon.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $fav.Dispose(); $blueImg.Dispose()
Write-Host "`n[6] favicon.png done"

Remove-Item $tmp -Force -ErrorAction SilentlyContinue
Write-Host "`n✅ Done — docs/brand/logo-true.png + logo-blue.png + logo-white.png + favicon.png"
Write-Host "   + src/FastCom.Client/wwwroot/img/logo.png"


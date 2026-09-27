# FastCom — معيار تصميم الحقول (Form Field Standard)

> **المرجع الوحيد** لتصميم حقول الإدخال في كل شاشات النظام.
> أي شاشة جديدة أو تعديل لشاشة قائمة **لازم** يتبع المواصفات دي.
> **الأقسام 1-6:** معيار الحقول (`.field-*`) · **القسم 7:** هيكل الشاشة والهيرو والزر العائم (Page Shell / Hero / FAB).
> تاريخ الاعتماد: **2026-09-24**

---

## 1) الأساسيات (Canonical Spec)

### الحاوية (Field Wrapper)
```css
.form-field {
    display: flex;
    flex-direction: column;
    gap: 6px;          /* بين الليبل والإنبوت */
    position: relative;
}
```

### الليبل (Label)
```css
.field-label {
    font-size: 12px;
    font-weight: 800;
    color: #344258;
    display: flex;
    align-items: center;
    gap: 4px;
}
```
- **الحقل الإجباري**: أضف `<span class="field-required">*</span>` → لون `#e03048` وحجم `14px`.
- الليبل دايمًا **فوق** الحقل (Vertical stack) — مفيش Labels جنب الحقول.

### الإنبوت (Input / Select / Textarea)
```css
.field-input {
    height: 42px;                 /* ارتفاع قياسي ثابت */
    width: 100%;
    border: 1px solid #d8e2f0;    /* حدود رمادية-زرقاء فاتحة */
    border-radius: 9px;
    background: #fafcff;          /* خلفية مائلة للأزرق الخفيف */
    color: #1c2c44;               /* var(--fc-text) */
    font-family: inherit;
    font-size: 13px;
    padding: 0 12px;
    outline: none;
    transition: border-color .15s ease, box-shadow .15s ease, background .15s ease;
}
```

### حالات الحقل (States)

| الحالة | CSS |
|---|---|
| **Focus** | `border-color: #1e6ef5` + `box-shadow: 0 0 0 3px rgba(30,110,245,.10)` + `background: #fff` |
| **Disabled** | `opacity: .7; background: #f3f5f8; cursor: not-allowed` |
| **Error** | كلاس `.field-error` → `border-color: #e03048` + `box-shadow: 0 0 0 3px rgba(224,48,72,.08)` |
| **Placeholder** | `color: #b0bcc8` |

### التكستاير
```css
.field-textarea {
    height: auto;
    min-height: 80px;
    padding: 10px 12px;
    resize: vertical;
}
```

---

## 2) الاشتقاقات (Variants)

### إنبوت بأيقونة Prefix (يمين — RTL)
```html
<div class="field-input-group">
    <span class="field-prefix">…icon 13×13…</span>
    <input class="field-input with-prefix" />
</div>
```
```css
.field-input-group { position: relative; display: flex; align-items: center; }
.field-prefix {
    position: absolute; right: 12px;
    display: flex; align-items: center;
    color: #9aaabb; pointer-events: none; z-index: 1;
}
.field-input.with-prefix { padding-right: 36px; }
```

### سيلكت بأيقونة (Chevron)
```css
.field-select-wrap { position: relative; display: flex; align-items: center; }
.field-select-icon { position: absolute; right: 12px; color: #9aaabb; pointer-events: none; z-index: 1; }
.field-select { padding-right: 36px; cursor: pointer; appearance: none; }
```

### التلميح والخطأ (Hint / Error message)
```css
.field-hint    { display: flex; align-items: center; gap: 5px; font-size: 10.5px; color: #6b7d95; font-weight: 500; }
.field-err-msg { font-size: 10.5px; color: #e03048; font-weight: 700; }
```

### قائمة اقتراحات (Dropdown للبحث)
```css
.field-dropdown {
    position: absolute; top: 100%; right: 0; left: 0; margin-top: 4px;
    background: #fafcff; border: 1px solid #d8e2f0; border-radius: 9px;
    box-shadow: 0 8px 24px rgba(0,0,0,.12);
    max-height: 240px; overflow-y: auto; z-index: 100;
}
.field-dropdown-item { padding: 10px 12px; font-size: 13px; cursor: pointer; transition: background .1s; }
.field-dropdown-item:hover { background: #eaf1ff; }
```

---

## 3) شبكة النماذج (Form Grid)

```css
.bk-form-grid {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(258px, 1fr));
    gap: 18px;
}
@media (max-width: 900px) { .bk-form-grid { grid-template-columns: 1fr; } }
```
- عمود كامل العرض للحقول الطويلة: `grid-column: 1 / -1`.
- **ممنوع** تقليل ارتفاع الإنبوت تحت `42px` أو تكبيره فوق `44px`.

---

## 4) ألوان ثابتة (Tokens)

| Token | القيمة | الاستخدام |
|---|---|---|
| `--fc-blue` | `#1e6ef5` | Focus / Border نشط / أزرار رئيسية |
| `--fc-blue-lt` | `#eaf1ff` | خلفيات أزرق فاتحة |
| `--fc-red` | `#e03048` | Error / إجباري / حذف |
| `--fc-red-lt` | `#fff0f2` | خلفية Error فاتحة |
| `--fc-text` | `#1c2c44` | نص الحقول |
| `--fc-muted` | `#6b7d95` | التلميحات |
| Border | `#d8e2f0` | حدود الحقول الافتراضية |
| Bg | `#fafcff` | خلفية الحقل الافتراضية |
| Placeholder | `#b0bcc8` | نص الـ placeholder |
| Prefix icon | `#9aaabb` | أيقونات داخل الحقل |

---

## 5) قواعد إلزامية (Do & Don't)

✅ **اعمل:**
- استخدم الكلاسات الأساسية: `.form-field` / `.field-label` / `.field-input` / `.field-hint`.
- ارتفاع ثابت `42px` لكل الحقول في نفس النموذج.
- Focus ring أزرق `3px` في كل الشاشات.
- الحقول الإجبارية ليها نجمة `field-required`.

❌ **متعملش:**
- مفيش inline styles على `<input>` أو `<textarea>`.
- مفيش أنماط حقول مختلفة داخل نفس الشاشة (ممنوع خليط `fc-field-*` و `field-*` مع بعض).
- مفيش `appearance: none` من غير أيقونة سيلكت واضحة.
- مفيش تقليل الخط تحت `13px` في الحقول.

---

## 6) حالة التوحيد الحالية (2026-09-24)

| النمط | الأماكن المستخدمة | الحالة |
|---|---|---|
| `.field-*` (المعيار) | BookingForm, CustomerForm, ExpenseForm, PriceLists, OperationForm, TripForm, ShippingAgentForm, Trips, Documents, Customers, Roles, PortalAdmin, VehicleAccount | ✅ **المعيار المعتمد** |
| `.fc-field-*` (قديم) | CashBoxForm, ContainerForm, Maintenance, SupplierInvoiceForm, SupplierPaymentForm, Treasury | ⏳ مرشح للترحيل |
| `.svc-field-*` | TripTypes, Services | ⏳ مرشح للترحيل |

**الخطوة القادمة:** ترحيل الصفوف المعلمة ⏳ للنمط `.field-*` شاشة شاشة، وحذف التعريفات المحلية بعد نقلها لـ `app.css`.

---


---

## 7) هيكل الشاشة والهيرو + الزر العائم (Page Shell / Hero / FAB)

> **النمط المعتمد** لشاشات عيلة: **الخدمات · طرق الدفع · شروط الدفع · قوائم الأسعار · المستندات · أنواع الرحلات · الفواتير**.
> **الشاشة المرجعية: `Services.razor`** — وبعدها `PaymentMethods.razor` و **`PaymentTerms.razor`** و **`Invoices.razor`** (اتعملوا بنفس النمط بالحرف).
> تاريخ الاعتماد: **2026-09-24**

### 7.1) الماركب (بالترتيب الإلزامي)

```razor
<div class="hero-wrapper">
    <div class="fc-hero fc-hero-slim">
        <div class="fc-hero-inner">
            <div class="doc-hero-row">
                <div class="doc-hero-left">
                    <div class="doc-hero-crumb">
                        <svg …12×12… aria-hidden="true"></svg>
                        <span>البيانات الأساسية · XXX</span>
                    </div>
                    <h1 class="fc-hero-title">XXX</h1>
                    <p class="fc-hero-sub">وصف مختصر</p>
                </div>
                <div class="doc-hero-right">
                    <span class="doc-hero-chip"><svg …/> @DateTime.Now.ToString("dddd، dd MMMM yyyy")</span>
                    @* اختياري عند وجود تنبيه: <span class="doc-hero-chip doc-chip-warn">…</span> *@
                </div>
            </div>
            @if (CanView)
            {
                <div class="doc-kpis-row">
                    <div class="doc-kpi doc-kpi-blue">
                        <div class="doc-kpi-inner">
                            <div class="doc-kpi-icon">
                                <MudIcon Icon="@Icons.Material.Filled.List" Size="Size.Small"/>
                            </div>
                            <div class="doc-kpi-data">
                                <div class="doc-kpi-num">@n</div>
                                <div class="doc-kpi-label">العنوان</div>
                            </div>
                        </div>
                    </div>
                    @* كرر الكارت بالألوان: doc-kpi-green · doc-kpi-amber · doc-kpi-red *@
                </div>
            }
        </div>
    </div>
</div>

<div class="doc-body"> … الـtoolbar + الجدول + المودال … </div>
```

### 7.1.b) الزر العائم (FAB) — يتحط بعد `.doc-body` وقبل المودال

```razor
@if (CanManage && !_loading)
{
    @if (_fabOpen)
    {
        <div class="fc-fab-scrim" @onclick="ToggleFab" aria-hidden="true"></div>
    }
    <div class="fc-fab-container">
        <div class="fc-fab-menu @(_fabOpen ? "fc-fab-menu--open" : "")" role="menu">
            <button type="button" class="fc-fab-item" role="menuitem" @onclick="HandleOpenCreate">
                <span class="fc-fab-icon"><MudIcon Icon="@Icons.Material.Filled.Add" Size="Size.Small"/></span>
                <span class="fc-fab-label">XXX جديد</span>
            </button>
            <button type="button" class="fc-fab-item" role="menuitem" @onclick="HandleReload">
                <span class="fc-fab-icon"><MudIcon Icon="@Icons.Material.Filled.Refresh" Size="Size.Small"/></span>
                <span class="fc-fab-label">تحديث البيانات</span>
            </button>
        </div>
        <button type="button"
                class="fc-fab-main fc-glow-gold @(_fabOpen ? "is-open" : "")"
                @onclick="ToggleFab" aria-label="التحكم" aria-expanded="@(_fabOpen ? "true" : "false")">
            <svg viewBox="0 0 24 24" width="24" height="24" fill="none" stroke="currentColor"
                 stroke-width="2.5" class="fc-fab-svg" aria-hidden="true">
                <path d="M12 5v14M5 12h14" stroke-linecap="round"/>
            </svg>
        </button>
    </div>
}
```

### 7.2) القواعد الإلزامية

- **الإنشاء دايمًا بأزرار عائمة (FAB)** — «XXX جديد» + «تحديث البيانات». **ممنوع** زر إضافة جوه الهيرو أو في الـtoolbar، والـFAB يظهر بس لصاحب الصلاحية: `@if (CanManage && !_loading)` — أو صلاحية الشاشة لو اسمها مختلف (`@if (CanCreate && !_loading)` في `Invoices.razor`). و**زر «فاتورة جديدة» في القائمة بقى يتنقّل بيه لصفحة مستقلة** (`Nav.NavigateTo("/invoices/new")`) — الإنشاء/التعديل اتحوّلوا من مودال لصفحة في 2026-09-26 (شوف §7.6).
- **الـscrim** (`.fc-fab-scrim`) يتحط **بس وقت `_fabOpen`** و**برّه** `.fc-fab-container`.
- **ألوان الـKPI:** `doc-kpi-blue` · `doc-kpi-green` · `doc-kpi-amber` · `doc-kpi-red` — اللون على `.doc-kpi-icon` بس، والرقم أبيض دايماً.
- **العرض:** `--fc-page-w` (1560px في `app.css:12`) — `.hero-wrapper` و `.doc-body` بنفس القيمة. **ممنوع `max-width` محلي** للصفحة. ولو الشاشة كان ليها كلاس جسم محلي (زي `.inv-body` بـ`max-width: 1400px`) **اتشال** واستُبدل بـ`.doc-body`.
- **الجسم** `padding-bottom: 100px` (و`90px` تحت 680px) عشان الـFAB ما يغطّيش آخر سطر.
- **التجاوب:** `900px` (الـtoolbar عمودي + `doc-kpis-row` عمودين) · `680px` (هيرو 12px + `doc-body` 12px + FAB `left/bottom: 16px`) · `480px` (`doc-kpis-row` → `1fr 1fr`).
- جوه `<style>` في ملف `.razor`: أوامر الميديا والأنيميشن تتكتب **`@@media` / `@@keyframes`** (وإلا الـRazor يفسّرها كتعبير).

### 7.3) من فين تجيب الأنماط: عام أم محلي؟

| المصدر | الكلاسات |
|---|---|
| **`app.css` (عام — متلمسوش)** | `.fc-hero` · `.fc-hero-slim` · `.fc-hero-inner` · `.fc-hero-title` · `.fc-hero-sub` · `.fc-page` · `.fc-kpis*` · `.fc-fab-*` (نسخة 56px) · `.fc-fab-item/label/icon` |
| **`<style>` الصفحة (محلي — انسخه من `Services.razor`)** | `.hero-wrapper` · `.doc-body` · `.doc-hero-row/left/right/crumb/chip` (+`.doc-chip-warn`) · `.doc-kpis-row` · `.doc-kpi` · `.doc-kpi-inner/icon/data/num/label` · `.fc-glow-gold` + نسخة الـFAB المحلية (60px + نبضة برتقالية) — **بتغلب `app.css`** لأن `<style>` بتتحقن بعده |

> ⚠️ درس: **مش كل أنماط الهيرو «عامة»** — الهيرو الجاهز في `app.css` هو `fc-hero`/`fc-kpis` بس، أما `hero-wrapper` و`doc-*` فهيّ أنماط محلية في كل شاشة ⇒ لازم تنسخها.

### 7.4) مصغّرات `@code` المطلوبة

```csharp
private bool _fabOpen;

private void ToggleFab()
{
    _fabOpen = !_fabOpen;
    StateHasChanged();
}

private async Task HandleReload()
{
    _fabOpen = false;
    StateHasChanged();
    await LoadAsync();
    Snackbar.Add("تم تحديث البيانات", Severity.Success);
}

private async Task HandleOpenCreate()
{
    _fabOpen = false;
    StateHasChanged();
    await Task.Delay(180);   // عشان القايمة تقفل بنعومة قبل ما المودال يفتح
    OpenCreate();            // أو OpenAdd() حسب اسم الدالة في الشاشة
}
```

### 7.5) النمط التاني — شاشات الأسطول/المخزون/الحجوزات

`fc-hero` › `fc-hero-inner` › `fc-page-head` (`.fc-crumb` + `.fc-hero-title` + `.fc-hero-sub` + `.fc-head-actions` بأزرار **جوه** الهيرو) › `fc-kpis.fc-kpis-glass`
أمثلة: `Trailers.razor` · `Vehicles.razor` · `Customers.razor` · `Users.razor` · `Containers.razor`.

> ⚠️ **`Invoices.razor` مش منهم** — كانت بالنمط التاني (`fc-hero` › `fc-page-head` › زر «فاتورة جديدة» جوه الهيرو + `.inv-body` بعرض 1400px)، و**اترحّلت للنمط الأول بالحرف + FAB** بتاريخ **2026-09-24** (الشقيقة: `Services.razor`). متستخدمهاش كمثال للنمط التاني.

- **ما تختلطش النمطين في نفس الشاشة.**
- **قبل ما تختار نمط أي شاشة: قلّد الشاشة الشقيقة اللي المستخدم بيشاور عليها بالحرف** (نفس الكلاسات · نفس الـCSS المحلي · نفس الإجراءات · نفس مكان الأزرار). اختيار النمط «الأكثر انتشارًا» من عندك = انحراف مرفوض وبيترجّع.

### 7.6) الإنشاء/التعديل بقى صفحة مستقلة (2026-09-26)

- **`Invoices.razor` = قائمة بس** (هيرو + KPIs + FAB) · **`InvoiceForm.razor` = إضافة/تعديل** بمسارين: `/invoices/new` و `/invoices/new/{Id:long}` — زي عيلة الـ15 شاشة `*Form` (الأشبه: `SupplierInvoiceForm.razor`).
- هيرو شاشة الـ**Form** هو **النمط التاني** (§7.5): `fc-hero › fc-page-head` بزرّي «رجوع/حفظ» — مش `hero-wrapper`.
- **البحث عن العميل داخل الحقل:** `field-input with-prefix` + `.field-dropdown` منسوخ من `BookingForm.razor` (الأقسام 1-6) مع `NormalizeArabic` — البحث بالاسم **أو** الكود.
- في القائمة: FAB و«فاتورة جديدة» (الحالة الفاضية) = `Nav.NavigateTo("/invoices/new")` · زر تعديل الصف = `/invoices/new/{Id}`.
- الشروط في الصفحة: `CanSave = _id is null ? INVOICE.CREATE : INVOICE.EDIT` و`Locked` للحالات غير `Draft|Approved` (نفس شرط زر الصف في القائمة) — والحقول بتبقى `disabled` والـ«حفظ» مختفي وقت `Locked`.

---

*مرجع الهيرو/الـFAB (قسم 7): `Services.razor` — والشاشات المطبَّقة بنفس النمط: `PaymentTerms.razor` · `Invoices.razor` (2026-09-24) · **`InvoiceForm.razor`** (2026-09-26 — إضافة/تعديل الفواتير المنفصلة، النمط التاني §7.5).*

*مرجع معيار الحقول (الأقسام 1-6): `BookingForm.razor` — قسم `<style>` (السطر ~1636) هو المصدر الذي استُخرج منه هذا المعيار.*
*(معيار هيكل الشاشة والهيرو والزر العائم: شوف **القسم 7** فوق ↑)*

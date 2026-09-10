using System.Text;
using FastCom.Infrastructure.Identity;
using FastCom.Infrastructure.Persistence;
using FastCom.Server.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

/* ==========================================================
   1) Serilog — Structured Logging (قسم 55)
   ========================================================== */
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/fastcom-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .CreateLogger();

builder.Host.UseSerilog();

/* ==========================================================
   2) Database — EF Core + SQL Server
   ========================================================== */
builder.Services.AddDbContext<FastComDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sql =>
        {
            sql.EnableRetryOnFailure(maxRetryCount: 3);
            sql.CommandTimeout(60);
        }));

/* ==========================================================
   3) 🔴 A5: ASP.NET Core Identity بـ int keys
   ========================================================== */
builder.Services
    .AddIdentity<ApplicationUser, ApplicationRole>(options =>
    {
        // Password Policy (قسم 49)
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;

        // Account Lockout (قسم 49)
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.AllowedForNewUsers = true;

        // User
        options.User.RequireUniqueEmail = false;
        options.SignIn.RequireConfirmedEmail = false;
    })
    .AddEntityFrameworkStores<FastComDbContext>()
    .AddDefaultTokenProviders();

/* ==========================================================
   4) JWT Authentication
   ⚠️  للـ Blazor WASM، الـ Token بيتخزن في الـ Browser
       فالـ Authorization كله لازم يكون على الـ Server (قسم 49)
   ========================================================== */
var jwtSettings = builder.Configuration.GetSection("Jwt");
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings["Issuer"],
            ValidAudience = jwtSettings["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSettings["Key"]
                    ?? throw new InvalidOperationException("Jwt:Key مش متعرّف في appsettings"))),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

/* ==========================================================
   4.5) 🔐 Step 3.5: Authorization بالصلاحيات من قاعدة البيانات
        [Authorize(Policy = "PERM:BOOKING.VIEW")] → فحص حي من
        AppUserPermissions مع كاش 60 ثانية — fail-closed عند أي خطأ
   ========================================================== */
builder.Services.AddAuthorization(options =>
{
    // 🔴 الحماية الافتراضية: أي endpoint من غير [AllowAnonymous]
    //    هيتطلب تسجيل دخول تلقائيًا — عشان أي Controller جديد
    //    في Step 4+ مايخرجش مفتوح بالغلط.
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();                       // 🔐 لأغراض الـ Audit (IUserContext)
builder.Services.AddScoped<FastCom.Infrastructure.Persistence.IUserContext, FastCom.Server.Auth.HttpUserContext>();
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<FastCom.Server.Services.INumberingService, FastCom.Server.Services.NumberingService>();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

// 🔐 Step 3.3: خدمة الـ JWT (توليد التوكن + جلب الصلاحيات)
builder.Services.AddScoped<TokenService>();

/* ==========================================================
   5) CORS — ⚠️ مهم للـ Blazor WASM
   ========================================================== */
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowClient", policy =>
    {
        // في الـ Development: أي Origin
        // في الـ Production: الـ Domain بتاعك بس
        policy.WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>())
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

/* ==========================================================
   6) Controllers + Swagger
   ========================================================== */
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "FastCom API",
        Version = "v1",
        Description = "Port Transport Management System"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "حط الـ JWT Token هنا"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

/* ==========================================================
   7) QuestPDF License
   ========================================================== */
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

/* ==========================================================
   8) Application Services (DI)
   ========================================================== */
// builder.Services.AddApplicationServices();       // TODO: Step 4
// builder.Services.AddInfrastructureServices(builder.Configuration);  // TODO: Step 4

var app = builder.Build();

/* ==========================================================
   9) Middleware Pipeline
   ========================================================== */

// ⚠️ Global Exception Handling (قسم 54) — مش هنعرض Exceptions للمستخدم
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        // 🔴 من غير السطر ده الـ Exception كان بيروح من غير ما يتسجل —
        //    والمستخدم كان بيشوف «حدث خطأ» من غير أي تفصيلة.
        var feature = context.Features.Get<IExceptionHandlerFeature>();
        var ex = feature?.Error;

        Log.Error(ex, "❌ Unhandled exception on {Method} {Path}",
                  context.Request.Method, context.Request.Path);

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            success = false,
            message = "حدث خطأ أثناء تنفيذ العملية. برجاء المحاولة مرة أخرى.",

            // للتشخيص في Development بس — نوع الاستثناء والرسالة (من غير stack trace)
            error     = context.RequestServices
                            .GetService<IHostEnvironment>()?.IsDevelopment() == true
                        ? ex?.GetType().Name : null,
            detail    = context.RequestServices
                            .GetService<IHostEnvironment>()?.IsDevelopment() == true
                        ? ex?.Message : null,
            path      = context.Request.Path.Value
        });
    });
});

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();   // 🔗 Blazor WASM debugging
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseCors("AllowClient");

app.UseBlazorFrameworkFiles();   // 🔗 Blazor WASM static assets
// 🔴 حماية: المستندات متخزنة تحت wwwroot/App_Data — من غير السطر ده
//    أي حد يعرف الرابط ينزّلها من غير DOCUMENT.DOWNLOAD.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/App_Data", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    await next();
});

app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// 🔗 SPA routing — أي route مش /api يروح للـ Client
// 🔴 AllowAnonymous إلزامي: الـ FallbackPolicy كان هيرفض يرجّع index.html
//    لمستخدم مش مسجّل دخول → التطبيق كله كان هيقع على 401
//    وشاشة الدخول مش هتظهر أصلًا.
//    الحماية الحقيقية على الـ API، والـ Client بيحوّل لـ /login بنفسه.
app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();

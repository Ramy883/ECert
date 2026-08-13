using System.Text.Json;
using System.Text.Json.Serialization;
using ECert.Data;
using ECert.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

var rawMySqlConn = ResolveMySqlConnectionString(builder.Configuration);
var mysqlConn = NormalizeMySqlConnectionString(rawMySqlConn);

builder.Services.AddDbContext<ECertDbContext>(options =>
    options.UseMySql(mysqlConn, ServerVersion.AutoDetect(mysqlConn)));

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
builder.Services.AddScoped<AuditLogService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddSingleton<CertificateSecurityService>();
builder.Services.AddSingleton<VerifyRequestGuardService>();
builder.Services.AddScoped<CertificateSchemaMigrationService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Auth/Login";
        options.LogoutPath = "/Auth/Logout";
        options.AccessDeniedPath = "/Home/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.None
            : CookieSecurePolicy.Always;
        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    context.Response.StatusCode = 401;
                    context.Response.ContentType = "application/json";
                    return context.Response.WriteAsync(
                        JsonSerializer.Serialize(new { success = false, message = "يجب تسجيل الدخول أولاً" }));
                }
                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            },
            OnRedirectToAccessDenied = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    context.Response.StatusCode = 403;
                    context.Response.ContentType = "application/json";
                    return context.Response.WriteAsync(
                        JsonSerializer.Serialize(new { success = false, message = "ليس لديك صلاحية الوصول" }));
                }
                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[] { "image/svg+xml", "application/json" });
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<ECertDbContext>();
        db.Database.EnsureCreated();
        DbSeeder.Seed(db);
        DbSeeder.SeedHomepageCms(db);
        var certificateMigration = scope.ServiceProvider.GetRequiredService<CertificateSchemaMigrationService>();
        await certificateMigration.EnsureAsync();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("Database initialization failed.");
        Console.Error.WriteLine(ex.ToString());
        throw;
    }
}

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseResponseCompression();

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var path = ctx.File.PhysicalPath ?? string.Empty;
        if (path.EndsWith(".js") || path.EndsWith(".css") || path.EndsWith(".woff2"))
            ctx.Context.Response.Headers[HeaderNames.CacheControl] = "public,max-age=604800";
        else if (path.EndsWith(".jpg") || path.EndsWith(".png") || path.EndsWith(".webp") || path.EndsWith(".svg") || path.EndsWith(".ico"))
            ctx.Context.Response.Headers[HeaderNames.CacheControl] = "public,max-age=2592000";
    }
});

if (!app.Environment.IsDevelopment())
{
    app.Use(async (context, next) =>
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data: https:; style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net; script-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net; font-src 'self' https://cdn.jsdelivr.net; connect-src 'self' https:; frame-ancestors 'none';";
        context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        await next();
    });
}

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

static string ResolveMySqlConnectionString(IConfiguration configuration)
{
    var candidates = new[]
    {
        configuration.GetConnectionString("MySqlConnection"),
        configuration["ConnectionStrings:MySqlConnection"],
        configuration["ConnectionStrings__MySqlConnection"],
        configuration["MySqlConnection"],
        configuration["DATABASE_URL"]
    };

    var value = candidates.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException(
            "MySQL connection string is missing. Supported keys: " +
            "ConnectionStrings:MySqlConnection, ConnectionStrings__MySqlConnection, MySqlConnection, DATABASE_URL.");
    }

    return value.Trim();
}

static string NormalizeMySqlConnectionString(string value)
{
    if (value.Contains("Server=", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("Host=", StringComparison.OrdinalIgnoreCase))
    {
        return value;
    }

    if (value.StartsWith("mysql://", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("mariadb://", StringComparison.OrdinalIgnoreCase))
    {
        return ConvertMySqlUrlToAdoNet(value);
    }

    throw new InvalidOperationException(
        "Unsupported MySQL connection string format. Use ADO.NET format or a mysql:// URL.");
}

static string ConvertMySqlUrlToAdoNet(string url)
{
    var uri = new Uri(url);
    var database = uri.AbsolutePath.Trim('/');
    if (string.IsNullOrWhiteSpace(database))
        throw new InvalidOperationException("MySQL URL must include a database name.");

    var userInfo = uri.UserInfo.Split(':', 2);
    var user = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : string.Empty;
    var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;

    if (string.IsNullOrWhiteSpace(user))
        throw new InvalidOperationException("MySQL URL must include a username.");

    var options = ParseQueryString(uri.Query);
    var sslMode = options.TryGetValue("sslmode", out var ssl)
        ? MapSslMode(ssl)
        : options.TryGetValue("ssl-mode", out var sslDash)
            ? MapSslMode(sslDash)
            : "None";

    var parts = new List<string>
    {
        $"Server={uri.Host}",
        $"Port={(uri.IsDefaultPort ? 3306 : uri.Port)}",
        $"Database={database}",
        $"User ID={user}",
        $"Password={password}",
        $"SslMode={sslMode}",
        "AllowPublicKeyRetrieval=True"
    };

    if (options.TryGetValue("charset", out var charset) && !string.IsNullOrWhiteSpace(charset))
        parts.Add($"Character Set={charset}");

    return string.Join(';', parts) + ";";
}

static Dictionary<string, string> ParseQueryString(string query)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    if (string.IsNullOrWhiteSpace(query))
        return result;

    var trimmed = query.TrimStart('?');
    foreach (var pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
    {
        var pieces = pair.Split('=', 2);
        var key = Uri.UnescapeDataString(pieces[0]);
        var value = pieces.Length > 1 ? Uri.UnescapeDataString(pieces[1]) : string.Empty;
        result[key] = value;
    }

    return result;
}

static string MapSslMode(string value)
{
    return value.Trim().ToLowerInvariant() switch
    {
        "required" => "Required",
        "require" => "Required",
        "preferred" => "Preferred",
        "verifyca" => "VerifyCA",
        "verifyfull" => "VerifyFull",
        "disabled" => "None",
        "disable" => "None",
        "none" => "None",
        _ => "None"
    };
}

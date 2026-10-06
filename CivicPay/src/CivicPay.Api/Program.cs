using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using CivicPay.Application;
using CivicPay.Domain;
using CivicPay.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
var provider = builder.Configuration["Database:Provider"] ?? "SqlServer";
if (provider is not ("SqlServer" or "Sqlite")) throw new InvalidOperationException("Database:Provider must be SqlServer or Sqlite.");
var connection = builder.Configuration.GetConnectionString("CivicPay") ?? (provider == "Sqlite" ? "Data Source=civicpay.db;Foreign Keys=True" : throw new InvalidOperationException("Set ConnectionStrings__CivicPay for SQL Server."));
if (provider == "SqlServer" && new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connection).MultipleActiveResultSets)
    throw new InvalidOperationException("Disable MultipleActiveResultSets: import row recovery requires EF transaction savepoints.");
if (provider == "Sqlite")
    connection = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connection) { ForeignKeys = true }.ConnectionString;
builder.Services.AddDbContext<CivicPayDbContext>(o => { if (provider == "Sqlite") o.UseSqlite(connection); else o.UseSqlServer(connection); });
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IConfigurationService, ConfigurationService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IImportService, ImportService>();
builder.Services.AddControllers().ConfigureApiBehaviorOptions(o =>
{
    o.SuppressMapClientErrors = true;
    o.InvalidModelStateResponseFactory = c =>
    {
        c.HttpContext.Items["ErrorCode"] = "INVALID_REQUEST";
        c.HttpContext.Items["ErrorMessage"] = "Request JSON or required fields are invalid.";
        return new BadRequestObjectResult(new ApiError(false, "INVALID_REQUEST", "Request JSON or required fields are invalid.", c.HttpContext.TraceIdentifier));
    };
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo { Title = "CivicPay implementation API", Version = "v1", Description = "Synthetic municipal payment records only. Set X-Api-Key outside explicit local demo mode. Dates use UTC; amounts are CAD decimals. See docs/API_INTEGRATION_GUIDE.md for examples and error codes." });
    o.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme { Type = SecuritySchemeType.ApiKey, Name = "X-Api-Key", In = ParameterLocation.Header, Description = "Required unless Demo:OpenAccess is enabled locally." });
    o.AddSecurityRequirement(document => new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("ApiKey", document)] = [] });
    var xml = Path.Combine(AppContext.BaseDirectory, "CivicPay.Api.xml");
    if (File.Exists(xml))
        o.IncludeXmlComments(xml);
});
var open = builder.Configuration.GetValue<bool>("Demo:OpenAccess");
var key = builder.Configuration["Security:ApiKey"];
if (open && !builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing")) throw new InvalidOperationException("Open demo access is permitted only in Development or Testing.");
if (!open && (string.IsNullOrWhiteSpace(key) || key.Length < 24)) throw new InvalidOperationException("Set Security__ApiKey to a random value of at least 24 characters, or explicitly enable local Development demo access.");
var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CivicPayDbContext>();
    if (builder.Configuration.GetValue<bool>("Database:Initialize"))
    {
        if (provider == "Sqlite")
            await db.Database.EnsureCreatedAsync();
        else
            await db.Database.MigrateAsync();
        await PaymentCatalogue.EnsureAsync(db);
    }
    if (builder.Configuration.GetValue<bool>("Database:Seed"))
        await DemoSeeder.SeedAsync(db);
}
app.Use(async (ctx, next) =>
{
    var correlation = Guid.NewGuid().ToString("N");
    ctx.TraceIdentifier = correlation;
    ctx.Response.Headers["X-Correlation-ID"] = correlation;
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Content-Security-Policy"] = "default-src 'self'; style-src 'self'; script-src 'self'; img-src 'self' data:; frame-ancestors 'none'";
    var timer = Stopwatch.StartNew();
    using var logScope = app.Logger.BeginScope(new Dictionary<string, object> { { "CorrelationId", correlation } });
    try
    {
        if (ctx.Request.Path.StartsWithSegments("/api") && !open)
        {
            var supplied = ctx.Request.Headers["X-Api-Key"].ToString();
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(supplied)), SHA256.HashData(Encoding.UTF8.GetBytes(key!))))
                throw new BusinessException("UNAUTHORIZED", "A valid X-Api-Key is required.", 401);
        }
        if (ctx.Request.Path.StartsWithSegments("/api/imports") && ctx.Request.ContentLength > 2_100_000)
            throw new BusinessException("IMPORT_TOO_LARGE", "Multipart upload must not exceed 2,100,000 bytes.", 413);
        await next(ctx);
    }
    catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
    {
        app.Logger.LogDebug("Request was cancelled by the client.");
    }
    catch (Exception ex)
    {
        var business = ex as BusinessException;
        var status = business?.Status ?? (ex is BadHttpRequestException badRequest ? badRequest.StatusCode : 500);
        var code = business?.Code ?? (status == 413 ? "IMPORT_TOO_LARGE" : status < 500 ? "INVALID_REQUEST" : "INTERNAL_ERROR");
        var message = business?.Message ?? (status < 500 ? "Request body is invalid or exceeds the permitted size." : "An internal error occurred. Provide the correlation ID when troubleshooting.");
        ctx.Items["ErrorCode"] = code;
        ctx.Items["ErrorMessage"] = message;
        if (status >= 500)
            app.Logger.LogError(ex, "Request failed with {ErrorCode}", code);
        else
            app.Logger.LogWarning("Request rejected with {ErrorCode}", code);
        if (!ctx.Response.HasStarted)
        {
            ctx.Response.StatusCode = status;
            await ctx.Response.WriteAsJsonAsync(new ApiError(false, code, message, correlation));
        }
        else
            ctx.Abort();
    }
    finally
    {
        timer.Stop();
        // Health polling does not inflate business integration request metrics.
        if (!ctx.RequestAborted.IsCancellationRequested && ctx.Request.Path.StartsWithSegments("/api") && !ctx.Request.Path.StartsWithSegments("/api/health") && !ctx.Request.Path.StartsWithSegments("/api/monitoring"))
        {
            app.Logger.LogInformation("Integration {Method} {Path} returned {StatusCode} in {DurationMs} ms for {MunicipalityCode}", ctx.Request.Method, ctx.Request.Path.Value, ctx.Response.StatusCode, timer.Elapsed.TotalMilliseconds, ctx.Items["MunicipalityCode"]);
            try
            {
                using var scope = app.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CivicPayDbContext>();
                var code = ctx.Items["ErrorCode"] as string ?? (ctx.Response.StatusCode >= 400 ? "INVALID_REQUEST" : null);
                var rawMunicipality = ctx.Items["MunicipalityCode"] as string;
                var municipality = rawMunicipality?[..Math.Min(rawMunicipality.Length, 40)];
                db.IntegrationEvents.Add(new()
                {
                    CorrelationId = correlation,
                    Path = ctx.Request.Path.Value![..Math.Min(ctx.Request.Path.Value!.Length, 200)],
                    Method = ctx.Request.Method[..Math.Min(ctx.Request.Method.Length, 10)],
                    MunicipalityCode = municipality,
                    StatusCode = ctx.Response.StatusCode,
                    DurationMs = timer.Elapsed.TotalMilliseconds,
                    ErrorCode = code
                });
                if (code != null)
                    db.ErrorLogs.Add(new()
                    {
                        CorrelationId = correlation,
                        MunicipalityCode = municipality,
                        ErrorCode = code,
                        Message = ctx.Items["ErrorMessage"] as string ?? "Request validation failed."
                    });
                using var telemetryTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await db.SaveChangesAsync(telemetryTimeout.Token);
            }
            catch (Exception ex) { app.Logger.LogError(ex, "Could not persist request telemetry for {CorrelationId}", correlation); }
        }
    }
});
app.UseStatusCodePages(async context =>
{
    var ctx = context.HttpContext;
    if (!ctx.Request.Path.StartsWithSegments("/api"))
        return;
    var (code, message) = ctx.Response.StatusCode switch
    {
        404 => ("ENDPOINT_NOT_FOUND", "API endpoint was not found."),
        405 => ("METHOD_NOT_ALLOWED", "HTTP method is not supported by this endpoint."),
        413 => ("IMPORT_TOO_LARGE", "Request exceeds the permitted size."),
        415 => ("UNSUPPORTED_MEDIA_TYPE", "Use the content type documented for this endpoint."),
        _ => ("INVALID_REQUEST", "Request could not be processed.")
    };
    ctx.Items["ErrorCode"] = code;
    ctx.Items["ErrorMessage"] = message;
    await ctx.Response.WriteAsJsonAsync(new ApiError(false, code, message, ctx.TraceIdentifier));
});
// Swagger UI owns inline styles/scripts; its own routes override the dashboard's strict CSP.
app.Use(async (ctx, next) => { if (ctx.Request.Path.StartsWithSegments("/swagger")) ctx.Response.Headers.Remove("Content-Security-Policy"); await next(ctx); });
app.UseSwagger();
app.UseSwaggerUI();
app.Environment.WebRootFileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(Path.Combine(AppContext.BaseDirectory, "wwwroot"));
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();
app.MapGet("/api/health", async (CivicPayDbContext db) => await db.Database.CanConnectAsync() ? Results.Ok(new { status = "Healthy", database = provider, synthetic = true }) : Results.Json(new { status = "Unhealthy" }, statusCode: 503));
app.Run();
public partial class Program
{
}

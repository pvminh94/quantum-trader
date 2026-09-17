// ═══════════════════════════════════════════════════════════════════
// Program.cs - Application entry point.
//
// Configures the entire .NET 9 WebAPI pipeline with:
// - SignalR for real-time bidirectional streaming
// - Background services for market data ingestion and bot execution
// - JWT authentication with HttpOnly cookies
// - Redis for caching and pub/sub
// - PostgreSQL for persistent storage
// - Structured logging with Serilog
// - Rate limiting middleware
// - Global exception handling
// ═══════════════════════════════════════════════════════════════════

using System.Text.Json;
using System.Text.Json.Serialization;
using TradingEngine.Api.Hubs;
using TradingEngine.Api.Middleware;
using TradingEngine.Application.BackgroundServices;
using TradingEngine.Application.Services;
using TradingEngine.Infrastructure.Caching;
using TradingEngine.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

// ═══════════════════════════════════════════════════════════════════
// SERVICE REGISTRATION
// ═══════════════════════════════════════════════════════════════════

// ── Controllers ───────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        opts.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        opts.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
    });

// ── SignalR ───────────────────────────────────────────────────────
builder.Services.AddSignalR(options =>
{
    // 100ms message buffer for batching high-frequency data
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.MaximumReceiveMessageSize = 64 * 1024; // 64KB
    options.StreamBufferCapacity = 50;
})
.AddJsonProtocol(options =>
{
    options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.PayloadSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

// ── CORS (for Next.js frontend) ───────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("TradingDashboard", policy =>
    {
        policy.WithOrigins(
                builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ??
                new[] { "http://localhost:3000" })
            .AllowCredentials()
            .AllowAnyHeader()
            .AllowAnyMethod()
            .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
    });
});

// ── Authentication ────────────────────────────────────────────────
builder.Services.AddAuthentication()
    // JWT Bearer token configuration
    .AddJwtBearer("Bearer", options =>
    {
        var jwtSection = builder.Configuration.GetSection("Jwt");
        options.TokenValidationParameters = new()
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSection["Issuer"] ?? "TradingEngine",
            ValidateAudience = true,
            ValidAudience = jwtSection["Audience"] ?? "TradingDashboard",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes(
                    jwtSection["SecretKey"] ?? "CHANGE-ME-IN-PRODUCTION-at-least-32-chars!!!"))
        };

        // Support JWT in both Authorization header and query string
        // (SignalR connections use query string for auth)
        options.Events = new()
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;

                if (!string.IsNullOrEmpty(accessToken) &&
                    path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

// ── Authorization ─────────────────────────────────────────────────
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireRole("Admin"));
});

// ── Application Services ──────────────────────────────────────────
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddSingleton<IRiskManagementService, RiskManagementService>();
builder.Services.AddSingleton<PositionSizeCalculator>();

// ── Infrastructure Services ───────────────────────────────────────
builder.Services.AddSingleton<RedisCacheProvider>();
builder.Services.AddSingleton<AesEncryptionProvider>();
builder.Services.AddSingleton<ApiKeyManager>();

// ── Background Services ───────────────────────────────────────────
builder.Services.AddHostedService<MarketDataIngestionService>();
builder.Services.AddHostedService<BotExecutionEngineService>();
builder.Services.AddHostedService<RiskMonitorService>();
builder.Services.AddHostedService<HealthCheckService>();

// ── Redis (StackExchange.Redis) ───────────────────────────────────
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis")
        ?? "localhost:6379";
    options.InstanceName = "TradingEngine:";
});

// ── OpenAPI / Swagger ─────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "Trading Engine API",
        Version = "v1",
        Description = "Real-time crypto trading dashboard and automated bot platform"
    });

    // Include XML comments
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
        options.IncludeXmlComments(xmlPath);
});

// ── Health Checks ─────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddRedis(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379",
        name: "redis")
    .AddDbContextCheck<TradingEngine.Infrastructure.Persistence.AppDbContext>(
        name: "postgresql");

// ═══════════════════════════════════════════════════════════════════
// MIDDLEWARE PIPELINE
// ═══════════════════════════════════════════════════════════════════

var app = builder.Build();

// ── Exception handling (MUST be first) ────────────────────────────
app.UseMiddleware<ExceptionHandlingMiddleware>();

// ── Security headers ──────────────────────────────────────────────
app.UseHsts();
app.UseHttpsRedirection();

// ── Swagger (development only) ────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Trading Engine API v1");
        options.RoutePrefix = "docs";
    });
}

// ── CORS ──────────────────────────────────────────────────────────
app.UseCors("TradingDashboard");

// ── Rate limiting ─────────────────────────────────────────────────
app.UseMiddleware<RequestRateLimitingMiddleware>();

// ── Authentication & Authorization ────────────────────────────────
app.UseAuthentication();
app.UseAuthorization();

// ── Request validation ────────────────────────────────────────────
app.UseMiddleware<RequestValidationMiddleware>();

// ── Map controllers ───────────────────────────────────────────────
app.MapControllers();

// ── Map SignalR hubs ─────────────────────────────────────────────
app.MapHub<MarketDataHub>("/hubs/market-data")
   .RequireCors("TradingDashboard");
//   .RequireAuthorization();  // Uncomment when JWT is configured

app.MapHub<BotTelemetryHub>("/hubs/bot-telemetry")
   .RequireCors("TradingDashboard");

// ── Health checks ─────────────────────────────────────────────────
app.MapHealthChecks("/health")
   .AllowAnonymous();

// ── Root endpoint ─────────────────────────────────────────────────
app.MapGet("/", () => Results.Ok(new
{
    Service = "TradingEngine API",
    Version = "v1.0.0",
    Status = "operational",
    Timestamp = DateTime.UtcNow
}));

// ═══════════════════════════════════════════════════════════════════
// START
// ═══════════════════════════════════════════════════════════════════

app.Logger.LogInformation("TradingEngine API starting up...");
app.Logger.LogInformation("SignalR hubs: /hubs/market-data, /hubs/bot-telemetry");
app.Logger.LogInformation("Health check: /health");

app.Run();

// For integration testing
public partial class Program { }
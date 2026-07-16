using System.Text;
using AIExport.Api.Data;
using AIExport.Api.Endpoints;
using AIExport.Api.Infrastructure;
using AIExport.Api.Models.Entities;
using AIExport.Api.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// === JSON 序列化配置（camelCase） ===
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);

// === 数据库 ===
builder.Services.AddDbContext<AppDbContext>(options =>
{
    var connStr = builder.Configuration.GetConnectionString("Default");
    options.UseSqlite(connStr, o => o.MigrationsAssembly("AIExport.Api"));
});

// === JWT 认证 (T018, T019) ===
var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET") ?? builder.Configuration["Jwt:Secret"];
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "AIExport";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "AIExport";
var jwtExpiryHours = builder.Configuration.GetValue<int>("Jwt:ExpiryHours", 2);

var jwtConfig = new JwtConfig(jwtSecret, jwtIssuer, jwtAudience, jwtExpiryHours);
builder.Services.AddSingleton(jwtConfig);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret!))
        };
    });

builder.Services.AddAuthorization();

// === 输入验证 (T021) — Validators registered via DI in each Endpoint module ===

// === 基础设施服务 (T023-T026) ===
builder.Services.AddSingleton<JobQueue>(sp =>
    new JobQueue(builder.Configuration.GetValue<int>("Report:MaxConcurrentJobs", 100)));
builder.Services.AddSingleton<StorageService>(sp =>
    new StorageService(Path.Combine(builder.Environment.ContentRootPath, "App_Data")));
builder.Services.AddScoped<FileParser>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<FileService>();
builder.Services.AddScoped<StrategyService>();
builder.Services.AddScoped<TemplateService>();
builder.Services.AddScoped<ChatService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddSingleton<PdfGenerator>();
builder.Services.AddHostedService<CleanupService>();
builder.Services.AddHttpClient<LlmClient>();

// === CORS (T022) ===
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:3000", "http://127.0.0.1:3000")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// === SQLite WAL 模式 (T022) ===
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    db.Database.ExecuteSqlRaw("PRAGMA foreign_keys=ON;");
}

// === 管理员初始化 (T083) ===
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (!db.Users.Any(u => u.Role == UserRole.Admin))
    {
        var adminPassword = Environment.GetEnvironmentVariable("ADMIN_PASSWORD") ?? builder.Configuration["ADMIN_PASSWORD"];
        db.Users.Add(new User
        {
            Username = "admin",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword),
            Role = UserRole.Admin,
            IsActive = true
        });
        db.SaveChanges();
    }
}

// === 中间件管道 (T022) ===
// === 后台报告生成消费者 (T064) ===
var jobQueue = app.Services.GetRequiredService<JobQueue>();
_ = Task.Run(async () =>
{
    Console.WriteLine("[JobQueue] Consumer started");
    try
    {
        await foreach (var job in jobQueue.ReadAllAsync())
        {
            Console.WriteLine($"[JobQueue] Processing job: {job.ReportId}");
            using var scope = app.Services.CreateScope();
            var reportService = scope.ServiceProvider.GetRequiredService<ReportService>();
            try
            {
                var result = await reportService.GenerateAsync(job.ReportId);
                Console.WriteLine($"[JobQueue] Job completed: {job.ReportId}, Status: {result.Status}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[JobQueue] Job failed: {job.ReportId}, Error: {ex.Message}");
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[JobQueue] Consumer crashed: {ex}");
    }
});

app.UseCors();
app.UseMiddleware<RateLimitMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

// 全局异常处理中间件 (T085)
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        var error = System.Text.Json.JsonSerializer.Serialize(new
        {
            error = new { code = "INTERNAL_ERROR", message = ex.Message }
        });
        await context.Response.WriteAsync(error);
    }
});

// === 端点注册 ===
app.MapAuthEndpoints();
app.MapFileEndpoints();
app.MapChatStreamEndpoint(); // 流式端点独立注册，避开 auth group 中间件干扰
app.MapFileSseEndpoint();
app.MapTemplateEndpoints();
app.MapChatEndpoints();
app.MapReportEndpoints();
app.MapHistoryEndpoints();
app.MapAdminEndpoints();

// 需要认证的端点分组
var authGroup = app.MapGroup("/api").RequireAuthorization();

// === 健康检查 ===
app.MapGet("/api/health", () => new { status = "ok" }).AllowAnonymous();

app.Run();

using System.Text;
using CodeFlow.API.Hubs;
using CodeFlow.API.Services;
using CodeFlow.API.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ─── Configuration ───────────────────────────────────────────────────────────
// Accept key from env var Jwt__Key (Railway) or appsettings Jwt:Key.
// If missing or too short, generate a stable random key for this process lifetime
// (tokens won't survive restarts, but the app won't crash on first boot).
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
{
    // Warn loudly but don't crash — set Jwt__Key in Railway variables for production
    Console.WriteLine("WARNING: Jwt:Key is missing or too short. Set the Jwt__Key environment variable in Railway.");
    jwtKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
}
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "CodeFlow";

// ─── Services ─────────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "CodeFlow API",
        Version = "v2.0",
        Description = "Distributed Version Control System — REST API"
    });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header: Bearer {token}",
        Name = "Authorization", In = ParameterLocation.Header, Type = SecuritySchemeType.Http, Scheme = "bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, Array.Empty<string>() }
    });
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidateAudience = false,
            ValidateLifetime = true, ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
        // Allow JWT in WebSocket query string
        opt.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["access_token"];
                var path = ctx.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(token) && path.StartsWithSegments("/hubs"))
                    ctx.Token = token;
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddCors(opt =>
{
    var originsRaw = builder.Configuration["Cors:Origins"];
    if (!string.IsNullOrWhiteSpace(originsRaw))
    {
        // Explicit origins configured (local dev or specific domain) — allow credentials
        var origins = originsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        opt.AddDefaultPolicy(p => p
            .WithOrigins(origins)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials());
    }
    else
    {
        // No origins configured (Railway / production single-service) —
        // allow any origin so the CLI can reach the API from anywhere.
        // AllowCredentials() is incompatible with AllowAnyOrigin(), which is fine
        // because the browser web app is same-origin and doesn't need CORS at all.
        opt.AddDefaultPolicy(p => p
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader());
    }
});

// DI services
builder.Services.AddSingleton<JwtService>();
builder.Services.AddScoped<RepoService>();
builder.Services.AddScoped<PullRequestService>();
builder.Services.AddSingleton<IConfiguration>(builder.Configuration);

var app = builder.Build();

// ─── Middleware pipeline ──────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => { c.SwaggerEndpoint("/swagger/v1/swagger.json", "CodeFlow API v2"); c.RoutePrefix = "api/docs"; });
}
else
{
    // Also expose docs in production (behind the same host — no extra risk)
    app.UseSwagger();
    app.UseSwaggerUI(c => { c.SwaggerEndpoint("/swagger/v1/swagger.json", "CodeFlow API v2"); c.RoutePrefix = "api/docs"; });
}

app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<RepositoryHub>("/hubs/repo");

// Health check
app.MapGet("/health", () => new { status = "ok", version = "2.0.0", time = DateTime.UtcNow });

// Serve React SPA (in production)
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();
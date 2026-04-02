using API.Middlewares;
using API.Services;
using BLL;
using BLL.Interfaces;
using DAL;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

var railwayPort = builder.Configuration["PORT"];
if (!string.IsNullOrWhiteSpace(railwayPort))
{
    builder.WebHost.UseUrls($"http://+:{railwayPort}");
}
else if (!builder.Environment.IsDevelopment())
{
    builder.WebHost.UseUrls("http://+:8080");
}

DatabaseConnectionStringResolver.GetRequiredConnectionString(builder.Configuration);

builder.Services.AddHttpContextAccessor();
builder.Services.AddBusinessLogic(builder.Configuration);
builder.Services.AddScoped<IAppNotificationRealtimeDispatcher, SignalRAppNotificationRealtimeDispatcher>();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var configuredCorsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?.Concat((builder.Configuration["Cors:AllowedOrigins"] ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    .Select(origin => origin.Trim().TrimEnd('/'))
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray()
    ?? Array.Empty<string>();

var developmentCorsOrigins = new[]
{
    "http://localhost:5173",
    "https://localhost:5173",
    "http://127.0.0.1:5173",
    "https://127.0.0.1:5173",
    "http://localhost:4173",
    "https://localhost:4173",
    "http://127.0.0.1:4173",
    "https://127.0.0.1:4173"
};

var effectiveCorsOrigins = configuredCorsOrigins.Length > 0
    ? configuredCorsOrigins
    : builder.Environment.IsDevelopment()
        ? developmentCorsOrigins
        : throw new InvalidOperationException(
            "FATAL: CORS allowed origins are not configured. " +
            "Set 'Cors:AllowedOrigins' or the environment variable 'Cors__AllowedOrigins' " +
            "with the public frontend URL, for example 'https://your-frontend.vercel.app'.");

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend",
        b => b.WithOrigins(effectiveCorsOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials());
});

var jwtSettings = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSettings["Key"];
const int minJwtKeyBytes = 32;

if (string.IsNullOrEmpty(jwtKey))
{
    throw new InvalidOperationException(
        "FATAL: JWT Key is not configured. " +
        "Set 'Jwt:Key' or the environment variable 'Jwt__Key' with at least 32 characters. " +
        "For production, use a cryptographically secure random key of 32+ characters.");
}

var keyBytes = Encoding.UTF8.GetBytes(jwtKey);
if (keyBytes.Length < minJwtKeyBytes)
{
    throw new InvalidOperationException(
        $"FATAL: JWT Key is too short ({keyBytes.Length} bytes). " +
        "JWT Key must be at least 32 bytes long for HMAC-SHA256 signing. " +
        $"Current key: '{jwtKey.Substring(0, Math.Min(8, jwtKey.Length))}...' " +
        "Please update 'Jwt:Key' or 'Jwt__Key' with a longer, secure key.");
}

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        ClockSkew = TimeSpan.Zero
    };
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        },
        OnForbidden = async context =>
        {
            var path = context.HttpContext.Request.Path.Value ?? string.Empty;
            if (!path.StartsWith("/api/projects", StringComparison.OrdinalIgnoreCase))
                return;

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            var method = context.HttpContext.Request.Method;
            var isAdmin = context.HttpContext.User.IsInRole("Admin");
            var isRead = HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);
            var message = isAdmin && !isRead
                ? "BR-ADM-18: Admin is not allowed to modify project information"
                : "BR-MNG-01: Only a Manager can create and manage labeling projects";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { message }));
        }
    };
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;

        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    });
builder.Services.AddSignalR(options =>
{
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
    options.MaximumReceiveMessageSize = 32 * 1024;
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
}).AddMessagePackProtocol();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Data Labeling API", Version = "v1" });

    var apiXmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var apiXmlPath = Path.Combine(AppContext.BaseDirectory, apiXmlFile);
    if (File.Exists(apiXmlPath))
    {
        c.IncludeXmlComments(apiXmlPath);
    }

    var coreXmlFile = "Core.xml";
    var coreXmlPath = Path.Combine(AppContext.BaseDirectory, coreXmlFile);
    if (File.Exists(coreXmlPath))
    {
        c.IncludeXmlComments(coreXmlPath);
    }

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Enter your token below (do not type 'Bearer ').",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            new string[] {}
        }
    });
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseMiddleware<ExceptionMiddleware>();
await app.Services.InitializeInfrastructureAsync(app.Environment.IsDevelopment());
app.UseSwagger();
app.UseSwaggerUI();

app.UseStaticFiles();
app.UseRouting();
app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new
{
    service = "Data Labeling API",
    status = "running",
    environment = app.Environment.EnvironmentName
})).AllowAnonymous();

app.MapGet("/health", async (DAL.ApplicationDbContext db, CancellationToken cancellationToken) =>
{
    try
    {
        var canConnect = await db.Database.CanConnectAsync(cancellationToken);
        return canConnect
            ? Results.Ok(new { status = "ok", database = "reachable" })
            : Results.Problem(
                title: "Database unavailable",
                statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (Exception ex)
    {
        return Results.Problem(
            title: "Database unavailable",
            detail: app.Environment.IsDevelopment() ? ex.Message : null,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).AllowAnonymous();

app.MapControllers();
app.MapHub<API.Hubs.AppNotificationHub>("/hubs/notifications");
app.Run();

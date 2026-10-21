using Application;
using Application.Interfaces;
using Api.Hubs;
using Api.Realtime;
using Infrastructure;
using Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.OpenApi.Models;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

builder.Services.AddSignalR();
builder.Services.AddSingleton<IUserIdProvider, JwtUserIdProvider>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Dropship API",
        Version = "v1",
        Description = "Enterprise SaaS Platform API"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT Authorization header using the Bearer scheme."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// Second line of defence behind ResilientBackgroundService: the default StopHost would let one
// background loop throwing (a transient Postgres timeout is enough) tear down the whole API and
// silently stop order sync until someone restarts it.
builder.Services.Configure<HostOptions>(options =>
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<IFulfilQueueRealtimePublisher, SignalRFulfilQueueRealtimePublisher>();

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendApps", policy =>
    {
        var configuredOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? ["http://localhost:5173", "http://localhost:5174"];
        var isDevelopment = builder.Environment.IsDevelopment();

        // SPA origins from config; chrome-extension:// for Product Scraper (unpacked IDs vary).
        // Dev also allows any localhost / 127.0.0.1 origin (Vite 5173/5174).
        policy.SetIsOriginAllowed(origin =>
            {
                if (string.IsNullOrWhiteSpace(origin))
                {
                    return false;
                }

                if (configuredOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                {
                    return false;
                }

                if (uri.Scheme.Equals("chrome-extension", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                return isDevelopment && (uri.Host is "localhost" or "127.0.0.1");
            })
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
    await seeder.SeedAsync();
}

app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("QueryString", httpContext.Request.QueryString.Value ?? "");
    };
    options.MessageTemplate =
        "HTTP {RequestMethod} {RequestPath}{QueryString} responded {StatusCode} in {Elapsed:0.0000} ms";
});
app.UseMiddleware<Api.Middleware.ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// IIS Application Initialization often hits http://localhost/.../health — do not redirect that
// away from the in-process pipeline or HostedServices may never start after recycle.
app.UseWhen(
    context => !IsLivenessPath(context.Request.Path),
    branch => branch.UseHttpsRedirection());
app.UseCors("FrontendApps");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<NotificationsHub>("/hubs/notifications");

static bool IsLivenessPath(PathString path) =>
    path.Equals("/api/v1/health", StringComparison.OrdinalIgnoreCase)
    || path.Equals("/api/v1/healthy", StringComparison.OrdinalIgnoreCase)
    || path.Equals("/health", StringComparison.OrdinalIgnoreCase)
    || path.Equals("/healthy", StringComparison.OrdinalIgnoreCase);

app.Run();

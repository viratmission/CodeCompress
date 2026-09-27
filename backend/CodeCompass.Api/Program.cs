using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using CodeCompass.Api.Data;
using CodeCompass.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Controllers ────────────────────────────────────────────────────────────
builder.Services.AddControllers();

// ── HTTP clients ───────────────────────────────────────────────────────────
builder.Services.AddHttpClient("watsonx");
builder.Services.AddHttpClient("iam");

// ── Application services ───────────────────────────────────────────────────
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IRepositoryAnalyzer, RepositoryAnalyzer>();
builder.Services.AddScoped<IArchitectureService, ArchitectureService>();
builder.Services.AddScoped<IContextRetrievalService, ContextRetrievalService>();
builder.Services.AddSingleton<WatsonxProvider>();
builder.Services.AddScoped<IAssistantService, AssistantService>();
builder.Services.AddScoped<IOnboardingService, OnboardingService>();
builder.Services.AddScoped<IStarterTaskService, StarterTaskService>();

// ── Authentication & JWT Bearer ────────────────────────────────────────────
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false; // Dev environment friendly
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = JwtKeyHelper.GetSecurityKey(builder.Configuration),
        ValidateIssuer = true,
        ValidIssuer = JwtKeyHelper.GetIssuer(builder.Configuration),
        ValidateAudience = true,
        ValidAudience = JwtKeyHelper.GetAudience(builder.Configuration),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(1)
    };
});

builder.Services.AddAuthorization();

// ── Swagger / OpenAPI ──────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ── Entity Framework Core / SQL Server ────────────────────────────────────
builder.Services.AddDbContext<CodeCompassDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ── CORS — allow the React Vite dev server ─────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("ReactDevServer", policy =>
    {
        policy.WithOrigins(
                "http://localhost:5173",   // Vite default
                "http://localhost:3000")   // fallback
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// ── Development middleware ─────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "CodeCompass API v1");
    });
}

app.UseHttpsRedirection();
app.UseCors("ReactDevServer");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

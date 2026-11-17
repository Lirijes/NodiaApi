using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using NodiaApi.Data;

var builder = WebApplication.CreateBuilder(args);

// --- Database (Supabase Postgres) ---
// Connection string is stored in user-secrets as ConnectionStrings:Supabase
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("Supabase"),
        // Explicit schema så att migrationernas RLS/REVOKE träffar rätt tabell.
        npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "public")));

// --- Auth (Supabase JWT) ---
var supabaseUrl = builder.Configuration["Supabase:Url"]
    ?? throw new InvalidOperationException("Supabase:Url is missing in appsettings.json");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Authority -> ASP.NET Core hämtar Supabase publika signeringsnycklar (JWKS)
        // via {Authority}/.well-known/openid-configuration och verifierar signaturen.
        options.Authority = $"{supabaseUrl}/auth/v1";

        // Behåll claim-namnen från JWT:n ("sub", "email", "role") i stället för
        // att .NET döper om dem till långa ClaimTypes-URI:er.
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new()
        {
            ValidateIssuerSigningKey = true,             // signatur mot JWKS
            ValidateIssuer = true,
            ValidIssuer = $"{supabaseUrl}/auth/v1",
            ValidateAudience = true,
            ValidAudience = "authenticated",             // bara inloggade användare
            ValidateLifetime = true,                     // exp / nbf
            ClockSkew = TimeSpan.FromSeconds(30),        // standard är 5 min
            NameClaimType = "sub",
            RoleClaimType = "role"
        };
    });
// --- Authorization: inloggning krävs som STANDARD ---
// DefaultPolicy gäller [Authorize]; FallbackPolicy gäller endpoints UTAN attribut,
// så en endpoint som glömmer [Authorize] är ändå skyddad. Öppna endpoints måste
// uttryckligen märkas med [AllowAnonymous].
builder.Services.AddAuthorization(options =>
{
    var authenticatedUser = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        // "sub" måste vara ett giltigt Supabase-användar-id (uuid) – se User.GetUserId().
        .RequireAssertion(ctx => Guid.TryParse(ctx.User.FindFirst("sub")?.Value, out _))
        // Supabase anonyma inloggningar har också aud=authenticated – de släpps inte in.
        .RequireAssertion(ctx => ctx.User.FindFirst("is_anonymous")?.Value != "true")
        .Build();

    options.DefaultPolicy = authenticatedUser;
    options.FallbackPolicy = authenticatedUser;
});

// --- Controllers + Swagger ---
builder.Services.AddControllers()
    // Enums som JSON-strängar i camelCase, t.ex. CompanyRole.Owner -> "owner".
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Nodia API", Version = "v1" });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Paste a Supabase access token"
    });

    c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// I Development körs vi bakom Vite-proxyn (och i Docker utan HTTPS),
// där en omdirigering till HTTPS skulle bryta /api-anropen.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

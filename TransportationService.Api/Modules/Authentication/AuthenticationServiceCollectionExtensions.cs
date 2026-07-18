using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using TransportationService.Api.Modules.Authentication.Services;

namespace TransportationService.Api.Modules.Authentication;

public static class AuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Strongly-typed, validated at startup. Missing/short signing key fails startup
        // clearly (in every environment; Development supplies a dev-only key in config).
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

        services.AddSingleton<ITokenService, TokenService>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IAuthService, AuthService>();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(
                            string.IsNullOrEmpty(jwt.SigningKey)
                                ? new string('0', 32) // placeholder; ValidateOnStart already rejects this config
                                : jwt.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub,
                    RoleClaimType = System.Security.Claims.ClaimTypes.Role,
                };

                // Emit ProblemDetails (not an empty body) on auth failure.
                options.Events = new JwtBearerEvents
                {
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        await WriteProblemAsync(
                            context.HttpContext,
                            StatusCodes.Status401Unauthorized,
                            "Unauthorized",
                            "Authentication is required and has failed or has not been provided.");
                    },
                    OnForbidden = context => WriteProblemAsync(
                        context.HttpContext,
                        StatusCodes.Status403Forbidden,
                        "Forbidden",
                        "You do not have permission to access this resource."),
                };
            });

        services.AddAuthorization();
        return services;
    }

    private static async Task WriteProblemAsync(HttpContext context, int status, string title, string detail)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        var payload = JsonSerializer.Serialize(new
        {
            type = $"https://httpstatuses.io/{status}",
            title,
            status,
            detail,
        });
        await context.Response.WriteAsync(payload);
    }
}

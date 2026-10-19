using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Authentication.Services;

namespace TransportationService.Api.Modules.Tenancy.Provisioning;

/// <summary>
/// Command-line entry for <see cref="TenantProvisioningService"/>, handled by Program.cs after
/// the host is BUILT (configuration, DbContext, mail provider, column encryption and the startup
/// security validator all exactly as the service uses them) but BEFORE it runs — no web server
/// is started, no background service runs, and the process exits when done:
///
/// <code>
/// dotnet TransportationService.Api.dll --provision-tenant --name "Transport Peeters BV" --slug transport-peeters \
///     --admin-email jan@peeters.be [--admin-first-name Jan] [--admin-last-name Peeters] \
///     [--legal-entity-name "Transport Peeters BV"] [--vat BE0123456749] [--company-number 0123456749] \
///     [--country BE] [--timezone Europe/Brussels] [--language nl] [--resend-activation] [--print-activation-link]
/// </code>
///
/// Output is non-secret: ids, what was created vs. what already existed, and that an activation
/// mail was queued with its expiry. The activation link (= a password-setting credential) is only
/// printed with the explicit <c>--print-activation-link</c> flag, intended for air-gapped installs
/// without working outbound mail — anyone who sees that terminal output or its scrollback/log can
/// take over the administrator account until it is used or expires.
/// </summary>
public static class TenantProvisioningCommand
{
    public const string Flag = "--provision-tenant";
    public const string PrintLinkFlag = "--print-activation-link";
    public const string ResendFlag = "--resend-activation";

    private static readonly string[] ValueOptions =
    [
        "--name", "--slug", "--admin-email", "--admin-first-name", "--admin-last-name",
        "--legal-entity-name", "--vat", "--company-number", "--country", "--timezone", "--language",
    ];

    private static readonly string[] SwitchOptions = [Flag, PrintLinkFlag, ResendFlag];

    public static bool IsRequested(IReadOnlyList<string> args) => args.Contains(Flag, StringComparer.Ordinal);

    /// <summary>
    /// The arguments the web host may see: provisioning options are removed so they never leak
    /// into IConfiguration (the command-line configuration provider would read "--name X" as a
    /// configuration key). Everything else (e.g. --environment) is passed through unchanged.
    /// </summary>
    public static string[] HostArguments(string[] args)
    {
        if (!IsRequested(args))
        {
            return args;
        }

        var result = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            var key = arg.Split('=', 2)[0];
            if (SwitchOptions.Contains(key, StringComparer.Ordinal))
            {
                continue;
            }

            if (ValueOptions.Contains(key, StringComparer.Ordinal))
            {
                if (!arg.Contains('=') && i + 1 < args.Length)
                {
                    i++; // skip the value too
                }

                continue;
            }

            result.Add(arg);
        }

        return [.. result];
    }

    /// <summary>Parses the provisioning options; <paramref name="error"/> explains the first problem.</summary>
    public static bool TryParse(
        IReadOnlyList<string> args, out TenantProvisioningRequest? request, out bool printActivationLink, out string? error)
    {
        request = null;
        printActivationLink = false;
        error = null;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var resend = false;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = arg.Split('=', 2);
            var key = parts[0];
            if (key == Flag)
            {
                continue;
            }

            if (key == PrintLinkFlag)
            {
                printActivationLink = true;
                continue;
            }

            if (key == ResendFlag)
            {
                resend = true;
                continue;
            }

            if (!ValueOptions.Contains(key, StringComparer.Ordinal))
            {
                continue; // host options (e.g. --environment) are not ours
            }

            string? value;
            if (parts.Length == 2)
            {
                value = parts[1];
            }
            else if (i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = args[++i];
            }
            else
            {
                error = $"Optie {key} verwacht een waarde.";
                return false;
            }

            values[key] = value;
        }

        foreach (var required in new[] { "--name", "--slug", "--admin-email" })
        {
            if (!values.TryGetValue(required, out var value) || string.IsNullOrWhiteSpace(value))
            {
                error = $"Optie {required} is verplicht.";
                return false;
            }
        }

        request = new TenantProvisioningRequest(
            values["--name"], values["--slug"], values["--admin-email"],
            values.GetValueOrDefault("--admin-first-name"), values.GetValueOrDefault("--admin-last-name"),
            values.GetValueOrDefault("--legal-entity-name"), values.GetValueOrDefault("--vat"),
            values.GetValueOrDefault("--company-number"), values.GetValueOrDefault("--country"),
            values.GetValueOrDefault("--timezone"), values.GetValueOrDefault("--language"),
            resend);
        return true;
    }

    /// <summary>Runs the command against the built host's services. Returns the process exit code.</summary>
    public static async Task<int> RunAsync(
        IServiceProvider services, string[] args, TextWriter output, TextWriter errorOutput, CancellationToken cancellationToken = default)
    {
        if (!TryParse(args, out var request, out var printLink, out var parseError))
        {
            await errorOutput.WriteLineAsync($"Provisioning geweigerd: {parseError}");
            await errorOutput.WriteLineAsync(Usage);
            return 2;
        }

        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;
        var db = provider.GetRequiredService<TransportationDbContext>();

        // The schema must be current: provisioning never migrates on its own (that is a
        // deliberate deploy step with its own backup), it refuses instead.
        if (db.Database.IsRelational())
        {
            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            if (pending.Count > 0)
            {
                await errorOutput.WriteLineAsync(
                    $"Provisioning geweigerd: de database heeft {pending.Count} openstaande migratie(s) "
                    + $"(eerste: {pending[0]}). Voer eerst 'dotnet ef database update' / de deploy-migratiestap uit.");
                return 3;
            }
        }

        return await ExecuteAsync(
            new TenantProvisioningService(
                db,
                provider.GetRequiredService<IPasswordHasher>(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<IHostEnvironment>(),
                provider.GetRequiredService<IConfiguration>(),
                provider.GetRequiredService<ILoggerFactory>()),
            request!, printLink, output, errorOutput, cancellationToken);
    }

    /// <summary>The part after parsing (shared with the tests): provision, then report.</summary>
    public static async Task<int> ExecuteAsync(
        TenantProvisioningService service, TenantProvisioningRequest request, bool printActivationLink,
        TextWriter output, TextWriter errorOutput, CancellationToken cancellationToken = default)
    {
        TenantProvisioningResult result;
        try
        {
            result = await service.ProvisionAsync(request, cancellationToken);
        }
        catch (TenantProvisioningException exception)
        {
            await errorOutput.WriteLineAsync($"Provisioning geweigerd: {exception.Message}");
            return 1;
        }

        await output.WriteLineAsync(result.TenantCreated
            ? $"Nieuwe tenant '{result.Slug}' ingericht."
            : $"Tenant '{result.Slug}' bestond al; ontbrekende onderdelen aangevuld.");
        foreach (var line in result.Report)
        {
            await output.WriteLineAsync($"  - {line}");
        }

        await output.WriteLineAsync($"Tenant-id: {result.TenantId}");
        await output.WriteLineAsync($"Beheerder-id: {result.AdminUserId}");

        if (result.Activation is { } activation)
        {
            if (printActivationLink)
            {
                // Explicit operator request only (air-gapped installs). This IS a credential.
                await output.WriteLineAsync(
                    "WAARSCHUWING: de volgende activatielink geeft toegang tot het beheerdersaccount tot hij gebruikt "
                    + "of verlopen is. Deel hem alleen rechtstreeks met de beheerder en wis daarna de terminalhistoriek.");
                await output.WriteLineAsync($"Activatielink: {service.BuildActivationLinkForOperator(result)}");
            }
            else
            {
                await output.WriteLineAsync(
                    $"De beheerder ontvangt de activatielink per e-mail (geldig tot {activation.ExpiresAtUtc:yyyy-MM-dd HH:mm} UTC). "
                    + "De e-mail wordt verstuurd door de draaiende TMS-service (outbox).");
            }
        }

        return 0;
    }

    public const string Usage =
        "Gebruik: dotnet TransportationService.Api.dll --provision-tenant --name \"<bedrijf>\" --slug <slug> "
        + "--admin-email <e-mail> [--admin-first-name <voornaam>] [--admin-last-name <achternaam>] "
        + "[--legal-entity-name \"<juridische naam>\"] [--vat <btw-nummer>] [--company-number <ondernemingsnummer>] "
        + "[--country BE] [--timezone Europe/Brussels] [--language nl|fr|en] [--resend-activation] [--print-activation-link]";
}

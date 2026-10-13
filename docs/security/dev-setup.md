# Lokale ontwikkelconfiguratie (secrets)

Sinds de securityhardening staan **geen** development-secrets meer in getrackte configuratie
(`appsettings.Development.json` bevat lege placeholders). De applicatie faalt bij het opstarten
als de signing key ontbreekt, een bekende placeholder is, of te kort is. Zet de lokale secrets
daarom eenmalig via **.NET user-secrets**:

```bash
cd TransportationService.Api

# JWT signing key (minimaal 32 bytes, willekeurig — nooit de oude gecommitte key hergebruiken)
dotnet user-secrets set "Jwt:SigningKey" "$(openssl rand -base64 48)"

# Lokale dev-databaseverbinding (wachtwoord hoort NIET in getrackte config)
dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  "Host=localhost;Port=5432;Database=transportation_service;Username=postgres;Password=postgres"
```

Op Windows PowerShell voor de key:

```powershell
$bytes = New-Object 'System.Byte[]' 48; [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
dotnet user-secrets set "Jwt:SigningKey" ([Convert]::ToBase64String($bytes))
```

## Dev-impersonatieheaders

`appsettings.Development.json` zet `Dev:AllowImpersonationHeaders: true`, zodat je in Scalar/curl
met `X-Dev-User-Id` en `X-Dev-Tenant-Id` kunt testen zonder login. Deze headers worden
**uitsluitend** in Development gehonoreerd; de host **weigert op te starten** wanneer de vlag in
een andere omgeving aan staat (`StartupSecurityValidator`).

## Gecompromitteerde key

De eerder gecommitte development signing key
(`dev-only-signing-key-change-me-32bytes-minimum!!`) moet als **gecompromitteerd** worden
beschouwd. Hij is opgenomen in de deny-list van `JwtOptionsValidator` en kan geen enkele
omgeving meer opstarten. Roteer alle sleutels die ooit met deze waarde zijn gebruikt. De
volledige verwijdering uit de Git-history vereist een history-rewrite (bijv. `git filter-repo`);
dat is een bewuste, gecoördineerde operationele actie — zie `operational-checklist.md`.

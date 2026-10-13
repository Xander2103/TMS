# Attendance — security

## Dreigingsmodel (kort)

De kiosk hangt fysiek bereikbaar aan een muur en werkt zonder gebruikerssessie; PIN's
leven in een kleine sleutelruimte. Belangrijkste dreigingen: gestolen/nagemaakt device,
PIN-bruteforce/enumeratie, kiosk als achterdeur naar ERP-data, cross-tenant-injectie,
credential-lekken uit een databankdump.

## PIN-opslag (AttendanceCredential)

Twee lagen, nooit plaintext, nooit omkeerbaar:

1. **`SecretHash`** — PBKDF2-HMAC-SHA512 via de bestaande `PasswordHasher`
   (IdentityV3-envelope, 210 000 iteraties). Geen `SHA256(PIN)`, geen encryptie.
2. **`LookupHash`** — HMAC-SHA256 over `tenantId:pin` met de serverside pepper
   **`Attendance:PinPepper`** (base64, ≥ 32 bytes). Alleen voor O(1)-identificatie
   (PIN-only-kioskflow) én de tenant-uniciteitsindex. Zonder de pepper valt uit een
   gelekte databank niets over PIN's af te leiden.

Fail-closed: zonder (geldige) pepper zijn PIN-beheer en de volledige kioskflow
uitgeschakeld (503/NotConfigured); een geconfigureerde-maar-ongeldige pepper laat de
host bij startup weigeren te booten (`StartupSecurityValidator`). De API geeft een PIN
uitsluitend éénmalig terug bij *genereren*; er bestaat geen uitlees-endpoint; de PIN
komt nooit in auditlog of serverlogs.

## Device-authenticatie (KioskDevice)

- Provisioning levert éénmalig een `deviceKey` = `{deviceId:N}.{secret}` met een
  256-bit random secret; server-side staat alleen `SHA256(secret)` (hoge-entropie token,
  password-grade werkfactor onnodig — zelfde afweging als API-tokens).
- Verificatie constant-time (`CryptographicOperations.FixedTimeEquals`); onbekend,
  uitgeschakeld of fout device ⇒ één generiek faalpad.
- Rotatie (`POST /api/attendance/kiosks/{id}/rotate-secret`) maakt de oude key per
  direct ongeldig; uitschakelen blokkeert alle kioskverkeer van dat device.
- De kiosk-endpoints zijn `[AllowAnonymous]` (bewust, allowlisted in
  `Phase1ConfigAndAuthTests`) omdat het device géén user-JWT heeft; de
  `X-Kiosk-Device`-header is de per-request-authenticatie. Ze geven zonder geldig
  device nooit data en met device alleen de identify/punch-flow van de tenant van dat
  device — nooit employee-lijsten, HR-data of andere ERP-endpoints.

## Tweestapsflow + interaction tokens

1. `identify` (PIN) → minimale respons (voornaam + status + toegestane acties) + een
   **single-use interaction token** (45 s TTL, gebonden aan het device, in-memory).
2. `punch` consumeert dat token voor exact één actie.

De kiosk krijgt dus nooit een algemene employee-credential; replay, cross-device-gebruik
en verlopen tokens eindigen allemaal in "sessie verlopen, voer uw code opnieuw in".

## Brute force / enumeratie

- **Rate limiting**: policy `kiosk` (15/min per client-IP, geen queue) bovenop de
  bestaande auth/webhook/session-policies.
- **Device-lockout**: foute codes zijn bij PIN-only-identificatie niet aan één
  credential toe te schrijven, dus de teller zit op de prikklok: na 5 ongeldige codes
  5 min backoff, verdubbelend tot 60 min.
- **Credential-lockout**: zelfde schema per credential (relevant zodra een lookup wél
  matcht maar verificatie faalt, en voor toekomstige credentialtypes).
- **Anti-enumeratie**: onbekende code, foute code, ingetrokken credential, inactieve
  medewerker en lockout geven exact dezelfde respons ("Code ongeldig."), en een
  onbekende code kost via een dummy-PBKDF2-verificatie evenveel tijd als een foute.
- *Bekende, bewust geaccepteerde beperking:* de lockout-tellers zijn read-modify-write
  (geen atomaire increment); onder extreme parallelle gokdruk kan een verhoging verloren
  gaan en de effectieve drempel iets oprekken. De per-device/per-IP rate limiter houdt de
  gokrate hoe dan ook ver onder brute-force-niveau, dus dit is gedocumenteerd i.p.v.
  gecompliceerd weggewerkt.

## Tenant-isolatie

Alle entiteiten zijn `ITenantOwned` (globale queryfilter + expliciete predicates).
Anonieme kioskrequests hebben geen tenantcontext: de tenant volgt uit het device en elke
vervolgquery is expliciet op die tenant gescoped — een PIN van tenant B bestaat niet
voor een prikklok van tenant A (getest in `KioskSecurityTests`).

## Employee-lifecycle

`EmployeeService.DeactivateAsync` trekt actieve prikklokcodes mee in; punch-endpoints
weigeren inactieve medewerkers; historiek blijft altijd bestaan.

## Testdekking

`TransportationService.Api.Tests/Attendance/*` (state machine, concurrency-invariant,
kiosk-aanvalsoppervlak, credentialbeheer, correctie-audit, sweep) plus de systemische
registers: Phase1-allowlist (3 kioskacties, met motivering), Phase10 (elk endpoint
geclassificeerd), Phase8 (alle 7 permissies attribuut-gecheckt) en de module-eigen
`AttendanceEndpointSecurityTests`.

## Logging

Geen PIN's, geen device-secrets, geen tokens in logs; sweep-logging bevat alleen
aantallen en tenant-id's, geen persoonsgegevens.

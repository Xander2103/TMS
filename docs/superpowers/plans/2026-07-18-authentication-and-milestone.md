# TransportationService — Authentication & Milestone Plan

Date: 2026-07-18
Author: Architecture/engineering (autonomous session)

## Context & root cause

The app has **no authentication layer**. A temporary *dev-header seam* is used:

- Frontend `apiClient` sends `X-Dev-User-Id` / `X-Dev-Tenant-Id` (from `VITE_DEV_USER_ID` /
  `VITE_DEV_TENANT_ID`, default `''`).
- `TenantContextMiddleware` reads those headers into `ICurrentUserContext` / `ITenantContext`.
- `RequirePermissionAttribute` returns **401** when `CurrentUserId` is null.

With no `.env` configured, no user header is sent → every protected endpoint 401s. The
middleware is documented in-code as *"the single seam to replace when real authentication
(JWT claims) is introduced."*

## Baseline (verified 2026-07-18)

- Backend: builds, 0 warnings; **49 tests pass**.
- Frontend: build passes; **lint has 4 pre-existing errors** in `hooks/usePagedQuery.ts`
  (`react-hooks/set-state-in-effect`) — to fix in hardening.
- Test project has transitive advisory NU1903 (SQLitePCLRaw) — hardening.

## Phase 1 — Authentication (this wave; the gate)

### Design decisions

1. **Password hashing:** ASP.NET Core `PasswordHasher<User>` (PBKDF2) via
   `Microsoft.Extensions.Identity.Core`. Never plain-text.
2. **JWT:** `Microsoft.AspNetCore.Authentication.JwtBearer`; symmetric HMAC-SHA256 key from
   config. Strongly-typed `JwtOptions` (Issuer, Audience, SigningKey, AccessTokenMinutes,
   RefreshTokenDays). Validated at startup; **fail fast outside Development** if key missing.
   Dev key lives in `appsettings.Development.json` (dev-only, not a production secret).
3. **Claims:** `sub` (userId), `tenant_id`, `email`, `given_name`, `family_name`, `role` (n),
   `permission` (n). No sensitive data in the token.
4. **Refresh tokens:** `RefreshToken` entity (hashed token, expiry, rotation, revoke-on-logout).
   Migration required. Complete implementation — no insecure partial.
5. **Endpoints:** `POST /api/auth/login`, `POST /api/auth/refresh`, `POST /api/auth/logout`,
   `GET /api/auth/me`. Generic invalid-credentials error (no user-enumeration).
6. **Context resolution:** `TenantContextMiddleware` resolves tenant & user from **claims when
   authenticated** (never trusts client tenant id then); dev-header fallback only in Development
   when unauthenticated. Existing consumers of `ITenantContext`/`ICurrentUserContext` unchanged.
7. **Middleware order:** HttpsRedirection → CORS → **UseAuthentication** → TenantContextMiddleware
   → UseAuthorization → MapControllers.
8. **ProblemDetails:** `AddProblemDetails`; JwtBearer emits ProblemDetails on 401;
   `RequirePermission` emits ProblemDetails 403.
9. **Dev admin:** `admin@dev.local` (already seeded, `PasswordHash=null`) gets a hashed dev
   password set **only when null** (never resets a deliberately-changed password). Dev-only.
   Reported at completion. Seeder runs only in Development.
10. **Guards:** disabled/blocked users and users of an inactive tenant cannot authenticate.
    Users only receive roles/permissions of their own tenant.

### Tests (backend)

login success / invalid email / invalid password / inactive user / blocked user /
inactive tenant / missing token → 401 / malformed token → 401 / invalid signature → 401 /
expired token → 401 / wrong issuer → 401 / wrong audience → 401 / me endpoint /
permission claims / role claims / tenant isolation of permissions / refresh rotation /
logout revokes / password hashing round-trip.

### Frontend

Auth store (`AuthProvider` + `useAuth`), professional login page, protected routes with
redirect + returnTo, bearer token in `apiClient`, 401 → clear state + redirect (no loops),
session restore on refresh (via refresh token), logout, current user + tenant in shell,
loading / invalid-credentials / server-error states, accessible & responsive.

### Verification

migration applies · backend build · backend tests · FE type-check · FE lint · FE build ·
manual: login, `/api/auth/me`, `/api/users` 200 after login, 401 without token, 403 for
missing permission.

## Phases 2–10 (roadmap)

Execute in verified waves after auth is green, following the milestone brief: Master Data
completion (Users, Roles/Permissions, Employees, Drivers, Qualifications, Qualification Types,
Eligibility, Locations, Company Settings, Reference Data) → Fleet → HR/Availability →
Transport Operations → Trips/Planning → Driver Workflow/Scanning → Pricing/Invoicing →
Dashboards/Reporting → Notifications → hardening pass. External integrations out of scope;
clean extension points only.

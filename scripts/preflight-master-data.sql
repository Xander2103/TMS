-- Master-data identity preflight (core/master-data remediation 2026-10-18).
--
-- READ-ONLY. Run against the production database BEFORE applying the migrations
--   20261018120257_ReferenceIntegrityAndFinalInvoiceSnapshot  (case-insensitive customer numbers)
--   20261018123011_IdentityNormalization                       (normalized e-mail, case-insensitive role names)
-- Those migrations deliberately ABORT (RAISE EXCEPTION) when they meet duplicates; they never merge or
-- rename anything themselves. This query lists every conflict up front, with the row ids, so they can
-- be resolved by a human first (docs/delivery/operations.md §3.6).
--
-- Usage:  psql -X -A -t -F $'\t' -v ON_ERROR_STOP=1 -h <host> -U <user> -d <db> -f scripts/preflight-master-data.sql
-- Output: one line per conflict:  kind <TAB> tenant <TAB> normalized value <TAB> rows <TAB> details
--         no output = no conflicts = the migrations will not abort on identity duplicates.
-- Works before AND after the migrations (it only reads the original columns).

-- 1. Customer numbers that differ only in case / surrounding spaces. Soft-deleted customers count too:
--    the unique index spans ALL rows, because history prints the number.
SELECT 'customer_number' AS kind,
       c."TenantId"::text AS tenant,
       upper(btrim(c."CustomerNumber")) AS normalized,
       count(*) AS rows,
       string_agg(format('%s "%s" %s%s', c."Id", c."CustomerNumber", c."Name",
                         CASE WHEN c."IsDeleted" THEN ' [verwijderd]' ELSE '' END), ' | ' ORDER BY c."CreatedAt") AS details
  FROM customers c
 GROUP BY c."TenantId", upper(btrim(c."CustomerNumber"))
HAVING count(*) > 1

UNION ALL

-- 2. User e-mail addresses that differ only in case / surrounding spaces, per tenant (login identity).
SELECT 'user_email',
       u."TenantId"::text,
       lower(btrim(u."Email")),
       count(*),
       string_agg(format('%s "%s" %s %s%s', u."Id", u."Email", u."FirstName", u."LastName",
                         CASE WHEN u."IsActive" THEN '' ELSE ' [inactief]' END), ' | ' ORDER BY u."CreatedAt")
  FROM users u
 GROUP BY u."TenantId", lower(btrim(u."Email"))
HAVING count(*) > 1

UNION ALL

-- 3. Role names that differ only in case, per tenant.
SELECT 'role_name',
       r."TenantId"::text,
       lower(r."Name"),
       count(*),
       string_agg(format('%s "%s"%s (%s gebruiker(s))', r."Id", r."Name",
                         CASE WHEN r."IsSystemRole" THEN ' [systeemrol]' ELSE '' END,
                         (SELECT count(*) FROM user_roles ur WHERE ur."RoleId" = r."Id")), ' | ' ORDER BY r."CreatedAt")
  FROM roles r
 GROUP BY r."TenantId", lower(r."Name")
HAVING count(*) > 1

ORDER BY 1, 2, 3;

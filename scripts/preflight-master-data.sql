-- Master-data identity preflight (core/master-data remediation 2026-10-18; fleet + template checks
-- added by the production-hardening remediation, production-readiness MIG-05).
--
-- READ-ONLY. Run against the production database BEFORE applying the migrations
--   20261017210920_FleetReadinessIdentityAndDamageLinks       (unique plate / VIN per tenant)
--   20261018120257_ReferenceIntegrityAndFinalInvoiceSnapshot  (case-insensitive customer numbers)
--   20261018123011_IdentityNormalization                       (normalized e-mail, case-insensitive role names)
--   20261018143508_WarehouseInventoryIntegrity                 (BM-nnnn template codes, < 10 000 per tenant)
-- Not covered here (the migrations guard them themselves): sales_categories text lengths
-- (CommercialWaveAuditFixes; its columns do not exist on every older baseline). The PostgreSQL >= 13
-- requirement is checked by scripts/deploy-transportationservice.sh.
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

UNION ALL

-- 4. Fleet identity (20261017210920_FleetReadinessIdentityAndDamageLinks, production-readiness MIG-05a):
--    the migration RAISEs on live (not soft-deleted) vehicles/trailers of one tenant that share a plate
--    after normalisation (letters + digits, upper case) or a VIN (trimmed, upper case, empty = unknown).
--    Same expressions as the migration; reads only the original columns.
SELECT 'vehicle_plate',
       v."TenantId"::text,
       upper(regexp_replace(v."LicensePlate", '[^A-Za-z0-9]', '', 'g')),
       count(*),
       string_agg(format('%s "%s" %s', v."Id", v."LicensePlate", v."InternalNumber"), ' | ' ORDER BY v."CreatedAt")
  FROM vehicles v
 WHERE NOT v."IsDeleted"
 GROUP BY v."TenantId", upper(regexp_replace(v."LicensePlate", '[^A-Za-z0-9]', '', 'g'))
HAVING count(*) > 1

UNION ALL

SELECT 'vehicle_vin',
       v."TenantId"::text,
       NULLIF(upper(btrim(v."Vin")), ''),
       count(*),
       string_agg(format('%s "%s" %s', v."Id", v."Vin", v."InternalNumber"), ' | ' ORDER BY v."CreatedAt")
  FROM vehicles v
 WHERE NOT v."IsDeleted" AND NULLIF(upper(btrim(v."Vin")), '') IS NOT NULL
 GROUP BY v."TenantId", NULLIF(upper(btrim(v."Vin")), '')
HAVING count(*) > 1

UNION ALL

SELECT 'trailer_plate',
       t."TenantId"::text,
       upper(regexp_replace(t."LicensePlate", '[^A-Za-z0-9]', '', 'g')),
       count(*),
       string_agg(format('%s "%s" %s', t."Id", t."LicensePlate", t."InternalNumber"), ' | ' ORDER BY t."CreatedAt")
  FROM trailers t
 WHERE NOT t."IsDeleted"
 GROUP BY t."TenantId", upper(regexp_replace(t."LicensePlate", '[^A-Za-z0-9]', '', 'g'))
HAVING count(*) > 1

UNION ALL

SELECT 'trailer_vin',
       t."TenantId"::text,
       NULLIF(upper(btrim(t."Vin")), ''),
       count(*),
       string_agg(format('%s "%s" %s', t."Id", t."Vin", t."InternalNumber"), ' | ' ORDER BY t."CreatedAt")
  FROM trailers t
 WHERE NOT t."IsDeleted" AND NULLIF(upper(btrim(t."Vin")), '') IS NOT NULL
 GROUP BY t."TenantId", NULLIF(upper(btrim(t."Vin")), '')
HAVING count(*) > 1

UNION ALL

-- 5. Issued-item template codes (20261018143508_WarehouseInventoryIntegrity, MIG-05d): codes are
--    'BM-' || lpad(n, 4, '0'), which truncates from 10 000 templates per tenant on and then collides
--    on the unique index. Deleted rows count (they keep their number).
SELECT 'issued_item_template_count',
       i."TenantId"::text,
       '>= 10000 sjablonen',
       count(*),
       'te veel sjablonen voor de BM-nnnn-codering; neem contact op met ontwikkeling'
  FROM issued_item_templates i
 GROUP BY i."TenantId"
HAVING count(*) >= 10000

ORDER BY 1, 2, 3;

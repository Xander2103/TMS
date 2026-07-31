-- =============================================================================
-- Fase 9 — PostgreSQL least-privilege & defence-in-depth (VOORBEREID SCRIPT)
-- =============================================================================
-- Dit script wordt door Ops/DBA toegepast op de productie-database (checklist
-- #2/#8). Het is bewust GEEN EF-migratie: rol-/privilegebeheer hoort bij de
-- database-eigenaar, niet bij het applicatie-account dat er zelf door wordt
-- ingeperkt. Pas namen/wachtwoordbronnen aan de omgeving aan (secrets via vault).
--
-- Doel:
--   1. gescheiden accounts: migraties (DDL) vs. runtime (DML);
--   2. runtime-account kan audit_logs nooit herschrijven (append-only, ook op
--      privilege-niveau — bovenop de trigger uit migratie AuditAppendOnly);
--   3. voorbereiding voor Row Level Security per tenant.

-- ---------------------------------------------------------------------------
-- 1. Rollen
-- ---------------------------------------------------------------------------
-- CREATE ROLE ts_migrator LOGIN PASSWORD :'migrator_password';
-- CREATE ROLE ts_runtime  LOGIN PASSWORD :'runtime_password';

-- Migrator: eigenaar van het schema, mag DDL.
-- ALTER SCHEMA public OWNER TO ts_migrator;
-- GRANT ALL ON ALL TABLES IN SCHEMA public TO ts_migrator;

-- Runtime: alleen DML, geen DDL, geen TRUNCATE.
GRANT USAGE ON SCHEMA public TO ts_runtime;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO ts_runtime;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO ts_runtime;
ALTER DEFAULT PRIVILEGES FOR ROLE ts_migrator IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO ts_runtime;

-- ---------------------------------------------------------------------------
-- 2. Append-only audit-privileges (naast de trigger uit AuditAppendOnly)
-- ---------------------------------------------------------------------------
REVOKE UPDATE, DELETE, TRUNCATE ON audit_logs FROM ts_runtime;
-- Retentie/archivering: aparte maintenance-rol, tijdelijk en gelogd (checklist #29):
-- CREATE ROLE ts_audit_maintenance LOGIN PASSWORD :'maintenance_password';
-- GRANT SELECT, DELETE ON audit_logs TO ts_audit_maintenance;
-- ALTER TABLE audit_logs DISABLE TRIGGER trg_audit_logs_append_only;  -- alleen tijdens venster
-- ALTER TABLE audit_logs ENABLE TRIGGER trg_audit_logs_append_only;

-- ---------------------------------------------------------------------------
-- 3. Row Level Security — voorbereiding (defence-in-depth naast de globale
--    EF-tenantfilter). Activeren per tabel zodra de applicatie de tenant als
--    sessieparameter zet (SET app.tenant_id = '<uuid>'), bv. via een
--    connection-interceptor. Sjabloon:
-- ---------------------------------------------------------------------------
-- ALTER TABLE employees ENABLE ROW LEVEL SECURITY;
-- CREATE POLICY tenant_isolation ON employees
--     USING ("TenantId" = current_setting('app.tenant_id', true)::uuid);
-- ALTER TABLE employees FORCE ROW LEVEL SECURITY;  -- geldt dan ook voor de eigenaar
--
-- Let op: background-/systeemprocessen (dispatcher, seeders) draaien
-- tenant-overstijgend en hebben een BYPASSRLS-rol of een policy-uitzondering
-- nodig; activeer RLS daarom gefaseerd en met de volledige testsuite per stap.

-- ---------------------------------------------------------------------------
-- 4. Verifiëren
-- ---------------------------------------------------------------------------
-- \du                         -- rollen
-- \dp audit_logs              -- privileges
-- SELECT * FROM pg_policies;  -- actieve RLS-policies

-- Enforce in the database what the application already promises: audit events are only
-- ever appended. UPDATE, DELETE and TRUNCATE on AuditEvents are rejected for every role
-- that does not first drop these triggers (which itself requires table ownership and is
-- detectable through the externally anchored chain head).

CREATE OR REPLACE FUNCTION audit_events_reject_change() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'AuditEvents is append-only: % is not permitted', TG_OP
        USING ERRCODE = 'insufficient_privilege';
END;
$$;

DROP TRIGGER IF EXISTS "TR_AuditEvents_NoUpdateDelete" ON "AuditEvents";
CREATE TRIGGER "TR_AuditEvents_NoUpdateDelete"
    BEFORE UPDATE OR DELETE ON "AuditEvents"
    FOR EACH ROW EXECUTE FUNCTION audit_events_reject_change();

DROP TRIGGER IF EXISTS "TR_AuditEvents_NoTruncate" ON "AuditEvents";
CREATE TRIGGER "TR_AuditEvents_NoTruncate"
    BEFORE TRUNCATE ON "AuditEvents"
    FOR EACH STATEMENT EXECUTE FUNCTION audit_events_reject_change();

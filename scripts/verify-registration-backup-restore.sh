#!/usr/bin/env bash
set -euo pipefail

: "${AEGIS_REGISTRATION_PGURL:?Set AEGIS_REGISTRATION_PGURL to the source PostgreSQL libpq connection URI.}"
: "${AEGIS_REGISTRATION_RESTORE_TEST_PGURL:?Set AEGIS_REGISTRATION_RESTORE_TEST_PGURL to a disposable empty PostgreSQL database.}"

for cmd in pg_dump pg_restore psql; do
  command -v "$cmd" >/dev/null 2>&1 || {
    echo "Required command not found: $cmd" >&2
    exit 2
  }
done

workdir="$(mktemp -d)"
trap 'rm -rf "$workdir"' EXIT
backup="$workdir/aegis-registration.dump"

echo "Creating registration database backup..."
pg_dump   --format=custom   --no-owner   --no-privileges   --file="$backup"   "$AEGIS_REGISTRATION_PGURL"

test -s "$backup" || {
  echo "FAIL: backup file is empty." >&2
  exit 1
}

echo "Resetting disposable restore target..."
psql "$AEGIS_REGISTRATION_RESTORE_TEST_PGURL" -v ON_ERROR_STOP=1 <<'SQL'
DROP TABLE IF EXISTS aegis_registration_audit CASCADE;
DROP TABLE IF EXISTS aegis_registration CASCADE;
DROP FUNCTION IF EXISTS aegis_registration_audit_immutable() CASCADE;
SQL

echo "Restoring backup into disposable target..."
pg_restore   --no-owner   --no-privileges   --exit-on-error   --dbname="$AEGIS_REGISTRATION_RESTORE_TEST_PGURL"   "$backup"

source_registration_count="$(psql "$AEGIS_REGISTRATION_PGURL" -At -v ON_ERROR_STOP=1 -c "SELECT count(*) FROM aegis_registration;")"
restored_registration_count="$(psql "$AEGIS_REGISTRATION_RESTORE_TEST_PGURL" -At -v ON_ERROR_STOP=1 -c "SELECT count(*) FROM aegis_registration;")"
source_audit_count="$(psql "$AEGIS_REGISTRATION_PGURL" -At -v ON_ERROR_STOP=1 -c "SELECT count(*) FROM aegis_registration_audit;")"
restored_audit_count="$(psql "$AEGIS_REGISTRATION_RESTORE_TEST_PGURL" -At -v ON_ERROR_STOP=1 -c "SELECT count(*) FROM aegis_registration_audit;")"

if [[ "$source_registration_count" != "$restored_registration_count" ]]; then
  echo "FAIL: registration row count mismatch: source=$source_registration_count restored=$restored_registration_count" >&2
  exit 1
fi

if [[ "$source_audit_count" != "$restored_audit_count" ]]; then
  echo "FAIL: audit row count mismatch: source=$source_audit_count restored=$restored_audit_count" >&2
  exit 1
fi

duplicate_active="$(psql "$AEGIS_REGISTRATION_RESTORE_TEST_PGURL" -At -v ON_ERROR_STOP=1 -c "
SELECT count(*)
FROM (
  SELECT lower(application_id), lower(instance_id), count(*)
  FROM aegis_registration
  WHERE state <> 'Purged'
  GROUP BY lower(application_id), lower(instance_id)
  HAVING count(*) > 1
) duplicates;")"

if [[ "$duplicate_active" != "0" ]]; then
  echo "FAIL: restored database contains duplicate active ApplicationId/InstanceId identities." >&2
  exit 1
fi

plaintext_credentials="$(psql "$AEGIS_REGISTRATION_RESTORE_TEST_PGURL" -At -v ON_ERROR_STOP=1 -c "
SELECT count(*)
FROM aegis_registration
WHERE credential_hash IS NOT NULL
  AND credential_hash NOT LIKE 'PBKDF2-SHA256$%';")"

if [[ "$plaintext_credentials" != "0" ]]; then
  echo "FAIL: restored database contains credential data outside the expected password-hash format." >&2
  exit 1
fi


if [[ "$restored_audit_count" != "0" ]]; then
  if psql "$AEGIS_REGISTRATION_RESTORE_TEST_PGURL" -v ON_ERROR_STOP=1 -c "
    UPDATE aegis_registration_audit
    SET action = action || '-tampered'
    WHERE audit_id = (SELECT min(audit_id) FROM aegis_registration_audit);
  " >/dev/null 2>&1; then
    echo "FAIL: append-only audit ledger allowed UPDATE." >&2
    exit 1
  fi

  if psql "$AEGIS_REGISTRATION_RESTORE_TEST_PGURL" -v ON_ERROR_STOP=1 -c "
    DELETE FROM aegis_registration_audit
    WHERE audit_id = (SELECT min(audit_id) FROM aegis_registration_audit);
  " >/dev/null 2>&1; then
    echo "FAIL: append-only audit ledger allowed DELETE." >&2
    exit 1
  fi

  echo "Audit ledger UPDATE protection: PASS"
  echo "Audit ledger DELETE protection: PASS"
else
  echo "Audit ledger tamper test: BLOCKED (no audit rows in restored database)"
fi

echo "PASS: registration database backup restored successfully."
echo "Registrations: $restored_registration_count"
echo "Audit events: $restored_audit_count"
echo "Active identity uniqueness: PASS"
echo "Credential hash format check: PASS"

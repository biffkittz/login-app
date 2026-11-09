#!/usr/bin/env bash
# One-off database bootstrap, run by the Container Apps job "caj-<project>-<environment>-dbinit" inside the VNet.
#
# Connects to PostgreSQL as the server administrator and makes sure the application's login role:
#   - exists, with the current password from Key Vault (rotating the password = updating the secret + re-running);
#   - may connect to each of the app's databases, one per instance of the app (PUBLIC may not);
#   - may create objects in schema public of each of them. Every instance applies its EF Core migrations at startup as
#     this role, so it owns its tables, but it gets no server-level rights (no CREATEDB, CREATEROLE, or
#     superuser-like membership).
#
# Idempotent: re-running it changes nothing except the password, and lets the role into databases added since.
# Connection settings come from the standard libpq environment variables set on the job (PGHOST, PGPORT, PGDATABASE,
# PGUSER, PGPASSWORD, PGSSLMODE, PGSSLROOTCERT) plus APP_DB_ROLE, APP_DB_PASSWORD and APP_DATABASES (space-separated;
# defaults to PGDATABASE).
set -euo pipefail

: "${PGDATABASE:?PGDATABASE must be set}"
: "${APP_DB_ROLE:?APP_DB_ROLE must be set}"
: "${APP_DB_PASSWORD:?APP_DB_PASSWORD must be set}"
read -r -a app_databases <<< "${APP_DATABASES:-${PGDATABASE}}"

echo "Bootstrapping role '${APP_DB_ROLE}' for databases ${app_databases[*]} on ${PGHOST:-localhost} (sslmode=${PGSSLMODE:-prefer})"

# The values travel as psql variables instead of being pasted into the SQL: :'name' expands to a quoted literal and
# :"name" to a quoted identifier, so no role name, database name or password can change the meaning of a statement.
# The password is sent to the server only inside the TLS session and is stored there as a SCRAM hash.
psql --no-psqlrc --no-password --quiet --set=ON_ERROR_STOP=1 \
  --set=app_role="${APP_DB_ROLE}" \
  --set=app_password="${APP_DB_PASSWORD}" <<'SQL'
-- CREATE ROLE has no IF NOT EXISTS; generate the statement only when the role is missing.
SELECT format('CREATE ROLE %I LOGIN', :'app_role')
WHERE NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = :'app_role')
\gexec

ALTER ROLE :"app_role" WITH LOGIN PASSWORD :'app_password';
SQL

# Schema privileges belong to a database, so each grant runs connected to the database it is for.
for app_database in "${app_databases[@]}"; do
  psql --no-psqlrc --no-password --quiet --set=ON_ERROR_STOP=1 \
    --dbname="${app_database}" \
    --set=app_role="${APP_DB_ROLE}" \
    --set=app_database="${app_database}" <<'SQL'
REVOKE ALL ON DATABASE :"app_database" FROM PUBLIC;
GRANT CONNECT, TEMPORARY ON DATABASE :"app_database" TO :"app_role";
GRANT USAGE, CREATE ON SCHEMA public TO :"app_role";
SQL
  echo "'${APP_DB_ROLE}' can connect to '${app_database}' and create its schema objects."
done

echo "Done."

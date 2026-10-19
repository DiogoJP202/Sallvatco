#!/bin/sh
# Executado pela imagem PostgreSQL apenas na criação de um volume vazio.
set -eu
SALLVAT_RUNTIME_PASSWORD="$(cat /run/secrets/database_app_password)"
export SALLVAT_RUNTIME_PASSWORD
test -n "$SALLVAT_RUNTIME_PASSWORD"
psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" --set ON_ERROR_STOP=1 <<'SQL'
\getenv runtime_password SALLVAT_RUNTIME_PASSWORD
CREATE ROLE sallvat_app LOGIN PASSWORD :'runtime_password' NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
REVOKE ALL ON DATABASE sallvat_staging FROM PUBLIC;
GRANT CONNECT ON DATABASE sallvat_staging TO sallvat_app;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO sallvat_app;
ALTER DEFAULT PRIVILEGES FOR ROLE sallvat_owner IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO sallvat_app;
ALTER DEFAULT PRIVILEGES FOR ROLE sallvat_owner IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO sallvat_app;
SQL
unset SALLVAT_RUNTIME_PASSWORD

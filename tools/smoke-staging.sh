#!/usr/bin/env bash
# Ensaio efêmero; nunca recebe arquivo de ambiente ou segredo operacional.
set -euo pipefail
image="${1:?Informe a imagem local aprovada}"
task_migrations="${2:-}"
task_dir="$(mktemp -d -t sallvat-staging-smoke-XXXXXXXX)"
task_project="$(basename "$task_dir" | tr '[:upper:]' '[:lower:]')"
task_compose="$(pwd)/deploy/staging/compose.yaml"
export SALLVAT_IMAGE="$image" STAGING_HOST=staging.example.invalid
export STAGING_HTTP_PORT=0 STAGING_HTTPS_PORT=0
export STAGING_APPLICATION_SETTINGS="$task_dir/application.json"
export STAGING_DB_OWNER_PASSWORD_FILE="$task_dir/owner.txt"
export STAGING_DB_APP_PASSWORD_FILE="$task_dir/app.txt"
export STAGING_TLS_CERTIFICATE_FILE="$task_dir/tls.crt"
export STAGING_TLS_KEY_FILE="$task_dir/tls.key"
export STAGING_ACCESS_PASSWORDS_FILE="$task_dir/access.htpasswd"
compose() { docker compose --project-name "$task_project" --file "$task_compose" "$@"; }
cleanup() {
  task_exit=$?
  trap - EXIT
  if [[ $task_exit -ne 0 ]]; then compose logs --tail 40 || true; fi
  compose down --volumes --remove-orphans >/dev/null || true
  if [[ "$task_dir" == /tmp/sallvat-staging-smoke-* ]]; then rm -rf -- "$task_dir"; fi
  exit "$task_exit"
}
trap cleanup EXIT
trap 'printf "Smoke Staging falhou na linha %s\n" "$LINENO" >&2' ERR
# Arquivos sintéticos descartáveis. Permissões amplas somente para fixtures sem segredos reais.
chmod 755 "$task_dir"
printf 'isolated-owner-only' > "$task_dir/owner.txt"
printf 'isolated-runtime-only' > "$task_dir/app.txt"
printf '{"ConnectionStrings":{"SallvatDatabase":"Host=postgres;Database=sallvat_staging;Username=sallvat_app;Password=isolated-runtime-only;Timeout=2"}}' > "$task_dir/application.json"
printf 'reviewer:%s\n' "$(openssl passwd -apr1 isolated-access-only)" > "$task_dir/access.htpasswd"
openssl req -x509 -newkey rsa:2048 -nodes -days 1 \
  -subj /CN=staging.example.invalid -addext subjectAltName=DNS:staging.example.invalid \
  -keyout "$task_dir/tls.key" -out "$task_dir/tls.crt" 2>/dev/null
chmod 644 "$task_dir"/*
compose config --quiet
compose up --detach --wait --wait-timeout 90
task_https="$(compose port nginx 8443 | awk -F: '{print $NF}')"
task_http="$(compose port nginx 8080 | awk -F: '{print $NF}')"
task_web="$(compose ps --quiet web)"
task_nginx="$(compose ps --quiet nginx)"
task_db="$(compose ps --quiet postgres)"
task_url="https://staging.example.invalid:$task_https"
request() {
  curl --silent --show-error --noproxy '*' --max-time 10 \
    --cacert "$task_dir/tls.crt" \
    --resolve "staging.example.invalid:$task_https:127.0.0.1" \
    --dump-header "$task_dir/headers" --output "$task_dir/body" \
    --write-out '%{http_code}' "$@"
}
expect() {
  local expected="$1" path="$2" actual=""
  shift 2
  for attempt in {1..30}; do
    actual="$(request "$task_url$path" "$@" || true)"
    if [[ "$actual" == "$expected" ]]; then return 0; fi
    sleep 1
  done
  printf 'Staging: %s esperado %s, recebido %s\n' "$path" "$expected" "$actual" >&2
  return 1
}
expect 401 /conta/entrar
grep -qi 'X-Robots-Tag: noindex' "$task_dir/headers"
expect 401 /css/app.css
expect 401 /conta/entrar --user reviewer:wrong-password
expect 200 /conta/entrar --user reviewer:isolated-access-only
grep -qi 'Strict-Transport-Security: max-age=86400' "$task_dir/headers"
grep -qi 'X-Content-Type-Options: nosniff' "$task_dir/headers"
expect 200 /css/app.css --user reviewer:isolated-access-only
cmp src/Sallvat.Web/wwwroot/css/app.css "$task_dir/body"
expect 404 /health/ready --user reviewer:isolated-access-only
# Nginx deve sobrescrever os cabeçalhos externos; redirect da autenticação deve ser HTTPS/canônico.
expect 302 /Admin --user reviewer:isolated-access-only \
  --header 'X-Forwarded-Proto: http' --header 'X-Forwarded-Host: attacker.invalid' \
  --header 'X-Forwarded-For: 198.51.100.5'
grep -qi 'Location: https://staging.example.invalid/conta/entrar' "$task_dir/headers"
! grep -qi 'attacker.invalid' "$task_dir/headers"
task_redirect="$(curl --silent --noproxy '*' --header 'Host: staging.example.invalid' \
  --dump-header "$task_dir/headers" --output /dev/null --write-out '%{http_code}' "http://127.0.0.1:$task_http/conta/entrar")"
[[ "$task_redirect" == 308 ]]
grep -qi 'Location: https://staging.example.invalid/conta/entrar' "$task_dir/headers"
if request "$task_url/conta/entrar" --header 'Host: attacker.invalid' --user reviewer:isolated-access-only >/dev/null 2>&1; then
  echo 'Host inesperado foi aceito pelo proxy.' >&2; exit 1
fi
if curl --silent --noproxy '*' --max-time 10 --insecure \
  --resolve "attacker.invalid:$task_https:127.0.0.1" "https://attacker.invalid:$task_https/" >/dev/null 2>&1; then
  echo 'SNI inesperado foi aceito pelo proxy.' >&2; exit 1
fi
[[ "$(docker exec "$task_nginx" id -u)" != 0 ]]
[[ -z "$(docker port "$task_web")" && -z "$(docker port "$task_db")" ]]
task_ip="$(docker inspect --format "{{(index .NetworkSettings.Networks \"${task_project}_proxy\").IPAddress}}" "$task_web")"
# O host Linux consulta readiness internamente; nunca expor o endpoint via proxy.
for attempt in {1..30}; do
  task_ready="$(curl --silent --noproxy '*' --max-time 5 --header 'Host: staging.example.invalid' \
    --output /dev/null --write-out '%{http_code}' "http://$task_ip:8080/health/ready" || true)"
  [[ "$task_ready" == 200 ]] && break
  sleep 1
done
[[ "$task_ready" == 200 ]]
[[ "$(docker exec "$task_db" psql -U sallvat_owner -d sallvat_staging -Atc "SELECT count(*) FROM information_schema.tables WHERE table_schema='public'")" == 0 ]]
[[ "$(docker exec "$task_db" psql -U sallvat_owner -d sallvat_staging -Atc "SELECT rolsuper OR rolcreatedb OR rolcreaterole FROM pg_roles WHERE rolname='sallvat_app'")" == f ]]
[[ "$(docker exec "$task_db" psql -U sallvat_owner -d sallvat_staging -Atc "SELECT has_schema_privilege('sallvat_app', 'public', 'CREATE')")" == f ]]
if [[ -n "$task_migrations" ]]; then
  # Aplicação explícita fora do startup; repetir prova idempotência do artefato SQL.
  for pass in 1 2; do
    docker exec -i "$task_db" psql -X -U sallvat_owner -d sallvat_staging -v ON_ERROR_STOP=1 < "$task_migrations" > "$task_dir/migrations-log"
  done
  task_expected="$(find src/Sallvat.Infrastructure/Persistence/Migrations -name '[0-9]*.cs' ! -name '*.Designer.cs' | wc -l)"
  task_applied="$(docker exec "$task_db" psql -U sallvat_owner -d sallvat_staging -Atc 'SELECT count(*) FROM __ef_migrations_history')"
  [[ "$task_applied" == "$task_expected" ]]
  expect 200 /perfumes --user reviewer:isolated-access-only
  printf 'SQL de migrations aprovado: aplicado duas vezes, schema completo e catalogo acessivel com role limitada.\n'
fi
# Recriação da aplicação preserva volumes e o resolver Nginx reencontra o serviço.
docker exec "$task_web" sh -c 'printf "staging-storage" > /var/lib/sallvat/images/staging-smoke.txt; sha256sum /var/lib/sallvat/keys/*.xml' > "$task_dir/keys-before"
compose up --detach --force-recreate --no-deps web
task_web="$(compose ps --quiet web)"
expect 200 /media/staging-smoke.txt --user reviewer:isolated-access-only
[[ "$(<"$task_dir/body")" == staging-storage ]]
docker exec "$task_web" sh -c 'sha256sum /var/lib/sallvat/keys/*.xml' > "$task_dir/keys-after"
cmp "$task_dir/keys-before" "$task_dir/keys-after"
printf 'Smoke Staging aprovado: TLS, acesso privado, proxy confiavel, redes, role minima e volumes apos recriacao.\n'

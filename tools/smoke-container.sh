#!/usr/bin/env bash
# Somente recursos efêmeros: nenhum ambiente de negócio ou segredo real.
set -euo pipefail

image="${1:?Informe a imagem local construída para o smoke}"
task_dir="$(mktemp -d -t sallvat-container-smoke-XXXXXXXX)"
task_network=""
task_app=""
task_db=""
cleanup() {
  task_exit=$?
  trap - EXIT
  if [[ $task_exit -ne 0 && -n "$task_app" ]]; then docker logs --tail 60 "$task_app" || true; fi
  if [[ -n "$task_app" ]]; then docker rm --force --volumes "$task_app" >/dev/null || true; fi
  if [[ -n "$task_db" ]]; then docker rm --force --volumes "$task_db" >/dev/null || true; fi
  if [[ -n "$task_network" ]]; then docker network rm "$task_network" >/dev/null || true; fi
  # Diretório absoluto, gerado por mktemp exclusivamente para este teste.
  if [[ "$task_dir" == /tmp/sallvat-container-smoke-* ]]; then rm -rf -- "$task_dir"; fi
  exit "$task_exit"
}
trap cleanup EXIT

task_network="$(docker network create --internal "sallvat-smoke-$(basename "$task_dir")")"
task_app="$(docker run --detach --network "$task_network" \
  --read-only --cap-drop ALL --security-opt no-new-privileges:true \
  --memory 512m --cpus 1 --pids-limit 128 \
  --tmpfs /tmp:rw,noexec,nosuid,size=64m \
  --mount type=volume,destination=/var/lib/sallvat/keys \
  --mount type=volume,destination=/var/lib/sallvat/images \
  --publish 127.0.0.1::8080 \
  --env ASPNETCORE_ENVIRONMENT=Staging \
  --env AllowedHosts=127.0.0.1 \
  --env AccountLinks__PublicOrigin=https://staging.example.invalid \
  --env 'ConnectionStrings__SallvatDatabase=Host=postgres;Port=5432;Database=sallvat_smoke;Username=sallvat_smoke;Password=isolated-smoke-only;Timeout=2;Command Timeout=2' \
  "$image")"
task_port="$(docker port "$task_app" 8080/tcp)"
task_url="http://${task_port}"

expect_status() {
  local path="$1" expected="$2" actual=""
  for attempt in {1..30}; do
    actual="$(curl --silent --max-time 5 --output "$task_dir/response" --write-out '%{http_code}' "$task_url$path" || true)"
    if [[ "$actual" == "$expected" ]]; then return 0; fi
    sleep 1
  done
  printf 'Status inesperado em %s: esperado %s; recebido %s\n' "$path" "$expected" "$actual" >&2
  return 1
}

expect_status /health/live 200
expect_status /health/ready 503
expect_status /css/app.css 200
cmp src/Sallvat.Web/wwwroot/css/app.css "$task_dir/response"
expect_status /conta/entrar 200
[[ "$(docker exec "$task_app" id -u)" != 0 ]]
docker exec "$task_app" sh -c 'test ! -w /app && test ! -d /usr/share/dotnet/sdk && ! command -v node && test ! -f /app/appsettings.Development.json'
docker exec "$task_app" sh -c 'test -f /app/runtimes/linux-x64/native/libSkiaSharp.so && ! ldd /app/runtimes/linux-x64/native/libSkiaSharp.so | grep "not found"'
docker exec "$task_app" sh -c 'printf "storage-smoke" > /var/lib/sallvat/images/smoke.txt; sha256sum /var/lib/sallvat/keys/*.xml' > "$task_dir/keys-before"
docker restart "$task_app" >/dev/null
expect_status /health/live 200
expect_status /media/smoke.txt 200
[[ "$(<"$task_dir/response")" == storage-smoke ]]
docker exec "$task_app" sh -c 'sha256sum /var/lib/sallvat/keys/*.xml' > "$task_dir/keys-after"
cmp "$task_dir/keys-before" "$task_dir/keys-after"

task_db="$(docker run --detach --network "$task_network" --network-alias postgres \
  --env POSTGRES_DB=sallvat_smoke --env POSTGRES_USER=sallvat_smoke \
  --env POSTGRES_PASSWORD=isolated-smoke-only \
  --mount type=volume,destination=/var/lib/postgresql \
  postgres:18.6-alpine3.24)"
expect_status /health/ready 200
# Readiness verifica conexão, não schema: startup não pode criar/aplicar migrations.
task_tables="$(docker exec "$task_db" psql -U sallvat_smoke -d sallvat_smoke -Atc "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public';")"
[[ "$task_tables" == 0 ]]
docker stop "$task_db" >/dev/null
expect_status /health/ready 503
expect_status /health/live 200
printf 'Smoke aprovado: runtime não-root, read-only, assets, login, storage/restart, readiness e ausência de migrations automáticas.\n'

#!/usr/bin/env bash
# Executa o roteiro real somente no runner descartável; nunca em VPS operacional.
set -euo pipefail
[[ "${GITHUB_ACTIONS:-}" == true && "${RUNNER_OS:-}" == Linux ]]
package="${1:?Informe o kit gerado no runner}"
[[ "$package" == "${RUNNER_TEMP:?}"/* ]]
[[ ! -e /srv/sallvat-staging ]]
[[ -z "$(docker volume ls --quiet --filter label=com.docker.compose.project=sallvat-staging)" ]]
task_created=false
cleanup() {
  task_exit=$?
  trap - EXIT
  if [[ "$task_created" == true ]]; then
    sudo docker compose --project-name sallvat-staging --env-file /srv/sallvat-staging/staging.env \
      --file "$package/deploy/staging/compose.yaml" down --volumes --remove-orphans >/dev/null || true
    # Caminho literal, ausência inicial verificada acima, criado exclusivamente neste ensaio.
    sudo rm -rf -- /srv/sallvat-staging
  fi
  exit "$task_exit"
}
trap cleanup EXIT
sudo python3 "$package/vps.py" verify
sudo python3 "$package/vps.py" load
sudo python3 "$package/vps.py" prepare
task_created=true
if sudo python3 "$package/vps.py" prepare; then exit 1; fi
sudo python3 "$package/vps.py" preflight
revision="$(git rev-parse HEAD)"
sudo python3 "$package/vps.py" initialize --confirm-revision "$revision"
if sudo python3 "$package/vps.py" initialize --confirm-revision "$revision"; then exit 1; fi
sudo python3 "$package/vps.py" start
sudo python3 "$package/vps.py" check
printf 'Kit VPS aprovado: importacao, secrets restritos, schema explicito, HTTPS e recusa de reinicializacao.\n'

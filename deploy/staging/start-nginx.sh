#!/bin/sh
set -eu
# Somente nomes DNS: nenhum caractere que possa injetar diretivas no template.
case "${STAGING_HOST:-}" in
  ''|*[!a-zA-Z0-9.-]*|.*|*.) echo 'STAGING_HOST deve ser um nome DNS sem porta.' >&2; exit 1 ;;
esac
envsubst '${STAGING_HOST}' < /etc/sallvat/nginx.conf.template > /tmp/nginx.conf
nginx -t -c /tmp/nginx.conf
exec nginx -c /tmp/nginx.conf -g 'daemon off;'

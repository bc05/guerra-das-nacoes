#!/bin/zsh
set -euo pipefail
umask 077
prototype_directory="${0:A:h}"
endpoint='http://127.0.0.1:8088'
if ! curl --fail --silent --max-time 1 "$endpoint/health" | /usr/bin/grep -q '"protocolVersion":2'; then
  mkdir -p "$prototype_directory/Server/data"
  nohup "$prototype_directory/IniciarServidor.command" </dev/null >"$prototype_directory/Server/data/server.log" 2>&1 &
  ready=0
  for attempt in {1..60}; do
    if curl --fail --silent --max-time 1 "$endpoint/health" | /usr/bin/grep -q '"protocolVersion":2'; then ready=1; break; fi
    /bin/sleep .2
  done
  if [[ "$ready" != 1 ]]; then
    print -u2 'O servidor 0.12 não iniciou. Confira Server/data/server.log; mantenha cliente e servidor na mesma versão.'
    exit 1
  fi
fi
/usr/bin/open "$endpoint/admin"
/usr/bin/open -R "$prototype_directory/Server/data/admin-secret.txt"
print 'Painel aberto. Entre com a chave do arquivo admin-secret.txt selecionado no Finder. Não compartilhe a chave.'

#!/bin/zsh
set -euo pipefail
prototype_directory="${0:A:h}"
pid_file="$prototype_directory/Server/data/server.pid"
if [[ ! -f "$pid_file" ]]; then
  print 'Nenhum servidor iniciado pelo lançador foi encontrado. Em um servidor aberto no Terminal, use Ctrl+C.'
  exit 0
fi
server_pid="$(<"$pid_file")"
if [[ "$server_pid" != <-> ]]; then
  print -u2 'Arquivo de processo inválido; nenhum processo foi encerrado.'
  exit 1
fi
server_command="$(ps -p "$server_pid" -o command= 2>/dev/null || true)"
if [[ "$server_command" == *"$prototype_directory/Server/Build/Server"* || "$server_command" == *"$prototype_directory/Server/bin/Release/net10.0/Server.dll"* ]]; then
  kill -TERM "$server_pid"
  rm -f "$pid_file"
  print 'Encerramento do servidor solicitado. O progresso confirmado permanece no banco.'
else
  print 'O processo registrado já encerrou ou pertence a outro programa; nenhum processo foi alterado.'
fi

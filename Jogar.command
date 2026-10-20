#!/bin/zsh
set -euo pipefail
umask 077

prototype_directory="${0:A:h}"
game_bundle="$prototype_directory/Unity/Builds/macOS/Guerra das Nações.app"
# Compatibilidade com builds anteriores, antes da correção do nome visível.
if [[ ! -f "$game_bundle/Contents/Info.plist" ]]; then
  game_bundle="$prototype_directory/Unity/Builds/macOS/GuerraDasNacoes.app"
fi
if [[ ! -f "$game_bundle/Contents/Info.plist" ]]; then
  print -u2 'Aplicativo Guerra das Nações não encontrado. Gere o build pelo Unity.'
  exit 1
fi
game_executable_name=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$game_bundle/Contents/Info.plist")
game_executable="$game_bundle/Contents/MacOS/$game_executable_name"

if [[ ! -x "$game_executable" ]]; then
  print -u2 'Build local não encontrado. No Unity, use Guerra das Nações > Gerar aplicativo macOS.'
  exit 1
fi

endpoint='http://127.0.0.1:8088'
for argument in "$@"; do
  if [[ "$argument" == --server=* ]]; then endpoint="${argument#--server=}"; fi
done
if [[ "$endpoint" == 'http://127.0.0.1:8088' ]]; then
  if ! curl --fail --silent --max-time 1 "$endpoint/health" | /usr/bin/grep -q '"authority":"server"'; then
    mkdir -p "$prototype_directory/Server/data"
    GAME_HOST=127.0.0.1 GAME_PORT=8088 nohup "$prototype_directory/IniciarServidor.command" </dev/null >"$prototype_directory/Server/data/server.log" 2>&1 &
    server_ready=0
    for attempt in {1..50}; do
      if curl --fail --silent --max-time 1 "$endpoint/health" | /usr/bin/grep -q '"authority":"server"'; then
        server_ready=1
        break
      fi
      /bin/sleep .2
    done
    if [[ "$server_ready" != 1 ]]; then
      print -u2 'O servidor não iniciou. Confira Server/data/server.log. O jogo não criará progresso offline.'
      exit 1
    fi
    # Dois clientes podem iniciar juntos; registre o processo que de fato abriu a porta.
    listening_pid=$(/usr/sbin/lsof -nP -t -iTCP:8088 -sTCP:LISTEN | /usr/bin/head -n 1)
    if [[ "$listening_pid" == <-> ]]; then
      print -r -- "$listening_pid" >"$prototype_directory/Server/data/server.pid"
    fi
  fi
fi
exec /usr/bin/open -n "$game_bundle" --args -screen-fullscreen 0 "$@"

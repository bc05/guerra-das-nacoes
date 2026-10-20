#!/bin/zsh
set -euo pipefail
umask 077
prototype_directory="${0:A:h}"
server_directory="$prototype_directory/Server"
export HOST="${GAME_HOST:-127.0.0.1}"
export PORT="${GAME_PORT:-8088}"
export DB_PATH="${GAME_DB_PATH:-$server_directory/data/characters.db}"
export CATACOMBS_COLLISION_PATH="${GAME_CATACOMBS_COLLISION_PATH:-$server_directory/catacombs-collision.json}"
export COLLISION_PATH="${GAME_COLLISION_PATH:-$server_directory/world-collision.json}"
cd "$server_directory"
if [[ -x "$server_directory/Build/Server" ]]; then
  exec "$server_directory/Build/Server"
fi
if [[ -x "$server_directory/.dotnet/dotnet" && -f "$server_directory/bin/Release/net10.0/Server.dll" ]]; then
  exec "$server_directory/.dotnet/dotnet" "$server_directory/bin/Release/net10.0/Server.dll"
fi
print -u2 'Servidor compilado não encontrado. Consulte Server/README.md para compilar com .NET10.'
exit 1

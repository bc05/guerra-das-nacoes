#!/bin/zsh
set -euo pipefail

prototype_directory="${0:A:h}"
unity_cli='/Applications/Unity Hub.app/Contents/Resources/unity'

if [[ ! -x "$unity_cli" ]]; then
  print -u2 'Unity Hub não foi encontrado em /Applications. Instale-o antes de abrir este projeto.'
  exit 1
fi

exec "$unity_cli" open "$prototype_directory/Unity"

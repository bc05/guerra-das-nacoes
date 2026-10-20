#!/bin/zsh
set -euo pipefail

prototype_directory="${0:A:h}"
blender_executable='/Applications/Blender.app/Contents/MacOS/Blender'

if [[ ! -x "$blender_executable" ]]; then
  print -u2 'Blender não foi encontrado em /Applications.'
  exit 1
fi

exec "$blender_executable" "$prototype_directory/ArtSource/Cenario.blend"

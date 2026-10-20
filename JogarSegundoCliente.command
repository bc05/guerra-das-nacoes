#!/bin/zsh
set -euo pipefail
prototype_directory="${0:A:h}"
exec "$prototype_directory/Jogar.command" --profile=segundo --name='Guerreiro 2' "$@"

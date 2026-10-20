#!/bin/zsh
set -euo pipefail
umask 077
prototype_directory="${0:A:h}"
cd "$prototype_directory/Server"
./Build/Server --self-test
python3 Tests/economy_http_checks.py --server "$PWD/Build/Server"
print 'Verificações concluídas em bancos temporários; o progresso principal foi preservado.'

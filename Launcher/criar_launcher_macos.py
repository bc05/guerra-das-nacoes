#!/usr/bin/env python3
"""Create the local macOS entry point. No downloads, accounts, secrets or character data."""
import argparse
from pathlib import Path
import plistlib
import shutil


def create(project):
    project = project.resolve()
    if not (project / 'Jogar.command').is_file():
        raise RuntimeError('Jogar.command não encontrado na pasta do protótipo.')
    icon = project / 'Unity/Assets/Art/Brand/GameIcon.icns'
    if not icon.is_file():
        raise RuntimeError('Ícone ICNS não encontrado.')
    app = project / 'Guerra das Nações.app'
    info_path = app / 'Contents/Info.plist'
    identifier = 'com.guerradasnacoes.prototipo.launcher'
    legacy = project / 'Entrar no Jogo.app'
    if not app.exists() and legacy.is_dir() and not legacy.is_symlink():
        old_info = legacy / 'Contents/Info.plist'
        if old_info.is_file() and plistlib.loads(old_info.read_bytes()).get('CFBundleIdentifier') == identifier:
            legacy.rename(app)
    if app.exists() and (not info_path.is_file() or plistlib.loads(info_path.read_bytes()).get('CFBundleIdentifier') != identifier):
        raise RuntimeError('Já existe outro aplicativo nesse caminho; ele foi preservado.')
    for folder in ('MacOS', 'Resources'):
        (app / 'Contents' / folder).mkdir(parents=True, exist_ok=True)
    executable = app / 'Contents/MacOS/Entrar'
    executable.write_text('''#!/bin/zsh
set -euo pipefail
umask 077
prototype_directory="${0:A:h:h:h:h}"
exec "$prototype_directory/Jogar.command" "$@"
''')
    executable.chmod(0o755)
    shutil.copy2(icon, app / 'Contents/Resources/GameIcon.icns')
    info_path.write_bytes(plistlib.dumps({
        'CFBundleDevelopmentRegion': 'pt_BR',
        'CFBundleDisplayName': 'Guerra das Nações',
        'CFBundleName': 'Guerra das Nações',
        'CFBundleExecutable': 'Entrar',
        'CFBundleIdentifier': identifier,
        'CFBundleIconFile': 'GameIcon.icns',
        'CFBundlePackageType': 'APPL',
        'CFBundleShortVersionString': '0.16.0',
        'CFBundleVersion': '16.0',
        'LSMinimumSystemVersion': '11.0',
        'NSHighResolutionCapable': True,
        'LSUIElement': True,
    }))
    (app / 'Contents/PkgInfo').write_bytes(b'APPL????')
    if not legacy.exists() and not legacy.is_symlink():
        legacy.symlink_to(app.name, target_is_directory=True)
    print('Entrada do jogo criada:', app)
    return app


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project', type=Path, default=Path(__file__).resolve().parent.parent)
    create(parser.parse_args().project)

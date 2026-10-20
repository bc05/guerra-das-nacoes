# Assets de ambiente — Poly Haven, CC0

Baixados em 18/10/2026 pelo API público oficial. Todos os arquivos originais têm MD5 verificado contra o catálogo oficial; `download-manifest.json` registra URL, tamanho e hash de cada download. Os mapas derivados e seus SHA-256 estão em `derived-manifest.json`.

Este catálogo preserva os nomes da etapa de processamento. Na entrega, `UnityReady/` corresponde a `Unity/Assets/Art/ThirdParty/Environment/`, relativo à raiz do projeto. O HDRI está ali como `KloofendalSky.hdr`. `inspect_rocks.py` foi um helper de processamento e não acompanha esta pasta; a integração reproduzível no Unity está em `Unity/Assets/Editor/ThirdPartyEnvironmentSetup.cs`.

Licença oficial: https://polyhaven.com/license
CC0 1.0: https://creativecommons.org/publicdomain/zero/1.0/
A Poly Haven permite uso comercial, modificação e redistribuição. Atribuição não é obrigatória, mas preservamos autoria e origem.

| Asset | Autor | Uso | Arquivos |
| --- | --- | --- | --- |
| [Rock Moss Set 01](https://polyhaven.com/a/rock_moss_set_01) | Kless Gyzen | Seis rochas com musgo, 63.127 triângulos no total | FBX original, Blender original, Blender com texturas empacotadas e seis FBX independentes em `UnityReady/Rocks/` |
| [Medieval Wall 02](https://polyhaven.com/a/medieval_wall_02) | Rob Tuytel | Parede medieval de pedra, extensão física 3,5 m na vertical | Albedo, Normal GL, Roughness e AO, 2K |
| [Cobblestone Floor 07](https://polyhaven.com/a/cobblestone_floor_07) | Rob Tuytel | Piso antigo com pedras e musgo; extensão física de 2 m | Albedo, Normal GL, Roughness e AO, 2K |
| [Forest Ground 04](https://polyhaven.com/a/forest_ground_04) | Rob Tuytel, Rico Cilliers | Terra irregular, pedras e cascalho; extensão física de 3,2 m | Albedo, Normal GL, Roughness e AO, 2K |
| [Kloofendal 48d Partly Cloudy (Pure Sky)](https://polyhaven.com/a/kloofendal_48d_partly_cloudy_puresky) | Greg Zaal, Jarod Guest | Céu e iluminação HDR natural | HDR equiretangular, 2K |

## Integração no Unity Built-in

`UnityReady/<id>/` contém mapas PNG de 2048 × 2048:

- `Albedo.png`: importar com sRGB, usar cor branca no material para preservar a fotografia.
- `Normal.png`: importar como Normal Map (OpenGL), espaço linear.
- `AO.png`: importar sem sRGB, usar no campo Occlusion do shader Standard.
- `MetallicSmoothness.png`: importar sem sRGB, RGB = 0 (pedra/terra dielétrica), A = 1 − Roughness; usar no campo Metallic do shader Standard. `_GlossMapScale` entre 0,7 e 1 conforme a aparência.
- Repetição Repeat; mipmaps e filtragem anisotrópica 4 ou 8.

`UnityReady/Rocks/Rock01.fbx` até `Rock06.fbx`: malhas preservadas, UV original; origens recuadas para o centro inferior de cada rocha, com transformação aplicada e exportação Y up. Altura original varia entre aproximadamente 1,1 e 1,77 m. Cada FBX usa o mesmo material `rock_moss_set_01` e pode compartilhar as quatro texturas desse conjunto.

O `.blend` empacotado preserva a disposição original de catálogo com as seis rochas separadas; o HDRI do preview ausente no arquivo original foi substituído pelo Kloofendal baixado. Os FBXs independentes não incluem esse HDRI. `inspect_rocks.py` registra o preparo reproduzível; `rock-mesh-info.json` registra a geometria original.

Nenhum asset do Cabal foi extraído ou redistribuído. A arquitetura autoral do protótipo pode receber os novos materiais e as rochas sem dependência de plugins. Não há kit de ruínas completo neste pacote; o conjunto é de materiais e elementos naturais.

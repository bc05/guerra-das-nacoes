# Créditos e licenças de terceiros

## Knight

- **Autor:** piacenti.
- **Fonte:** [Knight — OpenGameArt](https://opengameart.org/content/knight-2).
- **Licença:** [Creative Commons Attribution 3.0 Unported (CC BY 3.0)](https://creativecommons.org/licenses/by/3.0/).
- **Download original:** https://opengameart.org/sites/default/files/armored%20knight.zip.
- **Arquivos derivados:** `ArtSource/Cavaleiro.blend`, `Unity/Assets/Art/Cavaleiro.fbx`, partes de equipamento em `Unity/Assets/Art/Equipment/` e texturas em `Unity/Assets/Art/ThirdParty/Knight/`.
- **Alterações:** escala métrica, espelhamento, adaptação da pose e pesos ao esqueleto do protótipo, animações Idle/Walk/Attack, alinhamento da espada e pontos de rastro, conversão dos materiais e empacotamento de metallic/smoothness. Na versão 0.8, agrupamento dos triângulos em seis partes de acabamento e separação do escudo, com variações de tonalidade por peça; vértices, UVs, pesos e topologia preservados. Preservadas a geometria esculpida e as UVs/texturas do autor.
- **Atribuição:** “Knight”, de piacenti, licenciado sob CC BY 3.0. Adaptado para este protótipo. O autor não endossa o projeto.

## Animações do cavaleiro — Adobe Mixamo

- **Fonte:** [Mixamo](https://www.mixamo.com/), conjunto **Sword and Shield Pack**, obtido na conta do usuário em 19/10/2026.
- **Movimentos selecionados:** caminhada e corrida para a frente, guarda `sword and shield idle (4)`, básicos `sword and shield slash (3)`, `sword and shield slash` e `sword and shield attack (4)`, e pesado `sword and shield slash (5)`.
- **Termos de uso:** a [FAQ oficial da Adobe](https://helpx.adobe.com/creative-cloud/faq/mixamo-faq.html) permite personagens e animações em jogos pessoais e comerciais sem royalties. Os movimentos mantêm os direitos e termos do Mixamo.
- **Fontes e hashes:** `ArtSource/ThirdParty/Mixamo/SourceManifest.json`; inclui o rig de referência X Bot e os FBX selecionados.
- **Adaptação:** transferência dos movimentos para os 17 ossos existentes do cavaleiro, compensação das proporções e dos pés, remoção do deslocamento horizontal da animação, fechamento dos ciclos e ajuste temporal dos golpes. Os cortes descendente e amplo usam trechos da captura sobre a base de guarda das pernas, evitando deslizamento com a cápsula parada; as faixas e os canais preservados são registrados no perfil de movimentos. A malha Knight e seus créditos CC BY 3.0 permanecem descritos acima.
- **Derivados:** `ArtSource/Cavaleiro.blend`, `Unity/Assets/Art/Cavaleiro.fbx` e perfil de cadência `Unity/Assets/Art/CavaleiroMotionProfile.json`.

## Troll Mauler

- **Autor:** piacenti.
- **Fonte:** [Troll Mauler — OpenGameArt](https://opengameart.org/content/troll-mauler).
- **Download original:** https://opengameart.org/sites/default/files/troll.blend.
- **Licença escolhida:** [Creative Commons Attribution 3.0 Unported (CC BY 3.0)](https://creativecommons.org/licenses/by/3.0/).
- **Arquivos derivados utilizados:** `ArtSource/Troll.blend`, `Unity/Assets/Art/Troll.fbx` e mapas em `Unity/Assets/Art/ThirdParty/Troll/`.
- **Alterações:** seleção da malha média do autor; remoção de cena, modelos reserva e materiais não usados; conversão para materiais Principled; extração dos mapas albedo, normal e oclusão; preservação e associação da ação idle original; renomeação de quatro ossos de membros; normalização para 3,2 m, pés em zero; exportação FBX. Variantes de orcs, trolls e do chefão acrescentam cores, escalas, presas, armaduras e armas procedurais, além de animação e efeitos no Unity.
- **Atribuição:** “Troll Mauler”, de piacenti, licenciado sob CC BY 3.0. Adaptado para este protótipo.

A licença permite compartilhar e adaptar com atribuição. O autor não endossa este projeto. A preferência expressa pelo autor por saber onde o modelo é usado é facultativa e não foi tratada como condição adicional. A licença deve acompanhar cópias distribuídas do modelo e das texturas, com indicação das alterações.

## Ambiente — Poly Haven

Todos os assets abaixo são disponibilizados sob [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) pela [Poly Haven](https://polyhaven.com/license), permitindo uso comercial, modificação e redistribuição. Preservamos os créditos por transparência.

| Asset | Autor | Uso neste protótipo |
| --- | --- | --- |
| [Rock Moss Set 01](https://polyhaven.com/a/rock_moss_set_01) | Kless Gyzen | Seis modelos de rochas com musgo e suas texturas PBR 2K |
| [Medieval Wall 02](https://polyhaven.com/a/medieval_wall_02) | Rob Tuytel | Alvenaria de pedra das ruínas, PBR 2K |
| [Cobblestone Floor 07](https://polyhaven.com/a/cobblestone_floor_07) | Rob Tuytel | Piso antigo com pedras e musgo, PBR 2K |
| [Forest Ground 04](https://polyhaven.com/a/forest_ground_04) | Rob Tuytel, Rico Cilliers | Solo com terra, cascalho e pedras, PBR 2K |
| [Kloofendal 48d Partly Cloudy (Pure Sky)](https://polyhaven.com/a/kloofendal_48d_partly_cloudy_puresky) | Greg Zaal, Jarod Guest | Céu fotográfico e reflexos HDR, fonte 2K |

Os arquivos usados estão em `Unity/Assets/Art/ThirdParty/Environment/`. As rochas tiveram transformações aplicadas, origens centralizadas no chão e exportação para FBX; a geometria e as UVs foram preservadas. Os mapas albedo, normal e AO foram convertidos para PNG RGB 8 bits; a rugosidade original foi convertida em smoothness no canal alfa, com metalicidade zero nos canais RGB, para o shader Standard do Unity. As texturas recebem repetição por escala física no cenário e permanecem sem repetição nos modelos de rochas. O HDRI foi importado como cubemap pelo Unity.

As fontes foram obtidas dos endpoints oficiais `https://api.polyhaven.com/files/<id>`, com downloads em `https://dl.polyhaven.org/file/ph-assets/`. Os MD5 dos 19 downloads originais foram conferidos com os metadados do provedor em 18/10/2026. Nenhum modelo ou textura do Cabal foi extraído ou redistribuído.

## Servidor — versão 0.9

O executável local inclui o runtime .NET/ASP.NET Core e Microsoft.Data.Sqlite 10.0.13, com SQLitePCLRaw/SQLite. As licenças e avisos dessas dependências estão em `Server/Licenses/`; a publicação autônoma também os preserva em `Server/Build/Licenses/` e `Server/Build/ThirdPartyNotices.txt`. As versões transitivas estão fixadas em `Server/packages.lock.json`. Créditos da arte acima continuam obrigatórios ao redistribuir o cliente.

## Áudio — versão 0.10

Música ambiente: **“A Kind Of Hope” — Scott Buckley**.

'A Kind Of Hope' by Scott Buckley - released under CC-BY 4.0. www.scottbuckley.com.au

- Fonte oficial: https://www.scottbuckley.com.au/library/a-kind-of-hope/
- Arquivo original: https://www.scottbuckley.com.au/library/wp-content/uploads/2022/10/AKindOfHope.mp3
- Licença: Creative Commons Attribution 4.0 International — https://creativecommons.org/licenses/by/4.0/
- Alterações: conversão do mix completo para OGG/Vorbis, normalização para -23 LUFS e fades curtos na entrada/saída. Não foi usado trecho de outra composição. Crédito também deve permanecer acessível no painel Som do aplicativo. A música inteira toca em loop, em volume inicial de 8%; a transição ocorre na cauda suave da composição.
- Gravações/vídeos publicados com a trilha devem atribuir Scott Buckley na descrição, conforme orientação do autor.

Efeitos de espada, impactos físicos e passos: **Kenney — Impact Sounds (1.0) e RPG Audio**.

- Fontes oficiais: https://kenney.nl/assets/impact-sounds e https://kenney.nl/assets/rpg-audio
- Licença dos arquivos de áudio: CC0 1.0 — https://creativecommons.org/publicdomain/zero/1.0/
- Cópias dos avisos originais: `Unity/Assets/Audio/Kenney-Impact-Sounds-License.txt` e `Unity/Assets/Audio/Kenney-RPG-Audio-License.txt`.
- Foram usados trechos de `knifeSlice`, `drawKnife2`, `impactPunch`, `impactMetal_light`, `impactWood_heavy` e `footstep_concrete`. Algumas variantes foram mixadas, normalizadas, filtradas ou desaceleradas. Nomes exatos e hashes dos arquivos preparados constam em `Unity/Assets/Audio/AudioManifest.json`.

Vozes de monstros: **Ogrebane — Monster Sound Pack, Volume 1** (arquivo original publicado como `monster-sounds-volume-2.zip`).

- Fonte da publicação do autor: https://opengameart.org/content/monster-sound-pack-volume-1
- Download: https://opengameart.org/sites/default/files/monster-sounds-volume-2.zip
- Licença declarada na publicação: CC0 1.0 — https://creativecommons.org/publicdomain/zero/1.0/
- Sons selecionados: `Monster-1`, `Monster-2`, `Monster-3`, `Monster-4`, `Monster-5`, `Monster-6`, `monster-11` e `monster-14`. Foram normalizados e filtrados; as antecipações/dor têm pitch 10% mais grave, e as mortes têm pitch 25% mais grave e uma reflexão curta.

Camadas de ar das espadas, golpe de braço, vento ambiente, sons de proteção, cura, fúria e evolução foram sintetizadas originalmente para este protótipo, sem amostras externas. O gerador reproduzível usa ruído, filtros e harmônicos. O vento possui sobreposição de quatro segundos para eliminar cliques no loop.

Fontes e licenças verificadas em 18/10/2026. Não há indicação de endosso dos autores ao jogo.



### Corpo base vestido e cabeça — MakeHuman Community (CC0 1.0 Universal)

- Origem: [MakeHuman `base.obj`](https://github.com/makehumancommunity/makehuman/blob/master/makehuman/data/3dobjs/base.obj), arquivo oficial baixado em 18/10/2026.
- Licença oficial: https://github.com/makehumancommunity/makehuman/blob/master/LICENSE.md ; explicação da comunidade: https://static.makehumancommunity.org/about/license.html . A base, proxies e demais assets gráficos principais foram liberados sob CC0; o código do aplicativo tem licença própria e não foi incorporado.
- Entrada: `ArtSource/MakeHuman-base.obj`; licença original preservada em `ArtSource/MakeHuman-LICENSE.md`.
- Modificações: adaptação da anatomia aos 17 ossos existentes HeroRig; pesos de pele e poses de repouso compatíveis; seleção da cabeça, orelhas, rosto e mãos; proxy fechado de roupa convertido em túnica, calça e forro dos pés; cabelo curto, olhos e materiais originais. Os helpers genitais e os polígonos anatômicos de tronco/pelve sem roupa não são exportados para o jogo.
- Derivados: `Assets/Art/Equipment/BaseHero.fbx`, `ArtSource/BaseHero.blend`, `ArtSource/create_base_hero.py` e materiais BaseSkin/BaseLinen/BaseTrousers/BaseLeather/BaseHair/BaseEye/BaseIris.
- Cinturão, colar e dois aros: geometrias originais criadas proceduralmente para este protótipo, sem fonte externa.
- A armadura e espada Knight de piacenti mantêm UVs, atlas, licença e atribuição já documentadas; a separação por peça altera apenas grupos de triângulos e renderers.


### Pele masculina neutra — MakeHuman Community (CC0 1.0)

- Fonte e licença por asset: [MakeHuman Community — System Assets](https://static.makehumancommunity.org/assets/assetpacks/makehuman_system_assets.html), item `young_caucasian_male`, licença **CC0**. [CC0 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/).
- Direitos anteriores à dedicação CC0: Data Collection AB, Joel Palmius e Jonas Hauquier, conforme material original publicado pela comunidade. O `.mhmat` registra expressamente a liberação CC0 em setembro de 2020.
- Download oficial: https://files.makehumancommunity.org/asset_packs/makehuman_system_assets/makehuman_system_assets_cc0.zip ; entrada `skins/young_caucasian_male/young_lightskinned_male_diffuse.png`.
- PNG original 2048×2048, sem alteração de pixels, renomeado para `Unity/Assets/Art/ThirdParty/BaseHero/BaseSkin_Albedo.png`. Cabeça e mãos expostas do modelo vestido usam seus UVs MakeHuman mh08 originais. Túnica, calça, couro, cabelo e olhos mantêm materiais próprios.
- Albedo em sRGB, tint branco, pele não metálica e smoothness 0,30. O material fonte não fornece mapa normal; o Unity não usa relevo de outra pele nem deriva normal das sombras da fotografia.
- Material fonte, licença e hashes: `ArtSource/ThirdParty/MakeHumanYoungMaleSkin/`. Nenhum código do aplicativo MakeHuman/MPFB foi incorporado.
- SHA-256 albedo: `862a26e335e958b70534cb5f0d7c47ef30ab148a56c42b3e9da969cf76f12963`.

### Catacumbas e mortos-vivos — arte original do protótipo

O cenário subterrâneo, suas lajes, paredes, arcos, ossuário, ritual, tumbas e cripta foram construídos para este projeto. Os materiais de pedra reaproveitam as texturas Poly Haven já creditadas neste documento. Não houve novos downloads de assets nesta atualização.

Os esqueletos guerreiros e conjuradores, incluindo anatomia óssea articulada, espada, escudo, capuz, tecido e bastão, usam geometria e materiais procedurais originais em `PrototypeUndeadVisual.cs`. O modelo de troll existente permanece como molde de componentes de jogo, com sua apresentação oculta durante o uso desses visuais.

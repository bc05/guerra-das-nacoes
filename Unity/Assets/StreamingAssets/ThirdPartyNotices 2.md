# Créditos e licenças de terceiros

## Knight

- **Autor:** piacenti.
- **Fonte:** [Knight — OpenGameArt](https://opengameart.org/content/knight-2).
- **Licença:** [Creative Commons Attribution 3.0 Unported (CC BY 3.0)](https://creativecommons.org/licenses/by/3.0/).
- **Download original:** https://opengameart.org/sites/default/files/armored%20knight.zip.
- **Arquivos derivados:** `ArtSource/Cavaleiro.blend`, `Unity/Assets/Art/Cavaleiro.fbx` e texturas em `Unity/Assets/Art/ThirdParty/Knight/`.
- **Alterações:** escala métrica, espelhamento, adaptação da pose e pesos ao esqueleto do protótipo, animações Idle/Walk/Attack, alinhamento da espada e pontos de rastro, conversão dos materiais e empacotamento de metallic/smoothness. Preservadas a geometria esculpida e as UVs/texturas do autor.
- **Atribuição:** “Knight”, de piacenti, licenciado sob CC BY 3.0. Adaptado para este protótipo. O autor não endossa o projeto.

## Troll Mauler

- **Autor:** piacenti.
- **Fonte:** [Troll Mauler — OpenGameArt](https://opengameart.org/content/troll-mauler).
- **Download original:** https://opengameart.org/sites/default/files/troll.blend.
- **Licença escolhida:** [Creative Commons Attribution 3.0 Unported (CC BY 3.0)](https://creativecommons.org/licenses/by/3.0/).
- **Arquivos derivados utilizados:** `ArtSource/Troll.blend`, `Unity/Assets/Art/Troll.fbx` e mapas em `Unity/Assets/Art/ThirdParty/Troll/`.
- **Alterações:** seleção da malha média do autor; remoção de cena, modelos reserva e materiais não usados; conversão para materiais Principled; extração dos mapas albedo, normal e oclusão; preservação e associação da ação idle original; renomeação de quatro ossos de membros; normalização para 3,2 m, pés em zero; exportação FBX.
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

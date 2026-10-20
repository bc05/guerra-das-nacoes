# Fronteira expandida — fontes editáveis

O gerador incremental está em `Unity/Assets/Editor/WorldExpansionSetup.cs`. No Unity, use **Guerra das Nações → Expandir fronteira sem recriar pátio**. Ele conserva as raízes e posições do pátio, o personagem, os três encontros iniciais e o portal. Apenas os antigos objetos `Deco_Backdrop*`, que simulavam um terreno distante sem rotas jogáveis, ficam desativados e são substituídos pela área navegável.

O campo tem 100 × 100 metros, entre x = −50…50 e z = −20…80. O corredor central sai do arco norte do pátio; ramais levam ao acampamento Orc a oeste e ao bosque dos Trolls a leste. A arena de Grond fica em (0, 61). Colisões dos objetos de cena devem ser exportadas para a mesma grade do servidor depois de modificar a distribuição dos obstáculos.

As superfícies de solo, trilha e afloramentos reutilizam os mapas PBR e as seis malhas escaneadas **Poly Haven CC0**, já documentados em `ArtSource/ThirdPartyEnvironment/ASSETS-LICENCAS.md` e `ThirdPartyNotices.md`. Cascas, couro, ferro e tecido reutilizam os materiais editáveis do projeto. Árvores, folhas, gramíneas, tendas, placas, paliçadas, armaduras chanfradas, machados, presas e estandartes são geometria procedural criada para este protótipo. A folhagem recebe vento pelo shader `Unity/Assets/Shaders/WorldExpansion/FronteiraFolhagem.shader`.

Os Orcs e o chefe são **variantes do Troll Mauler de piacenti**, CC BY 3.0, com as texturas e o esqueleto originais, armaduras e acessórios novos. Não são modelos extraídos do Cabal. Créditos e licença do modelo base permanecem em `ThirdPartyNotices.md`. O visual de cada variante é aplicado por `PrototypeWorldExpansion`; ele não concede saques, experiência, dano ou cria janelas de chefe. Essas decisões pertencem ao servidor.

A floresta usa duas malhas compartilhadas por árvore e um LOD que descarta a árvore distante. Cada árvore tem 1.334 triângulos; a vegetação baixa é combinada em até 28 lotes, sem colisores. Há apenas uma luz adicional sem sombras na fogueira e sistemas de partículas com limites explícitos. O chefão tem corpo de colisão limitado a 1,65 m de raio no mundo, permitindo que o ataque corpo a corpo alcance sua silhueta ampliada.

Verificações disponíveis: `WorldExpansionSetup.Validate`, `WorldExpansionPlayModeChecks.Run` e `WorldExpansionPreview.Capture`. O último cria quatro imagens do cenário e de variantes de inspeção; essas instâncias são descartadas, sem salvar uma aparição de chefão fora da agenda.

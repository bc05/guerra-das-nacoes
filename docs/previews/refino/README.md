# Conferência do refinamento visual

Capturas geradas pelo próprio Unity 6000.3.26f1, com as malhas e os materiais do projeto. As imagens `equipment-*` usam duas luzes temporárias de inspeção e mostram somente as peças ativas; essas luzes não são gravadas na cena. `Cavaleiro_*` usa a iluminação do cenário. A câmera normal aparece em `../../../PreviewUnity.png`.

O refinamento mantém as posições dos objetos, as colisões, a identidade do controller e os 17 nomes dos ossos. Espada na mão direita, escudo na esquerda; a validação confere os marcadores do rastro, a empunhadura, o solo e as extremidades dos quatro golpes contra a malha deformada. As poses e transições da revisão temporal dos golpes foram validadas na geração completa, no Unity e na prévia atual de combate. Esses resultados antecedem o ajuste da caminhada para 2,25 m/s; a nova captura e os checks locais desse ajuste estão descritos abaixo, separadamente dos resultados históricos dos golpes.

A pasta `empunhadura` reúne renders Blender do modelo exportado: vistas frontal/traseira, detalhe do cabo e polegar, fases da caminhada, vista lateral e ciclos animados. Guarda, caminhada, corrida e golpes vêm do **Mixamo — Sword and Shield Pack**, adaptados ao rig existente por `ArtSource/retarget_mixamo_cavaleiro.py`. As oito fontes selecionadas e seus hashes estão em `ArtSource/ThirdParty/Mixamo/SourceManifest.json`. A ação auxiliar `A_BindPose` preserva os defaults de importação e não é usada pelo controller.

Os punhos acompanham os antebraços, o cabo permanece na palma e o escudo acompanha a guarda capturada. Corrigidos os vértices das pontas das luvas que estavam presos ao quadril; o diagnóstico rejeita essa ligação entre braços e pernas. Os quatro golpes têm duração visual de **0,60 s**, com impacto em **0,13 s** e recarga compartilhada de **0,65 s**. A preparação recorta os últimos 180 ms da fonte anteriores ao mesmo contato; pontes Hermite em quaternions com referência fixa suavizam a entrada e o retorno, com correção dos alvos dos pés por IK. A entrada autorada dura 0,125 s; o retorno à guarda ocupa 0,20 s em Attack/Attack3 e 34/120 s (aproximadamente 0,2833 s) em Attack2/HeavyAttack.

| Ação | Fonte Mixamo | Adaptação |
| --- | --- | --- |
| Attack | `sword and shield slash (3).fbx` | Corte diagonal; torso/braços capturados sobre pernas na guarda |
| Attack2 | `sword and shield slash.fbx` | Corte descendente; torso/braços capturados sobre pernas na guarda |
| Attack3 | `sword and shield attack (4).fbx` | Golpe alto, corpo inteiro capturado |
| HeavyAttack | `sword and shield slash (5).fbx` | Corte amplo; torso/braços capturados sobre pernas na guarda |

Os básicos alternam Attack → Attack2 → Attack3. O Corte Pesado usa HeavyAttack sem avançar esse ciclo; sua cobertura de 360° continua sendo a onda existente. A árvore mantém o estado Attack e escolhe um único clipe por valor discreto de AttackVariant. Os contadores são cosméticos por personagem; não são transmitidos pelo protocolo. A entrada mistura poses por 0,05 s; a saída começa após o clipe completo, na fase 1,0, e dura 0,025 s. A revisão temporal abrange os quatro golpes; as medições anteriores de `work/attack-library-*` são históricas e não comprovam a fluidez ou o contato com o solo das novas curvas.

O apoio dos pés acompanha a recuperação: o deslocamento fica bloqueado até 0,288 s e retorna gradualmente até 0,60 s. Fora dessa recuperação, a caminhada passa de 1,35 para **2,25 m/s** (aproximadamente 67% mais rápida), e a corrida permanece a **5,5 m/s com Shift**; dano, mana, impacto e recargas continuam sob as mesmas regras autoritativas.

O retarget compensa as proporções das pernas e a geometria real das botas. O plano dos joelhos é estabilizado quando a perna fonte fica quase reta, evitando amplificar pequenos movimentos laterais. Somente os 50 ms de cada lado da emenda Walk/Run recebem suavização localizada, seguida de correção dos alvos dos pés. Os contatos são medidos na ação final a 120 Hz; a excursão central do tornozelo durante o apoio ficou abaixo de 16 mm, incluindo o balanço de calcanhar/ponta. Essa medida não equivale a deslizamento puro da sola. Consulte `ArtSource/CavaleiroRigDiagnostics.json` e `Unity/Assets/Art/CavaleiroMotionProfile.json`.

No FBX importado, a separação entre tornozelos fica em aproximadamente 14–18 cm na caminhada e 18–24 cm na corrida; a largura máxima entre joelhos fica em 28/32 cm. As poses fecham e a maior diferença estimada de velocidade na emenda é 0,0037 m/s. A raiz permanece imóvel; o deslocamento continua sob controle do jogo.

A prévia atual `empunhadura/Locomocao-60fps.mp4` foi regenerada e inspecionada após o ajuste da caminhada: resolução de 960×600, seis segundos, 360 quadros a 60 Hz, com frente e lado simultaneamente no controller real. Mostra arrancada, caminhada normal a 2,25 m/s, transição para corrida a 5,5 m/s, frenagem e repouso. No jogo, a corrida exige segurar Shift. A grade de 2 m se desloca pela distância percorrida. O MP4 atual `empunhadura/Combate-60fps.mp4` foi regenerado e inspecionado: seis segundos, 360 quadros a 60 Hz, com as variantes `0,1,2,0,1,3` a cada 39 quadros (0,65 s), incluindo o retorno à guarda e o próximo golpe na cadência real. `empunhadura/Combate.gif` é uma referência antiga apenas do primeiro básico; `Caminhada.gif` e `Corrida.gif` continuam válidos, pois Idle/Walk/Run foram preservados.

A locomoção conserva a fase em uma única árvore. Walk percorre aproximadamente 1,487 m por ciclo de 1,1 s e Run 2,612 m por ciclo de 0,7 s. O controller ajusta a cadência à velocidade: Walk fica no limiar de 2,25 m/s, com reprodução em aproximadamente 1,665×, e segurar Shift ativa Run a 5,5 m/s, com transição contínua entre os dois movimentos. O áudio usa aproximadamente meio ciclo por passo — 0,743 m na caminhada e 1,306 m na corrida — e interpola essas distâncias pela velocidade real. As fontes FBX contêm chaves a 30 fps; a adaptação amostra a 120 Hz, preserva a duração e usa tangentes cúbicas periódicas nos ciclos.

Validação do ajuste atual da caminhada no projeto isolado:

- Aplicação dos assets aprovada, com Walk no limiar de 2,25 m/s e reprodução de 1,6646447×; Run conserva 5,5 m/s e reprodução de 1,4738742×.
- Checks locais de locomoção (35 verificações), seleção/alvo (33), aproximação bloqueada (11) e combate manual (157) aprovados.
- Capturas frontal e lateral confirmam Walk a 2,25 m/s; a prévia combinada foi regenerada e inspecionada em 360 quadros a 60 Hz.
- Check online aprovado: três pedidos, três ataques e três acertos, além de recusa e confirmação sem repetição. A fixture conecta somente ao servidor isolado na porta 18090, usa banco temporário e adia os ataques dos NPCs somente nesse banco, para impedir a morte do jogador de teste durante as esperas; o servidor e as regras do jogo não foram alterados.
- As nove etapas de aplicação, validação, captura e build encerraram com código zero; evidências em `work/brisk-walk-validation-results.json`. O controller testado e o aplicativo principal em `Unity/Builds/macOS/Guerra das Nações.app` foram atualizados.
- Assinatura local aprovada por `codesign --verify --deep --strict`; inicialização nativa ativa após oito segundos, sem erros e sem conexão automática (`work/brisk-walk-native-smoke.json`). São verificações locais, sem CI ou publicação remota.

Validação concluída na revisão temporal anterior dos golpes (não comprova o ajuste atual da velocidade de caminhada):

- Geração completa e quatro ataques amostrados a 480 Hz: solo, punhos, empunhadura, guarda e contato em 0,13 s aprovados. Os 17 ossos, matrizes de repouso, pesos, equipamentos e 52.938 chaves de Idle/Walk/Run, incluindo tangentes, foram preservados.
- `AttackFluidityPlayModeChecks.Run`: **73 verificações em cada taxa de 30/60/120 Hz**, com seis ataques pela API real no primeiro quadro elegível da recarga, guarda local/remota, variantes, pesado e recusa. A entrada dos golpes seguintes não acrescenta latência em relação ao primeiro. Use `GN_ATTACK_FLUIDITY_FRAME_RATE` e `GN_ATTACK_FLUIDITY_REPORT_PATH` para separar cada execução.
- As 14 etapas de validação, captura e build passaram, incluindo importação/rig, continuidade da locomoção, biblioteca de golpes, combate, ataque automático, aproximação bloqueada e equipamentos. Relatórios e logs: `work/attack-fluidity-*`.
- O check `TargetCombatPlayModeChecks` calcula a espera pela distância restante dividida por `DefaultWalkSpeed`, mais 0,5 s de margem, substituindo os antigos 1,6 s fixos; o comportamento do jogo não foi alterado. Os logs ainda contêm o erro preexistente do indexador `UnityEditor.Search`, sem falha de assets ou das etapas.
- Build macOS e arquivos derivados sincronizados. Assinatura aprovada por `codesign --verify --deep --strict`; inicialização nativa ativa após oito segundos, sem erros e sem conexão automática, conforme `work/attack-fluidity-native-smoke.json`.

Os checks locais PlayMode usam personagens de teste em memória; o check online conecta somente ao servidor isolado da porta 18090, com banco temporário. Nenhum deles conecta ao servidor dos jogadores. O refinamento não altera o schema nem os arquivos de progresso dos jogadores. Os resultados não constituem teste de carga ou benchmark de FPS.

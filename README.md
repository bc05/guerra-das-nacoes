# Guerra das Nações — protótipo 0.16.0

## Catacumbas

As Catacumbas são um mapa de combate separado do Vale dos Clãs, com vestíbulo, ossuário, câmara dos conjuradores e cripta dos guardiões. A entrada fica em **(0,19)** no Vale: aproxime-se do portal e pressione **E** ou use **Interagir**. O destino é o vestíbulo em **(0,0)**. O portal de saída, em **(0,−3)**, retorna ao Vale em **(0,17)**. O servidor valida proximidade e condições de viagem; a cidade mantém sua área segura.

Esqueletos e conjuradores usam o mesmo catálogo de espécies e nascimentos, com aparências próprias e recompensas confirmadas pelo servidor. Menu, HUD, destinos e saques identificam o mapa pelo nome. Cenário, criaturas, colisões e iluminação se alternam por mapa, preservando os modelos em cache durante as viagens. Ao sair das Catacumbas, a iluminação externa é restaurada e os avisos de ataque do mapa anterior são cancelados.

A faixa recomendada é **nível 16–24**, sem bloqueio de entrada por nível. Há três esqueletos do ossuário, três conjuradores das cinzas e um guardião de elite na cripta. Conjuradores anunciam a área atingida antes de lançar o ataque; afastar-se desse ponto permite esquivar. O vestíbulo fica afastado dos encontros e tem um portal de retorno.

A instalação aditiva leva o **conteúdo à versão 3**, preservando personagens, patrimônio, identificadores dos encontros existentes, versões administrativas, rascunhos e calendário de Grond. Protocolo e schema SQLite continuam em 2. O roteiro e as evidências desta versão estão em [combate e mapas](docs/combate-e-fronteira.md).

## Combate e Vale dos Clãs

A versão atual deixa cada golpe sob o controle do jogador. Clique esquerdo em um monstro ou Tab apenas selecionam o alvo; `1`, Espaço, botão direito ou o botão da habilidade pedem um único golpe por acionamento. Segurar a tecla ou o botão não repete o ataque. Se o alvo estiver distante, um pedido de golpe aproxima o herói até o alcance e executa somente esse golpe. Clique no chão, movimento manual ou troca de seleção cancelam o pedido ainda pendente. Mantém animações com apoio dos pés e resposta visual imediata no modo online. O Vale dos Clãs amplia o mapa para 100×100 m, com trilhas, vegetação ao vento, acampamento Orc, bosque Troll e arena de chefão. Os nomes dos monstros mudam de cor conforme a diferença de nível e inimigos próximos adquirem jogadores para atacar.

Grond nasce inicialmente todos os dias às **20h, America/Sao_Paulo**, por até 60 minutos, com **12.000 HP**, dano base **70** e **35%** de chance de um item lendário do seu grupo de drops. Dias, horários, fuso, atributos e probabilidades ficam no painel administrativo. A publicação aditiva conserva o progresso e as configurações existentes. Consulte [controles, calendário, perigos e validação](docs/combate-e-fronteira.md).

O catálogo do Vale agora separa **espécies** — atributos, drops, aparência e comportamento — de **pontos de nascimento** — identificador estável, espécie, mapa, posição e agenda. O Xamã lança um ataque à distância sobre a posição anunciada do jogador; o Troll Bruto e o Troll Ancião anunciam golpes em área. Sair da zona antes do impacto permite evitar o dano. O painel traz as abas **Espécies** e **Nascimentos**; as alterações valem para o próximo nascimento, preservando o perfil dos monstros vivos. A migração aditiva do conteúdo para a versão 2 mantém personagens, patrimônio, identificadores de encontros, versões anteriores e ajustes administrativos; ela não reinicia o banco nem muda a versão do protocolo.

Mantém a seleção de personagens antes de conectar, as configurações gráficas persistentes, a economia, a cidade, os NPCs, os drops e o painel administrativo. O progresso continua no servidor: nível, experiência, pontos, moedas, mochila e equipamentos, inclusive espaços vazios. Os fontes atuais estão nas pastas de desenvolvimento abaixo. Os pacotes `Fontes-*.zip` são retratos históricos anteriores aos últimos refinamentos; identidades, configurações pessoais e bancos de personagens não integram esses pacotes.

## Pasta de desenvolvimento

O projeto de desenvolvimento fica em `/Users/garcez/workspace/guerra-das-nacoes` (`~/workspace/guerra-das-nacoes`). Abra a pasta `Unity` no Unity Hub ou execute `AbrirNoUnity.command` na raiz.

```text
guerra-das-nacoes/
├── Unity/                 # cliente: Assets, Packages, ProjectSettings e builds
├── Server/                # servidor, painel admin, testes e dados locais
├── ArtSource/             # arquivos Blender, geradores e fontes de terceiros
├── Launcher/              # gerador do lançador macOS
├── docs/historico/        # pacotes de versões anteriores e capturas
├── work/                  # relatórios e arquivos auxiliares de desenvolvimento
├── Guerra das Nações.app  # lançador do jogo
├── *.command              # abrir editores, iniciar, jogar e verificar
└── README.md
```

Mantenha `Unity`, `Server`, `ArtSource` e `Launcher` como pastas irmãs: os scripts de arte e o servidor compartilham arquivos dessa estrutura. O caminho anterior em `Documents/Codex/2026-10-18/eu/outputs/GuerraDasNacoes` permanece como atalho para esta pasta. Os links dos anexos históricos também foram mantidos na raiz.

A movimentação preserva o banco existente em `Server/data/characters.db`, sem criar novo backup nem alterar o schema. Identidades dos jogadores e preferências continuam em `~/Library/Application Support/com.guerradasnacoes.prototipo`, na localização de dados do aplicativo gerenciada pelo macOS.

## Refinamento visual do personagem

O cavaleiro usa a espada na mão direita e o escudo na esquerda. A adaptação mantém os 17 ossos e os equipamentos removíveis. Guarda, caminhada, corrida e golpes usam movimentos capturados do **Mixamo — Sword and Shield Pack**, transferidos por `ArtSource/retarget_mixamo_cavaleiro.py`. `ArtSource/Cavaleiro.blend` contém sete ações editáveis: Idle/Walk/Run, três básicos Attack/Attack2/Attack3 e HeavyAttack. As fontes selecionadas, configurações e hashes ficam em `ArtSource/ThirdParty/Mixamo/SourceManifest.json`; os termos estão em `ThirdPartyNotices.md`.

A empunhadura alinha o centro real do cabo à palma da luva, com o polegar curvado sobre os dedos. Os punhos acompanham os antebraços, e o escudo acompanha a guarda capturada. Os ataques básicos alternam três capturas; o Corte Pesado usa uma quarta captura exclusiva e mantém sua onda em área. Nos cortes diagonal (Attack), descendente (Attack2) e amplo (HeavyAttack), o torso e os braços capturados usam a base de guarda das pernas para evitar que a cápsula parada deslize durante a animação. O golpe alto (Attack3) mantém o corpo inteiro capturado. As faixas fonte e os ajustes ficam registrados no perfil de movimentos. As pontas das luvas recebem pesos dos braços; a validação rejeita triângulos da luva presos ao quadril ou às pernas. A ação auxiliar `A_BindPose` conserva o repouso neutro exigido pela importação e pelos equipamentos, sem participar do controller do jogador.

A revisão temporal dos quatro golpes usa duração visual de **0,60 s**, impacto em **0,13 s** e recarga compartilhada de **0,65 s**. A preparação recorta os últimos 180 ms da fonte antes do mesmo contato; pontes Hermite em quaternions com referência fixa suavizam a entrada e o retorno, com ajuste dos alvos dos pés por IK. A entrada autorada dura 0,125 s; o retorno à guarda ocupa 0,20 s em Attack/Attack3 e 0,2833 s em Attack2/HeavyAttack. O controller entra no ataque com mistura de 0,05 s e inicia a saída somente após o clipe completo, na fase 1,0, com mistura de 0,025 s. A geração completa validou os quatro golpes a 480 Hz, preservando os 17 ossos, o repouso neutro, os equipamentos e as 52.938 chaves de Idle/Walk/Run, incluindo suas tangentes.

Os pés e o plano de flexão dos joelhos na locomoção seguem o movimento capturado, compensando as diferenças de proporção entre o Mixamo e o cavaleiro. A altura considera a geometria real das botas. Os diagnósticos verificam largura das passadas, solo, continuidade dos ciclos, punhos e empunhadura; o Unity confere 91 poses e as extremidades dos quatro golpes. O perfil registra os contatos da ação final e a excursão do tornozelo durante o apoio, que também inclui o movimento de calcanhar/ponta. As vistas frontal, lateral, detalhes e ciclos animados ficam em `docs/previews/refino/empunhadura`.

A locomoção usa a velocidade realmente percorrida: aceleração e frenagem curtas, transições entre repouso/caminhada/corrida, parada contra obstáculos e interpolação dos jogadores remotos. O movimento normal usa Walk a **2,25 m/s**, aproximadamente 67% mais rápido que os 1,35 m/s anteriores; segurar Shift ativa Run a **5,5 m/s**. Durante o golpe, o deslocamento fica bloqueado nos primeiros 0,288 s e é liberado gradualmente até 0,60 s, para acompanhar o apoio dos pés. Dano, impacto, mana e recarga mantêm as regras autoritativas existentes. O troll agora articula quadris, joelhos e tornozelos em função da distância percorrida.

A locomoção mantém a fase em uma única árvore ao começar, frear e mudar de velocidade. O perfil `Unity/Assets/Art/CavaleiroMotionProfile.json` fornece a distância capturada ajustada à proporção do cavaleiro: aproximadamente 1,487 m por ciclo de caminhada e 2,612 m por ciclo de corrida. O controller usa o limiar de 2,25 m/s para Walk, com reprodução em aproximadamente 1,665×, ajustada à distância capturada por ciclo; isso mantém o clipe de caminhada no movimento normal. Run permanece no limiar de 5,5 m/s ao segurar Shift. O áudio dos passos também acompanha a distância percorrida: usa aproximadamente 0,743 m por passo na caminhada e 1,306 m na corrida, interpolando entre as duas referências durante a aceleração. A exportação amostra a 120 Hz e a importação preserva as chaves com tangentes cúbicas periódicas. `LocomotionContinuityChecks.Run` mede o FBX importado a 120 Hz, incluindo largura das passadas e velocidade nas emendas. A prévia atual `Locomocao-60fps.mp4` foi regenerada e inspecionada após o ajuste: seis segundos, 360 quadros a 60 Hz, resolução de 960×600, com frente e lado simultaneamente, arrancada, caminhada a 2,25 m/s, transição para corrida a 5,5 m/s, frenagem e repouso sobre uma grade móvel. Essa captura determinística não é medição de desempenho do jogo.

Os materiais preservam as cores originais dos mapas PBR, com resposta de metal, relevo e rugosidade por superfície, atlas dos olhos do troll e pele neutra oficial MakeHuman CC0 (`young_caucasian_male`) na cabeça e nas mãos. A pele usa o albedo fotográfico original com tint branco e smoothness 0,30; a fonte oficial não fornece normal map. Texturas usam até 2K, mipmaps, filtragem trilinear/anisotrópica, compressão e nenhuma cópia legível de CPU. Isso controla o custo de memória; não representa uma medição ou garantia de FPS. O cenário mantém sua disposição e colisões; luz ambiente, oclusão, brilho e vinheta foram ajustados para revelar os detalhes.

Para reaplicar apenas esse refinamento **sem recriar a cena**, salve e feche o Editor e execute a partir da pasta `Unity`:

```zsh
'/Applications/Unity/Hub/Editor/6000.3.26f1/Unity.app/Contents/MacOS/Unity' -batchmode -quit -projectPath "$PWD" -executeMethod VisualRefinementSetup.Apply -logFile "$PWD/refinamento-visual.log"
'/Applications/Unity/Hub/Editor/6000.3.26f1/Unity.app/Contents/MacOS/Unity' -batchmode -projectPath "$PWD" -executeMethod LocomotionPlayModeChecks.Run -logFile "$PWD/locomocao.log"
'/Applications/Unity/Hub/Editor/6000.3.26f1/Unity.app/Contents/MacOS/Unity' -batchmode -projectPath "$PWD" -executeMethod AttackLibraryPlayModeChecks.Run -logFile "$PWD/golpes.log"
GN_ATTACK_FLUIDITY_FRAME_RATE=60 GN_ATTACK_FLUIDITY_REPORT_PATH="$PWD/../work/attack-fluidity-60-report.json" '/Applications/Unity/Hub/Editor/6000.3.26f1/Unity.app/Contents/MacOS/Unity' -batchmode -projectPath "$PWD" -executeMethod AttackFluidityPlayModeChecks.Run -logFile "$PWD/../work/attack-fluidity-60.log"
```

`VisualRefinementSetup.Apply` também executa a verificação temporal dos ciclos. O ajuste das curvas importadas usa as APIs oficiais [OnPostprocessAnimation](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AssetPostprocessor.OnPostprocessAnimation.html) e [SetEditorCurves](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AnimationUtility.SetEditorCurves.html).

Na revisão temporal anterior, `AttackFluidityPlayModeChecks.Run` passou em **73 verificações a cada taxa de 30/60/120 Hz**, incluindo seis ataques no primeiro quadro elegível da recarga, guarda local/remota, variantes, pesado e recusa. A entrada dos golpes seguintes não acrescentou latência em relação ao primeiro. Execute também com `GN_ATTACK_FLUIDITY_FRAME_RATE=30` e `120`, usando nomes de relatório/log distintos. As 14 etapas de validação, captura e build daquela revisão passaram; o build macOS teve a assinatura verificada e a inicialização nativa permaneceu ativa por oito segundos, sem erros e sem conexão automática. As evidências históricas ficam em `work/attack-fluidity-*`.

O MP4 atual `docs/previews/refino/empunhadura/Combate-60fps.mp4` foi regenerado e inspecionado: são seis segundos, 360 quadros a 60 Hz, com golpes a cada 39 quadros (0,65 s), nas variantes `0,1,2,0,1,3`. `Combate.gif` é uma referência antiga apenas do primeiro básico; `Caminhada.gif` e `Corrida.gif` continuam válidos porque essas curvas foram preservadas.

O check `TargetCombatPlayModeChecks` substituiu a espera fixa antiga de 1,6 s por distância restante dividida por `DefaultWalkSpeed`, mais 0,5 s de margem; a espera acompanha a velocidade normal configurada, agora de 2,25 m/s, sem alterar a lógica de aproximação do jogo. Os logs do Editor ainda registram o erro preexistente do indexador `UnityEditor.Search`, sem falha nos assets ou no resultado das etapas.

No ajuste atual da caminhada, as nove etapas de aplicação, validação, captura e build passaram no projeto isolado (`work/brisk-walk-validation-results.json`), incluindo locomoção (35 verificações), seleção/alvo (33), aproximação bloqueada (11) e combate manual (157). O check online confirmou três pedidos, três ataques e três acertos, além de recusa e confirmação sem repetição. Ele conecta somente ao servidor isolado na porta 18090, usa banco temporário e adia apenas os ataques dos NPCs dessa fixture para evitar que o jogador de teste morra durante as esperas; o servidor e as regras do jogo foram preservados. A prévia usa as novas referências de velocidade. O controller testado e o aplicativo macOS foram entregues; a assinatura local passou em `codesign --verify --deep --strict`, e a inicialização nativa permaneceu ativa por oito segundos, sem erros e sem conexão automática (`work/brisk-walk-native-smoke.json`). Essas verificações são locais e não representam CI ou publicação remota.

No Editor, o menu **Guerra das Nações → Aplicar refinamento visual sem recriar cenário** aplica os mesmos ajustes. As capturas atuais ficam em `docs/previews/refino`; imagens e pacotes de `docs/historico` continuam representando as versões antigas.

Os golpes básicos alternam suas capturas na apresentação local e remota; a habilidade pesada não consome uma posição dessa sequência. Confirmações não repetem o golpe previsto e uma recusa do comando atual devolve sua posição ao ciclo. Essa escolha é cosmética: dano, mana, recargas, alcance e confirmação de acerto continuam sob as regras existentes. O protocolo não transmite a variante; um observador que entra depois pode começar por outra captura básica, com as mesmas regras de combate.

## Começar e escolher personagem

1. Abra **Guerra das Nações.app**, identificado pelo ícone do escudo e da espada. Esse aplicativo chama **Jogar.command**, inicia o servidor local quando necessário e abre a tela de personagens; a conexão ao mundo acontece ao clicar em **Conectar** ou pressionar **Enter**. O comando **Jogar.command** continua disponível como alternativa.
2. Selecione um personagem na lista à esquerda. O perfil **principal** reutiliza a identidade anterior e o progresso já existente. O perfil **segundo** também é incorporado quando sua identidade já existe. O segundo lançador prepara seu cartão local mesmo antes da primeira conexão.
3. Consulte a prévia 3D central e arraste para girar o modelo. Quando o servidor responde, o painel apresenta nome, nível, último mapa e as peças equipadas desse personagem. A consulta não entra no mundo, não troca uma sessão em andamento e não cria um personagem no servidor.
4. Use **Novo personagem**, informe o **nick do personagem** e clique em **Verificar e criar**. O nick deve ter de 2 a 24 caracteres: letras, números, espaços, hífen ou sublinhado. O jogo consulta a disponibilidade no servidor antes de acrescentar o cartão. Há até **oito personagens por lista local de servidor**, cada um com identidade, moedas, atributos e equipamentos próprios. O personagem é criado no servidor somente ao conectar pela primeira vez.
5. Escolha **Conectar**. Os comandos do herói ficam bloqueados até o servidor confirmar o estado carregado. Falhas de conexão e de identidade são exibidas; o jogo não substitui automaticamente um personagem por outro.

O nick é único dentro do servidor. A comparação ignora diferenças entre maiúsculas/minúsculas, espaços repetidos e formas Unicode equivalentes, usando normalização FormKC; por exemplo, **Herói Azul**, **HERÓI AZUL** e **  Herói   Azul  ** disputam o mesmo nick. A consulta de disponibilidade não reserva o nome: o servidor verifica novamente na criação para impedir duas conexões simultâneas de obterem o mesmo nick. Se alguém o ocupar antes da conexão, a tela permite escolher outro sem substituir a identidade do personagem.

Um perfil antigo já criado continua com seu nome e progresso preservados. Cartões ainda sem personagem no servidor pedem **Definir nick e conectar**. Nomes repetidos de versões históricas permanecem acessíveis aos seus titulares e são reservados contra novas criações, sem renomear ou reiniciar personagens automaticamente.

**Esc** fecha primeiro os painéis do jogo e, sem outro painel aberto, abre o menu com **Continuar**, **Gráficos**, **Selecionar personagem** e **Sair do jogo**. A troca de personagem aguarda as ações pendentes e exige sair de combate. O menu não pausa o mundo online: os outros jogadores, monstros e efeitos continuam ativos.

**JogarSegundoCliente.command** abre uma segunda instância com preferência pelo perfil segundo; selecione-o e conecte. Continuar usando a mesma identidade em duas instâncias substitui a sessão anterior, como nas versões anteriores. Não compartilhe arquivos de identidade para simular personagens diferentes.

A tela inclui uma configuração avançada de endereço de servidor. Depois de alterar o endereço, use **Usar servidor e atualizar** antes de conectar. As identidades de destinos diferentes ficam separadas; a credencial local existente é preservada para o servidor distribuído em `http://127.0.0.1:8088`. Endereços públicos exigem HTTPS. As opções explícitas de ensaio LAN e seus limites continuam documentadas em `Server/README.md`.

Para automação e verificações, o argumento `--auto-connect` mantém a conexão direta, combinado com `--profile=nome` e, se necessário, `--server=https://host:porta`. O fluxo normal pelo lançador abre o menu. O aplicativo fornecido é macOS Apple Silicon; não há build Windows nesta entrega.

## Ícone e entrada do aplicativo

O ícone original representa as duas nações por campos vermelho/azul em um escudo metálico, com espada de energia ciano. Foi criado pela ferramenta integrada `image_gen`; o [prompt e os hashes](Icone-prompt.md) registram a origem e as conversões. O projeto inclui PNG de 1024 × 1024 e ICNS macOS em `Unity/Assets/Art/Brand/`. O build Unity configura o mesmo ícone no aplicativo do jogo. O menu **Guerra das Nações → Gerar aplicativo macOS** gera `Unity/Builds/macOS/Guerra das Nações.app`, com espaços e acentos também nos metadados de exibição. O executável interno usa `GuerraDasNacoes` para evitar diferenças de normalização Unicode na assinatura; o build renova e verifica a assinatura local após esse ajuste. O identificador e a pasta de dados permanecem os mesmos. Os atalhos leem `CFBundleExecutable` automaticamente e aceitam o caminho antigo quando o novo build ainda não existe.

**Guerra das Nações.app** é uma entrada local para essa instalação: mantenha-o ao lado de **Jogar.command**, na pasta do projeto. Ele pode ser fixado no Dock. O ZIP de fontes contém o gerador, em vez de incluir os aplicativos compilados. Depois de compilar cliente e servidor, execute a partir da raiz do projeto:

```zsh
python3 Launcher/criar_launcher_macos.py
```

## Configurar gráficos

Clique em **Gráficos** na seleção de personagens ou no menu aberto com Esc durante o jogo. As opções podem ser ajustadas antes de conectar. O painel oferece **Baixo**, **Médio**, **Alto** e **Ultra**, além de ajustes individuais:

| Opção | Efeito aplicado |
|---|---|
| Resolução | Altera a quantidade de pixels renderizados, conforme as opções disponíveis para a tela |
| Modo de exibição | Janela, janela sem bordas ou janela maximizada no Mac; tela cheia exclusiva quando executado no Windows |
| Sombras | Desligadas, baixas ou altas, com resolução e distância correspondentes |
| Texturas | Resolução completa, metade ou um quarto das texturas com mipmaps |
| Suavização de bordas | Desligada, 2×, 4× ou 8× MSAA, conforme suporte da GPU |
| Distância de visão | Ajusta o plano de recorte das câmeras do mundo entre 60 e 220 metros |
| Efeitos de iluminação | Ativa ou desativa o processamento de oclusão, brilho e HDR |
| Sincronização vertical | Sincroniza os quadros com a tela; quando ativa, tem precedência sobre o limite de FPS |
| Limite de quadros | 30, 60, 120 FPS ou sem limite |

Use **Aplicar** para visualizar e salvar alterações. Mudanças de resolução ou modo de exibição exigem confirmar **Manter** em até **15 segundos**. Sem confirmação, ou ao escolher **Restaurar**, as opções anteriores voltam automaticamente. Uma prévia não confirmada não substitui as preferências confirmadas; o jogo também restaura o estado anterior ao encerrar ou desativar o componente durante essa prévia. **Descartar** limpa as alterações ainda não aplicadas.

A configuração fica em `Application.persistentDataPath/GraphicsSettings.json`, com versão, gravação atômica e uma cópia anterior `.bak`. Arquivo danificado é tratado separadamente do progresso do personagem. As opções são locais para o computador, compartilhadas pelos personagens e não enviadas ao servidor. No Unity Editor e nas execuções batch, os ajustes de qualidade funcionam, mas resolução e modo não redimensionam a área de trabalho.

Baixo reduz o custo de sombras, texturas, distância e efeitos. Alto/Ultra aumentam a apresentação e o custo de renderização. Os perfis não garantem um número de FPS: o resultado depende da máquina, da resolução e da quantidade de personagens e efeitos na tela. Os modelos e habilidades do protótipo permanecem os mesmos.

## Preservar os personagens ao atualizar

- Preserve **Server/data/**, especialmente `characters.db` e os backups já existentes. Atualizar o cliente não recria o banco nem reinicia personagens.
- Preserve as identidades em `Application.persistentDataPath/Online/<perfil>/identity.json`. Elas continuam dando acesso aos personagens locais anteriores. Não envie nem publique esse arquivo.
- `Online/characters.json` guarda somente a lista de nomes/perfis; não contém a credencial nem o patrimônio. Uma lista inválida é preservada e exige recuperação, sem gerar novas identidades para encobrir o problema.
- Para outros servidores, listas e identidades ficam sob uma pasta específica daquele destino dentro de `Application.persistentDataPath/Servers/`. Mudar de endereço não envia a credencial do servidor anterior.
- As identidades continuam anônimas e locais. Oito espaços no menu não significam uma conta recuperável por e-mail ou acesso automático em outro computador. Autenticação de conta e hospedagem pública continuam fora desta entrega.

## Economia, cidade e administração

As funções da versão 0.12 permanecem: **E** interage com portais e NPCs, **I** abre equipamentos/mochila, **C** abre atributos, **B** abre saques pendentes e **F** usa uma poção comprada. As moedas e transações são confirmadas pelo servidor; saques pendentes podem ser recuperados sem um novo sorteio.

Abra **AbrirPainelAdmin.command** para administrar equipamentos, espécies, pontos de nascimento, tabelas de drop, NPCs e preços. Em **Espécies**, configure os atributos e o comportamento; em **Nascimentos**, escolha a espécie e a posição do encontro. **Chefões e horários** vincula a agenda ao nascimento do chefão. O painel local em `http://127.0.0.1:8088/admin` mantém autenticação separada, versões, simulação e auditoria. A chave administrativa permanece apenas em `Server/data/admin-secret.txt`; não faz parte do Unity nem do pacote de fontes. Os detalhes e as verificações anteriores estão registrados no histórico 0.12 abaixo.

## Validar e compilar esta versão

Execute `VerificarServidor.command` para as verificações do servidor em dados temporários. Para os novos checks Unity, feche outros Editores que estejam usando a mesma pasta e execute **um comando por vez**, a partir de `Unity`:

```zsh
'/Applications/Unity/Hub/Editor/6000.3.26f1/Unity.app/Contents/MacOS/Unity' -batchmode -quit -projectPath "$PWD" -executeMethod MenuRosterChecks.Run -logFile "$PWD/menu-lista.log"
'/Applications/Unity/Hub/Editor/6000.3.26f1/Unity.app/Contents/MacOS/Unity' -batchmode -projectPath "$PWD" -executeMethod GraphicsSettingsChecks.Run -logFile "$PWD/graficos.log"
'/Applications/Unity/Hub/Editor/6000.3.26f1/Unity.app/Contents/MacOS/Unity' -batchmode -projectPath "$PWD" -executeMethod MenuPlayModeChecks.Run -logFile "$PWD/menu-playmode.log"
'/Applications/Unity/Hub/Editor/6000.3.26f1/Unity.app/Contents/MacOS/Unity' -batchmode -projectPath "$PWD" -executeMethod ManualCombatPlayModeChecks.Run -logFile "$PWD/combate-manual.log"
'/Applications/Unity/Hub/Editor/6000.3.26f1/Unity.app/Contents/MacOS/Unity' -batchmode -quit -projectPath "$PWD" -executeMethod PrototypeSetup.BuildMacOS -logFile "$PWD/build.log"
```

Os checks de menu/lista/gráficos usam pastas temporárias; não utilizam as credenciais reais para criar personagens de teste. O modo Play offline no Editor continua disponível para verificações de combate independentes. As configurações gráficas no Editor não alteram a resolução da área de trabalho.

Resultados atuais da retirada do ataque automático:

- **14 etapas aprovadas:** compilação C#, 12 suítes PlayMode e build macOS, em um projeto isolado de validação.
- `ManualCombatPlayModeChecks.Run`: **157 verificações** de seleção sem movimento ou ataque, um golpe por comando, aproximação, quatro golpes, cancelamentos e rejeições tardias.
- `ManualCombatOnlinePlayModeChecks.Run`: **três pedidos explícitos, três ataques e três impactos confirmados** no servidor descartável. Esperas superiores a duas recargas não produziram novos ataques; troca durante recarga, respostas repetidas e recusa real não duplicaram nem reativaram golpes.
- `ApproachBlockPlayModeChecks.Run`: **11 verificações**, incluindo liberar um caminho antes bloqueado e aguardar mais duas recargas sem retomar o pedido cancelado. As suítes de alvo, combate, habilidades, biblioteca, impactos, locomoção, fluidez, menu e cliente online também passaram. A fluidez manteve suas **73 verificações a 120 Hz**.
- Aplicativo atualizado em `Unity/Builds/macOS/Guerra das Nações.app`, com assinatura local verificada. A inicialização nativa iniciou o motor e permaneceu ativa por oito segundos, sem erros detectados e sem conexão automática. O relatório está em `work/manual-combat-native-smoke.json`.

O resumo e os logs ficam em `work/manual-combat-validation-results.json` e `work/manual-combat-*.log`. Para conferir no jogo, reabra `Jogar.command`: selecione um inimigo próximo e outro distante e aguarde sem atacar. Acione `1` uma vez para executar um único golpe, com aproximação apenas quando necessária. Segurar `1`, Espaço ou botão direito não deve repetir; solte e faça um novo acionamento depois da recarga. Um acionamento durante a recarga não agenda o próximo golpe.

Resultados anteriores da versão 0.14.1, antes da retirada do ataque automático:

- **63 verificações específicas** de ataque automático e **nove suítes PlayMode** aprovadas, incluindo a confirmação de golpes pelo servidor, cancelamento pelo chão, troca durante recarga, morte/respawn e reconexão com dois jogadores.
- Aplicativo macOS **0.14.1** compilado e com assinatura local verificada. Nenhuma alteração no código do servidor ou no schema do banco. Os dois personagens existentes mantiveram o estado completo idêntico por hash.

Resultados históricos da versão 0.14:

- Servidor nativo: **284 verificações**, mais **40 verificações HTTP** em banco descartável, incluindo calendário do chefão, drops, autoridade do combate e painel administrativo.
- Unity: **223 verificações** de arte/rig, **15** de perigo por nível e **11 suítes PlayMode** aprovadas: alvo, aproximação bloqueada, mira/impacto tardio, combate, locomoção, monstros, habilidades, aparência dos equipamentos, cidade/vale, cliente online e equipamentos de dois jogadores.
- Servidor local atualizado, com **10 perfis** de monstros e a agenda de Grond instalada. Comparação por hashes confirmou os **dois personagens** existentes sem alterações de progresso, patrimônio, nome ou posição; schema 2 mantido, sem novo backup de banco.
- Aplicativo macOS **0.14.0** gerado; assinatura local verificada e inicialização nativa sem interface concluída sem erros críticos. As capturas Unity de `docs/previews/fronteira` foram revisadas; esta entrega não mede FPS nem carga de muitos jogadores.
- A inspeção visual do painel no navegador não foi executada: a revisão de permissões recusou a origem de teste. As verificações HTTP do painel passaram.

Resultados históricos da versão 0.13:

- Unity: **46** verificações de menu, **38** de lista/nicks e **72** de gráficos; total **156**.
- Fluxo online Unity/HTTP: **23** verificações adicionais, incluindo consulta real de disponibilidade, recusa de nick duplicado, conexão e troca de personagens; total **179** verificações Unity.
- Servidor: **233** verificações, incluindo **29** de nicks, normalização, concorrência e preservação de acesso.
- Integração HTTP: **36** verificações independentes em dados temporários.

- Build macOS **0.13.0** concluído com ícone configurado nos oito tamanhos de desktop. O aplicativo final passou em **78 verificações** com dois clientes, polling por mais de 15 segundos, fechamento/reabertura e preservação de atributos, mochila e equipamento removido.
- **Guerra das Nações.app** foi executado e abriu a seleção pela abertura nativa do macOS. A inspeção visual conferiu o menu e o novo fluxo foi usado em execução nativa; a ferramenta de mouse não forneceu uma conferência manual estável de todos os controles gráficos. Resolução/modo foram verificados nos testes com confirmação e reversão; confira-os na sua tela pelo roteiro abaixo.
- Banco principal íntegro e com backup: o personagem anterior foi preservado com o estado completo idêntico. **Bc05**, criado enquanto a atualização era conferida, também foi preservado: nível **12**, **350 XP** acumulados no nível, **257 Coroas**, **13 itens** e os **1.295 recibos** de comandos. A identidade anterior foi mantida; Bc05 ocupa um novo perfil local, sem substituir o principal. A importação tem lançamento de saldo e auditoria próprios, com snapshot e histórico detalhado privados. Monstros, configurações e histórico de recompensas do mundo principal foram preservados.

Para conferir manualmente: abra o atalho, selecione Bc05 ou o personagem anterior e conecte. Em **Esc → Gráficos**, aplique Baixo e Alto, mude a resolução e deixe a contagem expirar para confirmar a restauração; depois confirme uma resolução e reabra o jogo para verificar a preferência salva. Em **Novo personagem**, tente um nick já existente e confirme a recusa. Os testes do servidor também podem ser repetidos por **VerificarServidor.command**, usando bancos temporários.

As contagens do histórico 0.12 abaixo não representam uma nova execução nesta versão.

Referências das APIs: [Unity 6.3 — QualitySettings](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/QualitySettings.html), [Screen.SetResolution](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Screen.SetResolution.html), [MSAA](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/QualitySettings-antiAliasing.html) e [FullScreenMode](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/FullScreenMode.html). MSAA depende de renderização Forward e do suporte da GPU; os modos de janela variam por plataforma.

---

## Histórico de versões anteriores

As seções a seguir registram o comportamento e a evidência das entregas anteriores. O menu, a conexão manual e as configurações descritos acima prevalecem na versão 0.13. Arquivos ZIP de versões antigas são referências históricas; o pacote desta entrega é `Fontes-0.13.zip`.

# Guerra das Nações — protótipo 0.12.0

Esta versão integra economia autoritativa, equipamentos por instância, drops, cidade e administração local. Fontes e assets importados do projeto Unity: `Fontes-0.12.zip`. O pacote inclui os modelos/texturas necessários ao protótipo, sem builds, caches, identidades ou bancos de personagens. O servidor preserva os personagens da versão 0.11: nível, experiência, atributos distribuídos, mochila e equipamentos, incluindo espaços vazios. O kit antigo fica vinculado e não pode ser vendido para gerar moedas gratuitas.

## Jogar

1. Abra `Jogar.command`. O servidor local e o aplicativo são iniciados juntos.
2. Nas ruínas, aproxime-se do portal azul de retorno à cidade (perto do nascimento) e pressione **E** ou clique em **Interagir**.
3. Na Cidade da Fronteira, aproxime-se dos comerciantes e use **E**. Ferreiro: espada/escudo; armeira: armaduras; joalheira: acessórios; alquimista: poções; mercador: vendas, sucata e recuperação de saques.
4. Derrote monstros nas ruínas para obter **Coroas**, sucata e chances de equipamentos. As moedas são creditadas pelo servidor. Saques pessoais aparecem em pequenos baús e podem ser coletados por proximidade. **B** abre as recompensas pendentes; o mercador permite recuperá-las após desconexão ou mochila cheia.
5. **F** usa uma poção de vida comprada. A poção cura até 50% da vida máxima, consome uma unidade e não é gasta com vida cheia. **I** abre equipamentos/mochila; **C** abre atributos.

O equipamento exibido no personagem acompanha as peças equipadas. Novos personagens recebem apenas as doze peças de treinamento e começam sem moedas. Personagens antigos mantêm seu patrimônio. A mochila comporta 128 itens, com páginas no painel.

## Administrar

Abra `AbrirPainelAdmin.command`. O painel fica em **http://127.0.0.1:8088/admin** e aceita acesso somente no próprio computador. A chave individual é gerada em `Server/data/admin-secret.txt`, com acesso restrito ao usuário local. Não envie essa chave pelo chat, não a publique e não a inclua no cliente Unity. O comando abre também a pasta para localizar o arquivo.

O painel permite editar equipamentos e requisitos, vida/dano/XP dos monstros, tabelas de drop, localização de NPCs e ofertas. Você pode salvar um rascunho, validar, simular, revisar as diferenças e publicar imediatamente ou agendar. A reativação de uma versão anterior é auditada e preserva itens/moedas já entregues.

As probabilidades são separadas: chance do grupo e peso de cada item dentro dele. O simulador mostra a chance final por morte e quantidades esperadas; não garante recompensas em um número fixo de tentativas. Aparências usam somente os modelos e materiais instalados no aplicativo; arquivos 3D novos exigem atualização do cliente.

## Validação desta entrega

- Unity 6000.3.26f1: build macOS 0.12.0 concluído; validadores de combate, equipamentos, migração e economia passaram.
- Servidor compilado: 171 verificações de regras, transações, concorrência, recuperação, drops, mapas e administração passaram.
- Integração HTTP: 50 verificações passaram usando uma cópia do banco real v1, incluindo migração, permissões, publicação, reativação e reinício.
- Aplicativo compilado: dois clientes simultâneos e reabertura de um perfil; 78 verificações passaram, incluindo peças removidas e pontos preservados.
- Aparência e painel de equipamentos: testes PlayMode históricos adaptados para uma fixture legada explícita e executados com aprovação. Testes online antigos foram atualizados para os doze itens iniciais; a validação online executada nesta entrega é a nova suíte de economia e os dois clientes compilados.
- Cidade no PlayMode: NPCs com malhas, piso clicável, lojas, recusa de saldo insuficiente, portais e separação entre mapas passaram.
- Painel no navegador: login, edição de drop, salvamento, revisão, publicação e simulação validados em servidor isolado. Painel principal aberto sem publicar alterações de teste.
- Migração do banco principal: integridade SQLite confirmada e preservação de um personagem, 35 itens, nível, XP, pontos e espaços equipados/vazios. Backups anteriores mantidos.
- A consulta de vulnerabilidades do NuGet ficou indisponível (NU1900), inclusive na repetição com rede; compilação e testes usaram as dependências fixadas no arquivo de lock. Isso não representa uma auditoria de segurança concluída.
- Interface web orientada pelos critérios de foco visível e contraste da WCAG 2.2, consultados em 18/10/2026: https://www.w3.org/WAI/WCAG22/Understanding/focus-visible.html e https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html. Não foi feita certificação integral de acessibilidade.

## Dados e validação

- Banco principal: `Server/data/characters.db`. Não apague essa pasta para atualizar o jogo.
- A migração cria automaticamente um backup consistente `characters.db.pre-v2-*.bak` antes de atualizar o esquema. A entrega também mantém uma cópia anterior em `Server/data/backups/`.
- Identidades dos jogadores permanecem no diretório de dados local do aplicativo, fora dos fontes e do pacote de entrega.
- Execute `VerificarServidor.command` para testes de servidor e integração HTTP em bancos temporários. Os testes não usam o personagem principal.
- Os números iniciais precisam de testes de balanceamento. A experiência do protótipo ainda sobe rapidamente; não representa uma economia de produção aprovada.
- Este é um servidor local de protótipo. A administração remota permanece bloqueada; hospedagem pública exige configuração de contas, TLS, operação e capacidade para a escala desejada.

O protocolo agora é 2. Cliente e servidor devem ser atualizados juntos. Após começar a obter itens repetidos/moedas, uma versão antiga não consegue representar o patrimônio novo; uma recuperação deve usar a versão atual e os backups correspondentes.

---

# Guerra das Nações — protótipo 0.11

Protótipo com servidor local, persistência de personagem e um mapa compartilhado entre vários clientes. A versão 0.11 reorganiza os equipamentos em uma janela vertical com espaços anatômicos e mochila em grade, e faz a remoção das peças aparecer no herói e nos jogadores remotos. O corpo com roupa básica permanece visível sem armadura. Preserva combate, progressão, áudio e salvamento de nível, XP, pontos, mochila e equipamentos. É uma base multiplayer pequena, sem hospedagem pública ou capacidade de MMORPG já demonstrada. Cabal continua sendo referência artística.

Créditos, licenças, fontes e alterações estão em [ThirdPartyNotices.md](ThirdPartyNotices.md). A cópia de código, servidor, configuração, documentação, fonte OBJ do corpo e arquivos de áudio OGG/WAV está em [Fontes-0.11.zip](Fontes-0.11.zip); os modelos e texturas binários completos continuam nas pastas do projeto. Credenciais e bancos de jogadores não entram nesse arquivo.

## Jogar

Abra **Jogar.command**. Ele inicia o servidor local em `127.0.0.1:8088`, se necessário, e abre o cliente com o perfil **principal**. O aplicativo aguarda carregar o personagem antes de liberar os comandos. Ao sair e voltar pelo mesmo perfil, continua com o progresso confirmado pelo servidor. O aplicativo incluído é para teste no macOS; a plataforma final continua sendo PC, sem build Windows nesta entrega.

Use **JogarSegundoCliente.command** para abrir outra instância, com identidade e personagem independentes. Os dois clientes compartilham o mesmo mapa e a vida dos três trolls. Outros jogadores aparecem como cavaleiros com nome, vida, equipamentos e animação. A experiência pertence a quem dá o último golpe; ainda não há grupo ou divisão de XP.

**IniciarServidor.command** permite executar o servidor no Terminal. **PararServidor.command** solicita encerrar somente o servidor iniciado pelo lançador. Fechar um cliente não encerra o servidor. O progresso já confirmado permanece no SQLite mesmo após encerrar os dois programas. Um servidor aberto manualmente no Terminal pode ser encerrado com Ctrl+C.

Para editar, abra `AbrirNoUnity.command` e a cena `Assets/Scenes/Patio.unity`. Use Unity 6000.3.26f1. No Editor, Play usa **modo de teste offline**, sem persistência, para manter os checks independentes; o aplicativo compilado usa online por padrão. Os fontes Blender continuam em `ArtSource`.

Se esta pasta estiver no iCloud Drive, use **Manter Baixado** no menu de contexto do Finder para evitar pausas ao abrir modelos, texturas e arquivos do projeto. Consulte o [procedimento da Apple](https://support.apple.com/en-gb/guide/mac-help/mchl1a02d711/27/mac/27). A compilação 0.11 foi concluída a partir de uma cópia local com todos os recursos recuperados.

| Controle | Ação |
|---|---|
| WASD / setas | Mover e orientar |
| Clique no chão | Caminhar até o ponto e cancelar um golpe ainda pendente |
| Shift | Correr |
| Scroll | Aproximar ou afastar a câmera |
| Q / E | Girar a câmera |
| 1 / Espaço | Um básico por acionamento; aproxima até o alvo selecionado quando necessário |
| Botão direito | Um básico por clique; com alvo, aproxima até ele; sem alvo, orienta pelo cursor |
| 2 | Um Corte Pesado por acionamento: onda circular; aproxima até o alvo quando necessário |
| 3 | Guarda de Ferro |
| 4 | Segundo Fôlego |
| 5 | Fúria Arcana: bônus temporário de dano |
| C | Abrir/fechar o painel de atributos |
| Botões + no painel C | Investir um ponto pelo mouse |
| F6 / F7 / F8, com o painel aberto | Distribuir 1 ponto em Ataque / Defesa / Inteligência |
| I / botão Equipamentos | Abrir/fechar equipamentos e mochila |
| Mouse no painel I | Selecionar espaço e item, comparar, substituir ou remover |
| Tab / clique esquerdo no inimigo | Selecionar e consultar vida, sem mover ou atacar; cancela um golpe ainda pendente |
| Esc | Fechar painel/ajuda; sem janela aberta, limpar seleção |
| H / botão ? | Mostrar/ocultar ajuda |
| O / botão Som | Abrir/fechar volume geral, música, efeitos e silêncio |
| R, online | Renascer após derrota; preservar progresso e não reiniciar os monstros compartilhados |
| R, teste offline | Restaurar o confronto local e preservar progresso desta sessão de teste |
| F12, build de desenvolvimento | Captura completa em Capturas/Previa.png na pasta de dados do aplicativo |

## Combate e poderes

No modo online, o servidor valida movimento, alcance, obstáculos, mana, recargas, dano e morte dos monstros. O cliente envia comandos; não envia um novo nível, XP ou inventário para o servidor aceitar. O mapa usa uma grade de colisões exportada da cena Unity, com simulação plana e tick de 10 Hz. Isso permite testar poucos jogadores; a física e a rede ainda são de protótipo.

Cada acionamento pede um único golpe; segurar `1`, Espaço ou botão direito não o repete. A seleção sozinha apenas mostra as informações do inimigo. Com um alvo selecionado, pedir um golpe orienta o herói até ele e aproxima somente se estiver fora do alcance; ao chegar, executa esse golpe e aguarda outro comando. Sem alvo, `1` e Espaço atacam à frente, e o botão direito guarda a direção do cursor enquanto o herói gira para atacar. Trocar a seleção, clicar no chão, mover manualmente ou abrir um painel cancela o pedido ainda não executado. Um golpe já enviado conserva a direção e o comando originais, sujeitos à confirmação do servidor.

| Atalho | Habilidade | Efeito no nível 1, antes de distribuir pontos | MP | Recarga |
|---|---|---|---|---|
| 1 / Espaço | Ataque Básico | 18 por inimigo no arco frontal de 130°, até 2,65 m | 0 | 0,65s |
| 2 | Corte Pesado | 32 por inimigo ao redor, 360°, até 3,15 m | 16 | 5s |
| 3 | Guarda de Ferro | Reduz dano em 50% por 4 s | 20 | 12s |
| 4 | Segundo Fôlego | Cura 35% da vida máxima; exige vida incompleta | 25 | 15s |
| 5 | Fúria Arcana | Multiplica o dano de básico e pesado por 1,25 durante 8 s | 25 | 20s |

A barra tem oito espaços; 6–8 estão livres. Mana máxima 100, regeneração 8 MP/s enquanto vivo e intervalo global 0,20 s. Básico e pesado compartilham a recarga do ataque de 0,65 s. Golpes no vazio são válidos e usam recarga e custo normais. Usos recusados por morte, desativação do combate, recarga ou mana insuficiente não gastam recursos. Ainda não há remapeamento de teclas ou arraste de habilidades.

Guarda de Ferro mantém uma aura azul e uma runa de proteção enquanto está ativa. Fúria Arcana mantém energia âmbar ao redor do herói e na espada; não acumula consigo mesma, mas pode coexistir com Guarda. Segundo Fôlego mostra energia verde por 1,2 s após uma cura válida. A inteligência prolonga Guarda e Fúria a partir de suas durações base de 4 e 8 s; a animação curta da cura não muda. Expiração, morte, reinício e desativação removem os efeitos correspondentes. Recargas e custos não diminuem com atributos.

O dano é resolvido 0,13 s após o início do golpe, próximo à passagem da lâmina. A posição, direção e dano são fixados no início, incluindo atributos, equipamento e bônus ativo naquele instante. Ganhar nível, distribuir pontos, trocar ou remover a espada e encerrar Fúria durante esse intervalo não altera retroativamente o golpe emitido. Básico e Corte Pesado exigem espada equipada para iniciar; uma tentativa sem espada não gasta mana nem inicia recarga. Guarda é uma magia e funciona sem escudo. A duração de cada bônus também é fixada na ativação: mudar a inteligência por pontos ou equipamento só altera ativações futuras. Cada inimigo recebe dano uma única vez por execução; paredes bloqueiam o dano. Morte, reinício e desativação cancelam impactos pendentes. O efeito combina o rastro real de SwordBase/SwordTip com uma varredura frontal ou onda circular; faíscas de impacto exigem acerto.

O encontro tem dois trolls comuns e um elite:

| Perfil | Altura | Vida | Dano por golpe | XP por vitória |
|---|---|---|---|---|
| Troll Saqueador, dois exemplares | 3,2 m | 240 HP | 10 | 120 |
| Troll Ancião · Elite | 3,9 m | 600 HP | 16 | 240 |

Começam passivos; ao serem atingidos, perseguem e preparam o revide por **0,65 s**. O troll firma os pés, gira o tronco e ergue os braços; um aviso no chão ajuda a perceber a área ameaçada. A descida dos braços termina no contato e é seguida pela recuperação da postura. O golpe combina movimento, rastro, poeira e pequenos fragmentos de pedra. O Ancião elite tem impacto mais forte e fissuras visuais no solo. Esses detalhes são apresentação: as fissuras não criam uma nova área de dano nem aumentam os valores da tabela.

Afastar-se durante a preparação pode evitar o impacto. Alcance e obstáculos são reavaliados no momento do golpe. Uma preparação cancelada não gera dano nem efeito de acerto; morte, retorno e desativação limpam a apresentação pendente. Online, o efeito final depende do evento confirmado pelo servidor, com identificador para impedir repetição; o desaparecimento de uma preparação no snapshot não é interpretado como um golpe. Reconectar não reproduz ataques antigos.

Ao ultrapassarem a área de perseguição de 8 m, retornam e recuperam a vida sem conceder XP. Reaparecem em 6 s após morrer. Os perfis são configuráveis por inimigo; o reinício restaura a vida máxima correspondente e uma morte concede XP apenas uma vez.

O personagem começa com 100 HP e 18 de dano base. O nível 2 exige 100 XP e concede 120 HP/22 de dano base. Derrotar os três trolls concede 480 XP ao todo: partindo do nível 1 sem experiência, o personagem chega ao nível 4 com 30/250 XP, 160 HP e 30 de dano base. Os atributos e encantamentos modificam o resultado a partir dessa base. Recompensas de um mesmo impacto são somadas em um aviso, preservando a mensagem de evolução. Cada nível acrescenta 20 HP e 4 de dano base; limite 50.

## Som e música ambiente

Use **O** ou o botão **Som** para abrir o painel. Há três controles independentes: **Volume geral**, **Música ambiente** e **Efeitos de combate**, além de **Silenciar todo o som** e um botão para restaurar os padrões. Os valores iniciais são 100% geral, **8% música** e **60% efeitos**. As preferências ficam localmente no aplicativo, são mantidas ao reabrir e não alteram o personagem nem seu banco no servidor. Abrir o painel bloqueia os comandos do herói; o mundo e os inimigos continuam ativos.

A trilha **A Kind Of Hope**, de Scott Buckley, toca inteira em loop, com entrada suave e volume discreto. O OGG usa streaming, evitando carregar a música inteira descomprimida na memória. Um vento baixo complementa o ambiente. A trilha foi ajustada para −23 LUFS, com pico medido de −5,8 dBFS; o controle de música em 8% reduz ainda mais seu volume durante o jogo.

Há variações de som para a espada, impactos, preparação/ataque/dor/morte dos monstros, passos, proteção, cura, Fúria e evolução. As vozes dos monstros maiores usam um tom mais grave. Os efeitos são posicionados no mundo e atenuados pela distância, com limite de 24 vozes, intervalos por categoria e margem dinâmica para evitar saturação quando vários golpes coincidem. Os sons não decidem dano nem recompensas. O projeto inclui 34 clipes: 32 efeitos, música e vento.

**Crédito da música:** 'A Kind Of Hope' by Scott Buckley - released under CC-BY 4.0. www.scottbuckley.com.au. [Página oficial](https://www.scottbuckley.com.au/library/a-kind-of-hope/) · [licença CC-BY 4.0](https://creativecommons.org/licenses/by/4.0/). Ao publicar um vídeo ou transmissão com essa trilha, mantenha também a atribuição na descrição, conforme a orientação do autor.

Os efeitos utilizam [Kenney Impact Sounds](https://kenney.nl/assets/impact-sounds), [Kenney RPG Audio](https://kenney.nl/assets/rpg-audio) e [Ogrebane — Monster Sound Pack, Volume 1](https://opengameart.org/content/monster-sound-pack-volume-1), publicados como CC0, além de camadas sintetizadas para este protótipo. As adaptações, licenças e hashes estão em [ThirdPartyNotices.md](ThirdPartyNotices.md) e `Unity/Assets/Audio/`. Os recursos reproduzidos ficam em `Unity/Assets/Resources/Audio/`.

## Atributos e multiplicadores

O herói recebe **5 pontos livres na criação e mais 5 por nível obtido**, totalizando 250 pontos no nível 50. Use **C** para abrir o painel e os botões **+**, ou **F6/F7/F8**, para investir um ponto em Ataque, Defesa ou Inteligência. A alocação é imediata, exige saldo e não cura nem ressuscita o personagem. Não há redistribuição nesta versão.

O painel mostra pontos livres e atributos como **distribuídos + equipamentos = efetivos**, além do dano calculado e dos bônus ativos. Enquanto atributos, equipamentos, som ou ajuda estiverem abertos, movimento, giro/zoom da câmera e comandos de ataque ficam bloqueados; o mundo continua atualizando, incluindo inimigos, dano recebido, recargas e duração das habilidades. Portanto, abrir um painel não pausa a batalha.

As regras estão centralizadas no núcleo C# sem dependência do Unity: `PlayerProgression.cs`, `CombatScaling.cs`, `WeaponRequirements.cs` e `Equipment*.cs`. Considere `A`, `D` e `I` como atributos efetivos, somando pontos distribuídos e bônus equipados; `B = 18 + 4 × (nível − 1) + bônus de dano base dos equipamentos`:

| Cálculo | Fórmula |
|---|---|
| Dano básico | `B × (1 + 0,02 × A) × bônus de dano ativo` |
| Dano de habilidade | `B × poder × (1 + 0,01 × A) × bônus mágico, se aplicável × bônus de dano ativo` |
| Bônus mágico | `1 + 0,015 × I` |
| Duração de magia | `duração base × (1 + 0,005 × I)` |
| Dano recebido | `dano bruto × 100 / (100 + 2 × D) × proteção ativa` |

Ataque aumenta golpes básicos em 2% por ponto e habilidades em 1% por ponto. Inteligência aumenta dano de habilidades mágicas em 1,5% por ponto e duração mágica em 0,5% por ponto. Defesa usa redução com retorno decrescente: 10 pontos reduzem o dano recebido em aproximadamente 16,7%; não significam 20% de redução.

Fúria usa multiplicador **1,25** nos ataques; Guarda usa **0,5** no dano recebido. Os multiplicadores são compostos antes de um único arredondamento: metade arredonda para cima, golpes positivos causam no mínimo 1, e o dano aplicado nunca supera a vida restante. Valores inválidos não causam dano e cálculos excessivos são limitados sem overflow de inteiros.

**Corte Pesado é uma habilidade arcana de poder 1,8.** Usa a base `B`, o bônus de 1% por ponto de Ataque e o bônus mágico da Inteligência. Não reutiliza o dano básico já aumentado em 2%, evitando aplicar o Ataque duas vezes.

Exemplos no nível 2, investindo os mesmos 10 pontos, sem Fúria ou Guarda:

| Distribuição | Dano básico | Corte Pesado | Guarda / Fúria | Dano recebido de um golpe bruto de 40 |
|---|---:|---:|---|---:|
| 10 Ataque | 26 | 44 | 4 s / 8 s | 40 |
| 10 Defesa | 22 | 40 | 4 s / 8 s | 33 |
| 10 Inteligência | 22 | 46 | 4,2 s / 8,4 s | 40 |

No aplicativo online, nível, XP, pontos, mochila e equipamentos são mantidos no servidor ao morrer, renascer, fechar e reabrir. O modo offline do Editor continua descartável e não transfere seu progresso para o servidor.

## Salvamento e identidade online

O banco oficial é **Server/data/characters.db**. Ele guarda personagens separados por identidade, além do estado do mundo, HP, mana, posição, recargas e efeitos temporários. O servidor usa o mesmo código C# de atributos/equipamentos que o cliente, recalculando bônus pelo catálogo. O snapshot tem versão; dados corrompidos, versões desconhecidas, itens duplicados ou pontos incompatíveis com o nível são recusados sem substituir o personagem por um novo.

As alterações são confirmadas após a transação SQLite. O indicador mostra **enviando…** enquanto há comandos pendentes; aguarde a confirmação antes de fechar. Não é necessário clicar em Salvar. Se a conexão falhar, os controles são bloqueados e o cliente tenta retomar o transporte; ele não começa a gravar progresso offline. Uma sessão substituída ou um servidor reiniciado pede **Reconectar**, usando a mesma identidade. Reenviar uma ação com o mesmo UUID não repete o gasto ou a recompensa.

Nesta etapa, a identidade é **anônima por perfil**, criada uma vez com 256 bits aleatórios. O cliente guarda apenas a credencial em `Application.persistentDataPath/Online/<perfil>/identity.json`; o servidor guarda seu hash. No macOS/Linux, pasta e arquivo têm permissões 0700/0600. Não compartilhe nem inclua esse arquivo em versões do projeto: ele permite acessar o personagem. Apagá-lo perde o vínculo com esse personagem; não há recuperação por e-mail ou senha ainda. Para jogar em outro computador com uma conta recuperável, falta integrar autenticação de conta.

Para cópia de segurança, preserve a credencial privada do perfil separadamente e o banco do servidor. Com o servidor encerrado, copie a pasta **Server/data** inteira. Com ele em execução, use backup consistente do SQLite; copiar somente o `.db` pode omitir transações no WAL. Atualizar o aplicativo não deve excluir `Server/data` nem as identidades do cliente. A versão 0.8 não tinha salvamento e não possui um arquivo anterior para importar.

O servidor padrão fica acessível apenas neste Mac. Outro destino pode ser informado com `--server=https://host:porta`; `--profile=nome` seleciona outra identidade local. Para testar LAN, há opções explícitas de HTTP no cliente e servidor, descritas em [Server/README.md](Server/README.md). Não exponha esse modo HTTP à internet. Hospedagem pública, TLS e autenticação de conta ainda não foram configurados.

## Equipamentos, mochila e requisitos

Abra **I** ou clique em **Equipamentos e mochila** no quadro do herói. A janela à direita dispõe os 12 espaços ao redor de uma silhueta. A mochila abaixo mostra todos os itens em uma grade de sete colunas. Selecione um espaço ou uma peça e confira, no comparativo à esquerda, os valores atuais e após a troca. **Substituir peça/Equipar peça** aplica a troca; **Remover peça** devolve o item à mochila. Os espaços são espada, escudo, elmo, ombreiras, peitoral, luvas, calças, botas, dois anéis, cinturão e colar. Os botões funcionam pelo mouse, sem necessidade de arrastar.

O herói começa com 12 peças de Treinamento e 23 variantes na mochila, para experimentar o sistema sem uma loja ou drops. O conjunto de treino tem bônus adicionais zero, preservando os 18 de dano e 100 HP iniciais; Aço e Rúnico têm atributos próprios. Cada item possui identificador, espaço compatível, bônus, requisitos e acabamento. A peça substituída volta à mochila; a candidata sai dela. Cada instância só pode estar em um local, inclusive os anéis: para mudar um anel entre os dois espaços, remova-o e equipe no outro.

| Exemplos disponíveis | Bônus | Requisito especial |
|---|---|---|
| Espada de Aço | +2 Ataque, +4 dano base | 2 pontos distribuídos em Ataque; proficiência em espada 1 |
| Escudo de Aço | +3 Defesa, +12 HP máximo | Nível 1 |
| Peitoral de Aço | +3 Defesa, +24 HP máximo | Nível 1 |
| Manoplas de Aço | +2 Ataque | Nível 1 |
| Anel da Força / Sabedoria | +2 Ataque / +2 Inteligência | Nível 1 |
| Cinturão da Vitalidade | +20 HP máximo | Nível 1 |
| Colar do Aprendiz | +2 Inteligência, +5 HP máximo | Nível 1 |
| Lâmina Rúnica | +4 Ataque, +3 Inteligência, +9 dano base | Nível 3, 6 Ataque, 3 Inteligência, proficiência 1 |
| Peitoral Rúnico | +6 Defesa, +2 Inteligência, +40 HP máximo | Nível 3, 4 Inteligência |
| Espada do Veterano | +8 Ataque, +14 dano base | Nível 3, 6 Ataque, proficiência 2 |

Requisitos usam **pontos distribuídos**, nível e proficiência, sem contar bônus de equipamentos. Assim, Manoplas de Aço não liberam sozinhas a Espada de Aço. A proficiência em espada é distinta dos atributos e ainda permanece em 1; a Espada do Veterano serve para demonstrar a recusa explicada de um requisito ainda não alcançável. As demais peças rúnicas permitem experimentar requisitos por nível e atributos. A recusa mantém equipamento, mochila e bônus anteriores intactos, exibindo o que falta.

Os bônus são somados uma única vez: Ataque/Defesa/Inteligência entram nos multiplicadores; dano base adicional entra antes deles; HP adicional soma à vida do nível. Equipar HP **não cura nem ressuscita**; remover uma peça limita a vida atual ao novo máximo, sem recuperar o excesso ao reequipar. Cura, evolução e R consideram o máximo equipado. No nível 1, com 2 Ataque distribuídos e Espada de Aço, o herói tem 4 Ataque efetivo, base 22 e básico 24. Com Anel da Sabedoria, o pesado causa 42; Fúria eleva a 53, com um único arredondamento.

Espada, escudo e as seis partes da armadura desaparecem ao remover e reaparecem ao equipar. Um corpo anatômico com roupa básica cobre as regiões expostas, e a cabeça aparece sem o elmo. Os jogadores remotos usam as mesmas peças e o estado confirmado pelo servidor; abrir o painel ou selecionar uma candidata não altera a roupa do herói. As variantes Aço/Rúnico ainda compartilham a geometria de cada peça e mudam seu acabamento. Cinturão, colar e os dois anéis têm geometria própria removível. Mochila/equipamentos persistem no servidor. Loot e comércio ainda não foram implementados.

A composição segue o padrão solicitado de inventário MMORPG; os ícones, molduras e silhueta da interface foram desenhados para o protótipo. A interação mantém I/Esc e botões de ação explícitos, sem exigir arrastar itens. Orientações consultadas em 18/10/2026: [Apple HIG — Game controls](https://developer.apple.com/design/human-interface-guidelines/game-controls), para atalhos de uma tecla e suporte a mouse/teclado; [Apple HIG — Accessibility](https://developer.apple.com/design/human-interface-guidelines/accessibility), para contraste e legibilidade. As páginas não indicam uma versão numerada. O [canal oficial de atualizações](https://developer.apple.com/design/whats-new/) também foi conferido; a estética MMORPG é uma escolha deste projeto.

## Arte e apresentação

- **Corpo sem armadura:** derivado da base anatômica oficial MakeHuman (CC0), adaptado aos mesmos 17 ossos. Usa túnica, calça e forro dos pés; regiões cobertas deixam de ser renderizadas para não atravessar as placas. A cabeça aparece sem o elmo. Fontes e licença em `ArtSource/`; modelo ativo em `Unity/Assets/Art/Equipment/BaseHero.fbx`.
- **Cavaleiro:** modelo Knight de piacenti, com as UVs e texturas do autor, armadura metálica e espada. A adaptação usa o esqueleto de 17 ossos do protótipo e sete clipes Mixamo: Idle/Walk/Run, três básicos e um golpe pesado. O modelo ativo é `Unity/Assets/Art/Cavaleiro.fbx`; a fonte editável é `ArtSource/Cavaleiro.blend`.
- **Troll:** modelo Troll Mauler de piacenti, malha de 6.370 triângulos e esqueleto de 33 ossos, pele com mapas de cor/normal/oclusão, vestimenta e anatomia esculpida. O modelo foi normalizado para 3,2 m; o elite usa escala de 3,9 m e variação de material. Arquivos ativos: `Unity/Assets/Art/Troll.fbx` e `ArtSource/Troll.blend`.
- **Ambiente:** mantém a arena caminhável de 35 × 35 m com ruínas e arcadas. Integra seis modelos de rochas com musgo da Poly Haven e materiais PBR 2K de alvenaria, piso e solo; um HDRI fotográfico fornece céu e reflexos. Os arquivos ficam em `Unity/Assets/Art/ThirdParty/Environment/`, com fontes e manifestos em `ArtSource/ThirdPartyEnvironment/`.
- **Materiais e câmera:** mapas de cor, normal, oclusão e metalicidade/smoothness no shader Standard; câmera com órbita e zoom, sombras, oclusão de contato, brilho de energia e mapeamento de tons no pipeline Built-in.

As animações do cavaleiro foram adaptadas ao esqueleto do protótipo. O troll preserva a ação idle original no arquivo e usa movimentos procedurais de braços e pernas no combate. Essas adaptações ainda precisam de refinamento; não representam um conjunto final de animações com captura de movimento.

Os scripts `ArtSource/integrar_cavaleiro.py` e `ArtSource/preparar_troll.py` registram as adaptações. Arquivos `Personagem.*`, `Monstro.*` e os geradores procedurais antigos permanecem como fontes históricas; a cena atual usa **Cavaleiro.fbx** e **Troll.fbx**. A versão 0.8 divide a malha do cavaleiro em partes de acabamento e escudo, por `PrototypeEquipmentAppearanceEditor`, mantendo vértices, UVs, pesos, topologia e os marcadores da espada. A preparação do Unity recria a cena Patio; preserve edições manuais antes de executar **Preparar protótipo**.

Os fontes de entrada dos personagens ficam em `ArtSource/ThirdParty/`. Para repetir a adaptação, execute a partir da raiz deste projeto:

```zsh
'/Applications/Blender.app/Contents/MacOS/Blender' --background --disable-autoexec --python-exit-code 1 --python "$PWD/ArtSource/integrar_cavaleiro.py"
'/Applications/Blender.app/Contents/MacOS/Blender' --background --disable-autoexec "$PWD/ArtSource/ThirdParty/TrollOriginal.blend" --python-exit-code 1 --python "$PWD/ArtSource/preparar_troll.py"
```

Knight e Troll Mauler usam **CC BY 3.0**, com atribuição e indicação das alterações; mantenha os créditos ao redistribuir os modelos e texturas. Os assets Poly Haven usam **CC0**. Consulte [ThirdPartyNotices.md](ThirdPartyNotices.md) para os termos e créditos completos. Nenhum modelo ou textura foi extraído do Cabal.

## Validação

O servidor usa o SDK oficial [.NET 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) e `Microsoft.Data.Sqlite`, com versões fixadas pelo lockfile. A escolha permite compilar os mesmos arquivos C# de progressão/equipamentos no Unity e no servidor. Consulte [Server/README.md](Server/README.md) para comandos, protocolo, banco e limites.

No Editor, os menus **Guerra das Nações** oferecem preparação, validação e checks. Feche o Editor antes dos comandos batch. Execute a partir da pasta `Unity`, um por vez:

```zsh
'/Applications/Unity/Hub/Editor/6000.3.26f1/Unity.app/Contents/MacOS/Unity' -batchmode -quit -projectPath "$PWD" -executeMethod PrototypeSetup.PrepareAndValidate -logFile "$PWD/preparacao.log"
'/Applications/Unity/Hub/Editor/6000.3.26f1/Unity.app/Contents/MacOS/Unity' -batchmode -quit -projectPath "$PWD" -executeMethod ProgressionPersistenceChecks.Run -logFile "$PWD/persistencia.log"
'/Applications/Unity/Hub/Editor/6000.3.26f1/Unity.app/Contents/MacOS/Unity' -batchmode -quit -projectPath "$PWD" -executeMethod OnlineClientRulesChecks.Run -logFile "$PWD/identidade-online.log"
'/Applications/Unity/Hub/Editor/6000.3.26f1/Unity.app/Contents/MacOS/Unity' -batchmode -projectPath "$PWD" -executeMethod CombatPlayModeChecks.Run -logFile "$PWD/combate.log"
'/Applications/Unity/Hub/Editor/6000.3.26f1/Unity.app/Contents/MacOS/Unity' -batchmode -projectPath "$PWD" -executeMethod AbilityPlayModeChecks.Run -logFile "$PWD/habilidades.log"
'/Applications/Unity/Hub/Editor/6000.3.26f1/Unity.app/Contents/MacOS/Unity' -batchmode -projectPath "$PWD" -executeMethod AttributeAbilityPlayModeChecks.Run -logFile "$PWD/atributos-habilidades.log"
'/Applications/Unity/Hub/Editor/6000.3.26f1/Unity.app/Contents/MacOS/Unity' -batchmode -projectPath "$PWD" -executeMethod EquipmentAppearancePlayModeChecks.Run -logFile "$PWD/aparencia-equipamentos.log"
'/Applications/Unity/Hub/Editor/6000.3.26f1/Unity.app/Contents/MacOS/Unity' -batchmode -projectPath "$PWD" -executeMethod EquipmentPanelPlayModeChecks.Run -logFile "$PWD/painel-equipamentos.log"
'/Applications/Unity/Hub/Editor/6000.3.26f1/Unity.app/Contents/MacOS/Unity' -batchmode -projectPath "$PWD" -executeMethod EquipmentPlayModeChecks.Run -logFile "$PWD/equipamentos-combate.log"
'/Applications/Unity/Hub/Editor/6000.3.26f1/Unity.app/Contents/MacOS/Unity' -batchmode -projectPath "$PWD" -executeMethod AudioPresentationChecks.Run -logFile "$PWD/audio-apresentacao.log"
'/Applications/Unity/Hub/Editor/6000.3.26f1/Unity.app/Contents/MacOS/Unity' -batchmode -projectPath "$PWD" -executeMethod MonsterAttackPlayModeChecks.Run -logFile "$PWD/ataques-monstros.log"
'/Applications/Unity/Hub/Editor/6000.3.26f1/Unity.app/Contents/MacOS/Unity' -batchmode -quit -projectPath "$PWD" -executeMethod PrototypeSetup.BuildMacOS -logFile "$PWD/build.log"
```

`PrepareAndValidate` inclui as regras puras de combate, equipamentos e persistência. Os checks PlayMode omitem `-quit` para aguardar suas etapas e encerram o processo com código 0 ou 1. A preparação também exporta a grade do servidor: 140×140 células de 0,25 m, com 1.708 células bloqueadas na cena atual e todos os pontos de nascimento livres.

`EquipmentOnlinePlayModeChecks.Run` e `OnlineClientPlayModeChecks.Run` exigem um servidor **isolado**, com banco vazio em outra pasta e porta 18090. A partir de `Server`, crie uma pasta temporária com `check_directory="$(mktemp -d /tmp/guerra-check.XXXXXX)"`; nesse mesmo Terminal, use `HOST=127.0.0.1 PORT=18090 DB_PATH="$check_directory/characters.db" .dotnet/dotnet bin/Release/net10.0/Server.dll`. Em outro Terminal, a partir de `Unity`, execute o Unity batch sem `-quit`, trocando o método por `OnlineClientPlayModeChecks.Run`. Não use o banco dos seus personagens para esse check; ele cria identidades de teste e altera os trolls.

Na versão **0.11.0**, passaram a preparação do projeto, as regras puras de combate/equipamentos/persistência e cinco checks PlayMode: aparência, painel, equipamentos online, regressão de equipamentos no combate e cliente online geral. A remoção individual dos 12 espaços preserva os 35 itens, revela as regiões do corpo correspondentes e não restaura peças padrão ao recarregar. A aparência local e remota usa o estado confirmado; recusa e reconexão também foram verificadas. As regiões dos ombros e da pelve cobrem as transições quando o peitoral é retirado.

O check do painel verificou a disposição dos 12 espaços e das 35 células, regiões de clique, seleção, comparação, ações e bloqueio modal em dimensões de 1440×900 e 1024×768. Exercita os mesmos handlers usados pelos botões. Foram geradas e inspecionadas 12 prévias da malha: conjunto completo e corpo sem armadura em Idle/Walk/Attack, quatro remoções isoladas, retirada de elmo/peitoral e detalhe do rosto. Os materiais e renderers são reutilizados; as trocas não criam novas instâncias.

O aplicativo macOS **0.11.0** foi compilado e passou em **78 verificações com dois processos reais**, incluindo presença simultânea por 16,5 s, reabertura da identidade, preservação de 2 pontos investidos/3 livres, espada substituída, seis espaços de armadura vazios e 35 itens únicos. Os testes usaram apenas banco, perfis e servidor isolados. O aplicativo e seu painel foram conferidos visualmente em 1440×900, com um personagem de teste sem as 12 peças. A automação de cliques nativos do Mac retornou `noWindowsAvailable` nessa janela; a operação com o ponteiro físico permanece no roteiro manual, enquanto handlers e limites dos botões foram verificados pelos checks. O servidor e o esquema do banco não foram alterados nesta versão.

Os resultados da versão 0.10 abaixo são históricos.

Na versão **0.10.0**, passaram a preparação do projeto, as regras de combate/equipamentos/persistência, o diagnóstico de skinning, os checks PlayMode de combate, habilidades, áudio, ataques dos monstros e cliente online. O troll passou a ser importado com esqueleto Generic, preservando os pesos das três malhas; a validação agora rejeita um troll rígido sem bindposes. O material do pano respeita os recortes da textura.

`AudioPresentationChecks.Run` verificou os 34 clipes, música em streaming, uma única fonte musical e um único AudioListener, efeitos no espaço do mundo, limites de volume/silêncio e painel modal. Os arquivos passaram na decodificação e verificação de picos PCM. `MonsterAttackPlayModeChecks.Run` verificou deformação real da malha, antecipação, alcance, evasão sem impacto falso, dano único, poeira/pedras, recuperação, morte, desativação e snapshots cancelados. O teste online confirmou um novo golpe autoritativo do troll, impacto sem duplicação e retomada sem reproduzir efeitos históricos. O servidor passou nas **102 verificações internas** e nos **9 casos de integração HTTP** desta versão.

O aplicativo macOS **0.10.0** foi compilado com sucesso e passou em **75 verificações com dois processos reais**, incluindo polling simultâneo por 16,5 s, identidades distintas, reabertura preservando atributos, espada substituída e 35 itens únicos. Esses checks usaram personagens e servidor isolados; os perfis temporários foram removidos. Na conferência visual do aplicativo, o painel abriu com música em 8% e efeitos em 60%; o modelo e o combate foram conferidos em uma sessão de teste. Os cliques nativos do controle remoto não ficaram disponíveis nessa janela, portanto a alteração dos sliders pelo mouse e a escuta em uma saída de áudio normal permanecem no roteiro manual abaixo. Os testes de áudio verificam reprodução/configuração, não são uma avaliação auditiva. Os resultados da versão 0.9 abaixo são históricos.

Na versão 0.9, a preparação e as regras de snapshots/identidade passaram. O teste real do cliente Unity passou com carregamento inicial, distribuição e troca sem alterar o estado antes da confirmação, reconexão com a mesma identidade, conservação dos 35 itens, outro jogador com avatar sem componentes de combate local, ataque em área com atributos/equipamentos e HP compartilhado entre as conexões. Também passou o bloqueio de comandos ao perder a conexão.

O aplicativo macOS **0.9.0** foi compilado com sucesso após a passagem final do cliente online. A publicação autônoma do servidor passou novamente nas 83 verificações internas.

O executável 0.9 compilado também passou em 72 verificações com dois processos reais: conexão simultânea por mais de 16 s, dados iniciais, identidade privada, distribuição/troca no personagem de teste e reabertura mantendo a mesma identidade, pontos, espada e 35 itens únicos. Os perfis temporários foram removidos ao fim. Houve cancelamento de requisição Curl 42 apenas durante o encerramento intencional dos processos, sem falha durante o uso.

O roteiro HTTP em banco isolado da versão 0.9 passou na publicação final, cobrindo: duas identidades simultâneas, equipamentos, mochila, pontos, idempotência, rejeição de XP arbitrário/duplicações/teleporte/acesso sem token, combate real concedendo 120 XP e nível 2 com 20 XP excedentes, reinício do processo preservando progresso, retransmissão após descartar a resposta e exclusão de um segundo processo no mesmo banco. As 83 verificações internas do servidor incluem falha de gravação com rollback, commit ambíguo recuperado do banco e concorrência. Esses resultados não são teste de carga de MMORPG.

Para conferir manualmente no aplicativo:

1. Abra **Jogar.command** e aguarde **Online**. Em C, distribua dois pontos em Ataque pelo mouse; em I, equipe a Espada de Aço. Aguarde desaparecer **enviando…**. O básico deve ser 24.
2. Feche o aplicativo e abra novamente pelo mesmo lançador. Nível, XP, pontos, mochila e espada devem voltar, sem nova concessão de cinco pontos ou de itens.
3. Abra **JogarSegundoCliente.command**. Os personagens devem aparecer um para o outro, com progressos independentes; os dois devem receber a mesma vida dos trolls durante o combate.
4. Com os clientes fechados, use **PararServidor.command** e volte pelo lançador. O servidor reinicia e os personagens continuam no mesmo banco.
5. Se o servidor ficar indisponível, os comandos devem bloquear. Retome a conexão pelo botão exibido, preservando a identidade e o progresso confirmado.
6. Provoque um troll e observe a preparação, os braços e o aviso no chão. Afaste-se antes do golpe para testar a evasão; aproxime-se de novo para conferir contato, recuperação e som. Compare o impacto do Ancião elite. Cada ataque válido deve aplicar dano uma única vez.
7. Em **I**, remova elmo, peitoral, luvas e botas: cada peça deve desaparecer, revelando rosto/roupa. Remova as demais peças e acessórios; a mochila deve conter os 35 itens. Reequipar restaura a peça. Feche e reabra para confirmar que os espaços vazios continuam vazios. Confira o mesmo personagem no segundo cliente.
8. Em **O · Som**, ajuste música, efeitos e volume geral; teste silêncio e restaure os padrões. Feche e reabra o aplicativo para conferir as preferências. A música deve ficar baixa e os impactos próximos devem ser mais claros que os distantes. Use uma saída de áudio normal para essa conferência auditiva.
9. Com dois clientes, observe o mesmo golpe do troll nos dois. Reconecte um cliente e confira que ataques já encerrados não tocam novamente.

As imagens de auras e atributos da versão 0.7 permanecem como referências históricas; elas não demonstram persistência nem multiplayer. As variantes de equipamentos ainda compartilham geometria base. O Editor pode registrar uma exceção interna do indexador QuickSearch durante batch PlayMode; ela já existia antes desta integração e não impediu os checks.

Ainda não há servidor público, conta recuperável, escolha de nação, PvP, guerra agendada, comércio ou validação de centenas de jogadores. A entrega usa um servidor local de protótipo com SQLite e HTTP a 10 Hz. O movimento não calcula caminhos completos e a colisão do servidor representa uma grade plana da cena. Não houve deploy externo.

## Histórico

Na versão 0.5, a pasta original desapareceu durante a conferência final. Os fontes foram recuperados dos registros de alterações e o projeto foi regenerado e revalidado. A versão 0.6 introduziu os modelos e materiais de terceiros descritos acima; a 0.7 acrescentou atributos, encantamento de dano e efeitos de habilidade. A versão 0.8 trouxe mochila/equipamentos; a 0.9 acrescentou servidor local, persistência e mapa compartilhado. A 0.10 refina ataques dos monstros e acrescenta áudio espacial, música ambiente e controles de volume. A 0.11 acrescenta remoção visual de peças, roupa básica e inventário anatômico em grade. Capturas de versões anteriores não comprovam as funcionalidades novas.

## Referências

Direção artística: [Cabal — Guerreiro](https://cabal.br.playthisgame.com/pt/Classes/Warrior). Implementação: [Unity — OnRenderImage](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.OnRenderImage.html), [Unity — depth/normal textures](https://docs.unity3d.com/6000.0/Documentation/Manual/SL-CameraDepthTexture.html). Modelos, materiais, áudios e adaptações usados nesta versão estão identificados em [ThirdPartyNotices.md](ThirdPartyNotices.md).


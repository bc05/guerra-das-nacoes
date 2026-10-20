# Combate, Vale dos Clãs e Catacumbas

## Catacumbas — 0.16.0

As Catacumbas são o mapa `catacombs`, separado das áreas `ruins` e `city`. O vestíbulo leva ao ossuário, à câmara dos conjuradores e à cripta dos guardiões. Esqueletos e conjuradores têm espécies e nascimentos próprios; vida, dano, comportamento, experiência e drops continuam sendo decididos pelo servidor. A grade subterrânea é independente da grade do Vale.

Recomendação de nível: **16–24**, sem requisito obrigatório de entrada. O catálogo inicial contém três esqueletos de nível 16, três conjuradores de nível 20 e um guardião de elite de nível 23. O ataque dos conjuradores marca uma posição fixa durante 1,3 s antes do impacto em raio de 1,4 m, alcance de 8 m e intervalo de 2,6 s. Paredes bloqueiam o ataque; afastar-se da área anunciada evita o dano. O mapa usa geometria original e texturas já presentes no projeto, sem novas dependências ou downloads.

| Portal | Origem | Destino |
|---|---|---|
| Entrada das Catacumbas (`portal-catacombs`) | Vale dos Clãs, `(0,19)` | Catacumbas, `(0,0)` |
| Retorno ao Vale (`portal-catacombs-exit`) | Catacumbas, `(0,-3)` | Vale dos Clãs, `(0,17)` |

Aproxime-se do portal e pressione **E** ou clique na interação que informa o destino. A viagem depende da validação do servidor. A cidade mantém seus portais e a condição de área segura. O menu mostra o mapa salvo do personagem; o HUD e a lista de saques distinguem Catacumbas de Vale dos Clãs, inclusive para recompensas pendentes em outro mapa.

Os cenários compartilham coordenadas no Unity, mas somente o cenário do mapa atual fica ativo. Criaturas dinâmicas ficam sob a raiz do seu mapa e são reutilizadas ao retornar; seus objetos, colisões e avisos não ficam visíveis no outro mapa. A transição cancela alvo, intenção e efeitos de combate do cenário anterior. As Catacumbas usam iluminação contida e neblina próprias; céu, luz ambiente, neblina e luzes direcionais externas são restaurados ao sair.

No `/admin`, **Nascimentos** permite escolher **Catacumbas** e suas coordenadas; os limites de formulário são x=−24..24 e z=−10..62. A grade do servidor recusa posições bloqueadas ou fora da área transitável, mesmo dentro desses limites. **Espécies** inclui as aparências Esqueleto e Conjurador esqueleto. Como nos encontros existentes, uma publicação só troca o perfil de uma criatura no próximo nascimento.

A instalação é aditiva e eleva o **conteúdo para 3**, sem trocar protocolo ou schema SQLite, que continuam em 2. Mantém personagens, nível, XP, atributos, moedas, mochila, equipamentos, identificadores dos encontros do Vale, revisões administrativas, rascunhos personalizados e recibos do calendário de Grond. Não exige reiniciar personagens nem limpar o banco.

Valide em um projeto isolado com a cena preparada, sem conectar ao banco dos jogadores. Com o Unity fechado, execute a partir de `Unity`:

```zsh
'/Applications/Unity/Hub/Editor/6000.3.26f1/Unity.app/Contents/MacOS/Unity' -batchmode -projectPath "$PWD" -executeMethod CatacombsPlayModeChecks.Run -logFile "$PWD/catacumbas-playmode.log"
```

O check usa snapshots descartáveis para verificar isolamento dos cenários, cancelamento de alvo, restauração da iluminação e reutilização de criaturas nas transições. `UndeadVisualPlayModeChecks.Run` cobre anatomia, articulação, morte/reaparecimento e descarte dos recursos visuais. `CatacombsOnlinePlayModeChecks.Run` usa cliente e transporte reais em `127.0.0.1:18090`, com identidade e banco descartáveis; o orquestrador de desenvolvimento prepara seu manifesto e libera a conexão após a fixture estar pronta.

As grades são exportadas da física do Unity por `ServerWorldExporter.ExportAll`. A grade do Vale permanece idêntica à anterior; a das Catacumbas tem 192×288 células de 0,25 m. A exportação exige entrada, retorno e sete encontros livres e conectados e recusa bordas abertas. O servidor exige a grade subterrânea para abrir um banco que já recebeu conteúdo 3; a ausência dela interrompe a inicialização sem reposicionar personagens ou criar progresso substituto.

Para revisar a preservação em uma cópia, a partir da raiz do projeto:

```sh
./Server/Build/Server --verify-catalog-copy Server/data/characters.db /tmp/gdn-catacumbas-revisao.db Server/world-collision.json
./Server/Build/Server --catacombs-self-test
./Server/Build/Server --self-test
python3 Server/tests/economy_http_checks.py
```

O destino da cópia deve ser novo. O comando usa backup consistente do SQLite, incluindo transações confirmadas no WAL, e modifica somente a cópia. Para um backup exato, sem migrar a cópia, use `--backup-db origem destino`. Não use um executável anterior ao conteúdo 3 para abrir esse banco nem restaure uma cópia antiga sobre progresso posterior; reative configurações históricas pelo painel do servidor atualizado.

### Evidências locais da versão 0.16 — 19/10/2026

- Servidor Release `osx-arm64`: compilação sem avisos/erros e **404 verificações**, incluindo 78 das Catacumbas; mais **40 verificações HTTP** em banco temporário.
- Sete suítes Unity offline passaram: Catacumbas (18), anatomia dos mortos-vivos (47), comportamentos dos monstros (43), combate manual (157), expansão do mundo, ataques dos monstros e resposta visual de combate (17). Os números são verificações internas; as duas suítes sem número publicam o resultado por cenários.
- O cliente conectado passou **160 verificações** contra servidor e banco descartáveis: entrada, sete criaturas, modelos, reconexão no mapa, seleção sem movimento/ataque, um único golpe confirmado, retirada, saída e reentrada com os mesmos modelos. Apenas a fixture usa herói nível 50/defesa 250 e agressão passiva zero nas Catacumbas; vida, dano, preparação, experiência e drops dos monstros permanecem normais. Os testes de comportamento exercitam agressão e dano com os parâmetros de produção.
- Cenário validado pela colisão real; quatro prévias Unity inspecionadas. A cópia isolada de `Library` apresentou erro de indexação `UnityEditor.Search.SearchDatabase`; não houve exceções de runtime do jogo nas suítes aprovadas. Uma tentativa de captura caiu no compilador nativo Burst; a repetição e as capturas finais passaram.
- Aplicativo macOS **0.16.0** gerado em projeto isolado, verificado com `codesign --verify --deep --strict`, e teste nativo headless de oito segundos sem erros. O teste headless valida inicialização; as suítes em PlayMode validam o fluxo e a apresentação.
- Instalação local atualizada e `/health` confirmado. Os **dois personagens** foram comparados integralmente, exceto revisão/data da gravação, além dos **10 encontros anteriores**, cinco configurações históricas, recompensas, livro de moedas, NPCs e **35.264 recibos**. Os sete novos encontros foram acrescentados. A grade do Vale permanece idêntica byte a byte.
- Backup consistente pré-atualização: `Server/data/backups/before-catacombs-20261019T205524Z.db`, com acesso restrito ao usuário. Relatório de comparação: `work/catacombs/local-release.json`. Relatórios, logs e prévias estão em `work/catacombs/`. Nenhuma identidade real foi usada nos testes conectados.

### Correção da abertura pelo lançador — 0.16.0

O teste nativo anterior abria o executável diretamente. Ao abrir por `Jogar.command`, `PlistBuddy` recusava o `Info.plist` com `Encountered unexpected character [ on line 2 while parsing DTD`. A serialização de `XDocument` no Mono acrescentava um subconjunto DTD vazio (`[]`) que os parsers do macOS não aceitam, embora a assinatura e o leitor Python aceitassem o arquivo.

`PrototypeBranding.ConfigureMacOSBundle` agora remove a declaração DTD opcional antes de salvar e exige `plutil -lint` antes de assinar o pacote. O aplicativo foi regerado no projeto isolado, instalado e aprovado por `plutil`, `PlistBuddy` e `codesign`. O dicionário do plist, incluindo versão e identificador do aplicativo, permaneceu idêntico. `Jogar.command` retornou zero e a janela real exibiu o menu com os dois personagens e progresso consultado no servidor. O servidor permaneceu em execução, sem reinício ou migração do banco. Diagnóstico e log do build estão em `work/launch-repair/`.

Para validar a abertura a partir da raiz do projeto:

```sh
plutil -lint "Unity/Builds/macOS/Guerra das Nações.app/Contents/Info.plist"
codesign --verify --deep --strict "Unity/Builds/macOS/Guerra das Nações.app"
./Jogar.command
```

## Controles de combate

Clique esquerdo em um monstro ou Tab apenas selecionam o alvo e mostram suas informações. A seleção não inicia aproximação nem ataque. Para golpear, acione `1`, Espaço, botão direito ou o botão da habilidade: cada acionamento pede somente um golpe, sem repetição ao segurar a tecla ou o botão. Se o alvo estiver distante, esse pedido aproxima o herói até o alcance, executa um golpe e termina. Clicar no chão cancela o pedido ainda pendente e move o personagem até o ponto escolhido; a seleção visual pode permanecer.

O nome do monstro e o círculo no chão indicam o perigo. `1`, Espaço e botão direito pedem um básico; `2` pede um único corte em área ao chegar ao alcance. Outro golpe exige novo acionamento e respeita as recargas e confirmações do servidor. Golpes já comprometidos conservam sua direção e seu comando original. Sem alvo, continuam disponíveis a varredura frontal e o golpe circular. Troca de seleção, WASD, Esc, painéis, morte, saída do mapa, perda de conexão ou um caminho bloqueado cancelam o pedido pendente; um monstro que renascer não provoca novos ataques.

O servidor valida alvo vivo, mapa, alcance e obstáculos e direciona o setor do golpe. A animação pode começar imediatamente no cliente; dano, experiência, moedas, itens, mana e recargas dependem do servidor. O resultado não repete o efeito previsto e uma recusa cancela apenas a apresentação do comando correspondente.

As animações preservam o esqueleto e o sistema de equipamentos: apoio calcanhar/ponta na caminhada e corrida, contrarrotação do corpo, pés plantados durante o contato da espada e retorno gradual do movimento na recuperação. Um toque de botão direito no chão guarda a direção enquanto o herói gira antes de atacar. Um impacto confirmado com atraso ganha sua própria janela de faíscas, sem repetir a animação ou o rastro da espada.

Os quatro golpes mantêm a duração visual de 0,60 s, o impacto em 0,13 s e a recarga compartilhada de 0,65 s. A caminhada normal permanece a 1,35 m/s; Shift ativa a corrida a 5,5 m/s.

## Perigo por nível

| Nível do monstro em relação ao herói | Nome |
|---|---|
| 6 ou mais abaixo | Cinza |
| 3 a 5 abaixo | Verde |
| De 2 abaixo até 2 acima | Amarelo |
| 3 a 5 acima | Laranja |
| 6 a 9 acima | Vermelho |
| 10 ou mais acima | Roxo |

O painel de alvo também apresenta a descrição do perigo. Monstros adquirem jogadores próximos com linha de visão, perseguem dentro do território e retornam ao posto quando o jogador foge. A cidade permanece segura.

## Mapa e chefão

O mapa de combate passa de 35×35 para 100×100 metros, com limites x=−50..50 e z=−20..80. A passagem norte liga o pátio ao vale. O acampamento Orc fica a oeste, o bosque dos Trolls a leste e a arena de Grond ao norte, em (0,61). Árvores, rochas, grama ao vento, fogueira e partículas leves completam o ambiente. As variantes Orc, Xamã e chefe derivam do Troll licenciado, com equipamento e silhuetas próprios; as fontes estão em `ArtSource/FronteiraExpandida.md`.

Configuração inicial de **Grond, Soberano dos Clãs**:

- Nível 25, 12.000 HP e dano base 70.
- Nascimento diário às **20h**, fuso **America/Sao_Paulo**, com janela de 60 minutos.
- **35%** de chance de um item do grupo lendário; os pesos do grupo escolhem entre espada, escudo e colar do Soberano. A tabela também configura moedas e outros itens.
- Uma ocorrência nasce uma única vez. Morte, expiração e reinício do servidor preservam o recibo; não há renascimento normal de seis segundos para chefões.
- Ao terminar a janela, um chefão ainda vivo desaparece sem conceder recompensa. Janelas inteiramente perdidas com o servidor desligado não geram encontros retroativos.

No painel `/admin`, a aba **Chefões e horários** configura o nascimento do chefão, dias da semana, horário, fuso, duração e ativação. **Espécies** configura nível, vida, dano, experiência, aparência, agressão, território e comportamento. **Nascimentos** escolhe a espécie, o mapa e as coordenadas de cada encontro. **Tabelas de drop** configura probabilidades e pesos. Salvar altera o rascunho; validar e publicar torna a versão ativa. Encontros vivos conservam o perfil revisionado com que nasceram. Desativar uma agenda impede novos nascimentos, enquanto um chefão já ativo conserva seu prazo.

## Espécies e pontos de nascimento

Os atributos de uma criatura pertencem à espécie, em `monsterSpecies`. O lugar onde ela aparece pertence a um ponto de nascimento, em `monsterSpawns`. Cada nascimento mantém seu identificador e referencia uma espécie por `speciesId`; assim, mudar uma espécie atualiza os futuros nascimentos que a utilizarem sem duplicar atributos. A agenda de chefão continua apontando para o identificador do nascimento, por `monsterId`.

Os monstros do Vale passam a ter três comportamentos. Os números abaixo são os parâmetros iniciais; o administrador pode ajustá-los no rascunho:

| Comportamento | Espécie inicial | Antecipação | Intervalo entre inícios | Como evitar |
|---|---|---:|---:|---|
| Corpo a corpo (`melee`) | Demais criaturas e Grond | 0,65 s | 1,25 s | Sair do alcance do golpe |
| À distância (`ranged`) | Xamã (`orc-shaman-0`) | 1,1 s | 2 s | Sair do círculo de 1,1 m marcado no chão |
| Em área (`area`) | Troll Bruto (`troll-brute-0`) e Troll Ancião (`troll-elder-0`) | 1,4 s | 2,4 s | Sair da área anunciada de 3 m |

O Xamã inicia o preparo em até 7 m e fixa a posição anunciada no começo do ataque; o aviso não acompanha o jogador durante a antecipação. Os ataques em área anunciam uma zona ao redor do monstro. O servidor calcula o impacto ao concluir o preparo e só então confirma o dano. Uma zona anunciada não garante acerto: sair antes do impacto permite esquivar. O intervalo entre inícios precisa ser ao menos 0,1 s maior que a antecipação. O renascimento normal permanece em 6 s por padrão; o calendário substitui essa regra para chefões.

Editar atributos, comportamento, posição ou espécie de um nascimento não troca o perfil de um monstro que já esteja vivo. Ele termina seu encontro usando a revisão com que nasceu, inclusive para experiência e drops. A nova configuração passa a valer ao renascer.

## Preservação e validação

A expansão é uma publicação aditiva ao carregar a grade ampliada: mantém personagens, itens possuídos, moedas, revisões antigas e rascunhos administrativos. Itens do Soberano são definições de drop; não entram automaticamente na mochila nem no patrimônio legado de 35 itens. O protocolo e o schema do banco continuam na versão 2.

A separação das espécies instala a versão 2 **do conteúdo**, sem alterar a versão do protocolo ou do schema. Os identificadores dos nascimentos antigos são preservados; a lista legada `monsters` continua disponível para leitura de versões históricas, enquanto novas configurações usam `monsterSpecies` e `monsterSpawns`. As versões anteriores permanecem imutáveis. A reversão de conteúdo pelo painel continua disponível no servidor novo; o executável antigo não lê o catálogo separado. Rascunhos ainda inválidos são conservados sem impedir a inicialização do mundo válido. Rascunhos e configurações reativadas são adaptados pelo servidor, conservando os ajustes administrativos e sem substituir progresso, recompensas já entregues ou recibos de ocorrências de chefões.

Para validar esta alteração, execute `--self-test` com a versão compilada do servidor; a suíte trabalha em bancos temporários. Depois confira, em uma sessão de teste, o Xamã atacando uma posição fixa, a saída das áreas anunciadas dos Trolls e o dano confirmado pelo servidor. No painel, edite uma espécie e um nascimento separadamente, valide e simule os drops; confira que o chefão continua ligado ao nascimento correto. Uma publicação com monstros vivos deve preservar os encontros atuais até seu renascimento. Não use o banco dos jogadores como fixture de teste nem reinicie o progresso para preparar a validação.

Com o Unity fechado, execute na pasta `Unity`, um comando por vez:

```zsh
'/Applications/Unity/Hub/Editor/6000.3.26f1/Unity.app/Contents/MacOS/Unity' -batchmode -quit -projectPath "$PWD" -executeMethod FrontierReleaseSetup.ApplyAndCapture -logFile "$PWD/fronteira.log"
'/Applications/Unity/Hub/Editor/6000.3.26f1/Unity.app/Contents/MacOS/Unity' -batchmode -projectPath "$PWD" -executeMethod TargetCombatPlayModeChecks.Run -logFile "$PWD/alvo.log"
'/Applications/Unity/Hub/Editor/6000.3.26f1/Unity.app/Contents/MacOS/Unity' -batchmode -projectPath "$PWD" -executeMethod ManualCombatPlayModeChecks.Run -logFile "$PWD/combate-manual.log"
'/Applications/Unity/Hub/Editor/6000.3.26f1/Unity.app/Contents/MacOS/Unity' -batchmode -projectPath "$PWD" -executeMethod ApproachBlockPlayModeChecks.Run -logFile "$PWD/aproximacao.log"
'/Applications/Unity/Hub/Editor/6000.3.26f1/Unity.app/Contents/MacOS/Unity' -batchmode -projectPath "$PWD" -executeMethod CombatFeedbackPlayModeChecks.Run -logFile "$PWD/impacto.log"
'/Applications/Unity/Hub/Editor/6000.3.26f1/Unity.app/Contents/MacOS/Unity' -batchmode -projectPath "$PWD" -executeMethod WorldExpansionPlayModeChecks.Run -logFile "$PWD/vale.log"
```

`ManualCombatOnlinePlayModeChecks.Run` complementa a validação com o cliente real e um servidor isolado em `127.0.0.1:18090`, usando banco vazio descartável. O roteiro confere que selecionar não move nem ataca, um pedido explícito aproxima e produz um único básico confirmado, e não surgem novos golpes após a recarga. Também confere troca de alvo durante recarga, respostas repetidas e recusa real. Eventos de início e impacto são observados entre respostas diferentes do servidor.

Na revisão de controle manual, passaram **14 etapas**: compilação C#, **12 suítes PlayMode** e build macOS no projeto isolado. O check manual offline passou em **157 verificações**; o online confirmou **três pedidos explícitos, três ataques e três impactos**, sem repetição após esperas superiores a duas recargas. A aproximação bloqueada passou em **11 verificações**, incluindo liberar o caminho e esperar mais duas recargas sem retomar um pedido cancelado. A fluidez passou em **73 verificações a 120 Hz**, preservando a sequência dos quatro golpes e o retorno à guarda. Os resultados e logs estão em `work/manual-combat-validation-results.json` e `work/manual-combat-*.log`.

O aplicativo foi atualizado em `Unity/Builds/macOS/Guerra das Nações.app` e sua assinatura local foi verificada. O teste nativo iniciou o motor e permaneceu ativo por oito segundos, sem erros detectados e sem conexão automática; relatório em `work/manual-combat-native-smoke.json`.

Para conferir manualmente, reabra `Jogar.command`, selecione um monstro próximo e outro distante e aguarde: em ambos os casos, o herói deve permanecer parado. Acione `1` uma vez: ele deve se aproximar somente quando necessário, dar um golpe, retornar à guarda e aguardar. Segure `1`, Espaço ou o botão direito além da recarga: deve haver somente o golpe do acionamento inicial. Solte e faça um novo acionamento depois da recarga para o próximo golpe; um acionamento durante a recarga não agenda outro golpe. Durante a aproximação, troque de seleção ou clique no chão e confira que o pedido anterior não é retomado.

Os testes PlayMode usam progresso descartável e não acessam o banco dos jogadores. O servidor oferece `--self-test` e `--frontier-self-test`, executados em bancos temporários. As capturas de `docs/previews/fronteira` incluem instâncias temporárias para inspeção visual; não fazem um chefão nascer fora do horário no jogo.

O servidor continua local; o repouso do Mac interrompe a disponibilidade enquanto ele estiver suspenso. A implementação não é teste de carga de MMORPG nem benchmark de FPS.


## Validação do catálogo e dos comportamentos — 0.15.0

- Servidor: 326 verificações, incluindo migração com rascunho incompleto, espécies compartilhadas, encontros vivos revisionados, adoção do perfil ao renascer, esquiva, cobertura, área com dois jogadores e cancelamentos.
- Integração HTTP: 40 verificações de autenticação, publicação, simulação, reversão e reinício em banco temporário.
- Unity: `MonsterBehaviorPlayModeChecks.Run` passou em 43 verificações, acompanhado das regressões de combate manual (157), golpes de monstros, feedback (17) e mapa.
- Cliente online: `ManualCombatOnlinePlayModeChecks.Run` confirmou três pedidos explícitos, três golpes e três impactos, sem repetição ou reativação. A fixture SQLite isolada usa dano 1, intervalo 10 s e agressão zero para evitar morte durante as esperas do roteiro; os testes específicos dos monstros usam os parâmetros normais e o balanceamento do jogo permanece intacto.
- Build macOS 0.15.0: assinatura local verificada; smoke nativo permaneceu ativo por oito segundos, sem erros e sem conexão automática.
- Atualização local: dois personagens comparados integralmente, exceto revisão/data da gravação, e encontros anteriores preservados. Backup anterior protegido em `Server/data/backups/`; relatórios e capturas em `work/monster-catalog/`.

No cliente atualizado, reabra `Jogar.command`. Os monstros vivos mantêm a revisão anterior; o Xamã e os Trolls do bosque adotam os novos comportamentos no próximo nascimento. Círculo azul marca a posição fixa do disparo do Xamã; círculo laranja marca a área do Troll. Saia do círculo antes do impacto. No painel, confira as abas **Espécies** e **Nascimentos**; um rascunho previamente personalizado conserva seus valores até a publicação explícita.

# Servidor 0.16 — Catacumbas e encontros por mapa

### Catacumbas — conteúdo 3

O mapa `catacombs` usa sua própria grade de colisão e catálogo de nascimentos. A atualização aditiva acrescenta o vestíbulo, o ossuário, a câmara dos conjuradores e a cripta; personagens, progresso, patrimônio, encontros vivos, revisões, rascunhos e ocorrências de chefões permanecem no banco. A versão do conteúdo passa a 3; protocolo e schema SQLite continuam em 2. Use o servidor atualizado para reativar conteúdo histórico, sem substituir o banco por uma cópia antiga.

- `portal-catacombs`: origem `ruins`, posição `(0,19)`; destino `catacombs`, posição `(0,0)`.
- `portal-catacombs-exit`: origem `catacombs`, posição `(0,-3)`; destino `ruins`, posição `(0,17)`.

O servidor decide se a viagem é permitida e mantém combate, impacto, drops e coleta no mapa correspondente. O cliente alterna os cenários que compartilham coordenadas, cancela alvos e efeitos na transição e restaura a iluminação externa ao sair. O painel apresenta o mapa **Catacumbas** e as aparências **Esqueleto** (`skeleton`) e **Conjurador esqueleto** (`skeleton-caster`); a validação da grade continua sendo a confirmação de que cada nascimento está em um espaço livre.

A atualização foi validada com bancos temporários e cena preparada em projeto isolado: **404 verificações do servidor**, incluindo 78 das Catacumbas, e **40 verificações HTTP**. `CatacombsPlayModeChecks.Run` verifica a transição cidade/Vale/Catacumbas, o cancelamento de alvo, a iluminação restaurada e o cache de criaturas por mapa. O teste conectado usa cliente e transporte reais para conferir portais, reconexão no mapa, sete criaturas, um golpe manual e ida/volta com progresso preservado. Os relatórios locais estão em `work/catacombs/`.

O conteúdo requer `catacombs-collision.json`, carregado por `CATACOMBS_COLLISION_PATH` ou ao lado de `world-collision.json`. `IniciarServidor.command` configura ambos. Um banco já atualizado para conteúdo 3 é recusado se a grade faltar. A atualização mantém identificadores dos nascimentos ligados ao seu mapa; para criar um encontro em outro mapa use um novo identificador. Vida e perfil dos encontros existentes continuam presos à revisão em que nasceram até o próximo nascimento.

A versão 0.14 acrescenta validação de `targetId` nos ataques, agressão por proximidade, mapa de 100×100 m e chefões com calendário configurável. O conteúdo é instalado de forma aditiva ao carregar a grade ampliada, preservando personagens, revisões, rascunhos e histórico econômico. A aba administrativa **Chefões e horários** configura dias, horário, fuso, duração e ativação. Consulte [combate, calendário e drops](../docs/combate-e-fronteira.md).

### Espécies, nascimentos e comportamentos do Vale

`EconomyConfig.monsterSpecies` guarda o catálogo de espécies: atributos, experiência, tabela de drop, aparência, agressão, território e parâmetros de ataque. `EconomyConfig.monsterSpawns` guarda os encontros: `id`, `speciesId`, `mapId`, `x`, `z` e `scheduleId`. O `monsterId` de cada agenda aponta para o **identificador do nascimento**, permitindo reutilizar uma espécie em outros lugares. O painel `/admin` apresenta esses dados em **Espécies**, **Nascimentos** e **Chefões e horários**, com nomes legíveis nos seletores.

Cada espécie define `behavior` (`melee`, `ranged` ou `area`), `attackRange`, `attackPreparation`, `attackInterval`, `areaRadius` e `respawnDelay`. O intervalo é contado entre os inícios dos golpes e precisa superar o preparo em pelo menos 0,1 s. A configuração inicial mantém os ataques corpo a corpo e acrescenta:

- `orc-shaman-0`: ataque à distância iniciado em até 7 m, com 1,1 s de aviso e intervalo de 2 s. O alvo é uma posição fixa no chão, anunciada com raio de 1,1 m.
- `troll-brute-0` e `troll-elder-0`: ataque em área de 3 m, com 1,4 s de aviso e intervalo de 2,4 s.

O servidor continua decidindo o impacto e as recompensas. Monstros vivos conservam sua espécie, nascimento e revisão capturados ao nascer; publicar novos atributos, comportamento ou posição só afeta o próximo nascimento. Chefões continuam dependendo do calendário, sem usar o renascimento normal da espécie.

A atualização aditiva instala a **versão 2 do conteúdo**, distinta da revisão administrativa, do protocolo 2 e do schema SQLite 2. A conversão preserva os identificadores dos nascimentos legados, personagens, progresso, patrimônio, recibos de comandos e ocorrências de chefões. O campo antigo `monsters` continua sendo lido para compatibilidade; configurações novas usam os dois catálogos e deixam essa lista vazia. Versões históricas permanecem imutáveis e podem ser consultadas; ao reativar uma delas, o servidor adapta o rascunho ao catálogo atual. Ajustes de atributos, posições, drops e agendas em rascunhos administrativos são conservados na conversão.

Valide a migração e os novos comportamentos em bancos temporários com `./Build/Server --self-test` ou, apenas para o catálogo e os comportamentos, `./Build/Server --monster-catalog-self-test`, executados em `Server`. Confira no painel a separação das espécies e nascimentos, a simulação de drops e o vínculo da agenda; no jogo, confirme que o aviso permanece na posição anunciada e que sair dele evita o impacto. A versão 0.15.0 passou em 326 verificações do servidor, 40 de integração HTTP e nos seis checks Unity de comportamentos, combate manual offline/online, golpes dos monstros, feedback e mapa. O build macOS teve assinatura verificada e smoke nativo sem conexão automática. A atualização local preservou integralmente os dois personagens e os encontros existentes; evidências em `work/monster-catalog/`. O servidor antigo não lê o catálogo separado; use a reversão de conteúdo no painel do servidor atualizado.

A abertura normal do cliente apresenta a seleção de personagens; não cria uma sessão de jogo automaticamente. O menu consulta a prévia do personagem salvo e só conecta após a ação do jogador. A lista de personagens fica neste computador, separada por servidor, com até oito entradas. Cada entrada aponta para uma identidade local independente; isso não constitui login de conta nem oferece recuperação entre dispositivos.

- O perfil `principal` é importado automaticamente, preservando sua credencial atual. O perfil conhecido `segundo` é importado quando sua identidade já existe; abrir o iniciador do segundo cliente também pode preparar somente sua entrada local, sem credencial ou personagem no servidor. Se a lista estiver cheia, o iniciador informa o limite e não conecta o principal por engano. Pastas de perfis de testes não são enumeradas.
- Criar uma entrada na seleção grava apenas o nome e o identificador do perfil. A credencial e o personagem no servidor só são criados ao conectar. Arquivos de identidade ou de lista danificados produzem erro; não são substituídos automaticamente.
- O servidor local HTTP na porta 8088 preserva o diretório original de identidades, inclusive ao usar os aliases de loopback `127.0.0.1`, `localhost` e `::1`. Outros destinos usam `Servers/<SHA-256-da-origem>/Online` dentro dos dados persistentes do cliente. Uma chave existente não é enviada automaticamente a outro servidor.
- O cliente envia `preserveExistingName: true` ao conectar: uma legenda local antiga, ou a indisponibilidade da prévia, não renomeia um personagem existente. O nome informado continua sendo usado para criar um personagem novo. Requisições legadas sem essa opção mantêm seu comportamento anterior.
- Retornar à seleção encerra imediatamente a sessão fora de combate. Um golpe pendente ou um monstro vivo atacando o personagem impede a saída com HTTP 409. Personagens derrotados podem sair; reentrar não ressuscita, não recupera vida e não reinicia recargas ou efeitos.
- Trocar de servidor na seleção descarta as definições de equipamentos da origem anterior. Revisões continuam imutáveis dentro de cada servidor; servidores independentes podem usar o mesmo identificador e revisão com configurações diferentes. Uma prévia atrasada de outra origem é descartada.

### Rotas do menu — protocolo 2

| Rota | Entrada | Resultado e garantias |
|---|---|---|
| `POST /v1/characters/preview` | `OnlineConnectRequest`, com `protocolVersion: 2` e identidade Base64 de 32 bytes | `OnlineCharacterPreview`: existência, identificador, nome, nível, mapa, equipamentos públicos e definições para apresentação. Não cria personagem ou sessão, não substitui sessão existente, não renomeia e não grava o progresso. |
| `POST /v1/characters/nickname-check` | `OnlineNicknameCheckRequest`, com `protocolVersion: 2`, `nickname` e `deviceKey` opcional | `OnlineNicknameAvailability`: `exists`, `available`, nick normalizado e mensagem. É uma consulta sem reserva ou gravação. A identidade opcional permite reconhecer o titular do próprio nick. |
| `POST /v1/connect` | `OnlineConnectRequest`; cliente atual usa `preserveExistingName: true` | Carrega ou cria o personagem, confirma o estado e emite uma sessão. Uma reconexão revoga a sessão anterior da mesma identidade. |
| `POST /v1/disconnect` | `Authorization: Bearer <sessionToken>` | `OnlineDisconnectResult`. Remover novamente uma sessão já removida retorna sucesso; sessão em combate permanece ativa e retorna HTTP 409. |

A prévia exige a mesma credencial que permite conectar; não existe listagem pública de personagens por nome. Não registre payloads, chaves ou cabeçalhos de autenticação. Para servidores remotos, o cliente exige HTTPS, exceto o modo explícito de teste LAN.

### Nick e unicidade

Novos nicks têm de 2 a 24 unidades de texto UTF-16, aceitam letras, números, espaço, hífen e sublinhado. `CharacterNickname.Normalize` aplica Unicode Form KC, remove espaços das bordas e reduz espaços internos repetidos. A comparação ignora caixa; por exemplo, `Aster`, ` ASTER ` e `Ａｓｔｅｒ` disputam o mesmo nome. A consulta informa disponibilidade, mas somente a transação de conexão confirma a escolha.

O servidor verifica o nick dentro da mesma exclusão que controla o mundo, com todos os personagens persistidos carregados e um único processo autorizado pelo arquivo de lease do banco. Não é necessária migração de schema para esta instância. Nome ocupado retorna HTTP 409 com `nickname_taken`; formato inválido retorna HTTP 400 com `nickname_invalid`. A recusa acontece antes de criar personagem, avançar o mundo, salvar ou substituir uma sessão existente. Requisições simultâneas pelo mesmo nome produzem um único titular.

Reentradas com `preserveExistingName: true` preservam inclusive nomes históricos que antecedem as regras atuais. Se o banco já contiver titulares com nomes equivalentes, o servidor informa a quantidade de colisões no console, preserva os personagens e reserva esses nomes contra novos titulares. Esses casos exigem reconciliação administrativa explícita; a atualização não renomeia nem exclui personagens silenciosamente.

### Iniciadores e verificações do menu

A partir de `Server`, a distribuição usa:

```sh
../Jogar.command
../JogarSegundoCliente.command
./Build/Server --menu-self-test
./Build/Server --nickname-self-test
./Build/Server --self-test
```

Os dois iniciadores abrem a seleção; o segundo solicita a seleção do perfil `segundo` quando ele já está cadastrado. Para automação e testes que precisam entrar diretamente:

```sh
../Jogar.command --auto-connect --profile=principal
```

`--profile`, `--name` e `--server` continuam disponíveis; `--profile` sozinho não pula o menu. No cliente Unity, `ConfigureForTests` mantém a entrada direta dos testes de integração anteriores. O servidor aceita `--menu-self-test` para executar somente as verificações de seleção, prévia, saída e isolamento de catálogos.

A suíte completa contém **326 verificações**, cobrindo servidor, persistência, combate, economia, seleção e saída, nicks, calendário de chefões, recompensas e compatibilidade do patrimônio legado. Todas usam bancos temporários, sem alterar o banco dos personagens do jogo. Os testes Unity cobrem separadamente o menu e o ciclo de conexão e retomada do progresso.

## Economia e cidade — introduzidas na versão 0.12

O executável `Build/Server` atende o protocolo 2 e mantém a autoridade de combate, mapas, carteira, mochila, drops e comércio. Os caminhos `/v1/connect` e `/v1/commands` permanecem por compatibilidade de URL, mas exigem `protocolVersion: 2`.

- Mapas: `ruins` (combate) e `city` (seguro), com portais validados por distância, estado e combate. Snapshots e eventos são filtrados por mapa.
- Item: definição/revisão imutável + cópia por personagem. Requisitos usam pontos distribuídos. A migração v1 preserva todos os 35 itens antigos e os marca como vinculados, sem revenda.
- Recompensa: contribuição de dano por vida do monstro, identidade de morte persistida e sorteio no servidor. Moedas, XP, mundo, mochila, recibos e histórico econômico são confirmados na mesma transação.
- Compra/venda: proximidade, saldo e espaço verificados no servidor. Cada comando tem UUID e recibo. Cotações antigas exigem nova confirmação.
- Cidade: NPCs publicados pela configuração, lojas e poções. Poções curam uma única vez até 50% do máximo; sucata é vendida por 4 Coroas a unidade.
- Admin: `/admin`, autenticação por chave local e cookie de sessão, proteção de origem e cabeçalho, acesso somente loopback. Chave em `data/admin-secret.txt` (0600). Não distribuir `data/`.
- Configuração: rascunho com controle de concorrência, validação, simulação, versões, publicação agendada e auditoria. Recompensas de monstros vivos usam sua versão de nascimento. Alterações de atributos geram uma nova revisão de definição. Reativar configuração anterior não desfaz patrimônio já concedido.

### Verificações locais

No diretório `Server`:

```sh
./Build/Server --self-test
python3 Tests/economy_http_checks.py --server "$PWD/Build/Server"
```

Os testes criam bancos temporários. O script HTTP também aceita `--legacy-db /caminho/backup.db` para testar uma cópia de um banco antigo sem alterar o original. Se a porta de teste estiver ocupada, informe outra com `--port 18096`.

O banco atual é mantido por um único processo com SQLite e gravações serializadas. Ledger e versões são duráveis no snapshot transacional do mundo; grandes volumes exigirão armazenamento incremental e avaliação de carga antes de produção.

Não existe comando do cliente para conceder moedas ou experiência arbitrária, nem chave administrativa no Unity. Não restaure um backup anterior sobre uma economia em uso: concilie os dados e pare o servidor antes de qualquer recuperação operacional.

---

## Referência histórica da versão 0.10

Os detalhes abaixo documentam a implementação inicial. Para o estado atual, prevalecem as seções 0.13 e 0.12 acima: protocolo e esquema 2, cópias de equipamentos, drops por contribuição, mapas e comércio. Contagens antigas de testes e regras de recompensa descritas nesta referência não representam a versão atual.

O servidor mantém o progresso de cada personagem no SQLite e executa as regras de combate. Nível, XP, atributos, posse dos 35 itens, mochila, equipamentos, HP, mana, posição, recargas e efeitos temporários são reconstruídos do banco após fechar e abrir o servidor. O cliente envia intenções; não existe rota para gravar XP, nível ou itens arbitrários.

## Executar no macOS

A distribuição pronta para macOS Apple Silicon está em `Build/Server` e inclui o runtime; pode ser executada diretamente sem SDK. O iniciador da pasta principal utiliza esse executável.

Na pasta `Server`, com o SDK .NET 10 local já instalado:

```sh
DOTNET_CLI_TELEMETRY_OPTOUT=1 .dotnet/dotnet restore --locked-mode -r osx-arm64
DOTNET_CLI_TELEMETRY_OPTOUT=1 .dotnet/dotnet build --no-restore -c Release -r osx-arm64
.dotnet/dotnet bin/Release/net10.0/osx-arm64/Server.dll
```

O endereço padrão é `http://127.0.0.1:8088`. O arquivo `world-collision.json` é obrigatório e é exportado da cena Unity; o servidor recusa iniciar com grade ausente, inválida ou que bloqueie os pontos de nascimento. `HOST`, `PORT`, `DB_PATH` e `COLLISION_PATH` podem alterar os padrões. Fora de loopback é exigido `GAME_ALLOW_INSECURE_LAN=1`, exclusivamente para ensaios em rede confiável. Isso não transforma HTTP em conexão segura.

Ao usar `IniciarServidor.command`, os nomes correspondentes são `GAME_HOST`, `GAME_PORT`, `GAME_DB_PATH` e `GAME_COLLISION_PATH`. O lançador os converte para as variáveis do servidor, evitando herdar o `HOST` interno do zsh. `Jogar.command` inicia automaticamente apenas o destino local padrão, em `127.0.0.1:8088`.

O banco fica em `Server/data/characters.db`. Não exclua essa pasta para atualizar o programa. O esquema usa versão 1 e recusa versões futuras, arquivos corrompidos e snapshots inconsistentes sem criar personagens substitutos. WAL e `synchronous=FULL` mantêm a transação durável. Para backup consistente com o servidor em execução, utilize a API de backup do SQLite ou `VACUUM INTO`; copiar apenas o `.db` enquanto há WAL ativo pode perder gravações. Com o servidor encerrado, preserve a pasta `data` inteira.

O processo mantém um arquivo `characters.db.lock` aberto com exclusão por toda a execução. Outra instância usando o mesmo `DB_PATH`, inclusive em outra porta, é recusada para impedir que dois mundos sobrescrevam o banco. O arquivo pode continuar existindo depois do encerramento; a exclusão real é liberada pelo sistema operacional, inclusive após uma falha.

## Verificar

```sh
.dotnet/dotnet bin/Release/net10.0/osx-arm64/Server.dll --self-test
```

As 102 verificações usam bancos temporários isolados, sem alterar os personagens do jogo. Cobrem reinício, idempotência, falha de gravação e rollback, troca de equipamentos, requisitos, concorrência, duas identidades, revogação de sessão, recargas persistentes, velocidades, paredes, XP concedido por mortes calculadas pelo servidor e os eventos de golpes dos monstros com acerto, erro e cancelamento.

O roteiro HTTP independente está em `Tests/http_smoke.py`. Execute `python3 Tests/http_smoke.py` a partir de `Server`; usa a publicação em `Build`, a grade final e bancos temporários isolados. Ele abre e encerra seus próprios processos na porta18089, sem alterar `Server/data`. Requer Python3 e o SDK local para iniciar a DLL de teste. Não imprima payloads de conexão ou cabeçalhos de autenticação ao depurar.

## Protocolo

- `GET /health`: disponibilidade e versão de protocolo.
- `POST /v1/connect`: `OnlineConnectRequest`. `deviceKey` deve ser Base64 de 32 bytes aleatórios gerados uma vez pelo cliente. O servidor armazena apenas SHA-256 dessa chave. Conectar novamente a mesma identidade revoga a sessão anterior.
- `POST /v1/commands`: `Authorization: Bearer <sessionToken>` e `OnlineCommandBatch`. Até 32 comandos por lote, em ordem. Lote vazio consulta o estado. Polling recomendado: 10 Hz, limite de 40 requisições/s por sessão. HTTP 401 com `session_invalid` exige reconexão.
- Tipos de comando: `move`, `allocate`, `equip`, `unequip`, `ability` e `respawn`. Identificadores de comando são UUIDs persistidos em conjunto com o resultado e o novo estado. Reenviar o mesmo identificador e conteúdo retorna o resultado anterior; mudar seu conteúdo é rejeitado. A mensagem de sucesso é vazia.
- Dados compartilhados estão em `Unity/Assets/Scripts/OnlineContracts.cs` e são incluídos diretamente no projeto do servidor. As regras C# de progressão e equipamentos também são compartilhadas por referência aos arquivos reais, sem cópias divergentes.
- `monsterStrike` é emitido uma vez ao concluir o preparo de ataque do monstro: `sourceId` identifica o monstro, `targetId` o jogador, `ability` é 0 e `amount` é o dano aplicado. Quando o golpe termina sem alcançar o jogador ou é bloqueado por obstáculo, `amount` é 0 e não há `playerHit`. Preparo cancelado por fuga acima de 1,8 m, retorno ao nascimento ou morte não produz `monsterStrike`. O evento serve para apresentação; `playerHit` e o snapshot continuam confirmando o dano. Clientes 0.9 podem ignorar o novo tipo de evento; o protocolo e o esquema do banco permanecem na versão 1.

Uma falha de banco devolve HTTP 503 e nenhum sucesso é confirmado. Antes de aceitar novas escritas, o servidor recarrega o estado confirmado no SQLite; isso também trata um resultado de commit ambíguo sem sobrescrever uma gravação que já tenha sido concluída. Enquanto essa recuperação não for possível, as operações continuam indisponíveis. O cliente pode repetir os comandos com os mesmos UUIDs. Os eventos recentes são efêmeros, limitados aos últimos 128, com cursor recuperável após reinício. O snapshot completo é a fonte para reconciliar estado.

## Regras do mundo

Há três trolls compartilhados entre os jogadores conectados: dois de 240 HP e um de 600 HP. O servidor limita deslocamentos a 3,2 m/s ou 5,5 m/s correndo, com crédito persistido de até 1,5 m para absorver variações na chegada de pacotes e checagem do segmento na grade de colisão. O crédito começa em 0,15 m uma única vez, é consumido pela distância e só se recompõe com o tempo do servidor; reconectar não o repõe. O ataque básico usa arco de 130°/2,65 m; o corte pesado usa 360°/3,15 m. O dano é capturado na ativação e resolvido 130 ms depois. Paredes bloqueiam ataques e movimento.

Mana, GCD, recargas, guarda, cura e fúria são controlados pelo relógio do servidor. Duração de buff é fixada na ativação. Reconectar não cura nem limpa recargas. R só pode ressuscitar quem morreu; não reinicia os monstros compartilhados. O último golpe ganha a recompensa canônica uma única vez por morte. O tick do mundo roda a 10 Hz; jogadores sem contato por 15 segundos saem da presença online.

## Limites deste protótipo

A identidade é anônima e vinculada ao arquivo local de credencial do cliente. Ela funciona como credencial de acesso ao personagem: não a compartilhe. Não há recuperação de conta, login entre dispositivos ou serviço público hospedado. Nenhuma senha ou chave administrativa está embutida no build.

O servidor é uma instância com SQLite e serializa alterações do mundo para manter consistência. Cada commit grava o estado completo dos personagens carregados e do mundo; isso amplifica escritas e foi dimensionado para poucos clientes neste protótipo. Não há balanceamento entre servidores, milhares de jogadores, PvP ou guerra de nações nesta entrega. Monstros usam perseguição simples e deslizamento em obstáculos, sem navegação completa. A simulação é plana; a colisão representa a cena exportada, não toda a física 3D. Os recibos de comandos são preservados sem expiração para impedir replay; o banco cresce com o uso e precisa de política de retenção antes de operação prolongada.

Uma publicação na internet precisa de hospedagem definida, TLS, autenticação de conta, proteção contra abuso, backups e monitoramento. O modo LAN é uma opção explícita de teste e não deve ser exposto diretamente à internet.

## Dependências

O SDK é .NET 10 e a única dependência direta adicionada é `Microsoft.Data.Sqlite 10.0.13` (MIT), que inclui dependências SQLitePCLRaw (Apache-2.0) e SQLite (domínio público). O lockfile fixa as versões transitivas. Referências: [Microsoft.Data.Sqlite](https://github.com/dotnet/efcore), [SQLitePCLRaw](https://github.com/ericsink/SQLitePCL.raw), [SQLite](https://www.sqlite.org/copyright.html).

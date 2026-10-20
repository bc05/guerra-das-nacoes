using System.Security.Cryptography;
using System.Text;

namespace GuerraDasNacoes.Server;

public sealed class GameApiException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

public sealed partial class GameWorld
{
    private sealed class Player
    {
        public SavedPlayer state;
        public PlayerProgression stats;
    }
    private sealed class Session
    {
        public string playerId;
        public double lastSeen, expiresAt, rateWindow;
        public int requestCount;
    }
    private sealed class Checkpoint
    {
        public SavedPlayer[] players;
        public SavedWorld world;
        public OnlineWorldEvent[] events;
        public long eventId;
        public double lastTick;
    }
    private readonly object gate = new();
    private readonly SaveStore store;
    private readonly CollisionGrid grid;
    private readonly Dictionary<string, Player> players = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> identities = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Session> sessions = new(StringComparer.Ordinal);
    private readonly List<OnlineWorldEvent> events = new();
    private SavedMonster[] monsters;
    private EconomyWorldState economy;
    private CollisionGrid cityGrid;
    private long latestEventId;
    private double lastTick;
    private bool recoveryRequired;
    public static double UnixNow => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;
    public GameWorld(SaveStore store, CollisionGrid grid, double? now = null, CollisionGrid catacombsGrid = null)
    {
        this.store = store; this.grid = grid;
        grid.Validate();
        this.catacombsGrid=catacombsGrid;
        catacombsGrid?.Validate();
        cityGrid = new CollisionGrid { originX=-18,originZ=-18,cellSize=.5f,width=72,height=72,blocked=new int[72*72] };
        cityGrid.Validate();
        if (!grid.Free(0,0)) throw new InvalidDataException("Nascimento das ruínas bloqueado.");
        lastTick = now ?? UnixNow;
        var loaded=store.Load();
        economy=loaded.world?.economy ?? EconomyRules.Create();
        economy.bossSpawnWindows ??= Array.Empty<BossSpawnReceipt>();
        if(economy.worldContentVersion>=3&&catacombsGrid==null)throw new InvalidDataException("Grade das Catacumbas ausente; nenhum progresso foi alterado.");
        InstallWorldExpansion(lastTick);
        InstallMonsterCatalog(lastTick);
        InstallCatacombs(lastTick);
        var validation=EconomyRules.Validate(economy.active,null,MapFree);
        if(!validation.valid)throw new InvalidDataException("Economia salva inválida: "+string.Join("; ",validation.errors));
        EconomyRules.Register(economy);
        foreach(var state in loaded.players) RestorePlayer(state);
        if(loaded.world==null)
        {
            if(players.Count!=0)throw new InvalidDataException("Mundo ausente em banco com personagens.");
            monsters=MonsterCatalog.Resolve(economy.active).Where(p=>!p.isBoss).Select(CreateMonster).ToArray();
        }
        else
        {
            if(loaded.world.schemaVersion is <1 or >2 || loaded.world.monsters==null)throw new InvalidDataException("Versão de mundo inválida.");
            monsters=loaded.world.monsters;
            foreach(var monster in monsters)
            {
                monster.definitionId=monster.id;
                if(string.IsNullOrEmpty(monster.mapId))monster.mapId="ruins";
                if(monster.configVersion<1)monster.configVersion=economy.active.revision;
            }
            // Version-one encounters keep HP, position, respawn counter and pending attacks.
            monsters=monsters.Concat(MonsterCatalog.Resolve(economy.active).Where(p=>!p.isBoss&&!monsters.Any(m=>m.id==p.id)).Select(CreateMonster)).ToArray();
            ValidateMonsters();
        }
        AdvanceBossSchedules(lastTick);
        Commit(Array.Empty<Receipt>(),lastTick);
    }
    private SavedMonster CreateMonster(EconomyMonsterConfig profile) => new()
    { id=profile.id,definitionId=profile.id,mapId=profile.mapId,configVersion=economy.active.revision,spawn=NextSpawn(profile.id,profile.mapId),x=profile.x,z=profile.z,health=profile.health };
    private int NextSpawn(string id,string map)
    {
        string prefix=map+":"+id+":";
        int maximum=economy.rewardedDeaths.Where(d=>d.StartsWith(prefix,StringComparison.Ordinal)).Select(d=>int.TryParse(d[prefix.Length..],out int value)?value:0).DefaultIfEmpty(0).Max();
        return checked(maximum+1);
    }
    private EconomyConfig Config(int revision) => economy.releases.FirstOrDefault(r=>r.config.revision==revision)?.config ?? throw new InvalidDataException("Revisão econômica de encontro ausente.");
    private EconomyMonsterConfig Profile(SavedMonster monster) => MonsterCatalog.Resolve(Config(monster.configVersion)).FirstOrDefault(m=>m.id==monster.definitionId) ?? throw new InvalidDataException("Perfil de monstro ausente.");
    private bool MapFree(string map,float x,float z) => map=="city"?cityGrid.Free(x,z):map=="ruins"?grid.Free(x,z):map==CatacombsLayout.MapId&&catacombsGrid!=null&&catacombsGrid.Free(x,z);
    private CollisionGrid MapGrid(string map) => map switch { "city"=>cityGrid,"ruins"=>grid,CatacombsLayout.MapId when catacombsGrid!=null=>catacombsGrid,_=>throw new InvalidDataException("Grade do mapa ausente; progresso preservado.") };

    private void RestorePlayer(SavedPlayer state)
    {
        if (state == null || !Guid.TryParse(state.id, out _) || state.identityHash?.Length != 64 ||
            state.displayName == null || state.displayName.Length > 24 || !MapFree(state.mapId, state.x, state.z) ||
            !float.IsFinite(state.yaw) || !float.IsFinite(state.mana) || state.mana < 0 || state.mana > 100 ||
            !float.IsFinite(state.guardDuration) || state.guardDuration < 0 || !float.IsFinite(state.furyDuration) || state.furyDuration < 0 ||
            !double.IsFinite(state.movementCredit) || state.movementCredit < 0 || state.movementCredit > 1.5 ||
            state.cooldownUntil?.Length != 5 || state.cooldownUntil.Any(t => !double.IsFinite(t) || t < 0) ||
            new[] { state.guardUntil, state.furyUntil, state.healUntil, state.nextActionAt, state.nextAttackAt,
                state.lastResourceAt, state.lastMoveAt }.Any(t => !double.IsFinite(t) || t < 0) ||
            state.revision < 0 || state.savedAtUnixMs < 0)
            throw new InvalidDataException("Snapshot de personagem inválido; nenhum reset foi aplicado.");
        if (state.strike != null && (state.strike.damage <= 0 || state.strike.ability is < 0 or > 1 ||
            !double.IsFinite(state.strike.at) || !MapFree(state.mapId,state.strike.x,state.strike.z) || !float.IsFinite(state.strike.yaw)))
            throw new InvalidDataException("Golpe pendente inválido.");
        state.pendingLoot ??= Array.Empty<PendingLoot>();
        if(state.pendingLoot.Length>1000000 || state.pendingLoot.Any(l=>l==null||string.IsNullOrWhiteSpace(l.id)||l.quantity<1||l.definitionRevision<1||l.kind!="equipment"))
            throw new InvalidDataException("Saques pendentes inválidos.");
        var player = new Player { state = state, stats = PlayerProgression.FromSaveData(state.character) };
        players.Add(state.id, player);
        identities.Add(state.identityHash, state.id);
    }

    private void ValidateMonsters()
    {
        if(economy.bossSpawnWindows.Any(w=>w==null||string.IsNullOrEmpty(w.id)||string.IsNullOrEmpty(w.monsterId)||!double.IsFinite(w.startsAt)||!double.IsFinite(w.endsAt)||w.endsAt<=w.startsAt||!new[]{"active","defeated","expired","overlap-skipped"}.Contains(w.outcome)) ||
            economy.bossSpawnWindows.Select(w=>w.id).Distinct(StringComparer.Ordinal).Count()!=economy.bossSpawnWindows.Length)
            throw new InvalidDataException("Recibos de nascimento de chefão inválidos; mundo preservado.");
        var seen=new HashSet<string>();
        foreach(var m in monsters)
        {
            if(m==null||!seen.Add(m.id))throw new InvalidDataException("Monstro ausente/repetido.");
            var p=Profile(m);
            if (m.windupUntil > 0 && p.behavior != "melee" && (!m.attackPositionSet || !MapFree(m.mapId,m.attackX,m.attackZ)))
                throw new InvalidDataException("Alvo de ataque pendente inválido; mundo preservado.");
            if(m.spawn<1||m.health<0||m.health>p.health||m.mapId!=p.mapId||!MapFree(m.mapId,m.x,m.z)||!float.IsFinite(m.yaw)||
                (m.health==0&&m.respawnAt<=0)||(m.health>0&&m.respawnAt!=0)||new[]{m.windupUntil,m.nextAttackAt,m.respawnAt,m.expiresAt}.Any(t=>!double.IsFinite(t)||t<0))
                throw new InvalidDataException("Snapshot de monstro inválido; mundo preservado.");
        }
    }

    public OnlineSnapshot Connect(OnlineConnectRequest request, double? at = null)
    {
        if (request == null || request.protocolVersion != 2)
            throw new GameApiException(400, "protocol_invalid", "Versão de protocolo incompatível.");
        byte[] key;
        try { key = Convert.FromBase64String(request.deviceKey ?? ""); }
        catch (FormatException) { throw new GameApiException(400, "identity_invalid", "Identidade anônima inválida."); }
        if (key.Length != 32) throw new GameApiException(400, "identity_invalid", "A identidade precisa de 256 bits.");
        string identity = Convert.ToHexString(SHA256.HashData(key));
        CryptographicOperations.ZeroMemory(key);
        double now = at ?? UnixNow;
        lock (gate)
        {
            EnsureRecovered(now);
            bool existing = identities.TryGetValue(identity, out string existingId);
            // Validate uniqueness before Advance, save, character creation, or session replacement.
            // Older colliding labels remain accessible; their keys are reserved against new owners.
            string name = existing && request.preserveExistingName ? players[existingId].state.displayName
                : RequireAvailableNickname(request.displayName ?? "Aventureiro", existingId);
            string checkpoint = CaptureCheckpoint();
            Player player;
            try
            {
                Advance(now);
                if (!identities.TryGetValue(identity, out string id))
                {
                    var state = new SavedPlayer
                    {
                        id = Guid.NewGuid().ToString("D"), identityHash = identity, displayName = name,
                        character = new PlayerProgression().CaptureSaveData(), mapId="ruins", lastResourceAt = now, lastMoveAt = now
                    };
                    RestorePlayer(state);
                    id = state.id;
                }
                player = players[id];
                // Reconnection never restores health, mana, cooldowns, buffs or movement budget.
                if (!request.preserveExistingName) player.state.displayName = name;
                Commit(Array.Empty<Receipt>(), now);
            }
            catch { RecoverAfterFailure(checkpoint, now); throw; }
            foreach (string previous in sessions.Where(pair => pair.Value.playerId == player.state.id).Select(pair => pair.Key).ToArray())
                sessions.Remove(previous);
            string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            sessions[token] = new Session { playerId = player.state.id, lastSeen = now, expiresAt = now + 86400, rateWindow = now };
            OnlineSnapshot snapshot = Snapshot(player, 0, Array.Empty<OnlineCommandResult>(), now);
            snapshot.sessionToken = token;
            return snapshot;
        }
    }

    public OnlineSnapshot Commands(string token, OnlineCommandBatch batch, double? at = null)
    {
        if (batch == null || batch.protocolVersion != 2 || batch.commands == null || batch.commands.Length > 32 || batch.afterEventId < 0)
            throw new GameApiException(400, "batch_invalid", "Lote inválido: use até 32 comandos e protocolo 2.");
        double now = at ?? UnixNow;
        lock (gate)
        {
            if (token == null || !sessions.TryGetValue(token, out Session session) || session.expiresAt <= now)
                throw new GameApiException(401, "session_invalid", "Sessão expirada ou substituída. Conecte novamente.");
            if (now - session.rateWindow >= 1) { session.rateWindow = now; session.requestCount = 0; }
            if (++session.requestCount > 40) throw new GameApiException(429, "rate_limited", "Aguarde antes de enviar novos comandos.");
            EnsureRecovered(now);
            session.lastSeen=now;
            string checkpoint = CaptureCheckpoint();
            var receipts = new List<Receipt>();
            var results = new List<OnlineCommandResult>();
            try
            {
                Advance(now);
                Player player = players[session.playerId];
                foreach (OnlineCommand command in batch.commands)
                {
                    if (command == null || !Guid.TryParseExact(command.commandId, "D", out Guid parsedId))
                    {
                        results.Add(new OnlineCommandResult { commandId = command?.commandId, accepted = false, message = "commandId deve ser um UUID válido." });
                        continue;
                    }
                    command.commandId = parsedId.ToString("D");
                    string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(WireJson.Write(command))));
                    Receipt previous = receipts.FirstOrDefault(r => r.commandId == command.commandId) ?? store.FindReceipt(player.state.id, command.commandId);
                    if (previous != null)
                    {
                        results.Add(previous.requestHash == hash ? previous.result : new OnlineCommandResult
                        { commandId = command.commandId, accepted = false, message = "UUID já utilizado por outro comando." });
                        continue;
                    }
                    string reason = ApplyCommand(player, command, now);
                    var result = new OnlineCommandResult { commandId = command.commandId, accepted = reason == null, message = reason ?? string.Empty };
                    receipts.Add(new Receipt { playerId = player.state.id, commandId = command.commandId, requestHash = hash, result = result });
                    results.Add(result);
                }
                Commit(receipts, now);
                session.lastSeen = now;
                return Snapshot(player, batch.afterEventId, results.ToArray(), now);
            }
            catch { RecoverAfterFailure(checkpoint, now); throw; }
        }
    }

    public void Tick(double? at = null)
    {
        double now = at ?? UnixNow;
        lock (gate)
        {
            EnsureRecovered(now);
            if (now <= lastTick) return;
            if (!sessions.Values.Any(s => s.lastSeen > now - 15) && !economy.releases.Any(r=>r.status=="scheduled"&&r.activateAtUnixMs<=now*1000) && !BossScheduleNeedsTick(now)) { lastTick = now; return; }
            string checkpoint = CaptureCheckpoint();
            try { Advance(now); Commit(Array.Empty<Receipt>(), now); }
            catch { RecoverAfterFailure(checkpoint, now); throw; }
            foreach (string token in sessions.Where(pair => pair.Value.expiresAt <= now).Select(pair => pair.Key).ToArray()) sessions.Remove(token);
        }
    }

    private string ApplyCommand(Player player, OnlineCommand command, double now)
    {
        var state = player.state;
        switch (command.type)
        {
            case "portal": return Travel(player,command.portalId,now);
            case "buy": return Buy(player,command,now);
            case "sell": return Sell(player,command,now);
            case "collect": return Collect(player,command.lootId,now);
            case "potion": return DrinkPotion(player,now);
            case "allocate":
                return player.stats.TryAllocate((HeroAttribute)command.attribute, command.amount) ? null : "Pontos ou atributo inválidos.";
            case "equip":
                return player.stats.Equipment.TryEquip(command.itemId, (EquipmentSlot)command.slot, out string equipReason) ? null : equipReason;
            case "unequip":
                return player.stats.Equipment.TryUnequip((EquipmentSlot)command.slot, out string removeReason) ? null : removeReason;
            case "move":
                if (!player.stats.IsAlive) return "Personagem derrotado.";
                if (!float.IsFinite(command.x) || !float.IsFinite(command.z) || !float.IsFinite(command.yaw)) return "Movimento inválido.";
                double elapsed = Math.Max(0, now - state.lastMoveAt);
                // A persisted token bucket absorbs packet jitter. Only elapsed server time replenishes it;
                // no tolerance is regenerated by issuing extra commands or reconnecting.
                state.movementCredit = Math.Min(1.5, state.movementCredit + (command.running ? 5.5 : 3.2) * elapsed);
                state.lastMoveAt = now;
                double distance = Distance(state.x, state.z, command.x, command.z);
                if (distance > state.movementCredit + .000001) return "Deslocamento acima da velocidade permitida.";
                if (!MapGrid(state.mapId).ClearLine(state.x, state.z, command.x, command.z)) return "Movimento bloqueado pelo cenário.";
                state.movementCredit = Math.Max(0, state.movementCredit - distance);
                state.x = command.x; state.z = command.z;
                state.yaw = ((command.yaw % 360) + 360) % 360;
                state.lastMoveAt = now;
                return null;
            case "ability": return Cast(player, command.ability, command.targetId, now);
            case "respawn":
                if (player.stats.IsAlive) return "Só é possível renascer após a derrota.";
                player.stats.RestoreHealth(); state.x = 0; state.z = 0; state.yaw = 0;
                state.mapId="city";
                state.mana = 100; state.lastMoveAt = now; state.lastResourceAt = now;
                state.guardUntil = state.furyUntil = state.healUntil = 0; state.strike = null;
                Emit("respawn", state.id, state.id, 0, 0);
                return null;
            default: return "Tipo de comando desconhecido.";
        }
    }

    private string Cast(Player player, int ability, string targetId, double now)
    {
        var state = player.state;
        if(state.mapId=="city")return "A cidade é uma área segura.";
        if (ability is < 0 or > 4) return "Habilidade inválida.";
        if (!player.stats.IsAlive) return "Personagem derrotado.";
        if (now < state.nextActionAt || now < state.cooldownUntil[ability]) return "Habilidade em recarga.";
        if (ability == 2 && state.guardUntil > now) return "Guarda já está ativa.";
        if (ability == 4 && state.furyUntil > now) return "Fúria já está ativa.";
        if (ability == 3 && player.stats.Health >= player.stats.MaxHealth) return "A vida já está cheia.";
        if (ability <= 1 && (now < state.nextAttackAt || player.stats.EquippedWeapon == null)) return "Ataque indisponível; equipe uma espada e aguarde a recarga.";
        int cost = ability switch { 1 => 16, 2 => 20, 3 => 25, 4 => 25, _ => 0 };
        if (state.mana < cost) return "Mana insuficiente.";
        if (ability <= 1 && !string.IsNullOrEmpty(targetId))
        {
            var target=monsters.FirstOrDefault(m=>m.id==targetId);
            if(target==null||target.health<=0||target.returning||target.mapId!=state.mapId) return "Alvo indisponível.";
            float range=ability==1?3.15f:2.65f;
            if(Distance(state.x,state.z,target.x,target.z)>range) return "Aproxime-se do alvo para atacar.";
            if(!MapGrid(state.mapId).ClearLine(state.x,state.z,target.x,target.z))return "O cenário bloqueia o ataque ao alvo.";
            state.yaw=(float)(Math.Atan2(target.x-state.x,target.z-state.z)*180/Math.PI);
        }
        state.mana -= cost;
        state.nextActionAt = now + .2;
        state.cooldownUntil[ability] = now + (ability switch { 1 => 5, 2 => 12, 3 => 15, 4 => 20, _ => .65 });
        if (ability <= 1)
        {
            double multiplier = state.furyUntil > now ? 1.25 : 1;
            state.strike = new PendingStrike { at = now + .13, x = state.x, z = state.z, yaw = state.yaw, ability = ability,
                targetId = targetId, damage = ability == 0 ? player.stats.GetBasicDamage(multiplier) : player.stats.GetSkillDamage(1.8, true, multiplier) };
            state.nextAttackAt = now + .65;
            Emit("attack", state.id, null, ability, 0);
        }
        else if (ability == 2)
        {
            state.guardDuration = (float)player.stats.GetMagicDuration(4);
            state.guardUntil = now + state.guardDuration;
            Emit("attack", state.id, state.id, ability, 0);
        }
        else if (ability == 3)
        {
            int healed = player.stats.Heal((int)((long)player.stats.MaxHealth * 35 / 100));
            state.healUntil = now + 1.2;
            Emit("heal", state.id, state.id, ability, healed);
        }
        else
        {
            state.furyDuration = (float)player.stats.GetMagicDuration(8);
            state.furyUntil = now + state.furyDuration;
            Emit("attack", state.id, state.id, ability, 0);
        }
        return null;
    }

    private void Advance(double now)
    {
        ActivateScheduled(now);
        AdvanceBossSchedules(now);
        double dt = Math.Clamp(now - lastTick, 0, .25);
        lastTick = now;
        foreach (Player player in players.Values)
        {
            var state = player.state;
            if (player.stats.IsAlive)
                state.mana = Math.Min(100, state.mana + (float)(Math.Clamp(now - state.lastResourceAt, 0, 3600) * 8));
            state.lastResourceAt = now;
            if (state.strike != null && state.strike.at <= now)
            {
                PendingStrike strike = state.strike; state.strike = null;
                if (player.stats.IsAlive) ResolveStrike(player, strike, now);
            }
        }
        for (int index = 0; index < monsters.Length; index++) AdvanceMonster(index, now, dt);
    }

    private void ResolveStrike(Player player, PendingStrike strike, double now)
    {
        float range = strike.ability == 1 ? 3.15f : 2.65f;
        if(!string.IsNullOrEmpty(strike.targetId))
        {
            var target=monsters.FirstOrDefault(m=>m.id==strike.targetId&&m.mapId==player.state.mapId&&m.health>0&&!m.returning);
            if(target==null||Distance(strike.x,strike.z,target.x,target.z)>range||!MapGrid(player.state.mapId).ClearLine(strike.x,strike.z,target.x,target.z))return;
            strike.yaw=(float)(Math.Atan2(target.x-strike.x,target.z-strike.z)*180/Math.PI);
        }
        double minimumDot = Math.Cos(65 * Math.PI / 180);
        double radians = strike.yaw * Math.PI / 180;
        if(player.state.mapId=="city")return;
        for (int i = 0; i < monsters.Length; i++)
        {
            var monster = monsters[i];
            if (monster.mapId!=player.state.mapId || monster.health <= 0 || monster.returning) continue;
            double distance = Distance(strike.x, strike.z, monster.x, monster.z);
            if (distance > range || (strike.ability == 0 && distance > .01 &&
                ((monster.x - strike.x) * Math.Sin(radians) + (monster.z - strike.z) * Math.Cos(radians)) / distance < minimumDot)) continue;
            if (!MapGrid(player.state.mapId).ClearLine(strike.x, strike.z, monster.x, monster.z)) continue;
            var profile = Profile(monster);
            if (Distance(monster.x, monster.z, profile.x, profile.z) > profile.leashRadius ||
                Distance(player.state.x, player.state.z, profile.x, profile.z) > profile.leashRadius) continue;
            int applied = Math.Min(strike.damage, monster.health);
            monster.health -= applied;
            RecordDamage(monster,player,applied,now);
            monster.targetId = player.state.id;
            Emit("hit", player.state.id, monster.id, strike.ability, applied);
            if (monster.health == 0)
            {
                monster.respawnAt = now + profile.respawnDelay; monster.windupUntil = 0; monster.targetId = null;
                if(profile.isBoss)
                {
                    monster.respawnAt=Math.Max(now,monster.expiresAt);
                    var window=economy.bossSpawnWindows.FirstOrDefault(w=>w.id==monster.spawnWindowId);
                    if(window!=null)window.outcome="defeated";
                }
                GrantDeath(monster,profile,strike.ability,now);
                Emit("death", player.state.id, monster.id, strike.ability, 0);
            }
        }
    }

    private bool Online(string id, double now) => sessions.Values.Any(s => s.playerId == id && s.lastSeen > now - 15 && s.expiresAt > now);

    private void AdvanceMonster(int index, double now, double dt)
    {
        var monster = monsters[index];
        var profile = Profile(monster);
        if (monster.health <= 0)
        {
            if(profile.isBoss) return;
            if(!MonsterCatalog.Resolve(economy.active).Any(p=>p.id==monster.definitionId))
            {
                // A rollback may retire a spawn introduced later. Finish its pinned life, then remove it.
                monsters=monsters.Where(m=>m.id!=monster.id).ToArray();
                return;
            }
            if (monster.respawnAt <= now)
            {
                monster.configVersion=economy.active.revision;
                profile=Profile(monster);
                monster.contributions=Array.Empty<DamageContribution>();
                monster.health = profile.health; monster.mapId = profile.mapId; monster.x = profile.x; monster.z = profile.z;
                monster.spawn++; monster.respawnAt = 0; monster.returning = false; monster.targetId = null;
                monster.windupUntil = monster.nextAttackAt = 0;
                Emit("respawn", monster.id, monster.id, 0, 0);
            }
            return;
        }
        Player target = monster.targetId != null && players.TryGetValue(monster.targetId, out Player candidate) ? candidate : null;
        if (monster.targetId != null && (target == null || !target.stats.IsAlive || target.state.mapId!=monster.mapId || !Online(monster.targetId, now) ||
            Distance(monster.x, monster.z, profile.x, profile.z) > profile.leashRadius ||
            Distance(target.state.x, target.state.z, profile.x, profile.z) > profile.leashRadius))
        { monster.returning = true; monster.windupUntil = 0; monster.targetId = null; target = null; }
        if (monster.returning)
        {
            if (Distance(monster.x, monster.z, profile.x, profile.z) < .15)
            {
                monster.x = profile.x; monster.z = profile.z; monster.health = profile.health;
                monster.contributions=Array.Empty<DamageContribution>();
                monster.returning = false; monster.nextAttackAt = now + .5;
            }
            else MoveMonster(monster, profile.x, profile.z, profile.moveSpeed * 1.25 * dt);
            return;
        }
        if(target==null && profile.aggroRadius>0)
        {
            target=players.Values.Where(p=>p.stats.IsAlive&&p.state.mapId==monster.mapId&&Online(p.state.id,now)&&Distance(p.state.x,p.state.z,profile.x,profile.z)<=profile.leashRadius&&Distance(p.state.x,p.state.z,monster.x,monster.z)<=profile.aggroRadius&&MapGrid(monster.mapId).ClearLine(monster.x,monster.z,p.state.x,p.state.z))
                .OrderBy(p=>Distance(p.state.x,p.state.z,monster.x,monster.z)).ThenBy(p=>p.state.id,StringComparer.Ordinal).FirstOrDefault();
            if(target!=null)monster.targetId=target.state.id;
        }
        if (target == null) return;
        float targetX = target.state.x, targetZ = target.state.z;
        double distance = Distance(monster.x, monster.z, targetX, targetZ);
        monster.yaw = (float)(Math.Atan2(targetX - monster.x, targetZ - monster.z) * 180 / Math.PI);
        if (profile.behavior != "melee")
        {
            AdvanceSpecialAttack(monster, profile, target, now, dt);
            return;
        }
        if (monster.windupUntil > 0)
        {
            if (distance > profile.attackRange + .15) { monster.windupUntil = 0; monster.nextAttackAt = now + .25; }
            else if (now >= monster.windupUntil)
            {
                monster.windupUntil = 0; monster.nextAttackAt = now + Math.Max(.1, profile.attackInterval - profile.attackPreparation);
                int applied = 0;
                bool canHit = distance <= profile.attackRange && MapGrid(monster.mapId).ClearLine(monster.x, monster.z, targetX, targetZ);
                if (canHit)
                {
                    applied = target.stats.ReceiveDamage(profile.damage, target.state.guardUntil > now ? .5 : 1);
                }
                // A completed swing has presentation even when it misses. Canceled windups never reach here.
                Emit("monsterStrike", monster.id, target.state.id, 0, applied);
                if (canHit)
                {
                    Emit("playerHit", monster.id, target.state.id, 0, applied);
                    if (!target.stats.IsAlive)
                    {
                        target.state.guardUntil = target.state.furyUntil = target.state.healUntil = 0;
                        target.state.strike = null;
                        Emit("death", monster.id, target.state.id, 0, 0);
                    }
                }
            }
        }
        else if (distance > profile.attackRange)
            MoveMonster(monster, targetX, targetZ, Math.Min(profile.moveSpeed * dt, distance - (profile.attackRange-.15)));
        else if (now >= monster.nextAttackAt && MapGrid(monster.mapId).ClearLine(monster.x, monster.z, targetX, targetZ)) monster.windupUntil = now + profile.attackPreparation;
    }

    private void MoveMonster(SavedMonster monster, float targetX, float targetZ, double length)
    {
        double distance = Distance(monster.x, monster.z, targetX, targetZ);
        if (distance < .001) return;
        double fraction = Math.Min(1, Math.Max(0, length) / distance);
        float nextX = monster.x + (float)((targetX - monster.x) * fraction);
        float nextZ = monster.z + (float)((targetZ - monster.z) * fraction);
        var movementGrid=MapGrid(monster.mapId);
        if (movementGrid.ClearLine(monster.x, monster.z, nextX, nextZ)) { monster.x = nextX; monster.z = nextZ; }
        else if (movementGrid.ClearLine(monster.x, monster.z, nextX, monster.z)) monster.x = nextX;
        else if (movementGrid.ClearLine(monster.x, monster.z, monster.x, nextZ)) monster.z = nextZ;
    }

    private static double Distance(float ax, float az, float bx, float bz) => Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));
    private void Emit(string kind, string sourceId, string targetId, int ability, int amount)
    {
        events.Add(new OnlineWorldEvent { eventId = ++latestEventId, kind = kind, sourceId = sourceId, targetId = targetId, ability = ability, amount = amount, mapId = EventMap(sourceId,targetId) });
        if (events.Count > 128) events.RemoveAt(0);
    }

    private OnlinePlayerState PlayerState(Player player) => new()
    {
        id = player.state.id, displayName = player.state.displayName, mapId=player.state.mapId,
        x = player.state.x, z = player.state.z, yaw = player.state.yaw,
        health = player.stats.Health, maxHealth = player.stats.MaxHealth, mana = player.state.mana,
        guardUntil = player.state.guardUntil, furyUntil = player.state.furyUntil, healUntil = player.state.healUntil,
        guardDuration = player.state.guardDuration, furyDuration = player.state.furyDuration,
        nextActionAt = player.state.nextActionAt, nextAttackAt = player.state.nextAttackAt,
        cooldownUntil = (double[])player.state.cooldownUntil.Clone(), equipped = PublicEquipment(player)
    };

    private OnlineSnapshot Snapshot(Player player, long afterEventId, OnlineCommandResult[] results, double now) => new()
    {
        playerId=player.state.id,mapId=player.state.mapId,economyVersion=economy.active.revision,
        serverTime=now,saveRevision=player.state.revision,savedAtUnixMs=player.state.savedAtUnixMs,
        latestEventId=latestEventId,character=player.stats.CaptureSaveData(),self=PlayerState(player),
        itemDefinitions=economy.releases.SelectMany(r=>r.config.items).Concat(economy.active.items).GroupBy(i=>i.definitionId+":"+i.definitionRevision).Select(g=>g.Last()).ToArray(),
        bossWindows=BossWindowSnapshot(now),npcs=NpcSnapshot(player),portals=AvailablePortals.Where(p=>p.mapId==player.state.mapId).ToArray(),loot=LootSnapshot(player),
        players=players.Values.Where(p=>p.state.id!=player.state.id&&p.state.mapId==player.state.mapId&&Online(p.state.id,now)).Select(PlayerState).ToArray(),
        monsters=monsters.Where(m=>m.mapId==player.state.mapId).Select(m=> {var p=Profile(m);return new OnlineMonsterState
        { id=m.id,definitionId=m.definitionId,speciesId=Config(m.configVersion).monsterSpawns.FirstOrDefault(s=>s.id==m.id)?.speciesId??m.id,behavior=p.behavior,attackPreparation=p.attackPreparation,attackInterval=p.attackInterval,respawnDelay=p.respawnDelay,areaRadius=p.areaRadius,attackX=m.attackX,attackZ=m.attackZ,name=p.name,mapId=m.mapId,level=p.level,visualKind=p.visualKind,isBoss=p.isBoss,visualScale=p.visualScale,attackRange=p.attackRange,moveSpeed=p.moveSpeed,aggroRadius=p.aggroRadius,leashRadius=p.leashRadius,targetId=m.targetId,spawnWindowId=m.spawnWindowId,expiresAt=m.expiresAt,spawn=m.spawn,x=m.x,z=m.z,yaw=m.yaw,health=m.health,maxHealth=p.health,damage=p.damage,experience=p.experience,returning=m.returning,windupUntil=m.windupUntil,respawnAt=m.respawnAt };}).ToArray(),
        events=events.Where(e=>e.mapId==player.state.mapId&&(afterEventId>latestEventId||e.eventId>afterEventId)).ToArray(),results=results
    };
    private EquipmentSlotSave[] PublicEquipment(Player player) => player.stats.CaptureSaveData().equipped.Select(s=>new EquipmentSlotSave{slot=s.slot,itemId=player.stats.Equipment.GetDefinitionId(s.itemId),definitionId=s.definitionId,definitionRevision=s.definitionRevision}).ToArray();
    private string EventMap(string source,string target)
    {
        if(source!=null&&players.TryGetValue(source,out var p))return p.state.mapId;
        if(target!=null&&players.TryGetValue(target,out p))return p.state.mapId;
        return monsters.FirstOrDefault(m=>m.id==source||m.id==target)?.mapId??"ruins";
    }

    private SavedPlayer[] CapturePlayers()
    {
        foreach (Player player in players.Values) player.state.character = player.stats.CaptureSaveData();
        return players.Values.Select(p => p.state).ToArray();
    }
    private SavedWorld CaptureWorld() => new() { monsters = monsters, economy=economy };
    private string CaptureCheckpoint() => WireJson.Write(new Checkpoint
    {
        players = CapturePlayers(), world = CaptureWorld(), events = events.ToArray(), eventId = latestEventId, lastTick = lastTick
    });
    private void RestoreCheckpoint(string json)
    {
        Checkpoint checkpoint = WireJson.Read<Checkpoint>(json);
        economy=checkpoint.world.economy;
        EconomyRules.Register(economy);
        players.Clear(); identities.Clear();
        foreach (var state in checkpoint.players) RestorePlayer(state);
        monsters = checkpoint.world.monsters;
        events.Clear(); events.AddRange(checkpoint.events); latestEventId = checkpoint.eventId; lastTick = checkpoint.lastTick;
    }
    private void Commit(IEnumerable<Receipt> receipts, double now)
    {
        foreach (Player player in players.Values)
        {
            player.state.revision++;
            player.state.savedAtUnixMs = (long)(now * 1000);
        }
        store.Save(CapturePlayers(), CaptureWorld(), receipts);
        EconomyRules.Register(economy);
    }

    private void RecoverAfterFailure(string checkpoint, double now)
    {
        RestoreCheckpoint(checkpoint);
        recoveryRequired = true;
        // A commit error can have an ambiguous outcome. Read the database before accepting
        // further writes instead of risking an old in-memory snapshot overwriting a committed one.
        try { EnsureRecovered(now); }
        catch { /* Keep the recovery gate closed until a subsequent database read succeeds. */ }
    }

    private void EnsureRecovered(double now)
    {
        if (!recoveryRequired) return;
        var loaded = store.Load();
        if (loaded.world == null || loaded.world.schemaVersion != 2 || loaded.world.monsters == null)
            throw new InvalidDataException("Não foi possível recuperar o mundo confirmado no banco.");
        string persisted = WireJson.Write(new Checkpoint
        {
            players = loaded.players.ToArray(), world = loaded.world, events = events.ToArray(),
            eventId = latestEventId, lastTick = now
        });
        try { RestoreCheckpoint(persisted); }
        catch (ArgumentException error) { throw new InvalidDataException("Snapshot confirmado inválido; novas escritas estão bloqueadas.", error); }
        ValidateMonsters();
        recoveryRequired = false;
    }
}

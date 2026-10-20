using System.Security.Cryptography;

namespace GuerraDasNacoes.Server;

public sealed partial class GameWorld
{
    // Inspection intentionally does not Advance, Commit, Connect, create a session, or rename.
    public OnlineCharacterPreview CharacterPreview(OnlineConnectRequest request)
    {
        if (request == null || request.protocolVersion != 2)
            throw new GameApiException(400, "protocol_invalid", "Versão de protocolo incompatível.");
        byte[] key;
        try { key = Convert.FromBase64String(request.deviceKey ?? ""); }
        catch (FormatException) { throw new GameApiException(400, "identity_invalid", "Identidade anônima inválida."); }
        if (key.Length != 32) throw new GameApiException(400, "identity_invalid", "A identidade precisa de 256 bits.");
        string identity = Convert.ToHexString(SHA256.HashData(key));
        CryptographicOperations.ZeroMemory(key);
        lock (gate)
        {
            if (recoveryRequired) throw new GameApiException(503, "storage_unavailable", "Aguarde a recuperação do progresso salvo.");
            if (!identities.TryGetValue(identity, out string id)) return new OnlineCharacterPreview();
            Player player = players[id];
            return new OnlineCharacterPreview
            {
                exists = true, playerId = id, displayName = player.state.displayName,
                level = player.stats.Level, mapId = player.state.mapId,
                equipped = PublicEquipment(player),
                itemDefinitions = WireJson.Read<EconomyItemConfig[]>(WireJson.Write(economy.releases.SelectMany(r => r.config.items)
                    .Concat(economy.active.items).GroupBy(i => i.definitionId + ":" + i.definitionRevision).Select(g => g.Last()).ToArray()))
            };
        }
    }

    public OnlineDisconnectResult Disconnect(string token, double? at = null)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128)
            throw new GameApiException(401, "session_invalid", "Informe uma sessão válida.");
        lock (gate)
        {
            if (!sessions.TryGetValue(token, out Session session)) return new OnlineDisconnectResult { disconnected = true };
            if (recoveryRequired) throw new GameApiException(503, "storage_unavailable", "Aguarde a confirmação do progresso salvo antes de sair.");
            Player player = players[session.playerId];
            if (player.stats.IsAlive && (player.state.strike != null || monsters.Any(m => m.targetId == player.state.id && m.health > 0 && !m.returning)))
                throw new GameApiException(409, "combat_active", "Saia de combate e aguarde o golpe terminar antes de trocar de personagem.");
            // All character mutations were committed before acknowledgement. Session removal is
            // immediate and must not heal, tick, move, respawn, or reset cooldowns and buffs.
            sessions.Remove(token);
            return new OnlineDisconnectResult { disconnected = true, message = "Progresso preservado. Sessão encerrada." };
        }
    }
}

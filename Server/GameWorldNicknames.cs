using System.Security.Cryptography;

namespace GuerraDasNacoes.Server;

public sealed partial class GameWorld
{
    public int LegacyNicknameCollisionCount
    {
        get
        {
            lock (gate) return players.Values.GroupBy(p => CharacterNickname.Key(p.state.displayName), StringComparer.Ordinal)
                .Count(group => group.Count() > 1);
        }
    }

    private string RequireAvailableNickname(string supplied, string ownPlayerId)
    {
        string nickname;
        try { nickname = CharacterNickname.Normalize(supplied); }
        catch (ArgumentException error) { throw new GameApiException(400, "nickname_invalid", error.Message); }
        string key = CharacterNickname.Key(nickname);
        if (players.Values.Any(p => p.state.id != ownPlayerId && CharacterNickname.Key(p.state.displayName) == key))
            throw new GameApiException(409, "nickname_taken", "Este nick já está em uso. Escolha outro para o seu personagem.");
        return nickname;
    }

    public OnlineNicknameAvailability NicknameAvailability(OnlineNicknameCheckRequest request)
    {
        if (request == null || request.protocolVersion != 2)
            throw new GameApiException(400, "protocol_invalid", "Versão de protocolo incompatível.");
        string nickname;
        try { nickname = CharacterNickname.Normalize(request.nickname); }
        catch (ArgumentException error) { throw new GameApiException(400, "nickname_invalid", error.Message); }
        string identityHash = null;
        if (!string.IsNullOrEmpty(request.deviceKey))
        {
            byte[] key;
            try { key = Convert.FromBase64String(request.deviceKey); }
            catch (FormatException) { throw new GameApiException(400, "identity_invalid", "Identidade anônima inválida."); }
            if (key.Length != 32) throw new GameApiException(400, "identity_invalid", "A identidade precisa de 256 bits.");
            identityHash = Convert.ToHexString(SHA256.HashData(key));
            CryptographicOperations.ZeroMemory(key);
        }
        lock (gate)
        {
            if (recoveryRequired) throw new GameApiException(503, "storage_unavailable", "Aguarde a recuperação do progresso salvo.");
            string ownId = identityHash != null && identities.TryGetValue(identityHash, out string id) ? id : null;
            string nicknameKey = CharacterNickname.Key(nickname);
            var owners = players.Values.Where(p => CharacterNickname.Key(p.state.displayName) == nicknameKey).ToArray();
            bool available = owners.All(p => p.state.id == ownId);
            return new OnlineNicknameAvailability
            {
                exists = owners.Length > 0, available = available, nickname = nickname,
                message = available ? "Nick disponível para este personagem." : "Este nick já está em uso."
            };
        }
    }
}

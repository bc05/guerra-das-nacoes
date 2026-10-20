using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace GuerraDasNacoes.Server;

public static class NicknameSelfTests
{
    private static int assertions;
    private static void Check(bool condition, string reason)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException("NICKNAME-SELF-TEST: " + reason);
    }
    private static void Reject(Action action, int status, string code, string reason)
    {
        try { action(); }
        catch (GameApiException error) { Check(error.Status == status && error.Code == code, reason); return; }
        Check(false, reason);
    }
    private static CollisionGrid Grid() => new() { originX = -18, originZ = -18, cellSize = .25f,
        width = 144, height = 144, blocked = new int[144 * 144] };
    private static OnlineConnectRequest Identity(string nickname) => new()
        { deviceKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), displayName = nickname };
    private static OnlineSnapshot Poll(GameWorld world, string token, double now = 1000) =>
        world.Commands(token, new OnlineCommandBatch { commands = Array.Empty<OnlineCommand>() }, now);

    public static void Run()
    {
        assertions = 0;
        string directory = Path.Combine(Path.GetTempPath(), "gdn-nickname-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Check(CharacterNickname.Normalize("  Herói   Azul  ") == "Herói Azul", "normaliza bordas e espaços repetidos");
            Check(CharacterNickname.Normalize("Ａｓｔｅｒ") == "Aster", "normaliza largura Unicode");
            Check(CharacterNickname.Key(" Guardião ") == CharacterNickname.Key("GUARDIÃO"), "chave ignora caixa e bordas");
            Check(CharacterNickname.Normalize("Lâmina_2-Azul") == "Lâmina_2-Azul", "aceita acentos, números e separadores permitidos");
            var store = new SaveStore(Path.Combine(directory, "characters.db"));
            var world = new GameWorld(store, Grid(), 1000);
            string before = WireJson.Write(store.Load());
            var check = world.NicknameAvailability(new OnlineNicknameCheckRequest { nickname = "  Aster  " });
            Check(check.available && !check.exists && check.nickname == "Aster", "consulta normaliza e informa nome livre");
            Check(WireJson.Write(store.Load()) == before, "consulta de disponibilidade não grava nem cria personagem");
            Reject(() => world.NicknameAvailability(new() { nickname = "A" }), 400, "nickname_invalid", "recusa nome curto");
            Reject(() => world.NicknameAvailability(new() { nickname = new string('x', 25) }), 400, "nickname_invalid", "recusa nome longo");
            Reject(() => world.NicknameAvailability(new() { nickname = "Hero<script>" }), 400, "nickname_invalid", "recusa símbolos não permitidos");
            Reject(() => world.NicknameAvailability(new() { nickname = "Hero\nAzul" }), 400, "nickname_invalid", "recusa controle no nome");
            Reject(() => world.NicknameAvailability(new() { nickname = "Aster", deviceKey = "invalid" }), 400, "identity_invalid", "consulta valida identidade opcional");
            Reject(() => world.NicknameAvailability(new() { nickname = "Aster", protocolVersion = 1 }), 400, "protocol_invalid", "consulta valida versão");

            var first = Identity("  Aster  ");
            var connected = world.Connect(first, 1000);
            Check(connected.self.displayName == "Aster", "criação persiste nick normalizado");
            Check(!world.NicknameAvailability(new() { nickname = "ASTER" }).available, "outro usuário vê nick ocupado");
            check = world.NicknameAvailability(new() { nickname = "ａｓｔｅｒ", deviceKey = first.deviceKey });
            Check(check.available && check.exists, "titular consegue consultar o próprio nick");
            var second = Identity("ASTER");
            before = WireJson.Write(store.Load());
            Reject(() => world.Connect(second, 1000), 409, "nickname_taken", "outro personagem não pode usar mesmo nick com outra caixa");
            second.displayName = "  Ａｓｔｅｒ  ";
            Reject(() => world.Connect(second, 1000), 409, "nickname_taken", "largura Unicode não contorna unicidade");
            Check(WireJson.Write(store.Load()) == before, "conflito não altera banco ou revisão");
            Check(!world.CharacterPreview(second).exists, "conflito não cria personagem substituto");
            Check(Poll(world, connected.sessionToken).playerId == connected.playerId, "conflito mantém sessão do titular");

            second.displayName = "Lyra";
            var other = world.Connect(second, 1000);
            first.displayName = " lyra ";
            before = WireJson.Write(store.Load());
            Reject(() => world.Connect(first, 1000), 409, "nickname_taken", "renomeação legada também respeita unicidade");
            Check(WireJson.Write(store.Load()) == before && Poll(world, connected.sessionToken).self.displayName == "Aster",
                "renomeação recusada preserva sessão e nome anteriores");
            first.preserveExistingName = true;
            first.displayName = "x";
            var reconnect = world.Connect(first, 1000);
            Check(reconnect.self.displayName == "Aster", "reentrada protegida ignora nome inválido de interface antiga");
            world = new GameWorld(store, Grid(), 1000);
            Check(!world.NicknameAvailability(new() { nickname = "aster" }).available, "unicidade permanece após reiniciar");
            second.displayName = "ａｓｔｅｒ";
            Reject(() => world.Connect(second, 1000), 409, "nickname_taken", "reinício mantém reserva de nick");

            var concurrent = new ConcurrentBag<bool>();
            Parallel.For(0, 12, index =>
            {
                var request = Identity(index % 2 == 0 ? "Centelha" : " ＣＥＮＴＥＬＨＡ ");
                try { world.Connect(request, 1000); concurrent.Add(true); }
                catch (GameApiException error) when (error.Code == "nickname_taken") { concurrent.Add(false); }
            });
            Check(concurrent.Count(v => v) == 1 && concurrent.Count == 12, "conexões concorrentes produzem um único titular");

            // Historical collisions must remain visible to operators without resetting a hero.
            var legacy = store.Load();
            legacy.players.Single(p => p.id == other.playerId).displayName = " ASTER ";
            store.Save(legacy.players, legacy.world, Array.Empty<Receipt>());
            world = new GameWorld(store, Grid(), 1000);
            Check(world.LegacyNicknameCollisionCount == 1, "colisão histórica é identificada para reconciliação");
            Check(world.Connect(first, 1000).playerId == connected.playerId, "colisão histórica não impede acesso ao progresso protegido");
            Check(!world.NicknameAvailability(new() { nickname = "Aster" }).available, "colisão histórica bloqueia novos titulares");
            Console.WriteLine($"PASS: {assertions} verificações de nick, normalização, unicidade, concorrência e preservação de acesso.");
        }
        finally { Directory.Delete(directory, true); }
    }
}

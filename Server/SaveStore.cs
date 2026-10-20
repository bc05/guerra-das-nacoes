using Microsoft.Data.Sqlite;

namespace GuerraDasNacoes.Server;

public sealed class SavedPlayer
{
    public string id, identityHash, displayName;
    public string mapId = "ruins";
    public PendingLoot[] pendingLoot = Array.Empty<PendingLoot>();
    public CharacterSaveData character;
    public float x, z, yaw, mana = 100, guardDuration, furyDuration;
    public double movementCredit = .15;
    public double guardUntil, furyUntil, healUntil, nextActionAt, nextAttackAt, lastResourceAt, lastMoveAt;
    public double[] cooldownUntil = new double[5];
    public PendingStrike strike;
    public long revision, savedAtUnixMs;
}

public sealed class PendingStrike
{
    public int damage, ability;
    public string targetId;
    public float x, z, yaw;
    public double at;
}

public sealed class SavedMonster
{
    public string id, targetId;
    public string definitionId = "troll-saqueador", mapId = "ruins";
    public int configVersion = 1;
    public DamageContribution[] contributions = Array.Empty<DamageContribution>();
    public int spawn = 1, health;
    public float x, z, yaw;
    public bool returning;
    public string spawnWindowId;
    public double expiresAt;
    public double windupUntil, nextAttackAt, respawnAt;
    // Ground target is fixed at cast start and survives a restart during the warning.
    public float attackX, attackZ;
    public bool attackPositionSet;
}

public sealed class SavedWorld
{
    public int schemaVersion = 2;
    public SavedMonster[] monsters;
    public EconomyWorldState economy;
}

public sealed class Receipt
{
    public string playerId, commandId, requestHash;
    public OnlineCommandResult result;
}

public sealed class SaveStore
{
    private readonly string connectionString;
    public bool FailNextSaveForTests;
    public bool FailAfterCommitForTests;

    public SaveStore(string path)
    {
        string absolute = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute));
        connectionString = new SqliteConnectionStringBuilder { DataSource = absolute, Pooling = false }.ToString();
        using var connection = Open();
        using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "PRAGMA user_version";
        int version = Convert.ToInt32(versionCommand.ExecuteScalar());
        if (version > 2) throw new InvalidDataException("Banco foi criado por uma versão mais nova; nenhum dado foi alterado.");
        if (version == 0)
        {
            using var existing = connection.CreateCommand();
            existing.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
            if (Convert.ToInt64(existing.ExecuteScalar()) != 0)
                throw new InvalidDataException("Banco sem versão com tabelas existentes; restauração manual necessária.");
            using var migration = connection.BeginTransaction();
            using var create = connection.CreateCommand();
            create.Transaction = migration;
            create.CommandText = """
                CREATE TABLE players (
                    player_id TEXT PRIMARY KEY,
                    identity_hash TEXT NOT NULL UNIQUE,
                    state_json TEXT NOT NULL
                );
                CREATE TABLE world (id INTEGER PRIMARY KEY CHECK(id=1), state_json TEXT NOT NULL);
                CREATE TABLE command_receipts (
                    player_id TEXT NOT NULL REFERENCES players(player_id),
                    command_id TEXT NOT NULL,
                    request_hash TEXT NOT NULL,
                    result_json TEXT NOT NULL,
                    PRIMARY KEY(player_id, command_id)
                );
                PRAGMA user_version=1;
                """;
            create.ExecuteNonQuery();
            migration.Commit();
        }
        if (version == 1)
        {
            // BackupDatabase includes confirmed WAL contents; copying a live .db alone does not.
            string backup = absolute + ".pre-v2-" + DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + ".bak";
            using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backup, Pooling = false }.ToString()))
            {
                destination.Open();
                connection.BackupDatabase(destination);
            }
            using var upgrade = connection.BeginTransaction();
            using var versionUpdate = connection.CreateCommand();
            versionUpdate.Transaction = upgrade;
            versionUpdate.CommandText = "PRAGMA user_version=2";
            versionUpdate.ExecuteNonQuery();
            upgrade.Commit();
        }
        if (version == 0)
        {
            using var versionUpdate = connection.CreateCommand();
            versionUpdate.CommandText = "PRAGMA user_version=2";
            versionUpdate.ExecuteNonQuery();
        }
        using var integrity = connection.CreateCommand();
        integrity.CommandText = "PRAGMA quick_check";
        if (!string.Equals(Convert.ToString(integrity.ExecuteScalar()), "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Banco corrompido; o servidor preservou o arquivo e recusou a inicialização.");
        using var wal = connection.CreateCommand();
        wal.CommandText = "PRAGMA journal_mode=WAL";
        wal.ExecuteScalar();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var settings = connection.CreateCommand();
        settings.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000; PRAGMA synchronous=FULL;";
        settings.ExecuteNonQuery();
        return connection;
    }

    public (List<SavedPlayer> players, SavedWorld world) Load()
    {
        using var connection = Open();
        var players = new List<SavedPlayer>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT player_id,identity_hash,state_json FROM players ORDER BY player_id";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                SavedPlayer player = WireJson.Read<SavedPlayer>(reader.GetString(2));
                if (player == null || player.id != reader.GetString(0) || player.identityHash != reader.GetString(1))
                    throw new InvalidDataException("Registro de personagem inconsistente; banco não foi reiniciado.");
                players.Add(player);
            }
        }
        using var worldCommand = connection.CreateCommand();
        worldCommand.CommandText = "SELECT state_json FROM world WHERE id=1";
        var json = worldCommand.ExecuteScalar() as string;
        return (players, json == null ? null : WireJson.Read<SavedWorld>(json));
    }

    public Receipt FindReceipt(string playerId, string commandId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT request_hash,result_json FROM command_receipts WHERE player_id=$player AND command_id=$command";
        command.Parameters.AddWithValue("$player", playerId);
        command.Parameters.AddWithValue("$command", commandId);
        using var reader = command.ExecuteReader();
        return !reader.Read() ? null : new Receipt
        {
            playerId = playerId, commandId = commandId, requestHash = reader.GetString(0),
            result = WireJson.Read<OnlineCommandResult>(reader.GetString(1))
        };
    }

    // Snapshot, world and receipts commit as a unit. A failed commit must never produce a successful ACK.
    public void Save(IEnumerable<SavedPlayer> players, SavedWorld world, IEnumerable<Receipt> receipts)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        foreach (SavedPlayer player in players)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO players(player_id,identity_hash,state_json) VALUES($id,$identity,$state)
                ON CONFLICT(player_id) DO UPDATE SET state_json=excluded.state_json;
                """;
            command.Parameters.AddWithValue("$id", player.id);
            command.Parameters.AddWithValue("$identity", player.identityHash);
            command.Parameters.AddWithValue("$state", WireJson.Write(player));
            command.ExecuteNonQuery();
        }
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO world(id,state_json) VALUES(1,$state) ON CONFLICT(id) DO UPDATE SET state_json=excluded.state_json";
            command.Parameters.AddWithValue("$state", WireJson.Write(world));
            command.ExecuteNonQuery();
        }
        foreach (Receipt receipt in receipts)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO command_receipts(player_id,command_id,request_hash,result_json) VALUES($player,$id,$hash,$result)";
            command.Parameters.AddWithValue("$player", receipt.playerId);
            command.Parameters.AddWithValue("$id", receipt.commandId);
            command.Parameters.AddWithValue("$hash", receipt.requestHash);
            command.Parameters.AddWithValue("$result", WireJson.Write(receipt.result));
            command.ExecuteNonQuery();
        }
        if (FailNextSaveForTests)
        {
            FailNextSaveForTests = false;
            throw new IOException("Falha de gravação simulada pelo teste.");
        }
        transaction.Commit();
        if (FailAfterCommitForTests)
        {
            FailAfterCommitForTests = false;
            throw new IOException("Resultado de commit ambíguo simulado pelo teste.");
        }
    }
}

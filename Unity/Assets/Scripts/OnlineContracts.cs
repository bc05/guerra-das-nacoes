using System;

namespace GuerraDasNacoes
{
    [Serializable] public sealed class OnlineNicknameCheckRequest
    {
        public int protocolVersion = 2;
        public string nickname, deviceKey;
    }

    [Serializable] public sealed class OnlineNicknameAvailability
    {
        public bool exists, available;
        public string nickname, message;
    }

    [Serializable] public sealed class OnlineCharacterPreview
    {
        public int protocolVersion = 2;
        public bool exists;
        public string playerId, displayName, mapId;
        public int level;
        public EquipmentSlotSave[] equipped = Array.Empty<EquipmentSlotSave>();
        public EconomyItemConfig[] itemDefinitions = Array.Empty<EconomyItemConfig>();
    }

    [Serializable] public sealed class OnlineDisconnectResult
    {
        public bool disconnected;
        public string message;
    }

    // Public fields keep this wire contract compatible with Unity JsonUtility and System.Text.Json.
    [Serializable] public sealed class OnlineConnectRequest
    {
        public int protocolVersion = 2;
        public string deviceKey;
        public string displayName;
        public bool preserveExistingName;
    }

    [Serializable] public sealed class OnlineCommand
    {
        public string commandId;
        public string type;
        public int attribute;
        public int amount = 1;
        public string itemId;
        public string npcId, offerId, portalId, lootId, targetId;
        public int quoteVersion;
        public int slot;
        public int ability;
        public float x, z, yaw;
        public bool running;
    }

    [Serializable] public sealed class OnlineCommandBatch
    {
        public int protocolVersion = 2;
        public long afterEventId;
        public OnlineCommand[] commands;
    }

    [Serializable] public sealed class OnlineCommandResult
    {
        public string commandId;
        public bool accepted;
        public string message;
    }

    [Serializable] public sealed class OnlinePlayerState
    {
        public string id, displayName, mapId;
        public float x, z, yaw;
        public int health, maxHealth;
        public float mana;
        public float guardDuration, furyDuration;
        public double guardUntil, furyUntil, healUntil, nextActionAt, nextAttackAt;
        public double[] cooldownUntil;
        public EquipmentSlotSave[] equipped;
    }

    [Serializable] public sealed class OnlineMonsterState
    {
        public string id, definitionId, name, mapId;
        public string visualKind, targetId, spawnWindowId;
        public string speciesId, behavior = "melee";
        public float attackPreparation = .65f, attackInterval = 1.25f, respawnDelay = 6f, areaRadius = 3f;
        public float attackX, attackZ;
        public bool isBoss;
        public float visualScale = 1;
        public float attackRange, moveSpeed, aggroRadius, leashRadius;
        public double expiresAt;
        public int level;
        public int spawn;
        public float x, z, yaw;
        public int health, maxHealth, damage, experience;
        public bool returning;
        public double windupUntil, respawnAt;
    }

    [Serializable] public sealed class OnlineBossWindow
    {
        public string id, monsterId, name, timeZone;
        public double nextSpawnAt, endsAt;
        public bool active;
    }

    [Serializable] public sealed class OnlineWorldEvent
    {
        public long eventId;
        public string kind, sourceId, targetId, mapId;
        public int ability, amount;
    }

    [Serializable] public sealed class OnlineSnapshot
    {
        public int protocolVersion = 2;
        public string playerId, sessionToken, mapId;
        public int economyVersion;
        public OnlineNpc[] npcs;
        public OnlinePortal[] portals;
        public OnlineLoot[] loot;
        public EconomyItemConfig[] itemDefinitions;
        public double serverTime;
        public long saveRevision, savedAtUnixMs, latestEventId;
        public CharacterSaveData character;
        public OnlinePlayerState self;
        public OnlinePlayerState[] players;
        public OnlineMonsterState[] monsters;
        public OnlineBossWindow[] bossWindows;
        public OnlineWorldEvent[] events;
        public OnlineCommandResult[] results;
    }

    [Serializable] public sealed class OnlineShopOffer
    {
        public string id, definitionId, name;
        public long price, sellPrice;
        public int quoteVersion, definitionRevision;
    }
    [Serializable] public sealed class OnlineNpc
    {
        public string id, name, role, mapId;
        public float x, z;
        public OnlineShopOffer[] offers;
    }
    [Serializable] public sealed class OnlinePortal
    {
        public string id, mapId, destinationMapId;
        public float x, z;
    }
    [Serializable] public sealed class OnlineLoot
    {
        public string id, definitionId, name, mapId, kind;
        public float x, z;
        public int quantity, definitionRevision;
        public bool recoverable;
    }

    [Serializable] public sealed class OnlineError
    {
        public string code, message;
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace GuerraDasNacoes
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PrototypeAttackEffects), typeof(PrototypeAbilities), typeof(PrototypeAreaEffects))]
    public sealed class PrototypeCombat : MonoBehaviour
    {
        [SerializeField] private PrototypeMonster monster;
        [SerializeField] private float attackRange = 2.65f;
        [SerializeField] private float attackInterval = 0.65f;
        [SerializeField] private PrototypeMonster[] encounterMonsters;
        private const float StrikeDelay = 0.13f;
        public const float AttackPresentationDuration = 0.6f;
        private const int EnemyMask = 1 << 9;
        private const int ObstacleMask = ~((1 << 8) | (1 << 9) | (1 << 10));

        private PlayerProgression stats;
        private float nextAttackTime;
        private float attackUntil;
        private int requestedAttackSlot = -1;
        private float requestedAttackUntil;
        private long attackIntentVersion;
        private float messageUntil;
        private PrototypeAttackEffects attackEffects;
        private Animator attackAnimator;
        private bool hasAttackAnimation;
        private PrototypeAttackAnimation attackAnimation;
        private PrototypeAbilities abilities;
        private PrototypeAreaEffects areaEffects;
        private readonly Collider[] overlapResults = new Collider[64];
        private readonly HashSet<PrototypeMonster> struck = new HashSet<PrototypeMonster>();
        private bool strikePending;
        private float strikeAt, strikeRadius, strikeAngle;
        private int strikeDamage;
        private Vector3 strikeOrigin, strikeDirection;
        private bool resolvingStrike;
        private int strikeExperience;
        private long lastOnlineEventId = -1;
        private string submittedAttackId;
        private int submittedAttackSlot = -1;
        private long submittedAfterEventId;
        private long submittedAttackIntentVersion;
        private int submittedNextBasicVariant;
        private int submittedBasicSequenceVersion;
        public bool HasSubmittedAttackPresentation => !string.IsNullOrEmpty(submittedAttackId);
        private GameObject monsterTemplateRoot;
        private GameObject monsterTemplate;
        private readonly Dictionary<string, PrototypeMonster> onlineMonsters = new Dictionary<string, PrototypeMonster>();
        private static PrototypeOnlineClient Network => PrototypeOnlineClient.Active;
        private static bool UsesServer => Network != null && Network.IsOnlineMode;

        public PlayerProgression Stats => stats ?? (stats = new PlayerProgression());
        public bool IsAlive => Stats.IsAlive;
        public bool IsAttacking => IsAlive && Time.time < attackUntil;
        public float AttackProgress => IsAttacking ? 1f - (attackUntil - Time.time) / AttackPresentationDuration : 0f;
        public float CooldownRemaining => Mathf.Max(0f, nextAttackTime - Time.time);
        public float AttackInterval => attackInterval;
        public float AttackRange => attackRange;
        public bool CanAttack => !PrototypeGameMenu.BlocksGameplay && isActiveAndEnabled && IsAlive && Stats.EquippedWeapon != null &&
            (!UsesServer || (Network.CanPlay && !Network.HasPendingMutation)) && Time.time >= nextAttackTime &&
            (abilities == null || abilities.IsActionReady);
        public PrototypeMonster Target { get; private set; }
        public PrototypeMonster ValidTarget => IsValidTarget(Target) ? Target : null;
        public string SelectedTargetId => ValidTarget != null ? ValidTarget.OnlineId : null;
        public bool HasQueuedAttack => requestedAttackSlot >= 0;
        // Feet stay planted through contact, then regain movement progressively in the recovery.
        public float AttackMovementMultiplier => !IsAttacking ? 1f :
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.48f, 1f, AttackProgress));
        public PrototypeMonster Monster => monster;
        public IReadOnlyList<PrototypeMonster> Monsters => encounterMonsters ?? System.Array.Empty<PrototypeMonster>();
        public int LastHitCount { get; private set; }
        public int SwingCount { get; private set; }
        public bool HasPendingStrike => strikePending;
        public string Message { get; private set; }
        public bool HasMessage => Time.time < messageUntil && !string.IsNullOrEmpty(Message);
        public int LastDamage { get; private set; }
        public float LastDamageTime { get; private set; } = -100f;
        public int LastExperience { get; private set; }
        public float LastExperienceTime { get; private set; } = -100f;

        private void Awake()
        {
            CachePristineMonsterTemplate();
            attackEffects = GetComponent<PrototypeAttackEffects>();
            abilities = GetComponent<PrototypeAbilities>();
            areaEffects = GetComponent<PrototypeAreaEffects>();
            attackAnimator = GetComponentInChildren<Animator>();
            attackAnimation = new PrototypeAttackAnimation(attackAnimator);
            hasAttackAnimation = attackAnimation.HasTrigger;
        }

        private void CachePristineMonsterTemplate()
        {
            if (monster == null || monsterTemplate != null) return;
            // An inactive parent prevents Awake, AI and colliders from running in this hidden source.
            // Cache before the first combat pose/death/variant; meshes and materials remain shared.
            monsterTemplateRoot = new GameObject("Molde visual de monstros") { hideFlags = HideFlags.HideAndDontSave };
            monsterTemplateRoot.SetActive(false);
            monsterTemplate = Instantiate(monster.gameObject, monsterTemplateRoot.transform, true);
            monsterTemplate.name = "Troll original preservado";
            monsterTemplate.hideFlags = HideFlags.HideAndDontSave;
        }

        public void Configure(PrototypeMonster enemy, PrototypeMonster[] enemies = null)
        {
            monster = enemy;
            encounterMonsters = enemies ?? new[] { enemy };
        }

        private void Update()
        {
            if (Target != null && !IsValidTarget(Target)) SetTarget(null);
            if (HasQueuedAttack && (!IsAlive || Stats.EquippedWeapon == null || PrototypeHud.BlocksGameplay ||
                (UsesServer && !Network.CanPlay) || ValidTarget == null || Time.time >= requestedAttackUntil))
                CancelRequestedAttack();
            if (strikePending && Time.time >= strikeAt)
            {
                strikePending = false;
                if (IsAlive) ResolveAreaStrike();
            }
        }

        public void SetTarget(PrototypeMonster enemy)
        {
            PrototypeMonster next = IsValidTarget(enemy) ? enemy : null;
            if (Target != next) CancelRequestedAttack();
            Target = next;
        }

        private bool IsValidTarget(PrototypeMonster enemy) => enemy != null && enemy.IsAvailableForTargeting &&
            Mathf.Abs(enemy.transform.position.y - transform.position.y) <= 3f;

        public void CancelRequestedAttack()
        {
            requestedAttackSlot = -1;
            attackIntentVersion++;
        }

        // Each explicit command requests one strike; selecting a target never schedules it.
        // This is an intention, not damage: a distant selected monster is approached first.
        // Neither mana, cooldown nor an online command is committed while walking into range.
        public bool RequestAttack(int slot)
        {
            if ((slot != 0 && slot != 1) || abilities == null || !abilities.CanUse(slot)) return false;
            // A late response to an older command cannot cancel this newer manual intent.
            CancelRequestedAttack();
            if (ValidTarget == null)
            {
                return abilities.ExecuteAttackAbility(slot);
            }
            requestedAttackSlot = slot;
            requestedAttackUntil = Time.time + 15f;
            return TryExecuteRequestedAttack() || HasQueuedAttack;
        }

        public bool TryGetApproachDestination(out Vector3 point)
        {
            point = transform.position;
            PrototypeMonster target = ValidTarget;
            if (!HasQueuedAttack || target == null) return false;
            Vector3 toward = Vector3.ProjectOnPlane(target.transform.position - transform.position, Vector3.up);
            float range = requestedAttackSlot == 1 ? 3.15f : attackRange;
            // Leave a margin for a moving target and the server's next simulation step.
            float stoppingDistance = Mathf.Max(.6f, range - .38f);
            if (toward.magnitude <= stoppingDistance) return false;
            point = target.transform.position - toward.normalized * stoppingDistance;
            point.y = transform.position.y;
            return true;
        }

        public bool TryExecuteRequestedAttack()
        {
            if (!HasQueuedAttack || !CanAttack) return false;
            PrototypeMonster target = ValidTarget;
            if (target == null) { CancelRequestedAttack(); return false; }
            float range = requestedAttackSlot == 1 ? 3.15f : attackRange;
            Vector3 offset = Vector3.ProjectOnPlane(target.transform.position - transform.position, Vector3.up);
            if (offset.magnitude > range - .22f || Mathf.Abs(target.transform.position.y - transform.position.y) > 1.5f)
                return false;
            if (!HasClearLineTo(target))
            {
                CancelRequestedAttack();
                Notify("O alvo está atrás de um obstáculo. Escolha um caminho livre.");
                return false;
            }
            if (offset.sqrMagnitude > .001f && Vector3.Angle(transform.forward, offset) > 9f) return false;
            int slot = requestedAttackSlot;
            requestedAttackSlot = -1;
            return abilities.ExecuteAttackAbility(slot);
        }

        // Also used by the online transport immediately before serializing the command.
        // The authoritative server still validates identity, map, range, line of sight and damage.
        public bool PrepareTargetedAttack(int slot = 0)
        {
            PrototypeMonster target = ValidTarget;
            if (target == null)
            {
                if (Target != null) { SetTarget(null); return false; }
                return true;
            }
            Vector3 offset = Vector3.ProjectOnPlane(target.transform.position - transform.position, Vector3.up);
            float range = slot == 1 ? 3.15f : attackRange;
            if (offset.sqrMagnitude > range * range ||
                Mathf.Abs(target.transform.position.y - transform.position.y) > 1.5f || !HasClearLineTo(target)) return false;
            if (offset.sqrMagnitude > .001f) transform.rotation = Quaternion.LookRotation(offset);
            return true;
        }

        private bool HasClearLineTo(PrototypeMonster enemy)
        {
            Vector3 origin = transform.position + Vector3.up * .95f;
            Vector3 ray = enemy.transform.position + Vector3.up * .95f - origin;
            return ray.sqrMagnitude <= .001f || !Physics.Raycast(origin, ray.normalized, ray.magnitude,
                ObstacleMask, QueryTriggerInteraction.Ignore);
        }

        public bool TryAttack()
        {
            if (!PrepareTargetedAttack()) return false;
            if (UsesServer) return CanAttack && Network.TryUseAbility(0);
            return TryAttackDamage(Stats.GetBasicDamage(abilities != null ? abilities.OutgoingDamageMultiplier : 1d));
        }

        internal bool TryAttackDamage(int damage, bool heavy = false)
        {
            if (UsesServer || damage <= 0 || !isActiveAndEnabled || !IsAlive || Stats.EquippedWeapon == null || Time.time < nextAttackTime ||
                (abilities != null && !abilities.IsActionReady))
                return false;
            if (!PrepareTargetedAttack(heavy ? 1 : 0)) return false;
            // Keep the committed sector stable; selecting another monster cannot redirect a strike in flight.
            strikeOrigin = transform.position;
            strikeDirection = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            strikeRadius = heavy ? 3.15f : attackRange;
            strikeAngle = heavy ? 360f : 130f;
            strikeDamage = damage;
            strikeAt = Time.time + StrikeDelay;
            strikePending = true;
            LastHitCount = 0;
            SwingCount++;
            nextAttackTime = Time.time + attackInterval;
            attackUntil = Time.time + AttackPresentationDuration;
            if (abilities != null)
                abilities.CommitAction();
            attackAnimation.PlayNext(heavy);
            if (attackEffects != null)
                attackEffects.PlaySwing();
            if (areaEffects != null)
                areaEffects.Play(strikeOrigin, strikeDirection, strikeRadius, strikeAngle, heavy);
            PrototypeAudio.Play(heavy ? GameSound.HeavySwing : GameSound.SwordSwing, strikeOrigin);
            return true;
        }

        private void ResolveAreaStrike()
        {
            struck.Clear();
            resolvingStrike = true;
            strikeExperience = 0;
            try
            {
                int count = Physics.OverlapSphereNonAlloc(strikeOrigin + Vector3.up, strikeRadius + 0.6f,
                    overlapResults, EnemyMask, QueryTriggerInteraction.Ignore);
                float minimumDot = Mathf.Cos(strikeAngle * 0.5f * Mathf.Deg2Rad);
                for (int index = 0; index < count; index++)
                {
                    PrototypeMonster enemy = overlapResults[index].GetComponentInParent<PrototypeMonster>();
                    if (enemy == null || !struck.Add(enemy) || !enemy.IsAlive || enemy.IsReturning) continue;
                    Vector3 offset = enemy.transform.position - strikeOrigin;
                    if (Mathf.Abs(offset.y) > 1.5f) continue;
                    offset.y = 0f;
                    if (offset.sqrMagnitude > strikeRadius * strikeRadius ||
                        (strikeAngle < 359f && offset.sqrMagnitude > 0.01f &&
                         Vector3.Dot(strikeDirection, offset.normalized) < minimumDot)) continue;
                    if (!HasClearStrike(enemy)) continue;
                    int applied = enemy.TakeDamage(strikeDamage, this);
                    if (applied <= 0) continue;
                    LastHitCount++;
                    PrototypeAudio.Play(GameSound.BladeHit, enemy.transform.position, .85f);
                    // One impact spark accompanies the first hit; every victim has its own damage number.
                    if (LastHitCount == 1 && attackEffects != null)
                        attackEffects.ShowImpact(enemy.transform.position + Vector3.up * 1.1f);
                }
            }
            finally
            {
                resolvingStrike = false;
                if (strikeExperience > 0) GrantExperience(strikeExperience);
                strikeExperience = 0;
            }
        }

        private bool HasClearStrike(PrototypeMonster enemy)
        {
            Vector3 origin = strikeOrigin + Vector3.up * 0.95f;
            Vector3 ray = enemy.transform.position + Vector3.up * 0.95f - origin;
            return !Physics.Raycast(origin, ray.normalized, ray.magnitude, ObstacleMask, QueryTriggerInteraction.Ignore);
        }

        public int ReceiveDamage(int amount)
        {
            if (UsesServer) return 0;
            // Defense and temporary protection compose before a single rounding step.
            int applied = Stats.ReceiveDamage(amount, abilities != null ? abilities.IncomingDamageMultiplier : 1d);
            if (applied > 0)
            {
                LastDamage = applied;
                LastDamageTime = Time.time;
                PresentPlayerHit(applied);
                if (!IsAlive)
                {
                    CancelRequestedAttack();
                    attackUntil = 0f;
                    strikePending = false;
                    if (abilities != null)
                        abilities.CancelActiveEffects();
                    if (attackEffects != null)
                        attackEffects.Cancel();
                    if (areaEffects != null) areaEffects.Cancel();
                    Notify("Você foi derrotado. Pressione R para renascer.");
                }
            }
            return applied;
        }

        public void GrantExperience(int amount)
        {
            if (UsesServer) return;
            if (!IsAlive || amount <= 0)
                return;
            if (resolvingStrike)
            {
                strikeExperience = (int)System.Math.Min(int.MaxValue, (long)strikeExperience + amount);
                return;
            }
            int gained = Stats.GrantExperience(amount);
            if (gained > 0) PrototypeAudio.Play(GameSound.LevelUp, transform.position);
            LastExperience = amount;
            LastExperienceTime = Time.time;
            Notify(gained > 0
                ? $"Nível {Stats.Level}! +{amount} XP · +{gained * CombatScaling.PointsPerLevel} pontos de atributos. C para distribuir."
                : $"Vitória! +{amount} XP");
        }

        public void ResetEncounter()
        {
            if (UsesServer)
            {
                if (!Network.TryRespawn()) Notify("Renascer exige conexão com o servidor e personagem derrotado.");
                return;
            }
            ClearSubmittedAttackPresentation();
            Stats.RestoreHealth();
            if (abilities != null)
                abilities.ResetEncounter();
            nextAttackTime = 0f;
            attackUntil = 0f;
            attackAnimation?.ResetSequence();
            strikePending = false;
            LastHitCount = 0;
            if (attackEffects != null)
                attackEffects.Cancel();
            if (areaEffects != null) areaEffects.Cancel();
            if (hasAttackAnimation)
            {
                attackAnimator.ResetTrigger("Attack");
                attackAnimator.Play("Idle", 0, 0f);
            }
            LastDamageTime = -100f;
            LastExperienceTime = -100f;
            Target = null;
            CancelRequestedAttack();
            foreach (PrototypeMonster enemy in Monsters)
                if (enemy != null) enemy.ResetEncounter();
            Notify("Vida restaurada. Nível, experiência, atributos e equipamentos foram mantidos.");
        }

        public void ApplyOnlineSnapshot(OnlineSnapshot snapshot)
        {
            if (snapshot == null || snapshot.character == null || snapshot.self == null)
                throw new System.ArgumentException("O servidor enviou um personagem incompleto.");
            PlayerProgression restored = PlayerProgression.FromSaveData(snapshot.character);
            int previousLevel = stats != null ? stats.Level : restored.Level;
            stats = restored;
            strikePending = false;
            nextAttackTime = Time.time + (float)System.Math.Max(0d, snapshot.self.nextAttackAt - snapshot.serverTime);
            if (abilities != null) abilities.ApplyOnlineState(snapshot.self, snapshot.serverTime);
            SynchronizeOnlineMonsters(snapshot);
            if (snapshot.monsters != null)
                foreach (OnlineMonsterState value in snapshot.monsters)
                {
                    PrototypeMonster enemy = FindOnlineMonster(value.id);
                    if (enemy != null)
                    {
                        enemy.ApplyOnlineState(value, snapshot.serverTime);
                        PrototypeWorldExpansion.ConfigureMonsterVisual(enemy, value);
                    }
                }
            if (snapshot.latestEventId < lastOnlineEventId) lastOnlineEventId = -1;
            int rewardedExperience = 0;
            if (snapshot.events != null)
                foreach (OnlineWorldEvent value in snapshot.events)
                {
                    if (value.eventId <= lastOnlineEventId) continue;
                    lastOnlineEventId = value.eventId;
                    if (value.kind == "attack" && value.ability >= 0 && value.ability <= 1 && value.sourceId == snapshot.playerId)
                    {
                        bool alreadyPresented = HasSubmittedAttackPresentation && submittedAttackSlot == value.ability &&
                            value.eventId > submittedAfterEventId;
                        if (alreadyPresented) ClearSubmittedAttackTracking();
                        else PlayOnlineSwing(value.ability == 1);
                    }
                    if (value.kind == "attack" && (value.ability == 2 || value.ability == 4) &&
                        TryFindPlayerPosition(snapshot, value.sourceId, out Vector3 casterPosition))
                        PrototypeAudio.Play(value.ability == 2 ? GameSound.Guard : GameSound.Fury,
                            casterPosition);
                    if (value.kind == "monsterStrike")
                    {
                        PrototypeMonster attacker = FindOnlineMonster(value.sourceId);
                        if (attacker != null)
                        {
                            if (!TryFindPlayerPosition(snapshot, value.targetId, out Vector3 contact))
                                contact = attacker.transform.position + attacker.transform.forward * 1.45f;
                            attacker.PlayOnlineStrike(contact, value.amount > 0);
                        }
                    }
                    if (value.kind == "hit")
                    {
                        PrototypeMonster enemy = FindOnlineMonster(value.targetId);
                        if (enemy != null)
                        {
                            enemy.RecordOnlineHit(value.amount);
                            PrototypeAudio.Play(GameSound.BladeHit, enemy.transform.position, .85f);
                            if (value.sourceId == snapshot.playerId)
                            {
                                LastHitCount++;
                                if (attackEffects != null) attackEffects.ShowImpact(enemy.transform.position + Vector3.up * 1.1f);
                            }
                        }
                    }
                    if (value.kind == "playerHit" && value.targetId == snapshot.playerId)
                    {
                        LastDamage = value.amount;
                        LastDamageTime = Time.time;
                        if (value.amount > 0) PresentPlayerHit(value.amount);
                    }
                    if (value.kind == "experience" && value.targetId == snapshot.playerId)
                    {
                        rewardedExperience = (int)System.Math.Min(int.MaxValue, (long)rewardedExperience + value.amount);
                    }
                    if (value.kind == "heal" && value.targetId == snapshot.playerId && abilities != null)
                        abilities.StatusEffects.PlayHealing();
                    if (value.kind == "heal" && TryFindPlayerPosition(snapshot, value.targetId, out Vector3 healPosition))
                        PrototypeAudio.Play(GameSound.Heal, healPosition);
                    if (value.kind == "death")
                    {
                        PrototypeMonster defeated = FindOnlineMonster(value.targetId);
                        if (defeated != null)
                            PrototypeAudio.Play(GameSound.MonsterDeath, defeated.transform.position, 1f, defeated.MaxHealth > 240);
                    }
                    if (value.kind == "death" && value.targetId == snapshot.playerId)
                        Notify("Você foi derrotado. R para renascer no servidor.");
                }
            lastOnlineEventId = System.Math.Max(lastOnlineEventId, snapshot.latestEventId);
            if (!IsAlive || Stats.EquippedWeapon == null || (HasQueuedAttack && ValidTarget == null))
                CancelRequestedAttack();
            if (!IsAlive)
            {
                attackUntil = 0f;
                if (attackEffects != null) attackEffects.Cancel();
                if (areaEffects != null) areaEffects.Cancel();
            }
            if (snapshot.results != null)
                foreach (OnlineCommandResult result in snapshot.results)
                    if (!string.IsNullOrEmpty(result.message)) Notify(result.message);
            if (rewardedExperience > 0)
            {
                if (stats.Level > previousLevel) PrototypeAudio.Play(GameSound.LevelUp, transform.position);
                LastExperience = rewardedExperience;
                LastExperienceTime = Time.time;
                Notify(stats.Level > previousLevel
                    ? $"Nível {stats.Level}! +{rewardedExperience} XP · pontos salvos no servidor."
                    : $"Vitória! +{rewardedExperience} XP · progresso salvo.");
            }
        }

        private void SynchronizeOnlineMonsters(OnlineSnapshot snapshot)
        {
            if (onlineMonsters.Count == 0 && encounterMonsters != null)
                for (int index = 0; index < encounterMonsters.Length; index++)
                    if (encounterMonsters[index] != null) onlineMonsters["troll-" + index] = encounterMonsters[index];
            HashSet<string> present = new HashSet<string>();
            PrototypeCity maps = GetComponent<PrototypeCity>();
            if (snapshot.monsters != null)
                foreach (OnlineMonsterState value in snapshot.monsters)
                {
                    if (value == null || string.IsNullOrEmpty(value.id) || value.mapId != snapshot.mapId) continue;
                    present.Add(value.id);
                    Transform parent = maps != null ? maps.MonsterRoot(value.mapId) : null;
                    if (maps != null && parent == null) continue;
                    if (onlineMonsters.TryGetValue(value.id, out PrototypeMonster existing) && existing != null)
                    {
                        if (parent != null && existing.transform.parent != parent) existing.transform.SetParent(parent, true);
                        continue;
                    }
                    if (monsterTemplate == null) CachePristineMonsterTemplate();
                    if (monsterTemplate == null) continue;
                    GameObject copy = Instantiate(monsterTemplate, parent, true);
                    copy.hideFlags = HideFlags.None;
                    copy.SetActive(true);
                    copy.name = value.name + " · " + value.id;
                    // Cached opponents remain under their own map, including when that root is inactive.
                    if (value.id.StartsWith("sentinel", System.StringComparison.Ordinal)) copy.transform.localScale *= 1.12f;
                    PrototypeMonster created = copy.GetComponent<PrototypeMonster>();
                    created.BindOnlinePlayer(this);
                    PrototypeWorldExpansion.ConfigureMonsterVisual(created, value);
                    onlineMonsters[value.id] = created;
                }
            bool inCombatMap = snapshot.mapId == "ruins" || snapshot.mapId == "catacombs";
            foreach (KeyValuePair<string, PrototypeMonster> entry in onlineMonsters)
                if (entry.Value != null) entry.Value.gameObject.SetActive(inCombatMap && present.Contains(entry.Key));
            List<PrototypeMonster> active = new List<PrototypeMonster>();
            if (inCombatMap)
                foreach (KeyValuePair<string, PrototypeMonster> entry in onlineMonsters)
                    if (entry.Value != null && present.Contains(entry.Key)) active.Add(entry.Value);
            encounterMonsters = active.ToArray();
        }

        public void CancelMapPresentation()
        {
            ClearSubmittedAttackPresentation();
            CancelRequestedAttack();
            Target = null;
            strikePending = false;
            attackUntil = 0f;
            if (abilities != null) abilities.CancelActiveEffects();
            if (attackEffects != null) attackEffects.Cancel();
            if (areaEffects != null) areaEffects.Cancel();
        }

        private PrototypeMonster FindOnlineMonster(string id)
        {
            if (id != null && onlineMonsters.TryGetValue(id, out PrototypeMonster enemy)) return enemy;
            if (encounterMonsters == null) return null;
            for (int index = 0; index < encounterMonsters.Length; index++)
                if (id == "troll-" + index) return encounterMonsters[index];
            return null;
        }

        // Latency compensation is limited to animation/sound/trail. No hit, resource or progression is predicted.
        // The transport supplies its immutable command UUID, so a rejection cannot cancel a newer swing.
        public void PresentSubmittedAttack(int slot, string commandId)
        {
            if ((slot != 0 && slot != 1) || string.IsNullOrEmpty(commandId) || !IsAlive ||
                Stats.EquippedWeapon == null || HasSubmittedAttackPresentation) return;
            submittedAttackId = commandId;
            submittedAttackSlot = slot;
            submittedAfterEventId = lastOnlineEventId;
            submittedAttackIntentVersion = attackIntentVersion;
            submittedNextBasicVariant = attackAnimation.NextBasicVariant;
            PlayOnlineSwing(slot == 1);
            submittedBasicSequenceVersion = attackAnimation.BasicSequenceVersion;
        }

        public void ResolveSubmittedAttack(string commandId, bool accepted)
        {
            if (!HasSubmittedAttackPresentation || submittedAttackId != commandId) return;
            if (accepted) ClearSubmittedAttackTracking();
            else ClearSubmittedAttackPresentation(submittedAttackIntentVersion == attackIntentVersion);
        }

        private void ClearSubmittedAttackTracking()
        {
            submittedAttackId = null;
            submittedAttackSlot = -1;
            submittedAfterEventId = -1;
            submittedAttackIntentVersion = -1;
            submittedNextBasicVariant = 0;
            submittedBasicSequenceVersion = -1;
        }

        public void ClearSubmittedAttackPresentation()
        {
            ClearSubmittedAttackPresentation(true);
        }

        private void ClearSubmittedAttackPresentation(bool cancelIntent)
        {
            // A late rejection of the previous target must not erase a newer mouse click.
            if (cancelIntent) CancelRequestedAttack();
            bool wasPending = HasSubmittedAttackPresentation;
            if (wasPending && submittedAttackSlot == 0)
                attackAnimation.RestoreBasicSequence(submittedBasicSequenceVersion, submittedNextBasicVariant);
            ClearSubmittedAttackTracking();
            if (!wasPending) return;
            attackUntil = 0f;
            if (attackEffects != null) attackEffects.Cancel();
            if (areaEffects != null) areaEffects.Cancel();
            if (hasAttackAnimation && attackAnimator.isActiveAndEnabled)
            {
                attackAnimator.ResetTrigger("Attack");
                attackAnimator.CrossFadeInFixedTime("Idle", .08f, 0);
            }
        }

        private void PlayOnlineSwing(bool heavy)
        {
            LastHitCount = 0;
            SwingCount++;
            attackUntil = Time.time + AttackPresentationDuration;
            attackAnimation.PlayNext(heavy);
            if (attackEffects != null) attackEffects.PlaySwing();
            if (areaEffects != null)
                areaEffects.Play(transform.position, transform.forward, heavy ? 3.15f : attackRange, heavy ? 360f : 130f, heavy);
            PrototypeAudio.Play(heavy ? GameSound.HeavySwing : GameSound.SwordSwing, transform.position);
        }

        private bool TryFindPlayerPosition(OnlineSnapshot snapshot, string playerId, out Vector3 position)
        {
            position = transform.position;
            if (playerId == snapshot.playerId) return true;
            if (snapshot.players != null)
                foreach (OnlinePlayerState other in snapshot.players)
                    if (other != null && other.id == playerId)
                    {
                        position = new Vector3(other.x, transform.position.y, other.z);
                        return true;
                    }
            return false;
        }

        private void PresentPlayerHit(int amount)
        {
            PrototypeAudio.Play(GameSound.HeroHurt, transform.position);
            Camera camera = Camera.main;
            PrototypeCamera follow = camera != null ? camera.GetComponent<PrototypeCamera>() : null;
            if (follow != null) follow.RequestImpact(Mathf.Clamp(amount * .003f, .025f, .055f));
        }

        public void Notify(string message)
        {
            Message = message;
            messageUntil = Time.time + 4f;
        }

        private void OnDestroy()
        {
            if (monsterTemplateRoot != null) Destroy(monsterTemplateRoot);
        }

        private void OnDisable()
        {
            ClearSubmittedAttackPresentation();
            CancelRequestedAttack();
            Target = null;
            strikePending = false;
            attackUntil = 0f;
            if (abilities != null) abilities.CancelActiveEffects();
            if (attackEffects != null) attackEffects.Cancel();
            if (areaEffects != null) areaEffects.Cancel();
        }
    }
}

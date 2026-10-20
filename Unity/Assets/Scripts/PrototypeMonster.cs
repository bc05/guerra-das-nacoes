using System.Collections.Generic;
using UnityEngine;

namespace GuerraDasNacoes
{
    [RequireComponent(typeof(CharacterController))]
    [DefaultExecutionOrder(100)]
    public sealed class PrototypeMonster : MonoBehaviour
    {
        private enum EncounterState { Passive, Chasing, PreparingAttack, Returning, Dead }

        [SerializeField] private PrototypeCombat player;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private string displayName = "Guardião das Ruínas";
        [SerializeField] private int maxHealth = 60;
        [SerializeField] private int attackDamage = 8;
        [SerializeField] private int experienceReward = 60;
        [SerializeField] private int level = 1;
        [SerializeField] private bool isBoss;
        [SerializeField] private float aggroRadius = 5f;
        [SerializeField] private float walkSpeed = 2.25f;
        [SerializeField] private float leashRadius = 8f;
        [SerializeField] private float attackRange = 1.65f;
        [SerializeField] private float attackInterval = 1.25f;
        [SerializeField] private float attackPreparation = 0.65f;
        [SerializeField] private float respawnDelay = 6f;
        [SerializeField] private string speciesId;
        [SerializeField] private string behavior = "melee";
        [SerializeField] private float areaRadius = 3f;

        private CharacterController controller;
        private PrototypeCombat target;
        private EncounterState state;
        private Vector3 homePosition;
        private Quaternion homeRotation;
        private Vector3 visualPosition;
        private Quaternion visualRotation;
        private Transform leftArm;
        private Transform rightArm;
        private Transform leftLeg, rightLeg;
        private Quaternion leftArmRotation;
        private Quaternion rightArmRotation;
        private Quaternion leftLegRotation, rightLegRotation;
        private Vector3 leftLegPosition, rightLegPosition;
        private float stride;
        private float gaitPhase;
        private float legLength = 1.3f;
        private Vector3 lastPosePosition;
        private bool visualMoving;
        private float verticalSpeed;
        private float attackCooldown;
        private float windupRemaining;
        private Vector3 attackPosition;
        private float respawnRemaining;
        private float flashRemaining;
        private float strikeRemaining;
        private float strikeStartedAt = -100f;
        private const float StrikePresentationDuration = .48f;
        private float presentationPreparation;
        private float preparationHoldUntil;
        private PrototypeMonsterAttackEffects attackEffects;
        private readonly List<BonePose> articulatedBones = new List<BonePose>();
        private BonePose leftShoulder, rightShoulder, leftForearm, rightForearm, leftHand, rightHand;
        private BonePose lowerSpine, chest, head;
        private BonePose leftShin, rightShin, leftFoot, rightFoot;
        private float returnStalledTime;
        private float bestReturnDistance;
        private bool initialized;
        private OnlineMonsterState onlineState;
        private double onlineServerTime;
        private float onlineReceivedAt;
        private bool onlineHasPose;
        private Vector3 onlineFromPosition;
        private Quaternion onlineFromRotation;
        private float onlineInterpolationDuration = .1f;
        private static bool UsesServer => PrototypeOnlineClient.Active != null && PrototypeOnlineClient.Active.IsOnlineMode;
        private readonly List<RendererTint> rendererTints = new List<RendererTint>();

        private sealed class RendererTint
        {
            public Renderer Renderer;
            public MaterialPropertyBlock Properties;
            public int ColorProperty;
            public Color OriginalColor;
        }

        private sealed class BonePose
        {
            public Transform Bone;
            public Quaternion Rotation;
        }

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? "Guardião das Ruínas" : displayName;
        public int Level => Mathf.Max(1, level);
        public bool IsBoss => isBoss;
        public string OnlineId => onlineState != null ? onlineState.id : null;
        public bool IsAvailableForTargeting => gameObject.activeInHierarchy &&
            IsAlive && !IsReturning && !PrototypeCity.IsInCity;
        public float AggroRadius => aggroRadius;
        public float AttackReach => attackRange;
        public Transform VisualRoot => visualRoot;
        public string SpeciesId => speciesId;
        public string Behavior => behavior;
        public float AreaRadius => areaRadius;
        public Vector3 PreparedAttackPosition => attackPosition;
        private bool HasAreaAttack => behavior == "ranged" || behavior == "area";
        public string LevelName => (IsBoss ? "CHEFÃO · " : "") + DisplayName + " · Nv. " + Level;
        public int CurrentHealth { get; private set; } = 60;
        public int MaxHealth => Mathf.Max(1, maxHealth);
        public int AttackDamage => Mathf.Max(0, attackDamage);
        public int ExperienceReward => Mathf.Max(0, experienceReward);
        public bool IsAlive => state != EncounterState.Dead && CurrentHealth > 0;
        public bool IsReturning => state == EncounterState.Returning;
        public bool IsEngaged => state == EncounterState.Chasing || state == EncounterState.PreparingAttack;
        public bool IsPreparingAttack => state == EncounterState.PreparingAttack;
        public float AttackWindupRemaining => IsPreparingAttack ? windupRemaining : 0f;
        public PrototypeMonsterAttackEffects AttackEffects => attackEffects;
        public bool IsPresentingStrike => strikeRemaining > 0f;
        public float RespawnRemaining => state == EncounterState.Dead ? respawnRemaining : 0f;
        public int LastDamage { get; private set; }
        public float LastDamageTime { get; private set; } = -100f;
        public Vector3 HealthAnchor
        {
            get
            {
                CharacterController body = controller != null ? controller : GetComponent<CharacterController>();
                return body != null
                    ? transform.TransformPoint(body.center + Vector3.up * (body.height * 0.5f)) + Vector3.up * 0.25f
                    : transform.position + Vector3.up * 2.55f;
            }
        }

        public void BindOnlinePlayer(PrototypeCombat character) { player = character; target = null; }

        public void ConfigureThreat(int monsterLevel, bool boss = false, float aggression = 5f)
        {
            level = Mathf.Clamp(monsterLevel, 1, 100);
            isBoss = boss;
            aggroRadius = Mathf.Clamp(aggression, 0f, 25f);
        }

        public void ConfigureEncounter(float speed, float leash, float range)
        {
            walkSpeed = Mathf.Clamp(speed, .25f, 8f);
            leashRadius = Mathf.Clamp(leash, 2f, 45f);
            attackRange = Mathf.Clamp(range, .8f, 12f);
            ConfigureAttackEffects();
        }

        public void ConfigureBehavior(string species, string attackBehavior = "melee", float preparation = .65f,
            float interval = 1.25f, float respawn = 6f, float radius = 3f)
        {
            speciesId = species;
            behavior = attackBehavior == "ranged" || attackBehavior == "area" ? attackBehavior : "melee";
            attackPreparation = Mathf.Clamp(preparation, .1f, 10f);
            attackInterval = Mathf.Clamp(interval, attackPreparation, 30f);
            respawnDelay = Mathf.Clamp(respawn, .1f, 3600f);
            areaRadius = Mathf.Clamp(radius, .25f, 10f);
            ConfigureAttackEffects();
        }

        private void ConfigureAttackEffects()
        {
            if (attackEffects != null)
                attackEffects.Configure(attackRange, isBoss || MaxHealth >= 600, behavior, areaRadius);
        }

        public void Configure(PrototypeCombat character, Transform visual)
        {
            player = character;
            visualRoot = visual;
            if (initialized)
                CacheVisual();
        }

        // Presentation only: preserve player binding, encounter state, identity and current health.
        public void ReplaceVisualRoot(Transform visual)
        {
            if (visualRoot == visual) return;
            if (visualRoot != null)
            {
                RestoreVisualPose();
                visualRoot.gameObject.SetActive(false);
            }
            visualRoot = visual;
            CacheVisual();
            if (visualRoot != null) visualRoot.gameObject.SetActive(IsAlive);
        }

        // Profiles are serialized by the scene builder and also work after Awake.
        // Changing a profile never resurrects a dead monster; ResetEncounter owns that transition.
        public void ConfigureStats(string name, int health, int damage, int reward, bool restoreHealth = true)
        {
            displayName = string.IsNullOrWhiteSpace(name) ? "Guardião das Ruínas" : name.Trim();
            maxHealth = Mathf.Max(1, health);
            attackDamage = Mathf.Max(0, damage);
            experienceReward = Mathf.Max(0, reward);
            if (!initialized || (restoreHealth && state != EncounterState.Dead))
                CurrentHealth = MaxHealth;
            else
                CurrentHealth = Mathf.Min(CurrentHealth, MaxHealth);
            ConfigureAttackEffects();
        }

        private void Awake()
        {
            Initialize();
        }

        private void Initialize()
        {
            if (initialized)
                return;
            initialized = true;
            controller = GetComponent<CharacterController>();
            controller.minMoveDistance = 0f;
            homePosition = transform.position;
            homeRotation = transform.rotation;
            CurrentHealth = MaxHealth;
            state = EncounterState.Passive;
            attackEffects = GetComponent<PrototypeMonsterAttackEffects>();
            if (attackEffects == null) attackEffects = gameObject.AddComponent<PrototypeMonsterAttackEffects>();
            ConfigureAttackEffects();
            CacheVisual();
        }

        private void CacheVisual()
        {
            rendererTints.Clear();
            leftArm = null;
            rightArm = null;
            leftLeg = rightLeg = null;
            articulatedBones.Clear();
            leftShoulder = rightShoulder = leftForearm = rightForearm = leftHand = rightHand = null;
            lowerSpine = chest = head = null;
            leftShin = rightShin = leftFoot = rightFoot = null;
            lastPosePosition = transform.position;
            if (visualRoot == null)
                return;

            visualPosition = visualRoot.localPosition;
            visualRotation = visualRoot.localRotation;
            foreach (Transform child in visualRoot.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == "Braco.L")
                    leftArm = child;
                else if (child.name == "Braco.R")
                    rightArm = child;
                else if (child.name == "Perna.L") leftLeg = child;
                else if (child.name == "Perna.R") rightLeg = child;
                else if (child.name == "shin.L") leftShin = CacheBone(child);
                else if (child.name == "shin.R") rightShin = CacheBone(child);
                else if (child.name == "foot.L") leftFoot = CacheBone(child);
                else if (child.name == "foot.R") rightFoot = CacheBone(child);
                else if (child.name == "shoulder.L") leftShoulder = CacheBone(child);
                else if (child.name == "shoulder.R") rightShoulder = CacheBone(child);
                else if (child.name == "forearm.L") leftForearm = CacheBone(child);
                else if (child.name == "forearm.R") rightForearm = CacheBone(child);
                else if (child.name == "hand.L") leftHand = CacheBone(child);
                else if (child.name == "hand.R") rightHand = CacheBone(child);
                else if (child.name == "Bone.001") lowerSpine = CacheBone(child);
                else if (child.name == "Bone.002") chest = CacheBone(child);
                else if (child.name == "Bone.003") head = CacheBone(child);
            }
            if (leftArm != null)
                leftArmRotation = leftArm.localRotation;
            if (rightArm != null)
                rightArmRotation = rightArm.localRotation;
            if (leftLeg != null) { leftLegRotation = leftLeg.localRotation; leftLegPosition = leftLeg.localPosition; }
            if (rightLeg != null) { rightLegRotation = rightLeg.localRotation; rightLegPosition = rightLeg.localPosition; }
            if (leftLeg != null && leftShin != null && leftFoot != null)
                legLength = Mathf.Max(.1f, Vector3.Distance(leftLeg.position, leftShin.Bone.position) +
                    Vector3.Distance(leftShin.Bone.position, leftFoot.Bone.position));

            RefreshVisualMaterials();
        }

        public void RefreshVisualMaterials()
        {
            rendererTints.Clear();
            if (visualRoot == null) return;
            foreach (Renderer renderer in visualRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled) continue;
                Material material = renderer.sharedMaterial;
                if (material == null)
                    continue;
                int colorProperty = Shader.PropertyToID(material.HasProperty("_BaseColor") ? "_BaseColor" : "_Color");
                if (!material.HasProperty(colorProperty))
                    continue;
                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                rendererTints.Add(new RendererTint
                {
                    Renderer = renderer,
                    Properties = properties,
                    ColorProperty = colorProperty,
                    OriginalColor = material.GetColor(colorProperty)
                });
            }
        }

        private BonePose CacheBone(Transform bone)
        {
            var value = new BonePose { Bone = bone, Rotation = bone.localRotation };
            articulatedBones.Add(value);
            return value;
        }

        private void Update()
        {
            if (UsesServer)
            {
                UpdateOnlineView();
                return;
            }
            float delta = Time.deltaTime;
            if (state == EncounterState.Dead)
            {
                respawnRemaining = Mathf.Max(0f, respawnRemaining - delta);
                if (respawnRemaining <= 0f)
                    ResetEncounter();
                return;
            }

            // A fall cannot be repaired by walking; recover without awarding a kill.
            if (transform.position.y < homePosition.y - 8f)
            {
                ResetEncounter();
                return;
            }

            if (!IsReturning && (FlatDistance(transform.position, homePosition) > leashRadius ||
                (IsEngaged && (target == null || !target.IsAlive ||
                FlatDistance(target.transform.position, homePosition) > leashRadius))))
                BeginReturn();

            attackCooldown = Mathf.Max(0f, attackCooldown - delta);
            if (state == EncounterState.Passive && aggroRadius > 0f && player != null && player.IsAlive &&
                !PrototypeCity.IsInCity && FlatDistance(player.transform.position, transform.position) <= aggroRadius &&
                FlatDistance(player.transform.position, homePosition) <= leashRadius)
            {
                target = player;
                if (HasClearAttack()) state = EncounterState.Chasing;
                else target = null;
            }
            Vector3 movement = Vector3.zero;
            if (IsReturning)
            {
                float distance = FlatDistance(transform.position, homePosition);
                if (distance <= 0.14f)
                    FinishReturn();
                else
                {
                    Vector3 offset = homePosition - transform.position;
                    offset.y = 0f;
                    movement = offset.normalized * Mathf.Min(walkSpeed * 1.25f, distance / Mathf.Max(delta, 0.0001f));
                    TrackReturnProgress(distance, delta);
                    if (!IsReturning)
                        movement = Vector3.zero;
                }
            }
            else if (IsEngaged)
            {
                Vector3 offset = target.transform.position - transform.position;
                offset.y = 0f;
                Face(offset, delta);
                float distance = offset.magnitude;
                if (state == EncounterState.PreparingAttack)
                {
                    windupRemaining = Mathf.Max(0f, windupRemaining - delta);
                    if (!HasAreaAttack && distance > attackRange + 0.15f)
                    {
                        state = EncounterState.Chasing;
                        windupRemaining = 0f;
                        if (attackEffects != null) attackEffects.SetPreparation(false, 0f);
                        attackCooldown = Mathf.Max(attackCooldown, 0.25f);
                    }
                    else if (windupRemaining <= 0f)
                    {
                        // Check range and obstruction again at impact, so running away can evade the strike.
                        bool hit = (HasAreaAttack
                            ? FlatDistance(target.transform.position, attackPosition) <= areaRadius
                            : distance <= attackRange) && HasClearAttack();
                        Vector3 contact = HasAreaAttack ? attackPosition : StrikeContact(target.transform.position);
                        if (hit)
                            target.ReceiveDamage(AttackDamage);
                        PlayStrikePresentation(contact, hit);
                        state = EncounterState.Chasing;
                        attackCooldown = Mathf.Max(.1f, attackInterval - attackPreparation);
                    }
                }
                else if (distance > (behavior == "area" ? areaRadius : attackRange))
                    movement = offset.normalized * Mathf.Min(walkSpeed, (distance - ((behavior == "area" ? areaRadius : attackRange) - 0.1f)) / Mathf.Max(delta, 0.0001f));
                else if (attackCooldown <= 0f && HasClearAttack())
                {
                    state = EncounterState.PreparingAttack;
                    windupRemaining = Mathf.Max(0.1f, attackPreparation);
                    // Commit the warning's world position once; movement can then evade the area.
                    attackPosition = behavior == "ranged" ? target.transform.position : transform.position;
                    PrototypeAudio.Play(GameSound.MonsterWindup, transform.position, .8f, MaxHealth >= 600);
                }
            }

            if (movement.sqrMagnitude > 0.001f)
                Face(movement, delta);
            MoveWithGravity(movement, delta);
            UpdateVisual(movement.sqrMagnitude > 0.01f, delta);
        }

        public int TakeDamage(int amount, PrototypeCombat attacker)
        {
            if (UsesServer) return 0;
            Initialize();
            if (amount <= 0 || !IsAlive || IsReturning || attacker == null || !attacker.IsAlive ||
                (player != null && attacker != player))
                return 0;
            if (FlatDistance(transform.position, homePosition) > leashRadius ||
                FlatDistance(attacker.transform.position, homePosition) > leashRadius)
            {
                BeginReturn();
                return 0;
            }

            int applied = Mathf.Min(amount, CurrentHealth);
            CurrentHealth -= applied;
            LastDamage = applied;
            LastDamageTime = Time.time;
            flashRemaining = 0.10f;
            target = attacker;
            if (CurrentHealth == 0)
            {
                // Mark dead before callbacks: additional attacks or callbacks cannot award a second kill.
                state = EncounterState.Dead;
                windupRemaining = 0f;
                CancelAttackPresentation();
                respawnRemaining = Mathf.Max(0.1f, respawnDelay);
                controller.enabled = false;
                if (visualRoot != null)
                    visualRoot.gameObject.SetActive(false);
                attacker.GrantExperience(ExperienceReward);
                PrototypeAudio.Play(GameSound.MonsterDeath, transform.position, 1f, MaxHealth >= 600);
            }
            else if (state == EncounterState.Passive)
                state = EncounterState.Chasing;
            if (CurrentHealth > 0) PrototypeAudio.Play(GameSound.MonsterHurt, transform.position, .75f, MaxHealth >= 600);
            return applied;
        }

        public void ResetEncounter()
        {
            if (UsesServer) return;
            Initialize();
            controller.enabled = false;
            transform.SetPositionAndRotation(homePosition, homeRotation);
            controller.enabled = true;
            verticalSpeed = 0f;
            CurrentHealth = MaxHealth;
            state = EncounterState.Passive;
            target = null;
            attackCooldown = 0f;
            windupRemaining = 0f;
            respawnRemaining = 0f;
            flashRemaining = 0f;
            strikeRemaining = 0f;
            CancelAttackPresentation();
            returnStalledTime = 0f;
            LastDamage = 0;
            LastDamageTime = -100f;
            RestoreVisual();
        }

        public void ApplyOnlineState(OnlineMonsterState value, double serverTime)
        {
            Initialize();
            bool wasAlive = IsAlive;
            bool wasPreparing = IsPreparingAttack;
            bool hadOnlineState = onlineState != null;
            if (value == null) return;
            if (!string.IsNullOrWhiteSpace(value.name))
                displayName = value.name;
            level = Mathf.Max(1, value.level);
            isBoss = value.isBoss;
            if (value.attackRange > 0f && value.moveSpeed > 0f && value.leashRadius > 0f)
            {
                ConfigureThreat(value.level, value.isBoss, value.aggroRadius);
                ConfigureEncounter(value.moveSpeed, value.leashRadius, value.attackRange);
            }
            ConfigureBehavior(value.speciesId, value.behavior, value.attackPreparation, value.attackInterval,
                value.respawnDelay, value.areaRadius);
            attackPosition = new Vector3(value.attackX, homePosition.y, value.attackZ);
            maxHealth = Mathf.Max(1, value.maxHealth);
            ConfigureAttackEffects();
            attackDamage = Mathf.Max(0, value.damage);
            experienceReward = Mathf.Max(0, value.experience);
            onlineFromPosition = transform.position;
            onlineFromRotation = transform.rotation;
            onlineInterpolationDuration = onlineHasPose
                ? Mathf.Clamp((float)(serverTime - onlineServerTime), .033f, .3f) : .1f;
            Vector3 nextPosition = new Vector3(value.x, homePosition.y, value.z);
            if (!onlineHasPose || !wasAlive && value.health > 0 || (nextPosition - transform.position).sqrMagnitude > 64f)
            {
                controller.enabled = false;
                transform.SetPositionAndRotation(nextPosition, Quaternion.Euler(0f, value.yaw, 0f));
                onlineFromPosition = nextPosition;
                onlineFromRotation = transform.rotation;
            }
            onlineHasPose = true;
            onlineState = value;
            onlineServerTime = serverTime;
            onlineReceivedAt = Time.time;
            CurrentHealth = Mathf.Clamp(value.health, 0, MaxHealth);
            state = CurrentHealth <= 0 ? EncounterState.Dead : value.returning ? EncounterState.Returning :
                value.windupUntil > serverTime ? EncounterState.PreparingAttack :
                !string.IsNullOrEmpty(value.targetId) ? EncounterState.Chasing : EncounterState.Passive;
            if (!wasAlive && IsAlive) RestoreVisual();
            // Initial/reconnected snapshots restore the pose silently; no historical vocal is replayed.
            if (hadOnlineState && !wasPreparing && IsPreparingAttack && PrototypeOnlineClient.Active != null &&
                PrototypeOnlineClient.Active.CanPlay && PrototypeOnlineClient.Active.IsConnected)
                PrototypeAudio.Play(GameSound.MonsterWindup, transform.position, .8f, MaxHealth >= 600);
            // A cancelled preparation is not a strike. Only the confirmed world event starts impact FX.
            if (wasPreparing && !IsPreparingAttack && IsAlive && !value.returning && windupRemaining <= .12f)
                preparationHoldUntil = Time.time + .18f;
            if (!IsPreparingAttack && attackEffects != null) attackEffects.SetPreparation(false, 0f);
            if (!IsAlive || value.returning) CancelAttackPresentation();
            controller.enabled = IsAlive;
            if (visualRoot != null) visualRoot.gameObject.SetActive(IsAlive);
            UpdateOnlineView();
        }

        public void PlayOnlineStrike(Vector3 impactPosition, bool hit)
        {
            Initialize();
            if (!UsesServer || !IsAlive || IsReturning || PrototypeOnlineClient.Active == null ||
                !PrototypeOnlineClient.Active.CanPlay) return;
            PlayStrikePresentation(HasAreaAttack ? attackPosition : StrikeContact(impactPosition), hit);
        }

        private Vector3 StrikeContact(Vector3 targetPosition)
        {
            Vector3 offset = targetPosition - transform.position;
            offset.y = 0f;
            // A near miss still lands in front of the fists, rather than producing dust under a distant target.
            if (offset.sqrMagnitude < .01f) offset = transform.forward;
            float distance = Mathf.Clamp(offset.magnitude, .65f, attackRange);
            return transform.position + offset.normalized * distance;
        }

        private void PlayStrikePresentation(Vector3 contact, bool hit)
        {
            strikeRemaining = StrikePresentationDuration;
            strikeStartedAt = Time.time;
            presentationPreparation = 0f;
            preparationHoldUntil = 0f;
            ConfigureAttackEffects();
            attackEffects.PlayStrike(contact, hit);
            PrototypeAudio.Play(GameSound.MonsterStrike, transform.position, .8f, MaxHealth >= 600);
            if (hit) PrototypeAudio.Play(GameSound.MonsterImpact, contact, .8f, MaxHealth >= 600);
        }

        private void CancelAttackPresentation()
        {
            strikeRemaining = 0f;
            strikeStartedAt = -100f;
            presentationPreparation = 0f;
            preparationHoldUntil = 0f;
            if (attackEffects != null) attackEffects.Cancel();
        }

        public void RecordOnlineHit(int amount)
        {
            LastDamage = amount;
            LastDamageTime = Time.time;
            flashRemaining = .10f;
            if (IsAlive) PrototypeAudio.Play(GameSound.MonsterHurt, transform.position, .75f, MaxHealth >= 600);
        }

        private void UpdateOnlineView()
        {
            if (UsesServer && PrototypeOnlineClient.Active != null && !PrototypeOnlineClient.Active.CanPlay)
            {
                CancelAttackPresentation();
                UpdateVisual(false, Time.deltaTime);
                return;
            }
            if (onlineState == null)
            {
                UpdateVisual(false, Time.deltaTime);
                return;
            }
            double now = onlineServerTime + Time.time - onlineReceivedAt;
            respawnRemaining = (float)System.Math.Max(0d, onlineState.respawnAt - now);
            windupRemaining = (float)System.Math.Max(0d, onlineState.windupUntil - now);
            if (!IsAlive) return;
            Vector3 destination = new Vector3(onlineState.x, homePosition.y, onlineState.z);
            Vector3 previous = transform.position;
            float interpolation = Mathf.Clamp01((Time.time - onlineReceivedAt) / onlineInterpolationDuration);
            bool enabledBefore = controller.enabled;
            controller.enabled = false;
            transform.position = Vector3.Lerp(onlineFromPosition, destination, interpolation);
            transform.rotation = Quaternion.Slerp(onlineFromRotation, Quaternion.Euler(0f, onlineState.yaw, 0f), interpolation);
            controller.enabled = enabledBefore;
            bool moving = (transform.position - previous).sqrMagnitude > .000001f;
            UpdateVisual(moving, Time.deltaTime);
        }

        private void BeginReturn()
        {
            state = EncounterState.Returning;
            target = null;
            windupRemaining = 0f;
            attackCooldown = 0f;
            strikeRemaining = 0f;
            CancelAttackPresentation();
            CurrentHealth = MaxHealth;
            flashRemaining = 0f;
            bestReturnDistance = FlatDistance(transform.position, homePosition);
            returnStalledTime = 0f;
        }

        private void FinishReturn()
        {
            state = EncounterState.Passive;
            CurrentHealth = MaxHealth;
            transform.rotation = homeRotation;
            returnStalledTime = 0f;
        }

        private void TrackReturnProgress(float distance, float delta)
        {
            if (distance < bestReturnDistance - 0.05f)
            {
                bestReturnDistance = distance;
                returnStalledTime = 0f;
            }
            else
                returnStalledTime += delta;
            // This prototype has no navigation mesh. Recover only after a sustained collision blocks the return.
            if (returnStalledTime >= 5f)
                ResetEncounter();
        }

        private bool HasClearAttack()
        {
            if (target == null || !target.IsAlive)
                return false;
            Vector3 origin = transform.position + Vector3.up;
            Vector3 destination = target.transform.position + Vector3.up;
            Vector3 direction = destination - origin;
            if (!Physics.Raycast(origin, direction.normalized, out RaycastHit hit, direction.magnitude,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return true;
            return hit.transform == target.transform || hit.transform.IsChildOf(target.transform);
        }

        private void MoveWithGravity(Vector3 movement, float delta)
        {
            if (!controller.enabled)
                return;
            if (controller.isGrounded && verticalSpeed < 0f)
                verticalSpeed = -2f;
            verticalSpeed += Physics.gravity.y * delta;
            CollisionFlags flags = controller.Move((movement + Vector3.up * verticalSpeed) * delta);
            if ((flags & CollisionFlags.Below) != 0)
                verticalSpeed = -2f;
        }

        private void Face(Vector3 direction, float delta)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), 10f * delta);
        }

        private void UpdateVisual(bool moving, float delta)
        {
            // Animator evaluation runs after Update. The final articulated pose is applied in LateUpdate.
            visualMoving = moving;
        }

        private void LateUpdate()
        {
            if (IsAlive) ApplyVisualPose(visualMoving, Time.deltaTime);
        }

        private void ApplyVisualPose(bool moving, float delta)
        {
            flashRemaining = Mathf.Max(0f, flashRemaining - delta);
            strikeRemaining = Mathf.Max(0f, StrikePresentationDuration - (Time.time - strikeStartedAt));
            float preparation = IsPreparingAttack ? 1f - windupRemaining / Mathf.Max(0.1f, attackPreparation) : 0f;
            bool canPresent = IsAlive && (!UsesServer || (PrototypeOnlineClient.Active != null && PrototypeOnlineClient.Active.CanPlay));
            float targetPreparation = canPresent ? Mathf.Clamp01(preparation) : 0f;
            if (canPresent && Time.time < preparationHoldUntil) targetPreparation = 1f;
            presentationPreparation = Mathf.MoveTowards(presentationPreparation, targetPreparation, delta * 7f);
            if (attackEffects != null)
                attackEffects.SetPreparation(canPresent && IsPreparingAttack && windupRemaining > 0f && strikeRemaining <= 0f,
                    preparation, attackPosition);
            if (visualRoot == null) return;
            float raised = Mathf.SmoothStep(0f, 1f, presentationPreparation);
            float armAngle = -82f * raised;
            float shoulderAngle = -17f * raised;
            float elbowAngle = -32f * raised;
            float wristAngle = -14f * raised;
            float bodyAngle = -7f * raised;
            float twist = -7f * raised;
            float crouch = raised * .055f;
            if (strikeRemaining > 0f && canPresent)
            {
                float age = StrikePresentationDuration - strikeRemaining;
                float descent = Mathf.SmoothStep(0f, 1f, age / .095f);
                float recovery = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((age - .15f) / .33f));
                float weight = 1f - recovery;
                armAngle = Mathf.Lerp(-82f, -40f, descent) * weight;
                shoulderAngle = Mathf.Lerp(-17f, 6f, descent) * weight;
                elbowAngle = Mathf.Lerp(-32f, 11f, descent) * weight;
                wristAngle = Mathf.Lerp(-14f, 17f, descent) * weight;
                bodyAngle = Mathf.Lerp(-7f, 13f, descent) * weight;
                twist = Mathf.Lerp(-7f, 8f, descent) * weight;
                crouch = Mathf.Lerp(.055f, .15f, descent) * weight;
            }
            Vector3 travel = transform.position - lastPosePosition;
            travel.y = 0f;
            lastPosePosition = transform.position;
            // Advance the gait by ground distance: snapshot interpolation and a blocked
            // controller must not keep cycling the feet at a fixed clock speed.
            float distance = travel.magnitude;
            bool teleported = distance > 2f;
            float speed = !teleported && delta > .0001f ? distance / delta : 0f;
            if (!moving && speed < .15f) speed = 0f;
            speed = Mathf.Min(speed, walkSpeed * 1.5f);
            float targetStride = canPresent ? Mathf.Clamp(speed / Mathf.Max(.1f, walkSpeed), 0f, 1.15f) : 0f;
            if (teleported) stride = 0f;
            stride = Mathf.MoveTowards(stride, targetStride, delta * 6f);
            gaitPhase = Mathf.Repeat(gaitPhase + speed * delta * (Mathf.PI * 2f / 1.65f), Mathf.PI * 2f);
            float step = Mathf.Sin(gaitPhase) * stride;
            float leftSwing = Mathf.Max(0f, Mathf.Cos(gaitPhase));
            float rightSwing = Mathf.Max(0f, -Mathf.Cos(gaitPhase));
            float crouchBend = Mathf.Acos(Mathf.Clamp01(1f - crouch / legLength)) * Mathf.Rad2Deg;
            float leftHip = step * 18f - crouchBend;
            float rightHip = -step * 18f - crouchBend;
            float leftKnee = (5f + leftSwing * 32f) * stride + crouchBend * 2f;
            float rightKnee = (5f + rightSwing * 32f) * stride + crouchBend * 2f;
            float bob = Mathf.Abs(Mathf.Sin(gaitPhase)) * .023f * stride + Mathf.Sin(Time.time * 2f) * .006f * (1f - Mathf.Clamp01(stride));
            visualRoot.localPosition = visualPosition + Vector3.up * (bob - crouch);
            // The FBX root has an import rotation. Pre-multiply in the upright actor's axes.
            visualRoot.localRotation = Quaternion.Euler(bodyAngle * .25f, 0f, 0f) * visualRotation;
            // Body-space axes work with the imported rig's individual bone orientations.
            ApplyBone(lowerSpine, new Vector3(bodyAngle * .35f, twist * .45f, 0f));
            ApplyBone(chest, new Vector3(bodyAngle * .45f, twist * .55f, 0f));
            ApplyBone(head, new Vector3(-bodyAngle * .28f, -twist * .5f, 0f));
            ApplyBone(leftShoulder, new Vector3(shoulderAngle, -raised * 8f, -raised * 6f));
            ApplyBone(rightShoulder, new Vector3(shoulderAngle, raised * 8f, raised * 6f));
            if (leftArm != null)
                ApplyBodyRotation(leftArm, leftArmRotation, new Vector3(armAngle - step * 9f, -raised * 4f, raised * 18f));
            if (rightArm != null)
                ApplyBodyRotation(rightArm, rightArmRotation, new Vector3(armAngle + step * 9f, raised * 4f, -raised * 18f));
            ApplyBone(leftForearm, new Vector3(elbowAngle, 0f, raised * 3f));
            ApplyBone(rightForearm, new Vector3(elbowAngle, 0f, -raised * 3f));
            ApplyBone(leftHand, new Vector3(wristAngle, -raised * 5f, 0f));
            ApplyBone(rightHand, new Vector3(wristAngle, raised * 5f, 0f));
            if (leftLeg != null)
            {
                ApplyBodyRotation(leftLeg, leftLegRotation, new Vector3(leftHip, 0f, 0f));
                leftLeg.localPosition = leftLegPosition;
            }
            if (rightLeg != null)
            {
                ApplyBodyRotation(rightLeg, rightLegRotation, new Vector3(rightHip, 0f, 0f));
                rightLeg.localPosition = rightLegPosition;
            }
            ApplyBone(leftShin, new Vector3(leftKnee, 0f, 0f));
            ApplyBone(rightShin, new Vector3(rightKnee, 0f, 0f));
            ApplyBone(leftFoot, new Vector3(-leftHip - leftKnee + leftSwing * stride * 6f, 0f, 0f));
            ApplyBone(rightFoot, new Vector3(-rightHip - rightKnee + rightSwing * stride * 6f, 0f, 0f));
            float hitHighlight = Mathf.Clamp01(flashRemaining / 0.10f);
            SetVisualTint(hitHighlight * hitHighlight * 0.12f);
        }

        private void ApplyBone(BonePose pose, Vector3 angles)
        {
            if (pose != null) ApplyBodyRotation(pose.Bone, pose.Rotation, angles);
        }

        private void ApplyBodyRotation(Transform bone, Quaternion restRotation, Vector3 angles)
        {
            Quaternion worldOffset = transform.rotation * Quaternion.Euler(angles) * Quaternion.Inverse(transform.rotation);
            Quaternion parentRotation = bone.parent != null ? bone.parent.rotation : Quaternion.identity;
            bone.localRotation = Quaternion.Inverse(parentRotation) * worldOffset * parentRotation * restRotation;
        }

        private void SetVisualTint(float flash)
        {
            foreach (RendererTint tint in rendererTints)
            {
                if (tint.Renderer == null)
                    continue;
                tint.Properties.SetColor(tint.ColorProperty, Color.Lerp(tint.OriginalColor, Color.white, flash));
                tint.Renderer.SetPropertyBlock(tint.Properties);
            }
        }

        private void RestoreVisual()
        {
            if (visualRoot == null)
                return;
            visualRoot.gameObject.SetActive(true);
            RestoreVisualPose();
        }

        private void RestoreVisualPose()
        {
            if (visualRoot == null) return;
            visualRoot.localPosition = visualPosition;
            visualRoot.localRotation = visualRotation;
            if (leftArm != null)
                leftArm.localRotation = leftArmRotation;
            if (rightArm != null)
                rightArm.localRotation = rightArmRotation;
            foreach (BonePose pose in articulatedBones)
                if (pose.Bone != null) pose.Bone.localRotation = pose.Rotation;
            if (leftLeg != null) { leftLeg.localRotation = leftLegRotation; leftLeg.localPosition = leftLegPosition; }
            if (rightLeg != null) { rightLeg.localRotation = rightLegRotation; rightLeg.localPosition = rightLegPosition; }
            stride = 0f;
            gaitPhase = 0f;
            lastPosePosition = transform.position;
            SetVisualTint(0f);
        }

        private void OnDisable()
        {
            CancelAttackPresentation();
            // An offline map transition cannot resume a cast that was hidden in the city.
            if (!UsesServer && IsEngaged)
            {
                state = EncounterState.Passive;
                target = null;
                windupRemaining = 0f;
            }
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}

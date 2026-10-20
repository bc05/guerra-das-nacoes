using UnityEngine;

namespace GuerraDasNacoes
{
    [RequireComponent(typeof(CharacterController), typeof(PrototypeCombat))]
    public sealed class PrototypeCharacter : MonoBehaviour
    {
        public const float DefaultWalkSpeed = 2.25f;
        public const float DefaultRunSpeed = 5.5f;

        [SerializeField] private Transform visualRoot;
        [SerializeField] private float walkSpeed = DefaultWalkSpeed;
        [SerializeField] private float runSpeed = DefaultRunSpeed;
        [SerializeField] private LayerMask groundMask = 1 << 8;
        private const float Acceleration = 12f;
        private const float Deceleration = 20f;

        private CharacterController controller;
        private PrototypeCombat combat;
        private PrototypeAbilities abilities;
        private Animator animator;
        private Camera worldCamera;
        private Vector3 spawnPosition;
        private Vector3 destination;
        private Vector3 originalVisualPosition;
        private bool hasDestination;
        private bool hasMoveParameter;
        private bool hasLocomotionParameter;
        private float verticalSpeed;
        private Vector3 movementVelocity;

        private float strideTime;
        private float footstepDistance;
        private float approachStalledTime;
        private bool hasFreeAttack;
        private Vector3 freeAttackDirection;
        private float freeAttackUntil;


        public bool IsMoving { get; private set; }
        public bool IsAttacking => combat != null && combat.IsAttacking;
        public bool HasDestination => hasDestination;
        public bool HasFreeAttack => hasFreeAttack;
        public Transform VisualRoot => visualRoot;

        public void Configure(Transform visual)
        {
            visualRoot = visual;
        }

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            controller.minMoveDistance = 0f;
            combat = GetComponent<PrototypeCombat>();
            abilities = GetComponent<PrototypeAbilities>();
            worldCamera = Camera.main;
            spawnPosition = transform.position;
            if (visualRoot != null)
            {
                originalVisualPosition = visualRoot.localPosition;
                animator = visualRoot.GetComponentInChildren<Animator>();
            }

            if (animator != null && animator.runtimeAnimatorController != null)
            {
                animator.applyRootMotion = false;
                foreach (AnimatorControllerParameter parameter in animator.parameters)
                {
                    if (parameter.name == "MoveSpeed" && parameter.type == AnimatorControllerParameterType.Float)
                        hasMoveParameter = true;
                    if (parameter.name == "LocomotionSpeed" && parameter.type == AnimatorControllerParameterType.Float)
                        hasLocomotionParameter = true;
                }
            }


        }

        private void Update()
        {
            // Also consume the toggle key itself: HUD and character Update order is not guaranteed.
            if (PrototypeHud.BlocksGameplay || Input.GetKeyDown(KeyCode.C) || Input.GetKeyDown(KeyCode.H) || Input.GetKeyDown(KeyCode.I) || Input.GetKeyDown(KeyCode.O))
            {
                StopForInterface();
                return;
            }

            if (Input.GetKeyDown(KeyCode.R))
                ResetPosition();

            if (!combat.IsAlive)
            {
                CancelFreeAttack();
                hasDestination = false;
                IsMoving = false;
                movementVelocity = Vector3.zero;
                UpdateLocomotion(0f, true);
                if (visualRoot != null)
                    visualRoot.localRotation = Quaternion.Euler(0f, 0f, 75f);

                return;
            }

            if (Input.GetKeyDown(KeyCode.Tab))
            {
                combat.CancelRequestedAttack();
                CancelFreeAttack();
                SelectNextEnemy();
            }
            bool cancelAttackInput = Input.GetKeyDown(KeyCode.Escape);
            if (cancelAttackInput)
            {
                combat.SetTarget(null);
                combat.CancelRequestedAttack();
                CancelFreeAttack();
                hasDestination = false;
            }

            if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                CancelFreeAttack();
                abilities.TryUse(1);
            }
            if (Input.GetKeyDown(KeyCode.Alpha3))
                abilities.TryUse(2);
            if (Input.GetKeyDown(KeyCode.Alpha4))
                abilities.TryUse(3);
            if (Input.GetKeyDown(KeyCode.Alpha5))
                abilities.TryUse(4);

            Vector2 input = new Vector2(
                (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f) -
                (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f),
                (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1f : 0f) -
                (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1f : 0f));

            if (worldCamera == null)
                worldCamera = Camera.main;

            if (Input.GetMouseButtonDown(0) && worldCamera != null && !PrototypeHud.ContainsScreenPoint(Input.mousePosition))
            {
                Ray ray = worldCamera.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out RaycastHit selection, 250f, ~0, QueryTriggerInteraction.Ignore) &&
                    selection.collider.GetComponentInParent<PrototypeMonster>() is PrototypeMonster selectedMonster)
                {
                    SelectAttackTarget(selectedMonster);
                }
                else if (Physics.Raycast(ray, out RaycastHit hit, 250f, groundMask, QueryTriggerInteraction.Ignore))
                {
                    MoveTo(hit.point);
                }
            }

            // Input edges request one strike. A held button never queues a later cooldown cycle.
            bool mouseAttackPressed = Input.GetMouseButtonDown(1);
            bool basicAttackPressed = Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Space);
            bool selectionPressed = Input.GetMouseButtonDown(0);
            bool manualMovement = input.sqrMagnitude > .01f;
            if (manualMovement)
            {
                combat.CancelRequestedAttack();
                CancelFreeAttack();
            }
            else if (!cancelAttackInput && !selectionPressed && !hasFreeAttack && !mouseAttackPressed && basicAttackPressed)
            {
                hasDestination = false;
                combat.RequestAttack(0);
            }

            if (!manualMovement && !cancelAttackInput && !selectionPressed && mouseAttackPressed && worldCamera != null &&
                !PrototypeHud.ContainsScreenPoint(Input.mousePosition))
            {
                Ray aim = worldCamera.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(aim, out RaycastHit aimHit, 250f, ~0,
                    QueryTriggerInteraction.Ignore) && aimHit.collider.GetComponentInParent<PrototypeMonster>() is PrototypeMonster aimMonster)
                    SelectAttackTarget(aimMonster);
                if (combat.ValidTarget == null)
                {
                    Plane ground = new Plane(Vector3.up, transform.position);
                    if (ground.Raycast(aim, out float distance))
                        RequestFreeAttack(aim.GetPoint(distance) - transform.position);
                }
                else
                {
                    CancelFreeAttack();
                    combat.RequestAttack(0);
                }
                hasDestination = false;
            }

            UpdateFreeAttack();
            Vector3 movement = Vector3.zero;
            if (input.sqrMagnitude > 0.01f)
            {
                hasDestination = false;
                Vector3 forward = worldCamera != null ? worldCamera.transform.forward : Vector3.forward;
                forward.y = 0f;
                forward.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, forward);
                movement = (forward * input.y + right * input.x).normalized;
            }
            else if (combat.HasQueuedAttack)
            {
                hasDestination = false;
                if (combat.TryGetApproachDestination(out Vector3 approachPoint))
                    movement = Vector3.ProjectOnPlane(approachPoint - transform.position, Vector3.up).normalized;
                if (combat.ValidTarget != null && !combat.IsAttacking)
                {
                    Vector3 towardTarget = Vector3.ProjectOnPlane(combat.ValidTarget.transform.position - transform.position, Vector3.up);
                    if (towardTarget.sqrMagnitude > .001f)
                        transform.rotation = Quaternion.RotateTowards(transform.rotation,
                            Quaternion.LookRotation(towardTarget), 720f * Time.deltaTime);
                }
                combat.TryExecuteRequestedAttack();
            }
            else if (hasDestination)
            {
                Vector3 offset = destination - transform.position;
                offset.y = 0f;
                if (offset.sqrMagnitude <= 0.16f)
                    hasDestination = false;
                else
                    movement = offset.normalized;
            }

            bool running = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            float speed = running ? runSpeed : walkSpeed;
            speed *= combat.AttackMovementMultiplier;
            // A short acceleration/deceleration avoids the rigid instant start/stop of the capsule.
            // Animation uses the distance actually covered, including collisions, rather than keys.
            Vector3 desiredVelocity = movement * speed;
            // Steering accelerates along the current arc rather than snapping velocity around a corner.
            if (desiredVelocity.sqrMagnitude > .01f && movementVelocity.sqrMagnitude > .05f)
            {
                Vector3 steered = Vector3.RotateTowards(movementVelocity.normalized, desiredVelocity.normalized,
                    Mathf.Deg2Rad * 540f * Time.deltaTime, 0f);
                desiredVelocity = steered * desiredVelocity.magnitude;
            }
            movementVelocity = Vector3.MoveTowards(movementVelocity, desiredVelocity,
                (desiredVelocity.sqrMagnitude > movementVelocity.sqrMagnitude ? Acceleration : Deceleration) * Time.deltaTime);
            // Do not let a running capsule slide through the planted contact pose.
            if (combat.IsAttacking && combat.AttackProgress < .48f) movementVelocity = Vector3.zero;
            if (hasDestination && Vector3.Dot(destination - transform.position, movementVelocity) > 0f)
            {
                Vector3 remaining = Vector3.ProjectOnPlane(destination - transform.position, Vector3.up);
                if (movementVelocity.magnitude * Time.deltaTime > remaining.magnitude)
                    movementVelocity = remaining / Mathf.Max(Time.deltaTime, .0001f);
            }
            if (controller.isGrounded && verticalSpeed < 0f)
                verticalSpeed = -2f;
            verticalSpeed += Physics.gravity.y * Time.deltaTime;
            CollisionFlags collisions = controller.Move((movementVelocity + Vector3.up * verticalSpeed) * Time.deltaTime);
            if ((collisions & CollisionFlags.Below) != 0)
                verticalSpeed = -2f;
            if (hasDestination && (collisions & CollisionFlags.Sides) != 0)
            {
                hasDestination = false;
                movementVelocity = Vector3.zero;
            }

            Vector3 horizontalVelocity = controller.velocity;
            horizontalVelocity.y = 0f;
            IsMoving = horizontalVelocity.sqrMagnitude > 0.05f;
            if (IsMoving && controller.isGrounded)
            {
                // Half-cycle distances from the fitted Walk/Run motion profile.
                float gaitBlend = Mathf.InverseLerp(DefaultWalkSpeed, DefaultRunSpeed, horizontalVelocity.magnitude);
                float strideLength = Mathf.Lerp(.7434f, 1.3061f, gaitBlend);
                footstepDistance += horizontalVelocity.magnitude * Time.deltaTime;
                if (footstepDistance >= strideLength)
                {
                    footstepDistance %= strideLength;
                    PrototypeAudio.Play(GameSound.Footstep, transform.position, running ? 1f : .8f);
                }
            }
            else footstepDistance = 0f;
            if (combat.HasQueuedAttack && movement.sqrMagnitude > .01f)
            {
                bool stoppedAtObstacle = (collisions & CollisionFlags.Sides) != 0 && horizontalVelocity.magnitude < .12f;
                approachStalledTime = stoppedAtObstacle ? approachStalledTime + Time.deltaTime : 0f;
                if (approachStalledTime > .35f)
                {
                    combat.CancelRequestedAttack();
                    movementVelocity = Vector3.zero;
                    approachStalledTime = 0f;
                    combat.Notify("Caminho bloqueado. Aproxime-se por outro lado.");
                }
            }
            else approachStalledTime = 0f;
            if (movementVelocity.sqrMagnitude > 0.01f && !combat.IsAttacking && !combat.HasQueuedAttack)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(movementVelocity),
                    1f - Mathf.Exp(-14f * Time.deltaTime));

            UpdateLocomotion(IsMoving ? horizontalVelocity.magnitude : 0f);

            UpdateVisual();
            if (transform.position.y < -8f)
                ResetPosition();
        }

        // Shared by mouse input and PlayMode checks, so a selection does not depend on holding a key.
        public bool SelectAttackTarget(PrototypeMonster enemy)
        {
            combat.CancelRequestedAttack();
            CancelFreeAttack();
            hasDestination = false;
            movementVelocity = Vector3.zero;
            approachStalledTime = 0f;
            combat.SetTarget(enemy);
            return combat.ValidTarget != null && combat.ValidTarget == enemy;
        }

        public void MoveTo(Vector3 point)
        {
            combat.CancelRequestedAttack();
            CancelFreeAttack();
            destination = point;
            hasDestination = true;
        }

        // Retains a short mouse tap while turning, so a free swing hits the chosen direction.
        public bool RequestFreeAttack(Vector3 direction)
        {
            if (!isActiveAndEnabled || combat == null || !combat.CanAttack || combat.ValidTarget != null) return false;
            direction = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (direction.sqrMagnitude < .01f) return false;
            combat.CancelRequestedAttack();
            hasDestination = false;
            freeAttackDirection = direction.normalized;
            freeAttackUntil = Time.time + .75f;
            hasFreeAttack = true;
            return true;
        }

        public void CancelFreeAttack() => hasFreeAttack = false;

        private void UpdateFreeAttack()
        {
            if (!hasFreeAttack) return;
            if (!combat.IsAlive || combat.ValidTarget != null || Time.time >= freeAttackUntil)
            {
                CancelFreeAttack();
                return;
            }
            if (combat.IsAttacking) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation,
                Quaternion.LookRotation(freeAttackDirection), 720f * Time.deltaTime);
            if (Vector3.Angle(transform.forward, freeAttackDirection) <= 9f && combat.RequestAttack(0))
                CancelFreeAttack();
        }

        private void SelectNextEnemy()
        {
            int count = combat.Monsters.Count;
            if (count == 0) return;
            int selected = -1;
            for (int index = 0; index < count; index++)
                if (combat.Monsters[index] == combat.Target) selected = index;
            for (int step = 1; step <= count; step++)
            {
                PrototypeMonster enemy = combat.Monsters[(selected + step) % count];
                if (enemy != null && enemy.IsAvailableForTargeting)
                {
                    combat.SetTarget(enemy);
                    return;
                }
            }
            combat.SetTarget(null);
        }

        private void StopForInterface()
        {
            CancelFreeAttack();
            footstepDistance = 0f;
            combat.CancelRequestedAttack();
            movementVelocity = Vector3.zero;
            hasDestination = false;
            IsMoving = false;
            UpdateLocomotion(0f, true);
            if (!combat.IsAlive)
            {
                if (visualRoot != null) visualRoot.localRotation = Quaternion.Euler(0f, 0f, 75f);
                return;
            }
            if (controller.isGrounded && verticalSpeed < 0f) verticalSpeed = -2f;
            verticalSpeed += Physics.gravity.y * Time.deltaTime;
            CollisionFlags collisions = controller.Move(Vector3.up * verticalSpeed * Time.deltaTime);
            if ((collisions & CollisionFlags.Below) != 0) verticalSpeed = -2f;
            UpdateVisual();
        }

        private void UpdateLocomotion(float metresPerSecond, bool immediate = false)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
            float damping = immediate ? 0f : .06f;
            if (hasMoveParameter)
                animator.SetFloat("MoveSpeed", metresPerSecond / Mathf.Max(.1f, walkSpeed), damping, Time.deltaTime);
            if (hasLocomotionParameter)
                animator.SetFloat("LocomotionSpeed", metresPerSecond, damping, Time.deltaTime);
        }

        private void UpdateVisual()
        {
            if (visualRoot == null)
                return;

            visualRoot.localRotation = Quaternion.identity;
            if (!hasMoveParameter)
            {
                strideTime += Time.deltaTime * (IsMoving ? 10f : 2f);
                float height = IsMoving ? Mathf.Abs(Mathf.Sin(strideTime)) * 0.045f : Mathf.Sin(strideTime) * 0.008f;
                visualRoot.localPosition = originalVisualPosition + Vector3.up * height;
            }
        }

        private void OnDisable()
        {
            CancelFreeAttack();
            if (combat != null) combat.CancelRequestedAttack();
            movementVelocity = Vector3.zero;
            footstepDistance = 0f;
            hasDestination = false;
            IsMoving = false;
            UpdateLocomotion(0f, true);
        }

        public void ResetPosition()
        {
            CancelFreeAttack();
            if (PrototypeOnlineClient.Active != null && PrototypeOnlineClient.Active.IsOnlineMode)
            {
                combat.ResetEncounter();
                return;
            }
            controller.enabled = false;
            transform.SetPositionAndRotation(spawnPosition, Quaternion.identity);
            controller.enabled = true;
            verticalSpeed = 0f;
            movementVelocity = Vector3.zero;
            hasDestination = false;
            UpdateLocomotion(0f, true);
            if (visualRoot != null)
            {
                visualRoot.localPosition = originalVisualPosition;
                visualRoot.localRotation = Quaternion.identity;
            }
            combat.ResetEncounter();
        }


    }
}

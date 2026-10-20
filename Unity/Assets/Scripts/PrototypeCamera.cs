using UnityEngine;

namespace GuerraDasNacoes
{
    [RequireComponent(typeof(Camera))]
    public sealed class PrototypeCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private float distance = 10f;
        [SerializeField] private float pitch = 28f;
        private float yaw;
        private Vector3 velocity;
        private Vector3 followPosition;
        private float impactUntil, impactStartedAt, impactStrength;

        public Transform Target => target;

        private void Awake() { followPosition = transform.position; }

        public void Configure(Transform character)
        {
            target = character;
            SnapToTarget();
        }

        private void LateUpdate()
        {
            if (target == null)
                return;

            if (!PrototypeHud.BlocksGameplay)
            {
                distance = Mathf.Clamp(distance - Input.mouseScrollDelta.y * 0.8f, 4.5f, 18f);
                yaw += ((Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f)) * 65f * Time.deltaTime;
            }
            Vector3 focus = FocusPoint();
            Vector3 desired = focus + Quaternion.Euler(pitch, yaw, 0f) * Vector3.back * distance;
            followPosition = Vector3.SmoothDamp(followPosition, desired, ref velocity, 0.12f);
            Quaternion look = Quaternion.LookRotation(focus - followPosition, Vector3.up);
            float elapsed = Time.time - impactStartedAt;
            float envelope = Mathf.Clamp01((impactUntil - Time.time) / 0.20f);
            Vector3 impact = (look * Vector3.right * Mathf.Sin(elapsed * 83f) +
                Vector3.up * Mathf.Sin(elapsed * 67f) * .5f) * impactStrength * envelope * envelope;
            transform.SetPositionAndRotation(followPosition + impact, look);
        }

        public void RequestImpact(float strength)
        {
            impactStrength = Mathf.Clamp(strength, 0f, .06f);
            impactStartedAt = Time.time;
            impactUntil = Time.time + .20f;
        }

        private void SnapToTarget()
        {
            if (target == null)
                return;
            Vector3 focus = FocusPoint();
            transform.position = focus + Quaternion.Euler(pitch, yaw, 0f) * Vector3.back * distance;
            followPosition = transform.position;
            transform.rotation = Quaternion.LookRotation(focus - transform.position, Vector3.up);
            velocity = Vector3.zero;
        }

        private Vector3 FocusPoint()
        {
            return target.position + Vector3.up * 1.05f + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        }
    }
}

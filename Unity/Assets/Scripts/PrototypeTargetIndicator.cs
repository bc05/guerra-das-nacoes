using UnityEngine;

namespace GuerraDasNacoes
{
    [RequireComponent(typeof(PrototypeCombat))]
    public sealed class PrototypeTargetIndicator : MonoBehaviour
    {
        private PrototypeCombat combat;
        private LineRenderer ring;
        private Material material;
        private const int Segments = 56;

        private void Awake()
        {
            combat = GetComponent<PrototypeCombat>();
            GameObject marker = new GameObject("Alvo selecionado");
            marker.transform.SetParent(transform, false);
            ring = marker.AddComponent<LineRenderer>();
            material = new Material(Shader.Find("Sprites/Default"));
            ring.sharedMaterial = material;
            ring.useWorldSpace = false;
            ring.loop = true;
            ring.positionCount = Segments;
            ring.widthMultiplier = .045f;
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.receiveShadows = false;
            for (int i = 0; i < Segments; i++)
            {
                float angle = i * Mathf.PI * 2f / Segments;
                ring.SetPosition(i, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)));
            }
            ring.enabled = false;
        }

        private void LateUpdate()
        {
            PrototypeMonster target = combat.Target;
            bool visible = target != null && target.IsAvailableForTargeting && !PrototypeGameMenu.HidesHud;
            ring.enabled = visible;
            if (!visible) return;
            CharacterController capsule = target.GetComponent<CharacterController>();
            float radius = capsule != null ? capsule.radius * target.transform.lossyScale.x + .2f : .8f;
            Vector3 point = target.transform.position + Vector3.up * .04f;
            if (Physics.Raycast(point + Vector3.up * 2f, Vector3.down, out RaycastHit ground, 5f, 1 << 8))
                point.y = ground.point.y + .035f;
            ring.transform.SetPositionAndRotation(point, Quaternion.identity);
            ring.transform.localScale = Vector3.one * Mathf.Max(.6f, radius);
            Color tint = MonsterDanger.Tint(target.Level, combat.Stats.Level);
            ring.startColor = ring.endColor = tint;
        }

        private void OnDisable() { if (ring != null) ring.enabled = false; }
        private void OnDestroy() { if (material != null) Destroy(material); }
    }
}

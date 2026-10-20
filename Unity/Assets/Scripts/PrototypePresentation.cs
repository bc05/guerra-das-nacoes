using UnityEngine;

namespace GuerraDasNacoes
{
    [ExecuteAlways, RequireComponent(typeof(Camera))]
    public sealed class PrototypePresentation : MonoBehaviour
    {
        [SerializeField] private Shader presentationShader;
        private Material material;
        private Camera sceneCamera;
        private static bool postProcessingEnabled = true;

        public static bool PostProcessingEnabled { get { return postProcessingEnabled; } }

        public static void SetGraphicsQuality(bool enabled)
        {
            postProcessingEnabled = enabled;
            foreach (PrototypePresentation presentation in FindObjectsByType<PrototypePresentation>(FindObjectsSortMode.None))
                presentation.ApplyCameraQuality();
        }

        private void ApplyCameraQuality()
        {
            if (sceneCamera == null) sceneCamera = GetComponent<Camera>();
            sceneCamera.allowHDR = postProcessingEnabled;
            if (postProcessingEnabled) sceneCamera.depthTextureMode |= DepthTextureMode.DepthNormals;
            else sceneCamera.depthTextureMode &= ~DepthTextureMode.DepthNormals;
        }

        public void Configure(Shader shader) { presentationShader = shader; }
        private void OnEnable()
        {
            sceneCamera = GetComponent<Camera>();
            ApplyCameraQuality();
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (!postProcessingEnabled || presentationShader == null || !presentationShader.isSupported)
            {
                Graphics.Blit(source, destination);
                return;
            }
            if (material == null)
                material = new Material(presentationShader) { hideFlags = HideFlags.HideAndDontSave };
            float tangent = Mathf.Tan(sceneCamera.fieldOfView * Mathf.Deg2Rad * 0.5f);
            material.SetVector("_ProjectionScale", new Vector4(tangent * sceneCamera.aspect, tangent, sceneCamera.farClipPlane, 0f));
            int halfWidth = Mathf.Max(1, source.width / 2), halfHeight = Mathf.Max(1, source.height / 2);
            RenderTexture ao = RenderTexture.GetTemporary(halfWidth, halfHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            RenderTexture softAo = RenderTexture.GetTemporary(halfWidth, halfHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            RenderTexture bloom = RenderTexture.GetTemporary(Mathf.Max(1, source.width / 4), Mathf.Max(1, source.height / 4), 0, RenderTextureFormat.DefaultHDR);
            RenderTexture blur = RenderTexture.GetTemporary(bloom.width, bloom.height, 0, RenderTextureFormat.DefaultHDR);
            try
            {
                Graphics.Blit(source, ao, material, 0);
                Graphics.Blit(ao, softAo, material, 1);
                Graphics.Blit(source, bloom, material, 2);
                material.SetVector("_BlurDirection", Vector2.right);
                Graphics.Blit(bloom, blur, material, 3);
                material.SetVector("_BlurDirection", Vector2.up);
                Graphics.Blit(blur, bloom, material, 3);
                material.SetTexture("_OcclusionTex", softAo);
                material.SetTexture("_BloomTex", bloom);
                Graphics.Blit(source, destination, material, 4);
            }
            finally
            {
                RenderTexture.ReleaseTemporary(ao);
                RenderTexture.ReleaseTemporary(softAo);
                RenderTexture.ReleaseTemporary(bloom);
                RenderTexture.ReleaseTemporary(blur);
            }
        }

        private void OnDisable()
        {
            if (material != null)
            {
                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
                material = null;
            }
        }
    }
}

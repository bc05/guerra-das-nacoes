using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GuerraDasNacoes
{
    [DefaultExecutionOrder(-1000)]
    public sealed class PrototypeGraphicsSettings : MonoBehaviour
    {
        public const float ConfirmationSeconds = 15f;
        private static PrototypeGraphicsSettings instance;
        private GraphicsSettingsData current, confirmed;
        private string settingsPath;
        private double confirmationDeadline;
        private bool initialized, allowDisplayChanges;
        private float nextCameraRefresh;

        public static PrototypeGraphicsSettings Instance { get { return GetOrCreate(); } }
        public GraphicsSettingsData Current { get { EnsureInitialized(); return current.Clone(); } }
        public GraphicsSettingsData Confirmed { get { EnsureInitialized(); return confirmed.Clone(); } }
        public bool HasPendingConfirmation { get; private set; }
        public float PendingRemaining { get { return HasPendingConfirmation ? Mathf.Max(0f, (float)(confirmationDeadline - Time.realtimeSinceStartupAsDouble)) : 0f; } }
        public string Status { get; private set; } = "";
        public GraphicsResolution[] SupportedResolutions { get { return BuildResolutions(); } }

        public static PrototypeGraphicsSettings GetOrCreate()
        {
            if (instance != null) return instance;
            instance = FindFirstObjectByType<PrototypeGraphicsSettings>();
            if (instance == null) instance = new GameObject("Configurações gráficas").AddComponent<PrototypeGraphicsSettings>();
            return instance;
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(this); return; }
            instance = this;
            if (Application.isPlaying) DontDestroyOnLoad(gameObject);
            // Automated checks and editor play sessions must never resize the developer's desktop.
            allowDisplayChanges = !Application.isEditor && !Application.isBatchMode;
            settingsPath = Path.Combine(Application.persistentDataPath, "GraphicsSettings.json");
        }

        private void Start() { EnsureInitialized(); }

        private void EnsureInitialized()
        {
            if (initialized) return;
            if (string.IsNullOrEmpty(settingsPath)) settingsPath = Path.Combine(Application.persistentDataPath, "GraphicsSettings.json");
            GraphicsSettingsData fallback = CreatePreset(GraphicsPreset.High);
            fallback.width = Mathf.Max(960, Screen.width);
            fallback.height = Mathf.Max(540, Screen.height);
            fallback.windowMode = Screen.fullScreenMode == FullScreenMode.Windowed ? GraphicsWindowMode.Windowed : GraphicsWindowMode.Borderless;
            confirmed = GraphicsSettingsStorage.Load(settingsPath, fallback, out string notice);
            FitSupportedDisplay(confirmed);
            current = confirmed.Clone();
            initialized = true;
            ApplyEngine(current, allowDisplayChanges);
            Status = notice;
        }

        public bool Apply(GraphicsSettingsData proposed)
        {
            EnsureInitialized();
            if (proposed == null) { Status = "Configuração ausente."; return false; }
            if (!proposed.IsValid(out string reason)) { Status = reason; return false; }
            GraphicsSettingsData next = proposed.Clone();
            if (!IsSupportedResolution(next.width, next.height)) { Status = "Escolha uma das resoluções disponíveis para esta tela."; return false; }
            bool displayChanged = !next.HasSameDisplay(confirmed);
            // Persist the known-good baseline before previewing display changes, including on first launch.
            // Unity may remember its own last window size after a crash; our next startup must override it.
            if (displayChanged && !GraphicsSettingsStorage.TrySave(settingsPath, confirmed, out string baselineError))
            { Status = baselineError; return false; }
            current = next;
            ApplyEngine(current, allowDisplayChanges);
            if (displayChanged)
            {
                HasPendingConfirmation = true;
                confirmationDeadline = Time.realtimeSinceStartupAsDouble + ConfirmationSeconds;
                Status = "A imagem está boa? Confirme em até 15 segundos para manter estas opções.";
                return true;
            }
            HasPendingConfirmation = false;
            return SaveConfirmed();
        }

        public bool Confirm()
        {
            EnsureInitialized();
            if (!HasPendingConfirmation) return true;
            if (PendingRemaining <= 0f) { Revert(); return false; }
            return SaveConfirmed();
        }

        private bool SaveConfirmed()
        {
            if (!GraphicsSettingsStorage.TrySave(settingsPath, current, out string error))
            {
                Revert(); Status = error; return false;
            }
            confirmed = current.Clone();
            HasPendingConfirmation = false;
            Status = "Opções gráficas salvas neste computador.";
            return true;
        }

        public void Revert()
        {
            EnsureInitialized();
            HasPendingConfirmation = false;
            current = confirmed.Clone();
            ApplyEngine(current, allowDisplayChanges);
            Status = "A configuração anterior foi restaurada.";
        }

        private void Update()
        {
            EnsureInitialized();
            if (HasPendingConfirmation && PendingRemaining <= 0f) Revert();
            if (Time.unscaledTime >= nextCameraRefresh)
            {
                nextCameraRefresh = Time.unscaledTime + .5f;
                ApplyCameras(current);
            }
        }

        private void OnApplicationQuit() { if (initialized && HasPendingConfirmation) Revert(); }
        private void OnDisable() { if (initialized && HasPendingConfirmation) Revert(); }
        private void OnDestroy() { if (instance == this) instance = null; }

        public static GraphicsSettingsData CreatePreset(GraphicsPreset preset, GraphicsSettingsData existing = null)
        {
            GraphicsSettingsData value = existing == null ? new GraphicsSettingsData() : existing.Clone();
            value.schemaVersion = GraphicsSettingsData.CurrentSchemaVersion;
            value.preset = preset;
            switch (preset)
            {
                case GraphicsPreset.Low:
                    value.shadows = GraphicsShadowQuality.Off; value.antiAliasing = 0; value.textureQuality = 2;
                    value.viewDistance = 70f; value.postProcessing = false; break;
                case GraphicsPreset.Medium:
                    value.shadows = GraphicsShadowQuality.Low; value.antiAliasing = 2; value.textureQuality = 1;
                    value.viewDistance = 110f; value.postProcessing = true; break;
                case GraphicsPreset.High:
                    value.shadows = GraphicsShadowQuality.High; value.antiAliasing = 2; value.textureQuality = 0;
                    value.viewDistance = 150f; value.postProcessing = true; break;
                case GraphicsPreset.Ultra:
                    value.shadows = GraphicsShadowQuality.High; value.antiAliasing = 4; value.textureQuality = 0;
                    value.viewDistance = 220f; value.postProcessing = true; break;
            }
            return value;
        }

        private void ApplyEngine(GraphicsSettingsData value, bool changeDisplay)
        {
            // Preset is a UI label only: changing FPS or display marks it Custom without changing visual cost.
            bool lowestDetail = value.shadows == GraphicsShadowQuality.Off && value.textureQuality == 2 &&
                value.viewDistance <= 80f && !value.postProcessing;
            bool extendedShadows = value.shadows == GraphicsShadowQuality.High && value.viewDistance >= 190f;
            QualitySettings.globalTextureMipmapLimit = value.textureQuality;
            QualitySettings.antiAliasing = value.antiAliasing;
            QualitySettings.vSyncCount = value.vSync ? 1 : 0;
            Application.targetFrameRate = value.frameRateLimit;
            QualitySettings.shadows = value.shadows == GraphicsShadowQuality.Off ? ShadowQuality.Disable :
                value.shadows == GraphicsShadowQuality.Low ? ShadowQuality.HardOnly : ShadowQuality.All;
            QualitySettings.shadowResolution = value.shadows == GraphicsShadowQuality.Low ? ShadowResolution.Low :
                extendedShadows ? ShadowResolution.VeryHigh : ShadowResolution.High;
            QualitySettings.shadowDistance = value.shadows == GraphicsShadowQuality.Off ? 0f : Mathf.Min(value.viewDistance, value.shadows == GraphicsShadowQuality.Low ? 24f : 60f);
            QualitySettings.shadowCascades = value.shadows == GraphicsShadowQuality.High ? (extendedShadows ? 4 : 2) : 0;
            QualitySettings.pixelLightCount = lowestDetail ? 1 : value.viewDistance >= 190f ? 4 : 2;
            QualitySettings.anisotropicFiltering = value.textureQuality == 2 ? AnisotropicFiltering.Disable : AnisotropicFiltering.Enable;
            QualitySettings.lodBias = Mathf.Lerp(.5f, 2f, (value.viewDistance - 60f) / 160f);
            QualitySettings.softParticles = value.postProcessing;
            QualitySettings.realtimeReflectionProbes = !lowestDetail;
            PrototypePresentation.SetGraphicsQuality(value.postProcessing);
            ApplyCameras(value);
            if (changeDisplay)
                Screen.SetResolution(value.width, value.height, ToFullScreenMode(value.windowMode));
        }

        private void ApplyCameras(GraphicsSettingsData value)
        {
            foreach (Camera camera in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                // The equipment portrait renders its own layer and retains its local framing.
                if (camera.cameraType != CameraType.Game || camera.targetTexture != null) continue;
                camera.farClipPlane = value.viewDistance;
                camera.allowMSAA = value.antiAliasing > 0;
            }
        }

        private static FullScreenMode ToFullScreenMode(GraphicsWindowMode mode)
        {
            if (mode == GraphicsWindowMode.Windowed) return FullScreenMode.Windowed;
            // macOS uses native fullscreen windows; exclusive mode is Windows-specific.
            if (mode == GraphicsWindowMode.Fullscreen)
                return Application.platform == RuntimePlatform.WindowsPlayer ? FullScreenMode.ExclusiveFullScreen : FullScreenMode.MaximizedWindow;
            return FullScreenMode.FullScreenWindow;
        }

        private static GraphicsResolution[] BuildResolutions()
        {
            List<GraphicsResolution> values = new List<GraphicsResolution>();
            int maxWidth = Screen.currentResolution.width > 0 ? Screen.currentResolution.width : Mathf.Max(Screen.width, 1280);
            int maxHeight = Screen.currentResolution.height > 0 ? Screen.currentResolution.height : Mathf.Max(Screen.height, 720);
            foreach (Resolution resolution in Screen.resolutions) AddResolution(values, resolution.width, resolution.height, maxWidth, maxHeight);
            AddResolution(values, Screen.width, Screen.height, maxWidth, maxHeight);
            AddResolution(values, 960, 540, maxWidth, maxHeight);
            AddResolution(values, 1280, 720, maxWidth, maxHeight);
            AddResolution(values, 1600, 900, maxWidth, maxHeight);
            AddResolution(values, 1920, 1080, maxWidth, maxHeight);
            AddResolution(values, 2560, 1440, maxWidth, maxHeight);
            if (values.Count == 0) values.Add(new GraphicsResolution(960, 540));
            values.Sort((a, b) => ((long)a.width * a.height).CompareTo((long)b.width * b.height));
            return values.ToArray();
        }

        private static void AddResolution(List<GraphicsResolution> values, int width, int height, int maxWidth, int maxHeight)
        {
            if (width < 960 || height < 540 || width > maxWidth || height > maxHeight) return;
            foreach (GraphicsResolution value in values) if (value.width == width && value.height == height) return;
            values.Add(new GraphicsResolution(width, height));
        }

        private static bool IsSupportedResolution(int width, int height)
        {
            foreach (GraphicsResolution value in BuildResolutions()) if (value.width == width && value.height == height) return true;
            return false;
        }

        private static void FitSupportedDisplay(GraphicsSettingsData value)
        {
            if (IsSupportedResolution(value.width, value.height)) return;
            GraphicsResolution[] choices = BuildResolutions();
            GraphicsResolution fallback = choices[0];
            foreach (GraphicsResolution choice in choices)
                if (choice.width <= value.width && choice.height <= value.height) fallback = choice;
            value.width = fallback.width; value.height = fallback.height;
            value.windowMode = GraphicsWindowMode.Windowed;
        }

#if UNITY_EDITOR
        // Isolated checks set this before initialization, avoiding real player preferences.
        public void ConfigureForChecks(string path)
        {
            if (initialized) throw new InvalidOperationException("Configure checks before reading graphics settings.");
            settingsPath = path;
            allowDisplayChanges = false;
        }
#endif
    }
}

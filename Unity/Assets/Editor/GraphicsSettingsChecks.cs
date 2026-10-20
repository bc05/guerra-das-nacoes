using System;
using System.IO;
using System.Reflection;
using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class GraphicsSettingsChecks
{
    private const string Key = "GuerraDasNacoes.GraphicsChecks.";
    private static string folder;
    private static int assertions;
    static GraphicsSettingsChecks() { EditorApplication.update += Tick; }

    public static void RunCore()
    {
        folder = Path.Combine(Path.GetTempPath(), "guerra-graphics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try { CheckStorage(); Debug.Log("[GRAPHICS CORE PASS] " + assertions + " verificações de presets e persistência isolada."); }
        finally { Directory.Delete(folder, true); }
    }

    [MenuItem("Guerra das Nações/Validar configurações gráficas")]
    public static void Run()
    {
        SessionState.SetBool(Key + "Active", false);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Key + "Entered", false);
        SessionState.SetFloat(Key + "Timeout", (float)EditorApplication.timeSinceStartup + 75f);
        SessionState.SetBool(Key + "Active", true);
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Key + "Active", false)) return;
        try
        {
            Require(EditorApplication.timeSinceStartup < SessionState.GetFloat(Key + "Timeout", 0), "tempo máximo excedido");
            if (!EditorApplication.isPlaying)
            {
                Require(!SessionState.GetBool(Key + "Entered", false), "PlayMode encerrou antecipadamente");
                return;
            }
            SessionState.SetBool(Key + "Entered", true);
            if (EditorApplication.isCompiling || Time.frameCount < 3) return;
            folder = Path.Combine(Path.GetTempPath(), "guerra-graphics-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            CheckStorage();
            CheckEngine();
            Finish(true, assertions + " verificações: presets reais, texturas, sombras, câmera, pós-processamento, FPS/VSync, cópias independentes, JSON/cópia anterior, confirmação e reversão sem tocar no perfil.");
        }
        catch (Exception failure) { Finish(false, failure.ToString()); }
    }

    private static void CheckStorage()
    {
        string path = Path.Combine(folder, "GraphicsSettings.json");
        GraphicsSettingsData high = PrototypeGraphicsSettings.CreatePreset(GraphicsPreset.High);
        Require(high.IsValid(out _), "preset alto válido");
        GraphicsSettingsData clone = high.Clone(); clone.textureQuality = 2;
        Require(high.textureQuality == 0, "clone não altera original");
        foreach (GraphicsPreset preset in new[] { GraphicsPreset.Low, GraphicsPreset.Medium, GraphicsPreset.High, GraphicsPreset.Ultra })
            Require(PrototypeGraphicsSettings.CreatePreset(preset, high).IsValid(out _), "preset válido " + preset);
        Require(GraphicsSettingsStorage.TrySave(path, high, out _), "grava configuração inicial");
        GraphicsSettingsData loaded = GraphicsSettingsStorage.Load(path, high, out _);
        Require(loaded.preset == GraphicsPreset.High && loaded.width == high.width, "roundtrip preserva opções");
        GraphicsSettingsData low = PrototypeGraphicsSettings.CreatePreset(GraphicsPreset.Low, high);
        Require(GraphicsSettingsStorage.TrySave(path, low, out _), "grava próxima configuração atomicamente");
        Require(File.Exists(path + ".bak"), "mantém configuração anterior");
        File.WriteAllText(path, "{ JSON interrompido");
        loaded = GraphicsSettingsStorage.Load(path, low, out string notice);
        Require(loaded.preset == GraphicsPreset.High && notice.Length > 0, "JSON corrompido recupera último backup válido");
        Require(GraphicsSettingsStorage.TrySave(path, low, out _), "salva após recuperação");
        File.WriteAllText(path, "{}");
        loaded = GraphicsSettingsStorage.Load(path, low, out _);
        Require(loaded.preset == GraphicsPreset.High, "arquivo inválido não contamina backup válido");
        File.WriteAllText(path + ".bak", "{\"schemaVersion\":99}");
        loaded = GraphicsSettingsStorage.Load(path, low, out notice);
        Require(loaded.preset == GraphicsPreset.Low && notice.Length > 0, "corrupção dupla usa fallback sem apagar dados");
        GraphicsSettingsData invalid = low.Clone(); invalid.viewDistance = float.NaN;
        Require(!GraphicsSettingsStorage.TrySave(path, invalid, out _), "NaN nunca persiste");
        invalid = low.Clone(); invalid.frameRateLimit = 7;
        Require(!invalid.IsValid(out _), "FPS inválido rejeitado");
        invalid = low.Clone(); invalid.antiAliasing = 3;
        Require(!invalid.IsValid(out _), "AA inválido rejeitado");
        invalid = low.Clone(); invalid.width = -1;
        Require(!invalid.IsValid(out _), "resolução inválida rejeitada");
        invalid = low.Clone(); invalid.schemaVersion = 2;
        Require(!invalid.IsValid(out _), "versão futura rejeitada");
        Require(Directory.GetFiles(folder, "*.tmp").Length == 0, "gravações não deixam temporários");
        Require(!GraphicsSettingsStorage.TrySave(Path.Combine(path, "child.json"), low, out _), "falha de pasta não derruba o jogo");
    }

    private static void CheckEngine()
    {
        string path = Path.Combine(folder, "engine.json");
        GameObject cameraObject = new GameObject("Graphics check camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        cameraObject.AddComponent<PrototypePresentation>();
        GameObject owner = new GameObject("Graphics check preferences");
        PrototypeGraphicsSettings settings = owner.AddComponent<PrototypeGraphicsSettings>();
        settings.ConfigureForChecks(path);
        GraphicsSettingsData initial = settings.Current;
        GraphicsSettingsData low = PrototypeGraphicsSettings.CreatePreset(GraphicsPreset.Low, initial);
        Require(settings.Apply(low), "aplica preset baixo");
        Require(!settings.HasPendingConfirmation, "qualidade sem troca de tela salva imediatamente");
        Require(QualitySettings.shadows == ShadowQuality.Disable && QualitySettings.shadowDistance == 0f, "baixo desliga sombras reais");
        Require(QualitySettings.globalTextureMipmapLimit == 2 && QualitySettings.antiAliasing == 0, "baixo reduz texturas e MSAA");
        Require(!PrototypePresentation.PostProcessingEnabled && !camera.allowHDR && !camera.allowMSAA, "baixo evita AO/bloom/HDR e MSAA");
        Require(Mathf.Approximately(camera.farClipPlane, 70f), "baixo reduz distância real de renderização");
        cameraObject.SetActive(false); cameraObject.SetActive(true);
        Require(!camera.allowHDR, "reativar câmera não força HDR ligado");
        Require(QualitySettings.pixelLightCount == 1 && !QualitySettings.realtimeReflectionProbes, "baixo limita luzes e reflexos em tempo real");
        CheckCustomKeepsVisualCost(settings, low, "baixo");
        GraphicsSettingsData high = PrototypeGraphicsSettings.CreatePreset(GraphicsPreset.Ultra, initial);
        high.vSync = true; high.frameRateLimit = 120;
        Require(settings.Apply(high), "aplica ultra");
        Require(QualitySettings.shadows == ShadowQuality.All && QualitySettings.shadowCascades == 4, "ultra aplica sombras reais");
        Require(QualitySettings.globalTextureMipmapLimit == 0 && QualitySettings.antiAliasing == 4, "ultra restaura texturas e MSAA");
        Require(PrototypePresentation.PostProcessingEnabled && camera.allowHDR && camera.allowMSAA, "ultra restaura pós-processamento");
        Require(Mathf.Approximately(camera.farClipPlane, 220f), "distância aumenta de fato");
        Require(QualitySettings.vSyncCount == 1 && Application.targetFrameRate == 120, "sincronização e limite de FPS aplicados");
        Require(QualitySettings.pixelLightCount == 4 && QualitySettings.shadowResolution == ShadowResolution.VeryHigh, "ultra usa luzes e resolução de sombras máximas");
        CheckCustomKeepsVisualCost(settings, high, "ultra");
        GraphicsSettingsData detached = settings.Current; detached.width = 9999;
        Require(settings.Current.width != 9999, "UI recebe cópia independente");
        string saved = File.ReadAllText(path);
        GraphicsSettingsData display = settings.Current;
        display.windowMode = display.windowMode == GraphicsWindowMode.Windowed ? GraphicsWindowMode.Borderless : GraphicsWindowMode.Windowed;
        Require(settings.Apply(display) && settings.HasPendingConfirmation, "mudança de tela exige confirmação");
        Require(settings.PendingRemaining > 14f && settings.PendingRemaining <= 15f, "prazo inicial de 15 segundos");
        Require(File.ReadAllText(path) == saved, "prévia não grava opções não confirmadas");
        settings.Revert();
        Require(!settings.HasPendingConfirmation && settings.Current.HasSameDisplay(high), "cancelar restaura configuração confirmada");
        Require(settings.Apply(display) && settings.Confirm(), "confirmar aceita nova tela");
        Require(!settings.HasPendingConfirmation && settings.Confirmed.HasSameDisplay(display), "confirmação persiste nova tela");
        GraphicsSettingsData persisted = GraphicsSettingsStorage.Load(path, initial, out _);
        Require(persisted.HasSameDisplay(display), "reabertura carrega somente tela confirmada");
        GraphicsSettingsData pending = settings.Current;
        pending.windowMode = GraphicsWindowMode.Fullscreen;
        if (pending.windowMode == settings.Confirmed.windowMode) pending.windowMode = GraphicsWindowMode.Windowed;
        Require(settings.Apply(pending) && settings.HasPendingConfirmation, "prepara expiração");
        Time.timeScale = 0f;
        typeof(PrototypeGraphicsSettings).GetField("confirmationDeadline", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(settings, Time.realtimeSinceStartupAsDouble - 1d);
        typeof(PrototypeGraphicsSettings).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(settings, null);
        Require(!settings.HasPendingConfirmation && settings.Current.HasSameDisplay(display), "expira com tempo de jogo pausado");
        Time.timeScale = 1f;
        typeof(PrototypeGraphicsSettings).GetField("settingsPath", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(settings, Path.Combine(path, "child.json"));
        GraphicsSettingsData cannotSave = PrototypeGraphicsSettings.CreatePreset(GraphicsPreset.Low, settings.Current);
        Require(!settings.Apply(cannotSave), "falha de gravação é informada");
        Require(settings.Current.preset == GraphicsPreset.Ultra && QualitySettings.shadows == ShadowQuality.All, "falha de gravação restaura opções confirmadas");
        typeof(PrototypeGraphicsSettings).GetField("settingsPath", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(settings, path);
        string firstDisplayPath = Path.Combine(folder, "first-display.json");
        typeof(PrototypeGraphicsSettings).GetField("settingsPath", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(settings, firstDisplayPath);
        Require(!File.Exists(firstDisplayPath), "primeira prévia começa sem arquivo de opções");
        Require(settings.Apply(pending) && settings.HasPendingConfirmation, "prepara desativação pendente");
        persisted = GraphicsSettingsStorage.Load(firstDisplayPath, initial, out _);
        Require(persisted.HasSameDisplay(display), "primeira prévia grava baseline confirmado antes de mudar tela");
        settings.enabled = false;
        Require(!settings.HasPendingConfirmation && settings.Current.HasSameDisplay(display), "desativar reverte prévia pendente");
        Require(File.ReadAllText(path).Contains("\"schemaVersion\""), "preferências têm versão explícita");
        UnityEngine.Object.Destroy(owner);
        UnityEngine.Object.Destroy(cameraObject);
    }

    private static void CheckCustomKeepsVisualCost(PrototypeGraphicsSettings settings, GraphicsSettingsData preset, string label)
    {
        string baseline = CaptureVisualCost();
        GraphicsSettingsData custom = preset.Clone();
        custom.preset = GraphicsPreset.Custom;
        custom.frameRateLimit = preset.frameRateLimit == 30 ? 60 : 30;
        Require(settings.Apply(custom), label + " personalizado aceita somente novo FPS");
        Require(CaptureVisualCost() == baseline, label + " mantém custo visual ao mudar FPS e virar personalizado");
        Require(Application.targetFrameRate == custom.frameRateLimit, label + " aplica o FPS personalizado");
        GraphicsSettingsData display = custom.Clone();
        bool alternate = false;
        foreach (GraphicsResolution resolution in settings.SupportedResolutions)
        {
            if (resolution.width == display.width && resolution.height == display.height) continue;
            display.width = resolution.width; display.height = resolution.height; alternate = true; break;
        }
        Require(alternate, "teste possui resolução alternativa");
        Require(settings.Apply(display) && settings.HasPendingConfirmation, label + " personalizado aceita prévia de resolução");
        Require(CaptureVisualCost() == baseline, label + " mantém custo visual ao mudar somente resolução");
        settings.Revert();
        Require(CaptureVisualCost() == baseline, label + " mantém custo visual após cancelar resolução");
        Require(settings.Apply(preset), label + " restaura rótulo e FPS originais sem mudança visual");
    }

    private static string CaptureVisualCost()
    {
        return string.Join("|", new object[] { QualitySettings.shadows, QualitySettings.shadowResolution,
            QualitySettings.shadowCascades, QualitySettings.shadowDistance, QualitySettings.pixelLightCount,
            QualitySettings.realtimeReflectionProbes, QualitySettings.globalTextureMipmapLimit,
            QualitySettings.antiAliasing, QualitySettings.anisotropicFiltering, QualitySettings.lodBias,
            QualitySettings.softParticles, PrototypePresentation.PostProcessingEnabled });
    }

    private static void Require(bool value, string message)
    {
        assertions++;
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Finish(bool success, string message)
    {
        SessionState.SetBool(Key + "Active", false);
        Time.timeScale = 1f;
        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder)) Directory.Delete(folder, true);
        if (success) Debug.Log("[GRAPHICS PLAY PASS] " + message);
        else Debug.LogError("[GRAPHICS PLAY FAIL] " + message);
        EditorApplication.Exit(success ? 0 : 1);
    }
}

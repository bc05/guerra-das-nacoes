using System;
using System.Reflection;
using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class AudioPresentationChecks
{
    private const string Key = "GuerraDasNacoes.AudioChecks.";
    private static PrototypeAudio audio;
    private static float master, music, effects;
    private static bool muted, captured;
    private static int stage;
    private static float nextAt;
    static AudioPresentationChecks() { EditorApplication.update += Tick; }

    [MenuItem("Guerra das Nações/Validar som e controles de volume")]
    public static void Run()
    {
        SessionState.SetBool(Key + "Active", false);
        EditorSceneManager.OpenScene("Assets/Scenes/Patio.unity", OpenSceneMode.Single);
        SessionState.SetBool(Key + "Entered", false);
        SessionState.SetFloat(Key + "Timeout", (float)EditorApplication.timeSinceStartup + 60f);
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
                Require(!SessionState.GetBool(Key + "Entered", false), "PlayMode encerrou antes do check");
                return;
            }
            SessionState.SetBool(Key + "Entered", true);
            if (EditorApplication.isCompiling || Time.frameCount < 3 || Time.time < nextAt) return;
            if (stage == 0)
            {
                audio = UnityEngine.Object.FindFirstObjectByType<PrototypeAudio>();
                Require(audio != null && audio.HasMusicClip && audio.MusicSourceCount == 1, "música carregada em uma única fonte");
                Require(UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length == 1, "um único ouvinte na cena");
                AudioClip[] clips = Resources.LoadAll<AudioClip>("Audio");
                Require(clips.Length == 34, "34 clipes de música, ambiente e efeitos incluídos");
                AudioClip track = Resources.Load<AudioClip>("Audio/ambient_hope");
                Require(track.length > 300f && track.loadType == AudioClipLoadType.Streaming, "trilha longa usa streaming");
                master = PrototypeAudio.MasterVolume; music = PrototypeAudio.MusicVolume;
                effects = PrototypeAudio.EffectsVolume; muted = PrototypeAudio.Muted; captured = true;
                PrototypeAudio.MasterVolume = float.NaN;
                Require(PrototypeAudio.MasterVolume == 0f, "volume inválido não alcança saída");
                PrototypeAudio.MasterVolume = 2f;
                Require(PrototypeAudio.MasterVolume == 1f, "volume limitado a 100%");
                PrototypeAudio.Muted = true;
                PrototypeAudio.Play(GameSound.MonsterImpact, Vector3.zero);
                Require(audio.ActiveVoiceCount == 0, "silenciar impede novas vozes");
                PrototypeAudio.Muted = false;
                PrototypeAudio.EffectsVolume = .6f;
                PrototypeAudio.MusicVolume = .08f;
                Vector3 contact = Camera.main.transform.position + Vector3.forward * 2f;
                PrototypeAudio.Play(GameSound.BladeHit, contact);
                bool worldVoice = false;
                foreach (AudioSource source in UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
                    if (source.spatialBlend > .5f && source.clip != null)
                    {
                        Require(!source.transform.IsChildOf(Camera.main.transform), "impactos não se movem com a câmera");
                        worldVoice = true;
                    }
                Require(worldVoice, "banco de efeitos reproduz o clipe espacial");
                typeof(PrototypeHud).GetMethod("ToggleAudio", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
                Require(PrototypeHud.IsAudioOpen && PrototypeHud.BlocksGameplay, "painel de som bloqueia ações no mundo");
                stage = 1; nextAt = Time.time + .25f;
            }
            else
            {
                Require(PrototypeHud.BlocksGameplay, "painel permanece modal nos próximos quadros");
                typeof(PrototypeHud).GetMethod("ClosePanels", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
                Require(!PrototypeHud.IsAudioOpen, "painel fecha sem alterar personagem");
                Require(audio.ActiveVoiceCount <= PrototypeAudio.VoiceCapacity, "pool mantém limite de vozes");
                Finish(true, "34 clipes, música em streaming, uma fonte musical/um ouvinte, áudio 3D, volume seguro, silêncio e painel modal.");
            }
        }
        catch (Exception error) { Finish(false, error.ToString()); }
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Finish(bool success, string message)
    {
        SessionState.SetBool(Key + "Active", false);
        if (captured)
        {
            PrototypeAudio.MasterVolume = master; PrototypeAudio.MusicVolume = music;
            PrototypeAudio.EffectsVolume = effects; PrototypeAudio.Muted = muted;
            PrototypeAudio.SavePreferences();
        }
        if (success) Debug.Log("[AUDIO PLAY PASS] " + message);
        else Debug.LogError("[AUDIO PLAY FAIL] " + message);
        EditorApplication.Exit(success ? 0 : 1);
    }
}

using System;
using UnityEngine;

namespace GuerraDasNacoes
{
    public enum GameSound
    {
        SwordSwing, HeavySwing, BladeHit, HeroHurt, Guard, Heal, Fury, LevelUp,
        MonsterWindup, MonsterStrike, MonsterImpact, MonsterHurt, MonsterDeath, Footstep
    }

    /// <summary>Local presentation only. Sound never changes combat or progression.</summary>
    [DisallowMultipleComponent]
    public sealed class PrototypeAudio : MonoBehaviour
    {
        public const int VoiceCapacity = 24;
        const string PreferencePrefix = "GuerraDasNacoes.Audio.";
        const float DefaultMusicVolume = .08f;
        const float DefaultEffectsVolume = .60f;
        static PrototypeAudio instance;
        static bool preferencesLoaded;
        static bool preferencesDirty;
        static float masterVolume = 1f;
        static float musicVolume = DefaultMusicVolume;
        static float effectsVolume = DefaultEffectsVolume;
        static bool muted;

        sealed class Voice
        {
            public AudioSource Source;
            public GameSound Sound;
            public float Gain;
            public int Priority;
            public double EndsAt;
        }

        sealed class Bank
        {
            public AudioClip[] Clips;
            public float Gain;
            public float Cooldown;
            public int MaxVoices;
            public int Priority;
            public int Previous = -1;
            public double NextAt;
        }

        readonly Voice[] voices = new Voice[VoiceCapacity];
        readonly Bank[] banks = new Bank[14];
        Transform voiceRoot;
        AudioSource music;
        AudioSource ambience;
        float fade;
        float effectsHeadroom = 1f;
        float preferenceSaveAt;
        bool initialized;

        public static PrototypeAudio Instance => instance;
        public static float MasterVolume
        {
            get { LoadPreferences(); return masterVolume; }
            set { LoadPreferences(); float next = SafeVolume(value); if (masterVolume == next) return; masterVolume = next; PreferencesChanged(); }
        }
        public static float MusicVolume
        {
            get { LoadPreferences(); return musicVolume; }
            set { LoadPreferences(); float next = SafeVolume(value); if (musicVolume == next) return; musicVolume = next; PreferencesChanged(); }
        }
        public static float EffectsVolume
        {
            get { LoadPreferences(); return effectsVolume; }
            set { LoadPreferences(); float next = SafeVolume(value); if (effectsVolume == next) return; effectsVolume = next; PreferencesChanged(); }
        }
        public static bool Muted
        {
            get { LoadPreferences(); return muted; }
            set { LoadPreferences(); if (muted == value) return; muted = value; PreferencesChanged(); }
        }
        public int ActiveVoiceCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < voices.Length; i++)
                    if (voices[i] != null && voices[i].Source != null && voices[i].Source.isPlaying) count++;
                return count;
            }
        }
        public bool HasMusicClip => music != null && music.clip != null;
        public int MusicSourceCount => music != null ? 1 : 0;

        public static PrototypeAudio EnsureCreated()
        {
            if (instance != null) return instance;
            instance = FindFirstObjectByType<PrototypeAudio>();
            if (instance == null) instance = new GameObject("Audio do mundo").AddComponent<PrototypeAudio>();
            return instance;
        }

        public static void Play(GameSound sound, Vector3 worldPosition, float gain = 1f, bool elite = false)
        {
            // Calls during edit-time preparation must not create scene objects or play sound.
            if (!Application.isPlaying || (int)sound < 0 || (int)sound >= 14) return;
            if (!float.IsFinite(gain) || gain <= 0f || !Finite(worldPosition)) return;
            EnsureCreated().PlayInternal(sound, worldPosition, Mathf.Min(gain, 2f), elite);
        }

        void Awake()
        {
            if (instance != null && instance != this) { Destroy(this); return; }
            instance = this;
            if (!Application.isPlaying) return;
            Initialize();
        }

        void OnEnable()
        {
            if (!Application.isPlaying || instance != this || !initialized) return;
            fade = 0f;
            effectsHeadroom = 1f;
            if (music != null && music.clip != null && !music.isPlaying)
            {
                music.volume = 0f;
                music.Play();
            }
            if (ambience != null && ambience.clip != null && !ambience.isPlaying)
            {
                ambience.volume = 0f;
                ambience.Play();
            }
        }

        void Initialize()
        {
            if (initialized) return;
            initialized = true;
            LoadPreferences();
            // Music is a single 2D streamed source; the existing camera owns the AudioListener.
            music = CreateSource("Musica ambiente", false);
            music.clip = Resources.Load<AudioClip>("Audio/ambient_hope");
            music.loop = true;
            music.priority = 16;
            music.volume = 0f;
            if (music.clip != null) music.Play();
            ambience = CreateSource("Vento discreto", false);
            ambience.clip = Resources.Load<AudioClip>("Audio/ambient_wind");
            ambience.loop = true;
            ambience.priority = 160;
            ambience.volume = 0f;
            if (ambience.clip != null) ambience.Play();

            Configure(GameSound.SwordSwing, "sword_swing", 3, .55f, .065f, 4, 80);
            Configure(GameSound.HeavySwing, "heavy_swing", 2, .72f, .12f, 3, 50);
            Configure(GameSound.BladeHit, "blade_hit", 3, .72f, .04f, 5, 60);
            Configure(GameSound.HeroHurt, "hero_hurt", 2, .62f, .12f, 2, 40);
            Configure(GameSound.Guard, "magic_guard", 1, .60f, .18f, 2, 70);
            Configure(GameSound.Heal, "magic_heal", 1, .55f, .18f, 2, 70);
            Configure(GameSound.Fury, "magic_fury", 1, .65f, .18f, 2, 55);
            Configure(GameSound.LevelUp, "level_up", 1, .50f, .5f, 1, 25);
            Configure(GameSound.MonsterWindup, "monster_windup", 3, .57f, .13f, 3, 65);
            Configure(GameSound.MonsterStrike, "monster_strike", 3, .68f, .11f, 3, 45);
            Configure(GameSound.MonsterImpact, "monster_impact", 3, .82f, .08f, 4, 35);
            Configure(GameSound.MonsterHurt, "monster_hurt", 3, .43f, .18f, 3, 85);
            Configure(GameSound.MonsterDeath, "monster_death", 2, .62f, .12f, 3, 40);
            Configure(GameSound.Footstep, "footstep", 4, .24f, .065f, 4, 150);
            for (int i = 0; i < voices.Length; i++)
                voices[i] = new Voice { Source = CreateSource("Efeito " + (i + 1), true) };
        }

        AudioSource CreateSource(string sourceName, bool spatial)
        {
            var child = new GameObject(sourceName);
            if (spatial && voiceRoot == null) voiceRoot = new GameObject("Vozes espaciais").transform;
            // This root deliberately stays in world space when the camera/listener moves.
            child.transform.SetParent(spatial ? voiceRoot : transform, false);
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = spatial ? .80f : 0f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 6f;
            source.maxDistance = 24f;
            source.dopplerLevel = 0f;
            source.spread = 25f;
            source.reverbZoneMix = .08f;
            return source;
        }

        void Configure(GameSound sound, string prefix, int count, float gain, float cooldown, int maxVoices, int priority)
        {
            var clips = new AudioClip[count];
            for (int i = 0; i < clips.Length; i++)
                clips[i] = Resources.Load<AudioClip>("Audio/" + prefix + "_" + (i + 1));
            banks[(int)sound] = new Bank { Clips = clips, Gain = gain, Cooldown = cooldown, MaxVoices = maxVoices, Priority = priority };
        }

        void PlayInternal(GameSound sound, Vector3 position, float gain, bool elite)
        {
            if (!initialized || !isActiveAndEnabled || muted || masterVolume <= 0f || effectsVolume <= 0f) return;
            Bank bank = banks[(int)sound];
            double now = AudioSettings.dspTime;
            if (bank == null || now < bank.NextAt) return;
            int active = 0;
            Voice free = null;
            Voice replace = null;
            float replaceLoudness = float.MaxValue;
            Vector3 listenerPosition = Camera.main != null ? Camera.main.transform.position : transform.position;
            if ((listenerPosition - position).sqrMagnitude > 30f * 30f) return;
            for (int i = 0; i < voices.Length; i++)
            {
                Voice voice = voices[i];
                // Independent scene roots can be destroyed before this component during shutdown.
                if (voice == null || voice.Source == null) continue;
                bool playing = voice.Source.isPlaying && now < voice.EndsAt;
                if (!playing) { if (free == null) free = voice; continue; }
                if (voice.Sound == sound) active++;
                // A quiet footstep can yield to an impact, never the reverse.
                if (voice.Priority < bank.Priority) continue;
                float loudness = voice.Gain / (1f + (voice.Source.transform.position - listenerPosition).sqrMagnitude);
                if (loudness < replaceLoudness) { replace = voice; replaceLoudness = loudness; }
            }
            if (active >= bank.MaxVoices) return;
            Voice selected = free ?? replace;
            if (selected == null) return;
            int index = bank.Clips.Length == 1 ? 0 : UnityEngine.Random.Range(0, bank.Clips.Length - (bank.Previous >= 0 ? 1 : 0));
            if (bank.Clips.Length > 1 && bank.Previous >= 0 && index >= bank.Previous) index++;
            index = Mathf.Clamp(index, 0, bank.Clips.Length - 1);
            AudioClip clip = bank.Clips[index];
            if (clip == null) return;
            bank.Previous = index;
            bank.NextAt = now + bank.Cooldown;
            float pitch = UnityEngine.Random.Range(.96f, 1.045f) * (elite ? .84f : 1f);
            selected.Source.Stop();
            selected.Source.transform.position = position;
            selected.Source.clip = clip;
            selected.Source.pitch = pitch;
            selected.Source.priority = bank.Priority;
            selected.Sound = sound;
            selected.Priority = bank.Priority;
            selected.Gain = bank.Gain * gain;
            selected.Source.volume = 0f;
            selected.EndsAt = now + clip.length / pitch;
            selected.Source.Play();
            ApplyEffectVolumes(0f);
        }

        void Update()
        {
            if (!initialized) return;
            fade = Mathf.MoveTowards(fade, 1f, Time.unscaledDeltaTime / 2.2f);
            float master = muted ? 0f : masterVolume;
            if (music != null) music.volume = Mathf.MoveTowards(music.volume, musicVolume * master * fade, Time.unscaledDeltaTime * .45f);
            if (ambience != null) ambience.volume = Mathf.MoveTowards(ambience.volume, .20f * effectsVolume * master * fade, Time.unscaledDeltaTime * .45f);
            ApplyEffectVolumes(Time.unscaledDeltaTime);
            // Slider drags write preferences at most once per second, not on every GUI event.
            if (preferencesDirty && Time.unscaledTime >= preferenceSaveAt)
            {
                SavePreferences();
                preferenceSaveAt = Time.unscaledTime + 1f;
            }
        }

        void ApplyEffectVolumes(float deltaTime)
        {
            float summedGain = 0f;
            for (int i = 0; i < voices.Length; i++)
                if (voices[i] != null && voices[i].Source != null && voices[i].Source.isPlaying) summedGain += voices[i].Gain * effectsVolume;
            // Prepared SFX peak below .79; .90 reserves room for Vorbis reconstruction.
            // Music true peak is below .52; .72 also allows conservative codec headroom.
            // This bounds the combined music, wind and coincident effects below .92.
            float budget = Mathf.Max(.05f, .92f - .72f * musicVolume - .08f * effectsVolume);
            float target = Mathf.Min(1f, budget / Mathf.Max(.0001f, .90f * summedGain));
            // Immediate protection on a new transient; short recovery avoids audible pumping.
            effectsHeadroom = target < effectsHeadroom ? target : Mathf.MoveTowards(effectsHeadroom, target, deltaTime / .18f);
            float master = muted ? 0f : masterVolume;
            for (int i = 0; i < voices.Length; i++)
                if (voices[i] != null && voices[i].Source != null && voices[i].Source.isPlaying)
                    voices[i].Source.volume = voices[i].Gain * effectsVolume * master * effectsHeadroom;
        }

        void OnApplicationPause(bool paused) { if (paused) SavePreferences(); }
        void OnApplicationQuit() { SavePreferences(); }
        void OnDisable()
        {
            if (instance != this) return;
            if (music != null) music.Stop();
            if (ambience != null) ambience.Stop();
            for (int i = 0; i < voices.Length; i++)
                if (voices[i] != null && voices[i].Source != null) voices[i].Source.Stop();
            SavePreferences();
        }
        void OnDestroy()
        {
            if (voiceRoot != null) Destroy(voiceRoot.gameObject);
            if (instance == this) instance = null;
        }

        static void LoadPreferences()
        {
            if (preferencesLoaded) return;
            preferencesLoaded = true;
            masterVolume = SafeVolume(PlayerPrefs.GetFloat(PreferencePrefix + "Master", 1f));
            musicVolume = SafeVolume(PlayerPrefs.GetFloat(PreferencePrefix + "Music", DefaultMusicVolume));
            effectsVolume = SafeVolume(PlayerPrefs.GetFloat(PreferencePrefix + "Effects", DefaultEffectsVolume));
            muted = PlayerPrefs.GetInt(PreferencePrefix + "Muted", 0) == 1;
        }
        static void PreferencesChanged() { preferencesDirty = true; }
        public static void SavePreferences()
        {
            if (!preferencesLoaded || !preferencesDirty) return;
            PlayerPrefs.SetFloat(PreferencePrefix + "Master", masterVolume);
            PlayerPrefs.SetFloat(PreferencePrefix + "Music", musicVolume);
            PlayerPrefs.SetFloat(PreferencePrefix + "Effects", effectsVolume);
            PlayerPrefs.SetInt(PreferencePrefix + "Muted", muted ? 1 : 0);
            PlayerPrefs.Save();
            preferencesDirty = false;
        }
        static float SafeVolume(float value) => float.IsFinite(value) ? Mathf.Clamp01(value) : 0f;
        static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    }
}

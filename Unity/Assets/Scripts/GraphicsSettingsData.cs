using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace GuerraDasNacoes
{
    public enum GraphicsPreset { Low, Medium, High, Ultra, Custom }
    public enum GraphicsWindowMode { Windowed, Borderless, Fullscreen }
    public enum GraphicsShadowQuality { Off, Low, High }

    [Serializable]
    public sealed class GraphicsSettingsData
    {
        public const int CurrentSchemaVersion = 1;
        public int schemaVersion = CurrentSchemaVersion;
        public GraphicsPreset preset = GraphicsPreset.High;
        public int width = 1280, height = 720;
        public GraphicsWindowMode windowMode = GraphicsWindowMode.Windowed;
        public GraphicsShadowQuality shadows = GraphicsShadowQuality.High;
        public int antiAliasing = 2;
        // 0 = original textures, 1 = half resolution, 2 = quarter resolution.
        public int textureQuality;
        public float viewDistance = 150f;
        public bool postProcessing = true;
        public bool vSync;
        public int frameRateLimit = 60;

        public GraphicsSettingsData Clone() { return (GraphicsSettingsData)MemberwiseClone(); }

        public bool IsValid(out string reason)
        {
            if (schemaVersion != CurrentSchemaVersion) { reason = "Versão de configuração gráfica incompatível."; return false; }
            if (!Enum.IsDefined(typeof(GraphicsPreset), preset) || !Enum.IsDefined(typeof(GraphicsWindowMode), windowMode) ||
                !Enum.IsDefined(typeof(GraphicsShadowQuality), shadows)) { reason = "Opção gráfica desconhecida."; return false; }
            if (width < 960 || width > 16384 || height < 540 || height > 16384) { reason = "Resolução fora dos limites suportados."; return false; }
            if (antiAliasing != 0 && antiAliasing != 2 && antiAliasing != 4 && antiAliasing != 8) { reason = "Suavização de bordas inválida."; return false; }
            if (textureQuality < 0 || textureQuality > 2 || float.IsNaN(viewDistance) || float.IsInfinity(viewDistance) ||
                viewDistance < 60f || viewDistance > 220f) { reason = "Qualidade de textura ou distância inválida."; return false; }
            if (frameRateLimit != -1 && frameRateLimit != 30 && frameRateLimit != 60 && frameRateLimit != 120)
            { reason = "Limite de quadros inválido."; return false; }
            reason = ""; return true;
        }

        public bool HasSameDisplay(GraphicsSettingsData other)
        {
            return other != null && width == other.width && height == other.height && windowMode == other.windowMode;
        }
    }

    public readonly struct GraphicsResolution
    {
        public readonly int width, height;
        public string label { get { return width + " × " + height; } }
        public GraphicsResolution(int width, int height) { this.width = width; this.height = height; }
        public override string ToString() { return label; }
    }

    // Graphics preferences never read or write character profiles, credentials, or server saves.
    public static class GraphicsSettingsStorage
    {
        public static GraphicsSettingsData Load(string path, GraphicsSettingsData fallback, out string notice)
        {
            if (TryRead(path, out GraphicsSettingsData loaded)) { notice = ""; return loaded; }
            if (!File.Exists(path)) { notice = ""; return fallback.Clone(); }
            if (TryRead(path + ".bak", out loaded))
            {
                notice = "Configuração gráfica recuperada da cópia anterior.";
                return loaded;
            }
            notice = "Configuração gráfica inválida. Usando opções seguras; seus personagens não foram alterados.";
            GraphicsSettingsData safe = fallback.Clone();
            safe.windowMode = GraphicsWindowMode.Windowed;
            return safe;
        }

        public static bool TrySave(string path, GraphicsSettingsData data, out string error)
        {
            if (data == null) { error = "Configuração ausente."; return false; }
            if (!data.IsValid(out error)) return false;
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                string folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                byte[] bytes = new UTF8Encoding(false).GetBytes(JsonUtility.ToJson(data, true));
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(path))
                {
                    // Never replace a valid recovery copy with a corrupt primary file.
                    string backup = TryRead(path, out _) ? path + ".bak" : null;
                    File.Replace(temporary, path, backup);
                }
                else File.Move(temporary, path);
                error = ""; return true;
            }
            catch (Exception failure) when (failure is IOException || failure is UnauthorizedAccessException || failure is NotSupportedException)
            {
                error = "Não foi possível salvar as opções gráficas. Verifique o espaço e as permissões da pasta do jogo.";
                return false;
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static bool TryRead(string path, out GraphicsSettingsData data)
        {
            data = null;
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length > 16384) return false;
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith("{", StringComparison.Ordinal) ||
                    json.IndexOf("\"schemaVersion\"", StringComparison.Ordinal) < 0) return false;
                data = JsonUtility.FromJson<GraphicsSettingsData>(json);
                return data != null && data.IsValid(out _);
            }
            catch (Exception failure) when (failure is IOException || failure is UnauthorizedAccessException || failure is ArgumentException)
            { data = null; return false; }
        }
    }
}

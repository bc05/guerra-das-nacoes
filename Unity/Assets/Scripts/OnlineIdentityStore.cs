using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace GuerraDasNacoes
{
    [Serializable]
    public sealed class OnlineIdentity
    {
        public int version = 1;
        public string deviceKey;
    }

    // This local credential identifies one prototype account. Character state lives on the server.
    // A damaged credential is an actionable error: silently replacing it would lose access to the hero.
    public static class OnlineIdentityStore
    {
#if UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX || UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
        [DllImport("libc", EntryPoint = "chmod", SetLastError = true)]
        private static extern int Chmod(string path, uint mode);
#endif

        public static string GetIdentityPath(string persistentDirectory, string profile)
        {
            ValidateProfile(profile);
            if (string.IsNullOrEmpty(persistentDirectory))
                throw new ArgumentException("A pasta de dados do jogo não está disponível.");
            return Path.Combine(persistentDirectory, "Online", profile, "identity.json");
        }

        public static string GetIdentityRoot(string persistentDirectory, string endpoint)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri origin) ||
                (origin.Scheme != Uri.UriSchemeHttp && origin.Scheme != Uri.UriSchemeHttps) ||
                !string.IsNullOrEmpty(origin.UserInfo) || !string.IsNullOrEmpty(origin.Query) ||
                !string.IsNullOrEmpty(origin.Fragment) || (origin.AbsolutePath != "/" && origin.AbsolutePath != ""))
                throw new ArgumentException("Origem do servidor inválida.");
            string normalized = origin.GetLeftPart(UriPartial.Authority).TrimEnd('/').ToLowerInvariant();
            // Retain the original device credential only for the shipped local server.
            if (origin.Scheme == Uri.UriSchemeHttp && origin.IsLoopback && origin.Port == 8088) return persistentDirectory;
            using (SHA256 hash = SHA256.Create())
            {
                byte[] digest = hash.ComputeHash(Encoding.UTF8.GetBytes(normalized));
                string serverKey = BitConverter.ToString(digest).Replace("-", "").ToLowerInvariant();
                return Path.Combine(persistentDirectory, "Servers", serverKey);
            }
        }

        public static OnlineIdentity LoadOrCreate(string persistentDirectory, string profile)
        {
            string path = GetIdentityPath(persistentDirectory, profile);
            string profileDirectory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(profileDirectory);
            ProtectUnix(Path.GetDirectoryName(profileDirectory), 0x1C0); // 0700
            ProtectUnix(profileDirectory, 0x1C0); // 0700
            if (File.Exists(path)) return Read(path);
            byte[] secret = new byte[32];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create()) random.GetBytes(secret);
            OnlineIdentity identity = new OnlineIdentity { deviceKey = Convert.ToBase64String(secret) };
            Array.Clear(secret, 0, secret.Length);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(JsonUtility.ToJson(identity, true));
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    // Restrict the empty file before secret bytes ever reach the filesystem.
                    ProtectUnix(temporary, 0x180); // 0600
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                try { File.Move(temporary, path); }
                catch (IOException) when (File.Exists(path)) { return Read(path); }
                return identity;
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        // Menu inspection must never mint a new credential or replace a damaged one.
        public static bool TryLoadExisting(string persistentDirectory, string profile, out OnlineIdentity identity)
        {
            string path = GetIdentityPath(persistentDirectory, profile);
            identity = File.Exists(path) ? Read(path) : null;
            return identity != null;
        }

        public static void ValidateProfile(string profile)
        {
            if (string.IsNullOrEmpty(profile) || profile.Length > 32)
                throw new ArgumentException("O perfil deve ter de 1 a 32 letras, números, hífen ou sublinhado.");
            foreach (char c in profile)
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                    (c >= '0' && c <= '9') || c == '-' || c == '_'))
                    throw new ArgumentException("O perfil deve conter apenas letras, números, hífen ou sublinhado.");
        }

        private static OnlineIdentity Read(string path)
        {
            ProtectUnix(path, 0x180); // Tighten credentials created by earlier prototypes as well.
            try
            {
                if (new FileInfo(path).Length > 2048) throw new FormatException();
                OnlineIdentity identity = JsonUtility.FromJson<OnlineIdentity>(File.ReadAllText(path, Encoding.UTF8));
                if (identity == null || identity.version != 1 || string.IsNullOrWhiteSpace(identity.deviceKey) ||
                    Convert.FromBase64String(identity.deviceKey).Length != 32)
                    throw new FormatException();
                return identity;
            }
            catch (Exception error) when (error is FormatException || error is ArgumentException)
            {
                throw new InvalidDataException("A identidade local está danificada. Preserve identity.json e restaure sua cópia; " +
                    "o jogo não criará outro personagem automaticamente.", error);
            }
        }

        private static void ProtectUnix(string path, uint mode)
        {
#if UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX || UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
            if (Chmod(path, mode) != 0)
                throw new IOException("Não foi possível restringir o acesso à identidade local (permissão Unix " +
                    Marshal.GetLastWin32Error() + ").");
#endif
        }
    }
}

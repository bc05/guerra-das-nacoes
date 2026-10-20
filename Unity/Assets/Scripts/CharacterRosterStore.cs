using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace GuerraDasNacoes
{
    // Labels and approved local profile names only. Credentials remain in their original files.
    public static class CharacterRosterStore
    {
        public const int MaximumCharacters = 8;
        private static string FilePath(string directory) => Path.Combine(directory, "Online", "characters.json");

        public static CharacterRoster Load(string persistentDirectory)
        {
            string path = FilePath(persistentDirectory);
            CharacterRoster roster;
            if (File.Exists(path))
            {
                try
                {
                    if (new FileInfo(path).Length > 16384) throw new FormatException();
                    roster = JsonUtility.FromJson<CharacterRoster>(File.ReadAllText(path, Encoding.UTF8));
                    Validate(roster);
                }
                catch (Exception error) when (error is ArgumentException || error is FormatException)
                {
                    throw new InvalidDataException("A lista de personagens está danificada. Preserve Online/characters.json; nenhuma identidade foi substituída.", error);
                }
            }
            else roster = new CharacterRoster();

            List<CharacterRosterEntry> entries = new List<CharacterRosterEntry>(roster.entries);
            bool changed = false;
            if (!entries.Exists(e => e.profile == "principal"))
            {
                entries.Insert(0, new CharacterRosterEntry { profile = "principal", displayName = "Guerreiro" });
                changed = true;
            }
            // Import the known second launcher, never scan folders containing test credentials.
            if (File.Exists(OnlineIdentityStore.GetIdentityPath(persistentDirectory, "segundo")) &&
                !entries.Exists(e => e.profile == "segundo") && entries.Count < MaximumCharacters)
            {
                entries.Add(new CharacterRosterEntry { profile = "segundo", displayName = "Guerreiro 2" });
                changed = true;
            }
            roster.entries = entries.ToArray();
            if (changed || !File.Exists(path)) Save(persistentDirectory, roster);
            return roster;
        }

        public static CharacterRosterEntry AddCharacter(string persistentDirectory, CharacterRoster roster, string displayName)
        {
            Validate(roster);
            string name = CharacterNickname.Normalize(displayName);
            if (roster.entries.Length >= MaximumCharacters)
                throw new InvalidOperationException("Você já possui oito personagens locais.");
            var entry = new CharacterRosterEntry { profile = "h-" + Guid.NewGuid().ToString("N").Substring(0, 30), displayName = name };
            var entries = new List<CharacterRosterEntry>(roster.entries) { entry };
            var candidate = new CharacterRoster { entries = entries.ToArray() };
            Save(persistentDirectory, candidate);
            roster.entries = candidate.entries;
            return entry;
        }

        // This is the one additional profile explicitly supported by the shipped launcher.
        // Selecting its card never creates a device credential or scans arbitrary profiles.
        public static CharacterRosterEntry EnsureSecondLauncherCharacter(string persistentDirectory,
            CharacterRoster roster, string displayName)
        {
            Validate(roster);
            foreach (CharacterRosterEntry existing in roster.entries)
                if (existing.profile == "segundo") return existing;
            string name = CharacterNickname.Normalize(displayName);
            if (roster.entries.Length >= MaximumCharacters)
                throw new InvalidOperationException("A lista já possui oito personagens. O personagem do segundo atalho não foi selecionado; abra o atalho principal para escolher um personagem existente.");
            var entry = new CharacterRosterEntry { profile = "segundo", displayName = name };
            var entries = new List<CharacterRosterEntry>(roster.entries) { entry };
            var candidate = new CharacterRoster { entries = entries.ToArray() };
            Save(persistentDirectory, candidate);
            roster.entries = candidate.entries;
            return entry;
        }

        // This changes only the local label of a character that has not yet connected.
        // Actual character names remain server-owned and cannot be renamed through this file.
        public static CharacterRosterEntry UpdatePendingNickname(string persistentDirectory,
            CharacterRoster roster, string profile, string nickname)
        {
            Validate(roster);
            string normalized = CharacterNickname.Normalize(nickname);
            var entries = new List<CharacterRosterEntry>(roster.entries.Length);
            CharacterRosterEntry updated = null;
            foreach (CharacterRosterEntry entry in roster.entries)
            {
                if (entry.profile == profile)
                {
                    updated = new CharacterRosterEntry { profile = entry.profile, displayName = normalized };
                    entries.Add(updated);
                }
                else entries.Add(entry);
            }
            if (updated == null) throw new ArgumentException("O personagem selecionado não está nesta lista.");
            var candidate = new CharacterRoster { entries = entries.ToArray() };
            Save(persistentDirectory, candidate);
            roster.entries = candidate.entries;
            return updated;
        }

        public static void Save(string persistentDirectory, CharacterRoster roster)
        {
            Validate(roster);
            string path = FilePath(persistentDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(JsonUtility.ToJson(roster, true));
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(temporary, path, path + ".backup");
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        public static string ValidateName(string displayName)
        {
            string name = (displayName ?? "").Trim();
            if (name.Length < 2 || name.Length > 24)
                throw new ArgumentException("O nome deve ter de 2 a 24 caracteres.");
            foreach (char value in name)
                if (char.IsControl(value) || value == '<' || value == '>')
                    throw new ArgumentException("O nome contém caracteres não permitidos.");
            return name;
        }

        private static void Validate(CharacterRoster roster)
        {
            if (roster == null || roster.version != 1 || roster.entries == null || roster.entries.Length > MaximumCharacters)
                throw new FormatException("Versão ou tamanho da lista de personagens inválido.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (CharacterRosterEntry entry in roster.entries)
            {
                if (entry == null || !ApprovedProfile(entry.profile) || !seen.Add(entry.profile)) throw new FormatException("Perfil de personagem inválido.");
                ValidateName(entry.displayName);
            }
        }

        private static bool ApprovedProfile(string profile)
        {
            if (profile == "principal" || profile == "segundo") return true;
            if (profile == null || profile.Length != 32 || !profile.StartsWith("h-", StringComparison.Ordinal)) return false;
            for (int index = 2; index < profile.Length; index++)
                if (!(profile[index] >= '0' && profile[index] <= '9') && !(profile[index] >= 'a' && profile[index] <= 'f')) return false;
            return true;
        }
    }
}

using System;

namespace GuerraDasNacoes
{
    [Serializable] public sealed class CharacterRosterEntry
    {
        public string profile;
        public string displayName;
    }

    [Serializable] public sealed class CharacterRoster
    {
        public int version = 1;
        public CharacterRosterEntry[] entries = Array.Empty<CharacterRosterEntry>();
    }
}

using System;
using System.Text;

namespace GuerraDasNacoes
{
    public static class CharacterNickname
    {
        public const int MinimumLength = 2;
        public const int MaximumLength = 24;

        public static string Normalize(string nickname)
        {
            string normalized = NormalizeSpacing(nickname);
            if (normalized.Length < MinimumLength || normalized.Length > MaximumLength)
                throw new ArgumentException("O nick deve ter de 2 a 24 caracteres.");
            foreach (char value in normalized)
                if (!char.IsLetterOrDigit(value) && value != ' ' && value != '_' && value != '-')
                    throw new ArgumentException("Use letras, números, espaços, hífen ou sublinhado no nick.");
            return normalized;
        }

        // Also reserves historical names that predate validation, without rewriting a saved hero.
        public static string Key(string nickname) => NormalizeSpacing(nickname).ToUpperInvariant();

        private static string NormalizeSpacing(string nickname)
        {
            string normalized;
            try { normalized = (nickname ?? "").Normalize(NormalizationForm.FormKC).Trim(); }
            catch (ArgumentException) { throw new ArgumentException("O nick contém caracteres Unicode inválidos."); }
            var result = new StringBuilder(normalized.Length);
            bool previousSpace = false;
            foreach (char value in normalized)
            {
                if (value == ' ' && previousSpace) continue;
                result.Append(value);
                previousSpace = value == ' ';
            }
            return result.ToString();
        }
    }
}

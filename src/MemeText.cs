using System;

namespace CultCraft
{
    internal static class MemeText
    {
        // Match the game's markup before words, including protected I2 text and replacement parameters.
        internal const string Protected = @"\[i2nt\].*?\[/i2nt\]|<[^<>]+>|\[[^\[\]\r\n]+\]|\{[^{}\r\n]+\}|%(?:\d+\$)?[sdif%]|\\[nrt]|&(?:#\w+|[a-z]+);|https?://[^\s<>]+";
        internal const string Word = @"\b[A-Za-z]+(?:['’][A-Za-z]+)?\b";

        internal static string MatchCase(string original, string translated)
        {
            if (original.Length > 1 && original == original.ToUpperInvariant()) return translated.ToUpperInvariant();
            if (Char.IsUpper(original[0])) return Char.ToUpperInvariant(translated[0]) + translated.Substring(1);
            return translated;
        }
    }
}

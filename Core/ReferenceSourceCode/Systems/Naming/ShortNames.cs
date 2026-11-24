using System;
using System.Collections.Generic;

namespace Game.Systems.Naming {
    // Short names are shown where space is tight, so they are always a
    // single word. Leading articles and robot prefixes are decoration, not
    // the name, so they are skipped when a better word follows.
    public static class ShortNames {
        private const int MinLength = 3;
        private static readonly char[] quotes = { '"', '\'', '“', '”', '‘', '’' };
        private static readonly HashSet<string> fillerWords =
            new(StringComparer.OrdinalIgnoreCase) {
                "the", "and", "or", "that",
                "great", "first", "second", "third", "alpha", "amazing",
                "robo", "drone", "space", "star", "mech", "auto",
            };

        public static string Pick(string nickname, string firstName,
                string lastName) {
            if (!string.IsNullOrWhiteSpace(nickname)) {
                return PickFrom(nickname, null);
            }
            return PickFrom(firstName, lastName);
        }

        private static string PickFrom(string a, string b) {
            string fallback = null;
            var picked = PickFrom(a, ref fallback) ?? PickFrom(b, ref fallback);
            return picked ?? fallback;
        }

        private static string PickFrom(string text, ref string fallback) {
            if (string.IsNullOrWhiteSpace(text)) { return null; }
            var words = text.Split(' ');
            for (int i = 0; i < words.Length; i++) {
                var word = words[i].Trim(quotes);
                if (word.Length == 0) { continue; }
                fallback ??= word;
                if (word.Length >= MinLength && !fillerWords.Contains(word)) {
                    return word;
                }
            }
            return null;
        }
    }
}

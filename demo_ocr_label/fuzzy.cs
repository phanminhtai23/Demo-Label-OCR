using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace demo_ocr_label
{
    public static class fuzzy
    {
        // 1. Levenshtein distance
        public static int LevenshteinDistance(string a, string b)
        {
            a ??= "";
            b ??= "";
            int n = a.Length, m = b.Length;
            if (n == 0) return m;
            if (m == 0) return n;

            int[] prev = new int[m + 1];
            int[] curr = new int[m + 1];
            for (int j = 0; j <= m; j++) prev[j] = j;

            for (int i = 1; i <= n; i++)
            {
                curr[0] = i;
                char ca = a[i - 1];
                for (int j = 1; j <= m; j++)
                {
                    int cost = ca == b[j - 1] ? 0 : 1;
                    curr[j] = Math.Min(
                        Math.Min(prev[j] + 1, curr[j - 1] + 1), // delete / insert
                        prev[j - 1] + cost                      // replace
                    );
                }
                (prev, curr) = (curr, prev); // swap
            }
            return prev[m];
        }

        // 2. Levenshtein similarity (0..1)
        public static double LevenshteinSimilarity(string a, string b)
        {
            if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b)) return 1.0;
            int dist = LevenshteinDistance(a, b);
            int maxLen = Math.Max(a?.Length ?? 0, b?.Length ?? 0);
            return maxLen == 0 ? 1.0 : 1.0 - dist / (double)maxLen;
        }

        // 3. Jaro-Winkler similarity
        public static double JaroWinklerSimilarity(string s, string t, double prefixScale = 0.1)
        {
            if (s == t) return 1.0;
            if (string.IsNullOrEmpty(s) || string.IsNullOrEmpty(t)) return 0.0;

            int sLen = s.Length, tLen = t.Length;
            int matchDistance = Math.Max(sLen, tLen) / 2 - 1;

            bool[] sMatch = new bool[sLen];
            bool[] tMatch = new bool[tLen];

            int matches = 0;
            for (int i = 0; i < sLen; i++)
            {
                int start = Math.Max(0, i - matchDistance);
                int end = Math.Min(tLen - 1, i + matchDistance);
                for (int j = start; j <= end; j++)
                {
                    if (tMatch[j] || s[i] != t[j]) continue;
                    sMatch[i] = true;
                    tMatch[j] = true;
                    matches++;
                    break;
                }
            }
            if (matches == 0) return 0.0;

            int transpositions = 0;
            int k = 0;
            for (int i = 0; i < sLen; i++)
            {
                if (!sMatch[i]) continue;
                while (!tMatch[k]) k++;
                if (s[i] != t[k]) transpositions++;
                k++;
            }
            transpositions /= 2;

            double jaro =
                (matches / (double)sLen +
                 matches / (double)tLen +
                 (matches - transpositions) / (double)matches) / 3.0;

            int prefix = 0;
            for (int i = 0; i < Math.Min(4, Math.Min(sLen, tLen)); i++)
            {
                if (s[i] == t[i]) prefix++; else break;
            }

            return jaro + prefix * prefixScale * (1 - jaro);
        }

        // 4. Combined similarity WITHOUT Vietnamese accent handling
        //    Lowercase + trim only.
        public static double CombinedSimilarity(string a, string b)
        {
            string na = (a ?? "").Trim().ToLowerInvariant();
            string nb = (b ?? "").Trim().ToLowerInvariant();
            double lev = LevenshteinSimilarity(na, nb);
            double jw = JaroWinklerSimilarity(na, nb);
            return Math.Max(lev, jw);
        }

        // 5. Best match ≥ minScore, else empty string
        public static string BestOrEmpty(string a, IEnumerable<string> candidates, double minScore)
        {
            if (string.IsNullOrWhiteSpace(a) || candidates == null) return "";
            string best = "";
            double bestScore = minScore;

            foreach (var c in candidates)
            {
                if (string.IsNullOrWhiteSpace(c)) continue;
                double score = LevenshteinSimilarity(a, c);
                if (score >= bestScore)
                {
                    if (score > bestScore || best == "")
                    {
                        bestScore = score;
                        best = c;
                    }
                }
            }
            return best;
        }
    }
}
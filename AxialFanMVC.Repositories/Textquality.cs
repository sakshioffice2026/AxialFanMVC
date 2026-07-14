using System.Text.RegularExpressions;

namespace AxialFanMVC.Repositories
{
    // ═══════════════════════════════════════════════════════════════
    // TextQuality — flags handbook_chunks rows whose text looks like
    // failed OCR rather than real prose (typically: scanned diagrams,
    // vector arrows, and symbol-heavy figures that an OCR pass mangled
    // into strings of dashes/underscores/carets instead of words).
    //
    // Deliberately a TAG, not a filter that deletes/hides results —
    // a heuristic like this will have false positives on legitimate
    // symbol-heavy engineering text (e.g. formula lines), so silently
    // dropping matches risks hiding real content a user needed. Callers
    // should show flagged chunks with a visible "may contain scan
    // artifacts" notice instead of omitting them.
    //
    // Tune thresholds against your own corpus once you have a labeled
    // sample of good vs. garbled chunks — the numbers below are a
    // reasonable starting point, not a calibrated cutoff.
    // ═══════════════════════════════════════════════════════════════
    public static class TextQuality
    {
        // Characters that show up heavily in mis-OCR'd diagrams/vector
        // arrows but rarely in real prose at this density.
        private static readonly Regex JunkChars = new(@"[—_~\|<>¢»]", RegexOptions.Compiled);

        // A "real word" = 3+ letters in a row. Formula symbols, dashes,
        // and single-letter OCR fragments don't count.
        private static readonly Regex RealWord = new(@"[A-Za-z]{3,}", RegexOptions.Compiled);

        // 4+ repeats of the same junk character in a row — the classic
        // "———_——_—_——" artifact from a mis-read arrow/line in a figure.
        private static readonly Regex RepeatedJunkRun = new(@"([—_\-])\1{3,}", RegexOptions.Compiled);

        public readonly record struct Result(bool IsLikelyGarbled, double JunkCharRatio, double RealWordRatio, string Reason);

        public static Result Assess(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new Result(false, 0, 0, "empty");

            int len = text.Length;
            int junkCount = JunkChars.Matches(text).Count;
            double junkRatio = (double)junkCount / len;

            var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            int realWordCount = words.Count(w => RealWord.IsMatch(w));
            double realWordRatio = words.Length == 0 ? 0 : (double)realWordCount / words.Length;

            bool hasRepeatedJunkRun = RepeatedJunkRun.IsMatch(text);

            // Combined heuristic — any one signal alone can be a false
            // positive (e.g. a dense formula line has few "real words"
            // too), so garbled is only declared when multiple signals
            // agree.
            bool garbled =
                (junkRatio > 0.04 && realWordRatio < 0.55) ||
                (hasRepeatedJunkRun && realWordRatio < 0.65);

            string reason = garbled
                ? $"junk-char ratio {junkRatio:P1}, real-word ratio {realWordRatio:P0}" +
                  (hasRepeatedJunkRun ? ", repeated-symbol run detected" : "")
                : "looks like normal text";

            return new Result(garbled, junkRatio, realWordRatio, reason);
        }

        public static bool IsLikelyGarbled(string? text) => Assess(text).IsLikelyGarbled;

        // Splits a chunk into presentable lines: breaks on newlines AND
        // sentence-ending punctuation (chunks from OCR often arrive as
        // one dense paragraph with no real line breaks), drops exact
        // duplicate lines (case-insensitive), drops lines that are
        // individually pure OCR noise, and trims stray leading symbols
        // OCR tends to leave on a line ("— ", "¢", etc.).
        public static List<string> ToCleanBullets(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new List<string>();

            var rawLines = Regex.Split(text, @"(?<=[.!?])\s+|\r?\n")
                .Select(l => l.Trim(' ', '\t', '—', '_', '~', '|', '<', '>', '¢', '»', '-'))
                .Where(l => l.Length > 0);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();

            foreach (var line in rawLines)
            {
                if (line.Length < 3) continue;                 // stray fragments
                if (!seen.Add(line)) continue;                  // exact duplicate
                if (IsLikelyGarbled(line)) continue;             // per-line noise

                result.Add(line);
            }

            return result;
        }
    }
}
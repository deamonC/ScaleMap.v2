using System;
using System.Collections.Generic;
using System.Linq;

namespace ScaleMap
{
    public class Chord
    {
        public string Name;
        public int RootPc;
        public int[] Intervals;   // semitones above the chord root

        public IEnumerable<int> PitchClasses => Intervals.Select(i => (RootPc + i) % 12);
    }

    public class ProgressionStep
    {
        public Chord Chord;
        public int Bars;
    }

    /// <summary>
    /// A chord progression typed by the user, e.g. "Em | C | D | B7" or "Am:2 F C G".
    /// Each chord lasts one bar unless followed by :N (N bars).
    /// </summary>
    public class ChordProgression
    {
        public List<ProgressionStep> Steps { get; } = new List<ProgressionStep>();
        public List<string> Errors { get; } = new List<string>();
        public int TotalBars => Steps.Sum(s => s.Bars);

        static readonly Dictionary<string, int[]> qualities = new Dictionary<string, int[]>
        {
            { "", new[] { 0, 4, 7 } },
            { "M", new[] { 0, 4, 7 } },
            { "maj", new[] { 0, 4, 7 } },
            { "m", new[] { 0, 3, 7 } },
            { "min", new[] { 0, 3, 7 } },
            { "-", new[] { 0, 3, 7 } },
            { "5", new[] { 0, 7 } },
            { "7", new[] { 0, 4, 7, 10 } },
            { "maj7", new[] { 0, 4, 7, 11 } },
            { "M7", new[] { 0, 4, 7, 11 } },
            { "m7", new[] { 0, 3, 7, 10 } },
            { "min7", new[] { 0, 3, 7, 10 } },
            { "-7", new[] { 0, 3, 7, 10 } },
            { "mmaj7", new[] { 0, 3, 7, 11 } },
            { "m7b5", new[] { 0, 3, 6, 10 } },
            { "dim", new[] { 0, 3, 6 } },
            { "o", new[] { 0, 3, 6 } },
            { "dim7", new[] { 0, 3, 6, 9 } },
            { "o7", new[] { 0, 3, 6, 9 } },
            { "aug", new[] { 0, 4, 8 } },
            { "+", new[] { 0, 4, 8 } },
            { "sus2", new[] { 0, 2, 7 } },
            { "sus4", new[] { 0, 5, 7 } },
            { "sus", new[] { 0, 5, 7 } },
            { "7sus4", new[] { 0, 5, 7, 10 } },
            { "6", new[] { 0, 4, 7, 9 } },
            { "m6", new[] { 0, 3, 7, 9 } },
            { "9", new[] { 0, 4, 7, 10, 2 } },
            { "m9", new[] { 0, 3, 7, 10, 2 } },
            { "maj9", new[] { 0, 4, 7, 11, 2 } },
            { "add9", new[] { 0, 4, 7, 2 } },
            { "madd9", new[] { 0, 3, 7, 2 } },
        };

        static readonly Dictionary<char, int> letters = new Dictionary<char, int>
        {
            { 'C', 0 }, { 'D', 2 }, { 'E', 4 }, { 'F', 5 }, { 'G', 7 }, { 'A', 9 }, { 'B', 11 }
        };

        public static Chord ParseChord(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            text = text.Trim();

            // slash chords: the bass note does not change the chord tones we show
            int slash = text.IndexOf('/');

            if (slash > 0)
                text = text.Substring(0, slash);

            char letter = char.ToUpperInvariant(text[0]);

            if (!letters.TryGetValue(letter, out int root))
                return null;

            int pos = 1;

            while (pos < text.Length && (text[pos] == '#' || text[pos] == 'b'))
            {
                // "b" right after the letter is a flat, but "Bb" -> flat B, and "Cb5" etc. are rare enough to accept
                root += (text[pos] == '#') ? 1 : -1;
                pos++;
            }

            root = ((root % 12) + 12) % 12;

            string quality = text.Substring(pos);

            if (!qualities.TryGetValue(quality, out int[] intervals))
                return null;

            return new Chord { Name = text, RootPc = root, Intervals = intervals };
        }

        public static ChordProgression Parse(string text)
        {
            ChordProgression prog = new ChordProgression();

            if (string.IsNullOrWhiteSpace(text))
                return prog;

            string[] tokens = text.Split(new[] { ' ', '|', ',', ';', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string token in tokens)
            {
                string name = token;
                int bars = 1;

                int colon = token.IndexOf(':');

                if (colon > 0)
                {
                    name = token.Substring(0, colon);

                    if (!int.TryParse(token.Substring(colon + 1), out bars) || (bars < 1) || (bars > 64))
                    {
                        prog.Errors.Add(token);
                        continue;
                    }
                }

                Chord chord = ParseChord(name);

                if (chord == null)
                {
                    prog.Errors.Add(token);
                    continue;
                }

                prog.Steps.Add(new ProgressionStep { Chord = chord, Bars = bars });
            }

            return prog;
        }

        /// <summary>Index of the step playing at a given bar (0-based, counted from the start of the progression). The progression loops.</summary>
        public int StepAtBar(int bar)
        {
            int total = TotalBars;

            if (total == 0)
                return -1;

            bar = ((bar % total) + total) % total;

            for (int i = 0; i < Steps.Count; i++)
            {
                if (bar < Steps[i].Bars)
                    return i;

                bar -= Steps[i].Bars;
            }

            return Steps.Count - 1;
        }
    }
}

namespace ScaleMap
{
    /// <summary>Common progressions written relative to the key, turned into chord names for the selected key.</summary>
    public static class ProgressionPresets
    {
        public class Preset
        {
            public string Name;
            public (int Offset, string Suffix, int Bars)[] Chords;
        }

        public static readonly Preset[] Presets =
        {
            new Preset { Name = "i  bVI  bVII  (metal)", Chords = new[] { (0, "m", 1), (8, "", 1), (10, "", 1), (0, "m", 1) } },
            new Preset { Name = "i  bVII  bVI  V7  (andaluza)", Chords = new[] { (0, "m", 1), (10, "", 1), (8, "", 1), (7, "7", 1) } },
            new Preset { Name = "i  bVI  bIII  bVII", Chords = new[] { (0, "m", 1), (8, "", 1), (3, "", 1), (10, "", 1) } },
            new Preset { Name = "i  iv  v", Chords = new[] { (0, "m", 1), (5, "m", 1), (7, "m", 1), (0, "m", 1) } },
            new Preset { Name = "i  iv  V7  (menor harmónica)", Chords = new[] { (0, "m", 1), (5, "m", 1), (7, "7", 1), (0, "m", 1) } },
            new Preset { Name = "i5  bII5  (frígio)", Chords = new[] { (0, "5", 2), (1, "5", 2) } },
            new Preset { Name = "I  V  vi  IV  (pop)", Chords = new[] { (0, "", 1), (7, "", 1), (9, "m", 1), (5, "", 1) } },
            new Preset { Name = "I  IV  V", Chords = new[] { (0, "", 1), (5, "", 1), (7, "", 1), (0, "", 1) } },
            new Preset { Name = "ii  V  I  (jazz)", Chords = new[] { (2, "m7", 1), (7, "7", 1), (0, "maj7", 2) } },
            new Preset { Name = "Blues de 12 compassos", Chords = new[] { (0, "7", 4), (5, "7", 2), (0, "7", 2), (7, "7", 1), (5, "7", 1), (0, "7", 1), (7, "7", 1) } },
        };

        public static string Build(Preset preset, int keyRoot, Scale scale)
        {
            return string.Join(" | ", preset.Chords.Select(c =>
            {
                string name = Theory.NoteName(keyRoot + c.Offset, keyRoot, scale) + c.Suffix;

                return (c.Bars > 1) ? name + ":" + c.Bars : name;
            }));
        }
    }
}

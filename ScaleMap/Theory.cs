using System;
using System.Collections.Generic;
using System.Linq;

namespace ScaleMap
{
    public class Scale
    {
        public string Name;
        public int[] Intervals;
        public bool Blues;

        public Scale(string name, int[] intervals, bool blues = false)
        {
            Name = name;
            Intervals = intervals;
            Blues = blues;
        }
    }

    public class Tuning
    {
        public string Name;
        public int[] Pitches;   // MIDI note of each open string, lowest string first

        public Tuning(string name, int[] pitches)
        {
            Name = name;
            Pitches = pitches;
        }
    }

    public struct FretNote
    {
        public int String;
        public int Fret;
        public int Midi;

        public FretNote(int str, int fret, int midi)
        {
            String = str;
            Fret = fret;
            Midi = midi;
        }
    }

    public static class Theory
    {
        public const int NumFrets = 24;

        static readonly string[] sharpNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        static readonly string[] flatNames = { "C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B" };
        static readonly string[] intervalNames = { "1", "b2", "2", "b3", "3", "4", "b5", "5", "b6", "6", "b7", "7" };
        static readonly string[] cagedShapes = { "E", "D", "C", "A", "G" };

        public static readonly string[] KeyNames = { "C", "C# / Db", "D", "D# / Eb", "E", "F", "F# / Gb", "G", "G# / Ab", "A", "A# / Bb", "B" };

        public static readonly Scale[] Scales =
        {
            new Scale("Maior (Jónio)", new[] { 0, 2, 4, 5, 7, 9, 11 }),
            new Scale("Menor natural (Eólio)", new[] { 0, 2, 3, 5, 7, 8, 10 }),
            new Scale("Dórico", new[] { 0, 2, 3, 5, 7, 9, 10 }),
            new Scale("Frígio", new[] { 0, 1, 3, 5, 7, 8, 10 }),
            new Scale("Lídio", new[] { 0, 2, 4, 6, 7, 9, 11 }),
            new Scale("Mixolídio", new[] { 0, 2, 4, 5, 7, 9, 10 }),
            new Scale("Lócrio", new[] { 0, 1, 3, 5, 6, 8, 10 }),
            new Scale("Menor harmónica", new[] { 0, 2, 3, 5, 7, 8, 11 }),
            new Scale("Menor melódica", new[] { 0, 2, 3, 5, 7, 9, 11 }),
            new Scale("Frígio dominante", new[] { 0, 1, 4, 5, 7, 8, 10 }),
            new Scale("Húngara menor", new[] { 0, 2, 3, 6, 7, 8, 11 }),
            new Scale("Pentatónica menor", new[] { 0, 3, 5, 7, 10 }),
            new Scale("Pentatónica maior", new[] { 0, 2, 4, 7, 9 }),
            new Scale("Blues", new[] { 0, 3, 5, 6, 7, 10 }, blues: true),
            new Scale("Diminuta (tom, meio-tom)", new[] { 0, 2, 3, 5, 6, 8, 9, 11 }),
            new Scale("Tons inteiros", new[] { 0, 2, 4, 6, 8, 10 }),
        };

        public static readonly Tuning[] Tunings =
        {
            new Tuning("E Standard", new[] { 40, 45, 50, 55, 59, 64 }),
            new Tuning("Eb Standard", new[] { 39, 44, 49, 54, 58, 63 }),
            new Tuning("D Standard", new[] { 38, 43, 48, 53, 57, 62 }),
            new Tuning("Drop D", new[] { 38, 45, 50, 55, 59, 64 }),
            new Tuning("Drop C", new[] { 36, 43, 48, 53, 57, 62 }),
            new Tuning("7 cordas (B Standard)", new[] { 35, 40, 45, 50, 55, 59, 64 }),
            new Tuning("Baixo 4 cordas", new[] { 28, 33, 38, 43 }),
        };

        static int Mod12(int v) => ((v % 12) + 12) % 12;

        public static bool UseFlats(int root, Scale scale)
        {
            if (new[] { 5, 10, 3, 8, 1 }.Contains(root))
                return true;

            if (scale.Intervals.Contains(3) && new[] { 2, 7, 0 }.Contains(root))
                return true;

            return false;
        }

        public static string NoteName(int midiOrPc, int root, Scale scale)
        {
            return (UseFlats(root, scale) ? flatNames : sharpNames)[Mod12(midiOrPc)];
        }

        public static string IntervalName(int semitones, Scale scale)
        {
            if ((semitones == 6) && scale.Intervals.Contains(7) && !scale.Intervals.Contains(5))
                return "#4";

            return intervalNames[Mod12(semitones)];
        }

        public static bool IsMinorish(Scale scale) => scale.Intervals.Contains(3) && !scale.Intervals.Contains(4);

        public static bool UsesCaged(Scale scale, bool cagedSystem) => cagedSystem || scale.Blues || (scale.Intervals.Length == 5);

        public static int NumPositions(Scale scale, bool cagedSystem) => UsesCaged(scale, cagedSystem) ? 5 : scale.Intervals.Length;

        public static string PositionLabel(Scale scale, bool cagedSystem, int position)
        {
            if (position < 0)
                return "Todo o braço";

            if (UsesCaged(scale, cagedSystem))
                return "Posição " + (position + 1) + " (forma " + cagedShapes[position] + (IsMinorish(scale) ? "m" : "") + ")";

            return "Posição " + (position + 1) + " (3 notas/corda)";
        }

        // N notes per string, starting on scale degree "position" on the lowest string
        public static List<FretNote> PatternNps(int[] intervals, int root, int[] tuning, int position, int notesPerString)
        {
            int open0 = tuning[0];
            int rootFret = Mod12(root - open0);

            // positions climb the neck from the root; wrap down an octave only if they would run past the last fret
            int startFret = rootFret + intervals[position];

            if (startFret + 5 > NumFrets)
                startFret -= 12;

            HashSet<int> pitchClasses = new HashSet<int>(intervals.Select(i => Mod12(i + root)));

            int pitch = open0 + startFret;

            List<FretNote> notes = new List<FretNote>();

            for (int str = 0; str < tuning.Length; str++)
            {
                for (int k = 0; k < notesPerString; k++)
                {
                    if (!((str == 0) && (k == 0)))
                    {
                        pitch++;

                        while (!pitchClasses.Contains(Mod12(pitch)))
                            pitch++;
                    }

                    notes.Add(new FretNote(str, pitch - tuning[str], pitch));
                }
            }

            if (notes.Min(n => n.Fret) < 0)
                notes = notes.Select(n => new FretNote(n.String, n.Fret + 12, n.Midi + 12)).ToList();

            if ((notes.Max(n => n.Fret) > NumFrets) && (notes.Min(n => n.Fret) >= 12))
                notes = notes.Select(n => new FretNote(n.String, n.Fret - 12, n.Midi - 12)).ToList();

            return notes;
        }

        // CAGED: window of the pentatonic box (major or minor, following the scale's 3rd),
        // widened one fret each side and filled with every scale note inside it
        static List<FretNote> CagedNotes(Scale scale, int root, int[] tuning, int position)
        {
            int[] pent = IsMinorish(scale) ? new[] { 0, 3, 5, 7, 10 } : new[] { 0, 2, 4, 7, 9 };

            List<FretNote> box = PatternNps(pent, root, tuning, position, 2);

            HashSet<int> pitchClasses = new HashSet<int>(scale.Intervals.Select(i => Mod12(i + root)));

            // keep one note per pitch: prefer the one inside its string's box
            Dictionary<int, (FretNote note, bool inside)> best = new Dictionary<int, (FretNote, bool)>();

            for (int str = 0; str < tuning.Length; str++)
            {
                var frets = box.Where(n => n.String == str).Select(n => n.Fret).ToList();

                int a = frets.Min();
                int b = frets.Max();

                for (int f = Math.Max(0, a - 1); f <= b + 1; f++)
                {
                    int midi = tuning[str] + f;

                    if (!pitchClasses.Contains(Mod12(midi)))
                        continue;

                    bool inside = (f >= a) && (f <= b);

                    if (!best.TryGetValue(midi, out var existing) || (inside && !existing.inside))
                        best[midi] = (new FretNote(str, f, midi), inside);
                }
            }

            return best.Values.Select(v => v.note).ToList();
        }

        static List<FretNote> BluesNotes(int root, int[] tuning, int position)
        {
            List<FretNote> box = PatternNps(new[] { 0, 3, 5, 7, 10 }, root, tuning, position, 2);

            int lo = box.Min(n => n.Fret);
            int hi = box.Max(n => n.Fret);
            int blue = Mod12(root + 6);

            for (int str = 0; str < tuning.Length; str++)
            {
                for (int f = lo; f <= hi; f++)
                {
                    int midi = tuning[str] + f;

                    if (Mod12(midi) == blue)
                        box.Add(new FretNote(str, f, midi));
                }
            }

            return box;
        }

        public static List<FretNote> PositionNotes(Scale scale, int root, int[] tuning, int position, bool cagedSystem)
        {
            if (position < 0)
                position = 0;

            if (scale.Blues)
                return BluesNotes(root, tuning, position);

            if (UsesCaged(scale, cagedSystem))
                return CagedNotes(scale, root, tuning, position);

            return PatternNps(scale.Intervals, root, tuning, position, 3);
        }

        // Ascending then descending, one note per pitch - used by "Tocar posição"
        public static List<FretNote> PlaySequence(List<FretNote> positionNotes)
        {
            List<FretNote> up = positionNotes.OrderBy(n => n.Midi).ThenBy(n => n.String).ToList();

            List<FretNote> unique = new List<FretNote>();

            foreach (FretNote n in up)
            {
                if ((unique.Count == 0) || (unique[unique.Count - 1].Midi != n.Midi))
                    unique.Add(n);
            }

            List<FretNote> sequence = new List<FretNote>(unique);

            for (int i = unique.Count - 2; i >= 0; i--)
                sequence.Add(unique[i]);

            return sequence;
        }
    }
}

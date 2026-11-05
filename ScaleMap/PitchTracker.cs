using System;
using System.Collections.Concurrent;
using System.Threading;

namespace ScaleMap
{
    /// <summary>
    /// Monophonic note detection for the guitar input.
    /// The audio thread only copies samples into a ring buffer (Write); the analysis (YIN) runs on its own background thread.
    /// </summary>
    public class PitchTracker
    {
        const int RingSize = 1 << 16;          // must be a power of two
        const int RingMask = RingSize - 1;

        const float MinFrequency = 38;         // a bit below a 4-string bass low E (41 Hz)
        const float MaxFrequency = 1400;       // above the 24th fret of a high E string (1319 Hz)
        const float YinThreshold = 0.15f;

        readonly float[] ring = new float[RingSize];
        long writePos = 0;                      // total samples written (read with Interlocked)

        double sampleRate = 48000;
        int decimation = 2;
        int windowSize;                         // YIN integration window (decimated samples)
        int maxLag;
        int minLag;

        float[] raw;
        float[] work;
        float[] diff;
        readonly double[] refineVals = new double[32];

        Thread thread;
        volatile bool running = false;

        // note logic state (analysis thread only)
        int candidateMidi = -1;
        int candidateCount = 0;
        int silentFrames = 0;
        float lastRms = 0;
        int framesSinceOnset = 0;
        bool attackPending = false;

        /// <summary>Note currently heard (MIDI number), or -1 for silence / no clear pitch.</summary>
        public volatile int CurrentMidi = -1;

        /// <summary>Input level (RMS of the analysis window).</summary>
        public volatile float Level = 0;

        /// <summary>Each new note that starts is queued here (MIDI number).</summary>
        public ConcurrentQueue<int> Onsets { get; } = new ConcurrentQueue<int>();

        /// <summary>Minimum RMS level for a sound to count as a note.</summary>
        public float Gate { get; set; } = 0.007f;

        public PitchTracker()
        {
            SetSampleRate(48000);
        }

        public void SetSampleRate(double rate)
        {
            if ((rate <= 0) || ((rate == sampleRate) && (raw != null)))
                return;

            bool wasRunning = running;

            if (wasRunning)
                Stop();

            sampleRate = rate;

            decimation = (rate > 60000) ? 4 : 2;

            double decimatedRate = sampleRate / decimation;

            maxLag = (int)Math.Ceiling(decimatedRate / MinFrequency);
            minLag = Math.Max(2, (int)Math.Floor(decimatedRate / MaxFrequency));
            windowSize = maxLag + 64;

            int decimatedLength = windowSize + maxLag + 2;

            raw = new float[decimatedLength * decimation];
            work = new float[decimatedLength];
            diff = new float[maxLag + 2];

            if (wasRunning)
                Start();
        }

        /// <summary>Audio thread: store the input (mixed to mono).</summary>
        public void Write(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
        {
            long pos = Interlocked.Read(ref writePos);

            for (int i = 0; i < left.Length; i++)
            {
                ring[(int)((pos + i) & RingMask)] = (left[i] + right[i]) * 0.5f;
            }

            Interlocked.Add(ref writePos, left.Length);
        }

        public void Start()
        {
            if (running)
                return;

            running = true;

            thread = new Thread(Run) { IsBackground = true, Name = "ScaleMap pitch tracker", Priority = ThreadPriority.BelowNormal };
            thread.Start();
        }

        public void Stop()
        {
            running = false;

            Thread t = thread;

            if ((t != null) && (t != Thread.CurrentThread))
                t.Join(500);

            thread = null;
            CurrentMidi = -1;
        }

        void Run()
        {
            long lastAnalyzed = 0;

            while (running)
            {
                Thread.Sleep(8);

                long pos = Interlocked.Read(ref writePos);

                // analyse roughly every 15 ms of new audio
                if (pos - lastAnalyzed < (long)(sampleRate * 0.015))
                    continue;

                lastAnalyzed = pos;

                if (pos < raw.Length)
                    continue;

                long start = pos - raw.Length;

                for (int i = 0; i < raw.Length; i++)
                    raw[i] = ring[(int)((start + i) & RingMask)];

                Analyze(raw);
            }
        }

        /// <summary>Analyse one block of raw input samples (length = AnalysisLength). Public for testing.</summary>
        public int AnalysisLength => raw.Length;

        public void Analyze(float[] input)
        {
            // decimate with a simple boxcar low-pass
            int n = work.Length;

            double sumSq = 0;

            for (int i = 0; i < n; i++)
            {
                float acc = 0;
                int baseIndex = i * decimation;

                for (int k = 0; k < decimation; k++)
                    acc += input[baseIndex + k];

                acc /= decimation;

                work[i] = acc;
                sumSq += acc * acc;
            }

            float rms = (float)Math.Sqrt(sumSq / n);

            Level = rms;

            float freq = (rms >= Gate) ? Yin(work) : -1;

            if (freq > 0)
                freq = Refine(input, freq);

            UpdateNote(freq, rms);
        }

        float Yin(float[] x)
        {
            int w = windowSize;

            // difference function and cumulative mean normalised difference, computed lag by lag
            // so that the search can stop as soon as the first clear minimum is found
            double cumulative = 0;
            int best = -1;

            diff[0] = 1;

            for (int tau = 1; tau <= maxLag; tau++)
            {
                double sum = 0;

                for (int j = 0; j < w; j++)
                {
                    float d = x[j] - x[j + tau];
                    sum += d * d;
                }

                cumulative += sum;

                diff[tau] = (cumulative > 0) ? (float)(sum * tau / cumulative) : 1;

                if (tau <= minLag)
                    continue;

                if (best < 0)
                {
                    if (diff[tau - 1] < YinThreshold)
                        best = tau - 1;
                }

                if (best >= 0)
                {
                    // follow the dip down to its bottom, then stop
                    if (diff[tau] < diff[best])
                    {
                        best = tau;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            if ((best < 0) || (best >= maxLag))
                return -1;

            // guard against octave-down errors: if half the period is also a good match, the note is an octave higher
            int half = (int)Math.Round(best / 2.0);

            if (half > minLag + 2)
            {
                int halfBest = half;

                for (int t = half - 2; t <= half + 2; t++)
                {
                    if (diff[t] < diff[halfBest])
                        halfBest = t;
                }

                if (diff[halfBest] < YinThreshold * 2.0f)
                    best = halfBest;
            }

            // parabolic interpolation around the minimum
            float betterTau = best;

            if ((best > 1) && (best < maxLag))
            {
                float s0 = diff[best - 1];
                float s1 = diff[best];
                float s2 = diff[best + 1];
                float denom = (2 * s1) - s2 - s0;

                if (Math.Abs(denom) > 1e-9f)
                    betterTau = best + ((s2 - s0) / (2 * denom));
            }

            return (float)(sampleRate / decimation / betterTau);
        }

        /// <summary>
        /// The decimated search finds the period to within a couple of samples; refine it on the full-rate signal
        /// (important for high notes, where one decimated sample is a big pitch step).
        /// </summary>
        float Refine(float[] input, float freq)
        {
            double period = sampleRate / freq;

            int center = (int)Math.Round(period);
            int lo = Math.Max(2, center - decimation - 1);
            int hi = center + decimation + 1;

            int w = Math.Min(input.Length - hi - 1, (int)(windowSize * decimation));

            if (w < 64)
                return freq;

            int bestLag = -1;
            double bestVal = double.MaxValue;
            double[] vals = refineVals;

            for (int lag = lo; lag <= hi; lag++)
            {
                double sum = 0;

                for (int j = 0; j < w; j++)
                {
                    float d = input[j] - input[j + lag];
                    sum += d * d;
                }

                vals[lag - lo] = sum;

                if (sum < bestVal)
                {
                    bestVal = sum;
                    bestLag = lag;
                }
            }

            double refined = bestLag;
            int bi = bestLag - lo;

            if ((bi > 0) && (bi < vals.Length - 1))
            {
                double s0 = vals[bi - 1], s1 = vals[bi], s2 = vals[bi + 1];
                double denom = (2 * s1) - s2 - s0;

                if (Math.Abs(denom) > 1e-12)
                    refined = bestLag + ((s2 - s0) / (2 * denom));
            }

            return (float)(sampleRate / refined);
        }

        void UpdateNote(float freq, float rms)
        {
            if (freq <= 0)
            {
                silentFrames++;

                // short dropouts (vibrato, string noise) do not end the note
                if (silentFrames > 4)
                {
                    CurrentMidi = -1;
                    candidateMidi = -1;
                    candidateCount = 0;
                }

                lastRms = rms;
                return;
            }

            silentFrames = 0;

            int midi = (int)Math.Round(69 + (12 * Math.Log(freq / 440.0, 2)));

            if (midi == candidateMidi)
            {
                candidateCount++;
            }
            else
            {
                candidateMidi = midi;
                candidateCount = 1;
            }

            framesSinceOnset++;

            // a clear jump in level = the string was picked again. The pitch window still holds the old note for a
            // few frames, so wait until the pitch is stable again and then report the onset (same note or a new one)
            if ((framesSinceOnset >= 3) && (rms > lastRms * 1.6f) && (rms > Gate * 2))
            {
                attackPending = true;
                candidateCount = 0;
            }

            if ((candidateCount == 3) && ((midi != CurrentMidi) || attackPending))
            {
                CurrentMidi = midi;
                Onsets.Enqueue(midi);
                framesSinceOnset = 0;
                attackPending = false;
            }

            lastRms = rms;
        }
    }
}

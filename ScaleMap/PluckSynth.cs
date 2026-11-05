using System;
using System.Collections.Concurrent;

namespace ScaleMap
{
    /// <summary>
    /// Small plucked-string synth (Karplus-Strong) plus a step sequencer for "Tocar posição".
    /// The UI thread only calls Trigger / StartSequence / StopSequence; everything else runs on the audio thread.
    /// </summary>
    public class PluckSynth
    {
        const int MaxVoices = 12;
        const int MaxDelay = 8192;

        class Voice
        {
            public float[] Ring = new float[MaxDelay];
            public int Length;
            public int Index;
            public float Amp;
            public bool Active;
        }

        readonly Voice[] voices = new Voice[MaxVoices];
        readonly ConcurrentQueue<int> pending = new ConcurrentQueue<int>();
        readonly Random random = new Random();

        double sampleRate = 48000;
        int nextVoice = 0;

        // sequencer state (written by UI, read by audio thread)
        volatile int[] sequence = null;
        volatile bool sequenceLoop = false;
        volatile bool stopRequested = false;
        double samplesUntilStep = 0;
        int sequenceIndex = 0;

        /// <summary>Index of the note currently sounding in the sequence, or -1 when stopped.</summary>
        public volatile int CurrentStep = -1;

        public bool IsSequencePlaying => sequence != null;

        public float Volume { get; set; } = 0.35f;

        /// <summary>When true, a new note silences the previous one (like fretting on the same string).</summary>
        public bool Monophonic { get; set; } = false;

        public PluckSynth()
        {
            for (int i = 0; i < MaxVoices; i++)
                voices[i] = new Voice();
        }

        public void SetSampleRate(double rate)
        {
            if (rate > 0)
                sampleRate = rate;
        }

        public void Trigger(int midiNote)
        {
            pending.Enqueue(midiNote);
        }

        public void StartSequence(int[] midiNotes, bool loop)
        {
            sequenceLoop = loop;
            stopRequested = false;
            sequenceIndex = 0;
            samplesUntilStep = 0;
            sequence = midiNotes;
        }

        public void SetLoop(bool loop)
        {
            sequenceLoop = loop;
        }

        public void StopSequence()
        {
            stopRequested = true;
        }

        void StartVoice(int midiNote)
        {
            double freq = 440.0 * Math.Pow(2, (midiNote - 69) / 12.0);

            int length = (int)Math.Round(sampleRate / freq);

            if (length < 2)
                length = 2;

            if (length > MaxDelay)
                length = MaxDelay;

            if (Monophonic)
            {
                for (int i = 0; i < MaxVoices; i++)
                    voices[i].Active = false;
            }

            Voice v = voices[nextVoice];
            nextVoice = (nextVoice + 1) % MaxVoices;

            for (int i = 0; i < length; i++)
                v.Ring[i] = (float)(random.NextDouble() * 2 - 1);

            v.Length = length;
            v.Index = 0;
            v.Amp = 1;
            v.Active = true;
        }

        /// <summary>Adds the synth output to the given buffers. bpm drives the sequencer (eighth notes).</summary>
        public void Process(Span<float> left, Span<float> right, double bpm)
        {
            while (pending.TryDequeue(out int note))
                StartVoice(note);

            int[] seq = sequence;

            if (seq != null && stopRequested)
            {
                sequence = null;
                seq = null;
                CurrentStep = -1;
                stopRequested = false;
            }

            double samplesPerStep = sampleRate * 60.0 / Math.Max(20, bpm) / 2.0;

            // release so that a note fades out in roughly two seconds
            float release = (float)Math.Pow(0.001, 1.0 / (sampleRate * 2.0));

            for (int i = 0; i < left.Length; i++)
            {
                if (seq != null)
                {
                    samplesUntilStep -= 1;

                    if (samplesUntilStep <= 0)
                    {
                        if (sequenceIndex >= seq.Length)
                        {
                            if (sequenceLoop)
                            {
                                sequenceIndex = 0;
                            }
                            else
                            {
                                sequence = null;
                                seq = null;
                                CurrentStep = -1;
                            }
                        }

                        if (seq != null)
                        {
                            StartVoice(seq[sequenceIndex]);
                            CurrentStep = sequenceIndex;
                            sequenceIndex++;
                            samplesUntilStep += samplesPerStep;
                        }
                    }
                }

                float sum = 0;

                for (int v = 0; v < MaxVoices; v++)
                {
                    Voice voice = voices[v];

                    if (!voice.Active)
                        continue;

                    int idx = voice.Index;
                    int next = idx + 1;

                    if (next >= voice.Length)
                        next = 0;

                    float a = voice.Ring[idx];

                    voice.Ring[idx] = (a + voice.Ring[next]) * 0.5f * 0.996f;
                    voice.Index = next;

                    sum += a * voice.Amp;

                    voice.Amp *= release;

                    if (voice.Amp < 0.0005f)
                        voice.Active = false;
                }

                float outSample = sum * Volume;

                left[i] += outSample;
                right[i] += outSample;
            }
        }
    }
}

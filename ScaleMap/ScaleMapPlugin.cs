using System;
using System.Windows.Controls;
using AudioPlugSharp;
using AudioPlugSharpWPF;

namespace ScaleMap
{
    public class ScaleMapSaveState : AudioPluginSaveState
    {
        public int Key { get; set; } = 4;          // E
        public int Scale { get; set; } = 1;        // Menor natural
        public bool Caged { get; set; } = false;
        public int Position { get; set; } = 0;     // -1 = todo o braço
        public int Tuning { get; set; } = 0;
        public int Labels { get; set; } = 0;       // 0 notas, 1 intervalos, 2 só pontos
        public bool Ghost { get; set; } = true;
        public int Bpm { get; set; } = 100;
        public bool UseHostBpm { get; set; } = false;
        public bool Loop { get; set; } = false;
        public int Volume { get; set; } = 70;      // %

        // chord targets
        public bool ChordMode { get; set; } = false;
        public string Progression { get; set; } = "Em C D B7";
        public int BeatsPerBar { get; set; } = 4;
        public int StartBar { get; set; } = 1;

        // note detection
        public bool Detect { get; set; } = true;
        public int Sensitivity { get; set; } = 50;  // 0..100

        public ScaleMapSaveState()
        {
        }
    }

    public class ScaleMapPlugin : AudioPluginWPF
    {
        FloatAudioIOPort stereoInput;
        FloatAudioIOPort stereoOutput;

        ScaleMapView view = null;

        public PluckSynth Synth { get; } = new PluckSynth();
        public PitchTracker Tracker { get; } = new PitchTracker();

        // DAW transport, written by the audio thread and read by the editor
        public volatile bool HostPlaying = false;
        double hostBeat = 0;
        public double HostBeat => System.Threading.Volatile.Read(ref hostBeat);

        public static float GateFromSensitivity(int sensitivity)
        {
            // 0 = only loud notes count (gate 0.05), 100 = very sensitive (gate 0.001)
            return (float)(0.05 * Math.Pow(0.02, Math.Max(0, Math.Min(100, sensitivity)) / 100.0));
        }

        public ScaleMapSaveState Settings
        {
            get
            {
                if (!(SaveStateData is ScaleMapSaveState))
                    SaveStateData = new ScaleMapSaveState();

                return (ScaleMapSaveState)SaveStateData;
            }
        }

        public ScaleMapPlugin()
        {
            Company = "deamonC";
            Website = "https://github.com/deamonC";
            Contact = "";
            PluginName = "Scale Map";
            PluginCategory = "Fx";
            PluginVersion = "1.0.0";

            // Unique 64bit ID for the plugin
            PluginID = 0x5C3A1E7B2D4F6081;

            HasUserInterface = true;
            EditorWidth = 1300;
            EditorHeight = 560;

            SaveStateData = new ScaleMapSaveState();
        }

        public override void Initialize()
        {
            base.Initialize();

            InputPorts = new AudioIOPort[] { stereoInput = new FloatAudioIOPort("Stereo Input", EAudioChannelConfiguration.Stereo) };
            OutputPorts = new AudioIOPort[] { stereoOutput = new FloatAudioIOPort("Stereo Output", EAudioChannelConfiguration.Stereo) };
        }

        public override void InitializeProcessing()
        {
            base.InitializeProcessing();

            Synth.SetSampleRate(Host.SampleRate);
            Tracker.SetSampleRate(Host.SampleRate);
        }

        /// <summary>
        /// The DAW reads the window size when it opens the plugin window, so this applies the next time the window is opened.
        /// </summary>
        public void SetEditorSize(uint width, uint height)
        {
            EditorWidth = width;
            EditorHeight = height;
        }

        public override UserControl GetEditorView()
        {
            view = new ScaleMapView(this);

            return view;
        }

        public override void RestoreState(byte[] stateData)
        {
            base.RestoreState(stateData);

            // the editor may already exist when the DAW restores the project
            if (view != null)
            {
                view.Dispatcher.BeginInvoke(new Action(() => view.LoadSettings()));
            }
        }

        public override void Process()
        {
            base.Process();

            Host.ProcessAllEvents();

            Span<float> outLeft = stereoOutput.GetAudioBuffer(0);
            Span<float> outRight = stereoOutput.GetAudioBuffer(1);

            // pass the guitar through untouched, so the plugin can sit anywhere on the track
            ReadOnlySpan<float> inLeft = stereoInput.GetAudioBuffer(0);
            ReadOnlySpan<float> inRight = stereoInput.GetAudioBuffer(1);

            inLeft.CopyTo(outLeft);
            inRight.CopyTo(outRight);

            ScaleMapSaveState settings = Settings;

            // the note detector gets the dry input (put the plugin before the amp sim)
            Tracker.Gate = GateFromSensitivity(settings.Sensitivity);
            Tracker.Write(outLeft, outRight);

            // DAW position in beats (assumes a constant tempo)
            HostPlaying = Host.IsPlaying;

            if ((Host.BPM > 0) && (Host.SampleRate > 0))
                System.Threading.Volatile.Write(ref hostBeat, Host.CurrentProjectSample / Host.SampleRate * Host.BPM / 60.0);

            double bpm = settings.Bpm;

            if (settings.UseHostBpm && (Host.BPM > 0))
                bpm = Host.BPM;

            Synth.Volume = 0.5f * settings.Volume / 100.0f;
            Synth.Process(outLeft, outRight, bpm);
        }
    }
}

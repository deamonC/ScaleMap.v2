using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ScaleMap
{
    public class ScaleMapView : UserControl
    {
        // ---- colours ----
        static readonly Brush PageBrush = Hex("#1F232B");
        static readonly Brush InkBrush = Hex("#E9EBEF");
        static readonly Brush MutedBrush = Hex("#9AA1AE");
        static readonly Brush WoodBrush = Hex("#2B1C14");
        static readonly Brush WoodEdgeBrush = Hex("#1A100B");
        static readonly Brush FretBrush = Hex("#C9C3B6");
        static readonly Brush NutBrush = Hex("#EFE9DA");
        static readonly Brush StringBrush = Hex("#D8D3C8");
        static readonly Brush InlayBrush = Hex("#E9E4D6");
        static readonly Brush NoteBrush = Hex("#F3EFE4");
        static readonly Brush NoteInkBrush = Hex("#21170F");
        static readonly Brush RootBrush = Hex("#D9A441");
        static readonly Brush RootInkBrush = Hex("#1F1406");
        static readonly Brush PlayBrush = Hex("#4FC3C9");
        static readonly Brush ChordToneBrush = Hex("#4FC3C9");
        static readonly Brush ChordRootBrush = Hex("#FF8A3D");
        static readonly Brush GoodBrush = Hex("#3FD46B");
        static readonly Brush BadBrush = Hex("#FF4D4D");
        static readonly Brush PanelBrush = Hex("#272C35");

        // ---- geometry ----
        const double OpenWidth = 46;
        const double StringSpacing = 30;
        const double TopMargin = 22;
        const double NoteRadius = 12.5;

        readonly ScaleMapPlugin plugin;

        ComboBox keyBox, scaleBox, systemBox, positionBox, tuningBox, labelsBox, windowBox;
        TextBlock windowHint;

        static readonly (string Name, uint Width, uint Height)[] windowPresets =
        {
            ("Pequena (1000 x 430)", 1000, 430),
            ("Média (1300 x 560)", 1300, 560),
            ("Grande (1600 x 690)", 1600, 690),
            ("Muito grande (1900 x 820)", 1900, 820),
        };

        // chord targets
        CheckBox chordCheck;
        TextBox progressionBox;
        ComboBox presetBox, beatsBox, startBarBox;
        Button prevChordButton, nextChordButton;
        TextBlock chordNowText, chordNextText, progressionError;
        ChordProgression progression = new ChordProgression();
        int manualStep = 0;
        int shownStep = -2;

        // note detection
        CheckBox detectCheck;
        Slider sensitivitySlider;
        Rectangle levelBar;
        Button resetStatsButton;
        TextBlock playingText, statsText;
        Canvas overlay;
        int shownMidi = -2;
        int statTotal = 0, statInScale = 0, statChordTotal = 0, statChordHits = 0;
        CheckBox ghostCheck, loopCheck, hostBpmCheck;
        Slider bpmSlider, volumeSlider;
        TextBlock bpmText, notesText, formulaText, positionText;
        Button playButton;
        Canvas board;
        ScrollViewer boardScroll;

        bool loading = false;

        List<FretNote> positionNotes = new List<FretNote>();
        List<FretNote> playSequence = new List<FretNote>();
        readonly Dictionary<long, Ellipse> noteShapes = new Dictionary<long, Ellipse>();
        readonly Dictionary<long, Brush> noteFills = new Dictionary<long, Brush>();
        long highlightedKey = -1;
        int[] currentTuning = Theory.Tunings[0].Pitches;

        readonly DispatcherTimer timer;

        static Brush Hex(string hex)
        {
            SolidColorBrush brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        static long NoteKey(int str, int fret) => (str * 100L) + fret;

        public ScaleMapView(ScaleMapPlugin plugin)
        {
            this.plugin = plugin;

            // controls fire change events while they are being created - ignore them until the settings are loaded
            loading = true;

            Background = PageBrush;
            Foreground = InkBrush;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;

            BuildLayout();

            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            timer.Tick += Timer_Tick;

            Loaded += (s, e) => { LoadSettings(); timer.Start(); UpdateTracker(); };
            Unloaded += (s, e) => { timer.Stop(); plugin.Tracker.Stop(); };
        }

        // ------------------------------------------------------------------ layout

        StackPanel Labeled(string label, FrameworkElement control)
        {
            StackPanel panel = new StackPanel { Margin = new Thickness(0, 0, 12, 6) };

            panel.Children.Add(new TextBlock { Text = label, Foreground = MutedBrush, Margin = new Thickness(0, 0, 0, 2) });
            panel.Children.Add(control);

            return panel;
        }

        ComboBox MakeCombo(double width)
        {
            ComboBox combo = new ComboBox { Width = width, Height = 26 };
            combo.SelectionChanged += (s, e) => SettingsChanged();
            return combo;
        }

        CheckBox MakeCheck(string text)
        {
            CheckBox check = new CheckBox
            {
                Content = text,
                Foreground = InkBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 16, 0)
            };

            check.Checked += (s, e) => SettingsChanged();
            check.Unchecked += (s, e) => SettingsChanged();

            return check;
        }

        void BuildLayout()
        {
            DockPanel root = new DockPanel { Margin = new Thickness(12, 10, 12, 8), LastChildFill = true };

            // --- top: selectors ---
            WrapPanel selectors = new WrapPanel();

            keyBox = MakeCombo(90);
            foreach (string k in Theory.KeyNames) keyBox.Items.Add(k);

            scaleBox = MakeCombo(190);
            foreach (Scale sc in Theory.Scales) scaleBox.Items.Add(sc.Name);

            systemBox = MakeCombo(150);
            systemBox.Items.Add("3 notas por corda");
            systemBox.Items.Add("CAGED");

            positionBox = MakeCombo(200);

            tuningBox = MakeCombo(170);
            foreach (Tuning t in Theory.Tunings) tuningBox.Items.Add(t.Name);

            labelsBox = MakeCombo(150);
            labelsBox.Items.Add("Nomes das notas");
            labelsBox.Items.Add("Intervalos");
            labelsBox.Items.Add("Só pontos");

            selectors.Children.Add(Labeled("Tom", keyBox));
            selectors.Children.Add(Labeled("Escala", scaleBox));
            selectors.Children.Add(Labeled("Sistema", systemBox));
            selectors.Children.Add(Labeled("Posição", positionBox));
            selectors.Children.Add(Labeled("Afinação", tuningBox));
            selectors.Children.Add(Labeled("Mostrar", labelsBox));

            windowBox = new ComboBox { Width = 190, Height = 26 };
            foreach (var preset in windowPresets) windowBox.Items.Add(preset.Name);
            windowBox.SelectionChanged += (s, e) => WindowSizeChanged();
            selectors.Children.Add(Labeled("Tamanho da janela", windowBox));

            DockPanel.SetDock(selectors, Dock.Top);
            root.Children.Add(selectors);

            // --- chord targets ---
            WrapPanel chordRow = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };

            chordCheck = MakeCheck("Notas-alvo do acorde");
            chordRow.Children.Add(Labeled(" ", chordCheck));

            presetBox = new ComboBox { Width = 230, Height = 26 };
            presetBox.Items.Add("Escolher uma progressão...");
            foreach (var preset in ProgressionPresets.Presets) presetBox.Items.Add(preset.Name);
            presetBox.SelectedIndex = 0;
            presetBox.SelectionChanged += (s, e) => PresetChosen();
            chordRow.Children.Add(Labeled("Progressões comuns (no tom escolhido)", presetBox));

            progressionBox = new TextBox { Width = 300, Height = 26, VerticalContentAlignment = VerticalAlignment.Center, FontSize = 14 };
            progressionBox.TextChanged += (s, e) => ProgressionChanged();
            chordRow.Children.Add(Labeled("Acordes (um por compasso, Am:2 = 2 compassos)", progressionBox));

            beatsBox = MakeCombo(60);
            for (int b = 2; b <= 7; b++) beatsBox.Items.Add(b.ToString());
            chordRow.Children.Add(Labeled("Tempos", beatsBox));

            startBarBox = MakeCombo(70);
            for (int b = 1; b <= 64; b++) startBarBox.Items.Add(b.ToString());
            chordRow.Children.Add(Labeled("Começa no compasso", startBarBox));

            prevChordButton = new Button { Content = "◀", Width = 32, Height = 26, Margin = new Thickness(0, 0, 4, 0) };
            prevChordButton.Click += (s, e) => StepChord(-1);
            nextChordButton = new Button { Content = "▶", Width = 32, Height = 26 };
            nextChordButton.Click += (s, e) => StepChord(1);

            StackPanel stepButtons = new StackPanel { Orientation = Orientation.Horizontal };
            stepButtons.Children.Add(prevChordButton);
            stepButtons.Children.Add(nextChordButton);
            chordRow.Children.Add(Labeled("Com a DAW parada", stepButtons));

            DockPanel.SetDock(chordRow, Dock.Top);
            root.Children.Add(chordRow);

            // --- note detection ---
            WrapPanel detectRow = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };

            detectCheck = MakeCheck("Detetar o que toco");
            detectCheck.VerticalAlignment = VerticalAlignment.Center;
            detectRow.Children.Add(detectCheck);

            detectRow.Children.Add(new TextBlock { Text = "Sensibilidade", Foreground = MutedBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });

            sensitivitySlider = new Slider { Minimum = 0, Maximum = 100, Width = 120, VerticalAlignment = VerticalAlignment.Center };
            sensitivitySlider.ValueChanged += (s, e) => SettingsChanged();
            detectRow.Children.Add(sensitivitySlider);

            Border levelBack = new Border { Width = 120, Height = 8, Background = PanelBrush, Margin = new Thickness(10, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
            levelBar = new Rectangle { Width = 0, Height = 8, Fill = GoodBrush, HorizontalAlignment = HorizontalAlignment.Left };
            levelBack.Child = levelBar;
            detectRow.Children.Add(levelBack);

            resetStatsButton = new Button { Content = "Repor contagem", Padding = new Thickness(10, 2, 10, 2), VerticalAlignment = VerticalAlignment.Center };
            resetStatsButton.Click += (s, e) => { statTotal = statInScale = statChordTotal = statChordHits = 0; UpdateStatsText(); };
            detectRow.Children.Add(resetStatsButton);

            detectRow.Children.Add(new TextBlock
            {
                Text = "Para a deteção funcionar bem, põe o Scale Map antes do amp sim e toca uma nota de cada vez.",
                Foreground = MutedBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16, 0, 0, 0)
            });

            DockPanel.SetDock(detectRow, Dock.Top);
            root.Children.Add(detectRow);

            // --- bottom: info ---
            StackPanel info = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };

            notesText = new TextBlock { Foreground = InkBrush, Margin = new Thickness(0, 0, 0, 2) };
            formulaText = new TextBlock { Foreground = InkBrush, Margin = new Thickness(0, 0, 0, 2) };
            positionText = new TextBlock { Foreground = MutedBrush };

            info.Children.Add(notesText);
            info.Children.Add(formulaText);
            info.Children.Add(positionText);

            windowHint = new TextBlock { Foreground = RootBrush, Margin = new Thickness(0, 2, 0, 0) };
            info.Children.Add(windowHint);

            progressionError = new TextBlock { Foreground = BadBrush, Margin = new Thickness(0, 2, 0, 0) };
            info.Children.Add(progressionError);

            DockPanel.SetDock(info, Dock.Bottom);
            root.Children.Add(info);

            // --- bottom: playback bar ---
            WrapPanel bar = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };

            playButton = new Button
            {
                Content = "Tocar posição",
                Padding = new Thickness(14, 4, 14, 4),
                Margin = new Thickness(0, 0, 16, 0),
                Background = RootBrush,
                Foreground = RootInkBrush,
                FontWeight = FontWeights.SemiBold,
                BorderThickness = new Thickness(0)
            };
            playButton.Click += (s, e) => TogglePlay();
            bar.Children.Add(playButton);

            bar.Children.Add(new TextBlock { Text = "BPM", Foreground = MutedBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });

            bpmSlider = new Slider { Minimum = 40, Maximum = 220, Width = 140, IsSnapToTickEnabled = true, TickFrequency = 1, VerticalAlignment = VerticalAlignment.Center };
            bpmSlider.ValueChanged += (s, e) => SettingsChanged();
            bar.Children.Add(bpmSlider);

            bpmText = new TextBlock { Foreground = InkBrush, Width = 36, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 12, 0) };
            bar.Children.Add(bpmText);

            hostBpmCheck = MakeCheck("Usar BPM da DAW");
            bar.Children.Add(hostBpmCheck);

            loopCheck = MakeCheck("Repetir");
            bar.Children.Add(loopCheck);

            ghostCheck = MakeCheck("Mostrar o resto da escala esbatido");
            bar.Children.Add(ghostCheck);

            bar.Children.Add(new TextBlock { Text = "Volume", Foreground = MutedBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });

            volumeSlider = new Slider { Minimum = 0, Maximum = 100, Width = 100, VerticalAlignment = VerticalAlignment.Center };
            volumeSlider.ValueChanged += (s, e) => SettingsChanged();
            bar.Children.Add(volumeSlider);

            DockPanel.SetDock(bar, Dock.Bottom);
            root.Children.Add(bar);

            // --- status: chord now/next, note being played, score ---
            Grid status = new Grid { Margin = new Thickness(0, 6, 0, 0), Background = PanelBrush };
            status.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            status.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            status.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });

            StackPanel chordStatus = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 6, 10, 6) };
            chordNowText = new TextBlock { FontSize = 22, FontWeight = FontWeights.Bold, Foreground = ChordRootBrush, VerticalAlignment = VerticalAlignment.Center };
            chordNextText = new TextBlock { FontSize = 15, Foreground = MutedBrush, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            chordStatus.Children.Add(chordNowText);
            chordStatus.Children.Add(chordNextText);
            Grid.SetColumn(chordStatus, 0);
            status.Children.Add(chordStatus);

            playingText = new TextBlock { FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(10, 6, 10, 6), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(playingText, 1);
            status.Children.Add(playingText);

            statsText = new TextBlock { FontSize = 15, Foreground = InkBrush, Margin = new Thickness(10, 6, 10, 6), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
            Grid.SetColumn(statsText, 2);
            status.Children.Add(statsText);

            DockPanel.SetDock(status, Dock.Bottom);
            root.Children.Add(status);

            // --- centre: fretboard ---
            board = new Canvas { Background = Brushes.Transparent, Cursor = Cursors.Hand };
            board.MouseLeftButtonDown += Board_MouseLeftButtonDown;

            // chord rings and the detected note are drawn on a layer above the fretboard,
            // so they can change many times a second without redrawing the whole neck
            overlay = new Canvas { IsHitTestVisible = false };

            Grid boardLayers = new Grid();
            boardLayers.Children.Add(board);
            boardLayers.Children.Add(overlay);

            boardScroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = boardLayers,
                Margin = new Thickness(0, 4, 0, 0)
            };

            root.Children.Add(boardScroll);

            // Everything (text, buttons, fretboard) is laid out at one design width and then scaled as a whole
            // to fit the plugin window, so it always looks proportioned whatever the window or screen size.
            root.Width = FretX(Theory.NumFrets) + 16 + 24 + 6;

            Content = new Viewbox
            {
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.Both,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Child = root
            };
        }

        // ------------------------------------------------------------------ settings

        public void LoadSettings()
        {
            ScaleMapSaveState st = plugin.Settings;

            loading = true;

            keyBox.SelectedIndex = Clamp(st.Key, 0, 11);
            scaleBox.SelectedIndex = Clamp(st.Scale, 0, Theory.Scales.Length - 1);
            systemBox.SelectedIndex = st.Caged ? 1 : 0;
            tuningBox.SelectedIndex = Clamp(st.Tuning, 0, Theory.Tunings.Length - 1);
            labelsBox.SelectedIndex = Clamp(st.Labels, 0, 2);
            ghostCheck.IsChecked = st.Ghost;
            loopCheck.IsChecked = st.Loop;
            hostBpmCheck.IsChecked = st.UseHostBpm;
            bpmSlider.Value = Clamp(st.Bpm, 40, 220);
            volumeSlider.Value = Clamp(st.Volume, 0, 100);

            RebuildPositions(st.Position);

            windowBox.SelectedIndex = Array.FindIndex(windowPresets, p => (p.Width == plugin.EditorWidth) && (p.Height == plugin.EditorHeight));

            chordCheck.IsChecked = st.ChordMode;
            progressionBox.Text = st.Progression ?? "";
            beatsBox.SelectedIndex = Clamp(st.BeatsPerBar, 2, 7) - 2;
            startBarBox.SelectedIndex = Clamp(st.StartBar, 1, 64) - 1;
            detectCheck.IsChecked = st.Detect;
            sensitivitySlider.Value = Clamp(st.Sensitivity, 0, 100);

            progression = ChordProgression.Parse(progressionBox.Text);
            UpdateProgressionError();
            shownStep = -2;

            loading = false;

            Render();
        }

        static int Clamp(int v, int min, int max) => Math.Max(min, Math.Min(max, v));

        void RebuildPositions(int wanted)
        {
            Scale scale = Theory.Scales[Math.Max(0, scaleBox.SelectedIndex)];
            bool caged = systemBox.SelectedIndex == 1;

            bool wasLoading = loading;
            loading = true;

            positionBox.Items.Clear();
            positionBox.Items.Add(Theory.PositionLabel(scale, caged, -1));

            int count = Theory.NumPositions(scale, caged);

            for (int p = 0; p < count; p++)
                positionBox.Items.Add(Theory.PositionLabel(scale, caged, p));

            // item 0 is "Todo o braço" (-1), item p+1 is position p
            positionBox.SelectedIndex = ((wanted >= -1) && (wanted < count)) ? wanted + 1 : 1;

            loading = wasLoading;
        }

        void WindowSizeChanged()
        {
            if (loading || (windowBox.SelectedIndex < 0))
                return;

            var preset = windowPresets[windowBox.SelectedIndex];

            plugin.SetEditorSize(preset.Width, preset.Height);

            windowHint.Text = "Fecha e volta a abrir a janela do plugin para aplicar o novo tamanho.";
        }

        void SettingsChanged()
        {
            if (loading || (plugin == null))
                return;

            ScaleMapSaveState st = plugin.Settings;

            bool structureChanged = (st.Scale != scaleBox.SelectedIndex) || (st.Caged != (systemBox.SelectedIndex == 1));

            st.Key = keyBox.SelectedIndex;
            st.Scale = scaleBox.SelectedIndex;
            st.Caged = systemBox.SelectedIndex == 1;
            st.Tuning = tuningBox.SelectedIndex;
            st.Labels = labelsBox.SelectedIndex;
            st.Ghost = ghostCheck.IsChecked == true;
            st.Loop = loopCheck.IsChecked == true;
            st.UseHostBpm = hostBpmCheck.IsChecked == true;
            st.Bpm = (int)Math.Round(bpmSlider.Value);
            st.Volume = (int)Math.Round(volumeSlider.Value);
            st.ChordMode = chordCheck.IsChecked == true;
            st.BeatsPerBar = beatsBox.SelectedIndex + 2;
            st.StartBar = startBarBox.SelectedIndex + 1;
            st.Detect = detectCheck.IsChecked == true;
            st.Sensitivity = (int)Math.Round(sensitivitySlider.Value);

            UpdateTracker();
            shownStep = -2;
            shownMidi = -2;

            if (structureChanged)
                RebuildPositions(st.Position);

            st.Position = positionBox.SelectedIndex - 1;

            plugin.Synth.SetLoop(st.Loop);

            Render();
        }

        // ------------------------------------------------------------------ drawing

        static double FretX(int n)
        {
            double x = OpenWidth;

            for (int k = 1; k <= n; k++)
                x += 58 - ((k - 1) * 1.0);

            return x;
        }

        static double NoteX(int fret) => (fret == 0) ? OpenWidth / 2 : (FretX(fret - 1) + FretX(fret)) / 2;

        double StringY(int str, int numStrings) => TopMargin + ((numStrings - 1 - str) * StringSpacing);

        void AddRect(double x, double y, double w, double h, Brush fill, double radius = 0, Brush stroke = null)
        {
            Rectangle r = new Rectangle { Width = Math.Max(0, w), Height = Math.Max(0, h), Fill = fill, RadiusX = radius, RadiusY = radius, IsHitTestVisible = false };

            if (stroke != null)
            {
                r.Stroke = stroke;
                r.StrokeThickness = 1;
            }

            Canvas.SetLeft(r, x);
            Canvas.SetTop(r, y);
            board.Children.Add(r);
        }

        void AddDot(double cx, double cy, double radius, Brush fill, double opacity)
        {
            Ellipse e = new Ellipse { Width = radius * 2, Height = radius * 2, Fill = fill, Opacity = opacity, IsHitTestVisible = false };

            Canvas.SetLeft(e, cx - radius);
            Canvas.SetTop(e, cy - radius);
            board.Children.Add(e);
        }

        void AddText(string text, double cx, double cy, Brush brush, double size, FontWeight weight, double width = 30)
        {
            TextBlock t = new TextBlock
            {
                Text = text,
                Foreground = brush,
                FontSize = size,
                FontWeight = weight,
                Width = width,
                TextAlignment = TextAlignment.Center,
                IsHitTestVisible = false
            };

            Canvas.SetLeft(t, cx - (width / 2));
            Canvas.SetTop(t, cy - (size * 0.68));
            board.Children.Add(t);
        }

        void Render()
        {
            if (board == null)
                return;

            int rootPc = Math.Max(0, keyBox.SelectedIndex);
            Scale scale = Theory.Scales[Math.Max(0, scaleBox.SelectedIndex)];
            bool caged = systemBox.SelectedIndex == 1;
            int position = positionBox.SelectedIndex - 1;
            int labels = Math.Max(0, labelsBox.SelectedIndex);
            bool ghost = ghostCheck.IsChecked == true;

            currentTuning = Theory.Tunings[Math.Max(0, tuningBox.SelectedIndex)].Pitches;
            int[] tuning = currentTuning;
            int numStrings = tuning.Length;

            bpmText.Text = ((int)Math.Round(bpmSlider.Value)).ToString();

            HashSet<int> pitchClasses = new HashSet<int>(scale.Intervals.Select(i => (i + rootPc) % 12));

            positionNotes = Theory.PositionNotes(scale, rootPc, tuning, position, caged);

            HashSet<long> inPosition = new HashSet<long>(positionNotes.Select(n => NoteKey(n.String, n.Fret)));

            board.Children.Clear();
            noteShapes.Clear();
            noteFills.Clear();
            highlightedKey = -1;

            double width = FretX(Theory.NumFrets) + 16;
            double height = (TopMargin * 2) + (StringSpacing * (numStrings - 1)) + 22;

            board.Width = width;
            board.Height = height;

            double boardTop = TopMargin - 12;
            double boardBottom = StringY(0, numStrings) + 12;
            double midY = (boardTop + boardBottom) / 2;

            // wood
            AddRect(OpenWidth, boardTop, FretX(Theory.NumFrets) - OpenWidth, boardBottom - boardTop, WoodBrush, 3, WoodEdgeBrush);

            // inlays
            foreach (int f in new[] { 3, 5, 7, 9, 15, 17, 19, 21 })
                AddDot(NoteX(f), midY, 6, InlayBrush, 0.85);

            foreach (int f in new[] { 12, 24 })
            {
                double d = (boardBottom - boardTop) / 4;

                AddDot(NoteX(f), midY - d, 6, InlayBrush, 0.85);
                AddDot(NoteX(f), midY + d, 6, InlayBrush, 0.85);
            }

            // frets and nut
            for (int f = 1; f <= Theory.NumFrets; f++)
                AddRect(FretX(f) - 1.5, boardTop, 3, boardBottom - boardTop, FretBrush);

            AddRect(OpenWidth - 5, boardTop - 2, 7, boardBottom - boardTop + 4, NutBrush, 2);

            // strings (lower strings thicker)
            for (int s = 0; s < numStrings; s++)
            {
                double thick = 1 + ((1 - ((double)s / Math.Max(1, numStrings - 1))) * 1.6);

                AddRect(OpenWidth - 4, StringY(s, numStrings) - (thick / 2), FretX(Theory.NumFrets) - OpenWidth + 4, thick, StringBrush);
            }

            // fret numbers
            foreach (int f in new[] { 1, 3, 5, 7, 9, 12, 15, 17, 19, 21, 24 })
                AddText(f.ToString(), NoteX(f), height - 12, MutedBrush, 12, FontWeights.SemiBold);

            // notes
            for (int s = 0; s < numStrings; s++)
            {
                for (int f = 0; f <= Theory.NumFrets; f++)
                {
                    int midi = tuning[s] + f;
                    int pc = midi % 12;

                    if (!pitchClasses.Contains(pc))
                        continue;

                    bool inPos = (position < 0) || inPosition.Contains(NoteKey(s, f));

                    if (!inPos && !ghost)
                        continue;

                    bool isRoot = pc == rootPc;
                    int semitones = ((pc - rootPc) % 12 + 12) % 12;

                    string label = "";

                    if (labels == 0)
                        label = Theory.NoteName(pc, rootPc, scale);
                    else if (labels == 1)
                        label = Theory.IntervalName(semitones, scale);

                    double cx = NoteX(f);
                    double cy = StringY(s, numStrings);
                    double opacity = inPos ? 1.0 : 0.28;

                    Brush fill = isRoot ? RootBrush : NoteBrush;

                    Ellipse e = new Ellipse
                    {
                        Width = NoteRadius * 2,
                        Height = NoteRadius * 2,
                        Fill = fill,
                        Stroke = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)),
                        StrokeThickness = 1,
                        Opacity = opacity,
                        IsHitTestVisible = false
                    };

                    Canvas.SetLeft(e, cx - NoteRadius);
                    Canvas.SetTop(e, cy - NoteRadius);
                    board.Children.Add(e);

                    long key = NoteKey(s, f);
                    noteShapes[key] = e;
                    noteFills[key] = fill;

                    if (label.Length > 0)
                    {
                        TextBlock t = new TextBlock
                        {
                            Text = label,
                            Foreground = isRoot ? RootInkBrush : NoteInkBrush,
                            FontSize = 12,
                            FontWeight = FontWeights.Bold,
                            Width = 30,
                            TextAlignment = TextAlignment.Center,
                            Opacity = opacity,
                            IsHitTestVisible = false
                        };

                        Canvas.SetLeft(t, cx - 15);
                        Canvas.SetTop(t, cy - 8.5);
                        board.Children.Add(t);
                    }
                }
            }

            // info lines
            notesText.Text = "Notas:  " + string.Join("   ", scale.Intervals.Select(i => Theory.NoteName(i + rootPc, rootPc, scale)));
            formulaText.Text = "Fórmula:  " + string.Join("   ", scale.Intervals.Select(i => Theory.IntervalName(i, scale)));

            if (position < 0)
            {
                positionText.Text = "Todo o braço. Escolhe uma posição para ver o padrão (e para \"Tocar posição\").";
            }
            else if (positionNotes.Count > 0)
            {
                int lo = positionNotes.Min(n => n.Fret);
                int hi = positionNotes.Max(n => n.Fret);
                FretNote first = positionNotes.OrderBy(n => n.Midi).First();

                positionText.Text = "Trastes " + lo + " a " + hi + ", começa em " + Theory.NoteName(first.Midi, rootPc, scale) + " na corda mais grave.";

                // bring the position into view
                double target = Math.Max(0, FretX(Math.Max(0, lo - 2)) - 20);

                if ((target < boardScroll.HorizontalOffset) || (target > boardScroll.HorizontalOffset + boardScroll.ViewportWidth - 200))
                    boardScroll.ScrollToHorizontalOffset(target);
            }

            shownStep = -2;
            shownMidi = -2;
            UpdateLive();
        }

        // ------------------------------------------------------------------ chord targets and note detection

        void UpdateTracker()
        {
            if (detectCheck.IsChecked == true)
                plugin.Tracker.Start();
            else
                plugin.Tracker.Stop();
        }

        void PresetChosen()
        {
            if (loading || (presetBox.SelectedIndex <= 0))
                return;

            var preset = ProgressionPresets.Presets[presetBox.SelectedIndex - 1];

            progressionBox.Text = ProgressionPresets.Build(preset, Math.Max(0, keyBox.SelectedIndex), Theory.Scales[Math.Max(0, scaleBox.SelectedIndex)]);

            chordCheck.IsChecked = true;

            // back to the prompt, so the same preset can be chosen again after changing key
            loading = true;
            presetBox.SelectedIndex = 0;
            loading = false;
        }

        void ProgressionChanged()
        {
            if (loading)
                return;

            plugin.Settings.Progression = progressionBox.Text;

            progression = ChordProgression.Parse(progressionBox.Text);
            manualStep = 0;
            shownStep = -2;

            UpdateProgressionError();
        }

        void UpdateProgressionError()
        {
            progressionError.Text = (progression.Errors.Count > 0)
                ? "Acordes que não percebi: " + string.Join(", ", progression.Errors) + "  (exemplos válidos: Em, C#m7, Bb, D5, Gsus4, F#m7b5, Am:2)"
                : "";
        }

        void StepChord(int delta)
        {
            if (progression.Steps.Count == 0)
                return;

            manualStep = ((manualStep + delta) % progression.Steps.Count + progression.Steps.Count) % progression.Steps.Count;
            shownStep = -2;
        }

        /// <summary>Which chord of the progression is sounding now: from the DAW position while it plays, otherwise the one chosen with the arrows.</summary>
        int CurrentStep(out bool beforeStart)
        {
            beforeStart = false;

            if ((chordCheck.IsChecked != true) || (progression.Steps.Count == 0))
                return -1;

            if (!plugin.HostPlaying)
                return Math.Min(manualStep, progression.Steps.Count - 1);

            int beatsPerBar = beatsBox.SelectedIndex + 2;
            int startBar = startBarBox.SelectedIndex + 1;

            int bar = (int)Math.Floor(plugin.HostBeat / beatsPerBar) - (startBar - 1);

            if (bar < 0)
            {
                beforeStart = true;
                return 0;
            }

            return progression.StepAtBar(bar);
        }

        Chord CurrentChord()
        {
            int step = CurrentStep(out bool beforeStart);

            return ((step >= 0) && !beforeStart) ? progression.Steps[step].Chord : null;
        }

        /// <summary>Fret range used for the chord rings and to place the detected note: the selected position, or the whole neck.</summary>
        void PositionWindow(out int lo, out int hi)
        {
            if ((positionBox.SelectedIndex - 1 >= 0) && (positionNotes.Count > 0))
            {
                lo = Math.Max(0, positionNotes.Min(n => n.Fret) - 1);
                hi = Math.Min(Theory.NumFrets, positionNotes.Max(n => n.Fret) + 1);
            }
            else
            {
                lo = 0;
                hi = Theory.NumFrets;
            }
        }

        void Ring(double cx, double cy, double radius, Brush stroke, double thickness)
        {
            Ellipse e = new Ellipse { Width = radius * 2, Height = radius * 2, Stroke = stroke, StrokeThickness = thickness };

            Canvas.SetLeft(e, cx - radius);
            Canvas.SetTop(e, cy - radius);
            overlay.Children.Add(e);
        }

        void Dot(double cx, double cy, double radius, Brush fill, string label, Brush labelBrush)
        {
            Ellipse e = new Ellipse { Width = radius * 2, Height = radius * 2, Fill = fill, Stroke = Brushes.White, StrokeThickness = 2 };

            Canvas.SetLeft(e, cx - radius);
            Canvas.SetTop(e, cy - radius);
            overlay.Children.Add(e);

            if (!string.IsNullOrEmpty(label))
            {
                TextBlock t = new TextBlock { Text = label, Foreground = labelBrush, FontSize = 12, FontWeight = FontWeights.Bold, Width = 30, TextAlignment = TextAlignment.Center };

                Canvas.SetLeft(t, cx - 15);
                Canvas.SetTop(t, cy - 8.5);
                overlay.Children.Add(t);
            }
        }

        void RenderOverlay(int step, int midi)
        {
            overlay.Children.Clear();

            overlay.Width = board.Width;
            overlay.Height = board.Height;

            int rootPc = Math.Max(0, keyBox.SelectedIndex);
            Scale scale = Theory.Scales[Math.Max(0, scaleBox.SelectedIndex)];
            HashSet<int> scalePcs = new HashSet<int>(scale.Intervals.Select(i => (i + rootPc) % 12));

            int[] tuning = currentTuning;
            int numStrings = tuning.Length;

            PositionWindow(out int lo, out int hi);

            Chord chord = CurrentChord();

            // chord tones: orange ring on the chord root, teal ring on the other chord tones
            if (chord != null)
            {
                HashSet<int> chordPcs = new HashSet<int>(chord.PitchClasses);

                for (int s = 0; s < numStrings; s++)
                {
                    for (int f = lo; f <= hi; f++)
                    {
                        int pc = (tuning[s] + f) % 12;

                        if (!chordPcs.Contains(pc))
                            continue;

                        bool isChordRoot = pc == chord.RootPc;
                        Brush brush = isChordRoot ? ChordRootBrush : ChordToneBrush;

                        double cx = NoteX(f);
                        double cy = StringY(s, numStrings);

                        // a chord tone outside the scale (e.g. the major 3rd of V7 in a minor key) has no dot underneath
                        if (!scalePcs.Contains(pc))
                            Dot(cx, cy, 8, brush, null, null);

                        Ring(cx, cy, NoteRadius + 3.5, brush, isChordRoot ? 3.5 : 2.5);
                    }
                }
            }

            // the note being played: green if it is in the scale, red if not
            if (midi >= 0)
            {
                List<(int s, int f)> places = new List<(int, int)>();

                for (int s = 0; s < numStrings; s++)
                {
                    int f = midi - tuning[s];

                    if ((f >= 0) && (f <= Theory.NumFrets))
                        places.Add((s, f));
                }

                List<(int s, int f)> inWindow = places.Where(p => (p.f >= lo) && (p.f <= hi)).ToList();

                if ((inWindow.Count == 0) && (places.Count > 0))
                {
                    double center = (lo + hi) / 2.0;

                    inWindow.Add(places.OrderBy(p => Math.Abs(p.f - center)).First());
                }

                bool inScale = scalePcs.Contains(midi % 12);
                string name = Theory.NoteName(midi, rootPc, scale);

                foreach (var p in inWindow)
                    Dot(NoteX(p.f), StringY(p.s, numStrings), NoteRadius + 2, inScale ? GoodBrush : BadBrush, name, Brushes.Black);
            }
        }

        void UpdateStatsText()
        {
            string scalePart = (statTotal > 0)
                ? "Na escala: " + (100 * statInScale / statTotal) + "%  (" + statInScale + "/" + statTotal + ")"
                : "Na escala: -";

            string chordPart = (statChordTotal > 0)
                ? "    Notas do acorde: " + (100 * statChordHits / statChordTotal) + "%  (" + statChordHits + "/" + statChordTotal + ")"
                : "";

            statsText.Text = scalePart + chordPart;
        }

        /// <summary>Called by the timer: follow the DAW, the detected note and the score.</summary>
        void UpdateLive()
        {
            if (overlay == null)
                return;

            int rootPc = Math.Max(0, keyBox.SelectedIndex);
            Scale scale = Theory.Scales[Math.Max(0, scaleBox.SelectedIndex)];

            int step = CurrentStep(out bool beforeStart);

            bool detecting = detectCheck.IsChecked == true;
            int midi = detecting ? plugin.Tracker.CurrentMidi : -1;

            // score each new note
            while (plugin.Tracker.Onsets.TryDequeue(out int onset))
            {
                if (!detecting)
                    continue;

                int pc = onset % 12;

                statTotal++;

                if (scale.Intervals.Any(i => (i + rootPc) % 12 == pc))
                    statInScale++;

                Chord chordNow = CurrentChord();

                if (chordNow != null)
                {
                    statChordTotal++;

                    if (chordNow.PitchClasses.Contains(pc))
                        statChordHits++;
                }

                UpdateStatsText();
            }

            // input level meter
            levelBar.Width = detecting ? Math.Min(120, 120 * Math.Sqrt(Math.Min(1, plugin.Tracker.Level * 8))) : 0;

            int stepKey = beforeStart ? -100 - step : step;

            if ((stepKey == shownStep) && (midi == shownMidi))
                return;

            shownStep = stepKey;
            shownMidi = midi;

            // chord readout
            if (step < 0)
            {
                chordNowText.Text = "";
                chordNextText.Text = (chordCheck.IsChecked == true) ? "Escreve ou escolhe uma progressão" : "";
            }
            else if (beforeStart)
            {
                chordNowText.Text = "";
                chordNextText.Text = "Começa no compasso " + (startBarBox.SelectedIndex + 1) + " com " + progression.Steps[0].Chord.Name;
            }
            else
            {
                chordNowText.Text = progression.Steps[step].Chord.Name;
                chordNextText.Text = "a seguir: " + progression.Steps[(step + 1) % progression.Steps.Count].Chord.Name;
            }

            // note readout
            if (midi < 0)
            {
                playingText.Text = "";
            }
            else
            {
                int pc = midi % 12;
                bool inScale = scale.Intervals.Any(i => (i + rootPc) % 12 == pc);
                int semis = ((pc - rootPc) % 12 + 12) % 12;

                string text = Theory.NoteName(midi, rootPc, scale) + "  (" + Theory.IntervalName(semis, scale) + ")";

                Chord chord = CurrentChord();

                if ((chord != null) && chord.PitchClasses.Contains(pc))
                    text += "  nota do acorde";
                else if (!inScale)
                    text += "  fora da escala";

                playingText.Text = text;
                playingText.Foreground = inScale ? GoodBrush : BadBrush;
            }

            RenderOverlay(step, midi);
        }

        // ------------------------------------------------------------------ interaction

        void Board_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Point p = e.GetPosition(board);

            int[] tuning = currentTuning;
            int numStrings = tuning.Length;

            int row = (int)Math.Round((p.Y - TopMargin) / StringSpacing);

            if ((row < 0) || (row >= numStrings))
                return;

            int str = numStrings - 1 - row;

            int fret = -1;

            if (p.X < OpenWidth - 5)
            {
                fret = 0;
            }
            else
            {
                for (int f = 1; f <= Theory.NumFrets; f++)
                {
                    if (p.X <= FretX(f))
                    {
                        fret = f;
                        break;
                    }
                }
            }

            if (fret < 0)
                return;

            plugin.Synth.Trigger(tuning[str] + fret);
        }

        void TogglePlay()
        {
            if (plugin.Synth.IsSequencePlaying)
            {
                plugin.Synth.StopSequence();
                playButton.Content = "Tocar posição";
                return;
            }

            if (positionNotes.Count == 0)
                return;

            playSequence = Theory.PlaySequence(positionNotes);

            plugin.Synth.StartSequence(playSequence.Select(n => n.Midi).ToArray(), loopCheck.IsChecked == true);

            playButton.Content = "Parar";
        }

        void Timer_Tick(object sender, EventArgs e)
        {
            int step = plugin.Synth.CurrentStep;

            long key = -1;

            if ((step >= 0) && (step < playSequence.Count))
                key = NoteKey(playSequence[step].String, playSequence[step].Fret);

            if (key != highlightedKey)
            {
                if ((highlightedKey >= 0) && noteShapes.TryGetValue(highlightedKey, out Ellipse old))
                    old.Fill = noteFills[highlightedKey];

                if ((key >= 0) && noteShapes.TryGetValue(key, out Ellipse current))
                    current.Fill = PlayBrush;

                highlightedKey = key;
            }

            if (!plugin.Synth.IsSequencePlaying && ((string)playButton.Content != "Tocar posição"))
                playButton.Content = "Tocar posição";

            UpdateLive();
        }
    }
}

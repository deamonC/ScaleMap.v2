# Scale Map

VST3 plugin (Windows) to practise scales over backing tracks inside the DAW.

- Fretboard with 16 scales, 12 keys, 7 tunings (incl. 7-string and 4-string bass)
- Positions: 3 notes per string or CAGED; labels as note names, intervals or dots
- Click a note to hear it; "Tocar posição" plays the pattern up and down (own BPM or the DAW's)
- Note detection: the note you play lights up green (in the scale) or red (outside), with a running score
- Chord targets: type a progression (or pick a common one) and the chord tones of the chord playing now are ringed on the neck, following the DAW's position
- The whole interface scales with the window; window size presets in the editor
- Audio input is passed through untouched

Built with [AudioPlugSharp](https://github.com/mikeoliphant/AudioPlugSharp).

## Install
1. Download the `ScaleMapVST3Plugin` artifact from the latest successful build (Actions tab).
2. Extract it into `C:\Program Files\Common Files\VST3\ScaleMap`.
3. Rescan plugins in the DAW. Requires the .NET 10 Desktop Runtime.

## Tips
- Put Scale Map first on the guitar track (before the amp sim): note detection needs the clean signal.
- Play one note at a time and let previous notes stop ringing for the most reliable detection.

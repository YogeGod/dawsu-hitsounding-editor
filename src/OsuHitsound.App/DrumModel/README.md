# Drum detection model

`adtof-frame-rnn.onnx` is the **ADTOF Frame_RNN** drum transcription model by Mickaël Zehren, Marco Alunno and Paolo Bientinesi, converted to ONNX for dawsu!.

- Source: https://github.com/MZehren/ADTOF, commit `b3968fb332f69b65ee07c089fc62f436503755db`, file `adtof/models/Frame_RNN_adtofAll_0` (TensorFlow checkpoint).
- Licence: [Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International](https://creativecommons.org/licenses/by-nc-sa/4.0/) (CC BY-NC-SA 4.0). This ONNX file is an adaptation of that model and is shared under the same licence. It may not be used commercially.
- Papers:
  - M. Zehren, M. Alunno and P. Bientinesi, "ADTOF: A large dataset of non-synthetic music for automatic drum transcription", ISMIR 2021, pp. 818–824.
  - M. Zehren, M. Alunno and P. Bientinesi, "High-Quality and Reproducible Automatic Drum Transcription from Crowdsourced Data", Signals 2023, 4, 768–787. https://doi.org/10.3390/signals4040042

## What changed in the conversion

- The trained weights are unchanged. They were read from the official checkpoint and placed in the same network: four 3×3 convolutions with batch normalisation, two (1, 3) max pools, three bidirectional GRUs with 60 units, and a sigmoid layer with 5 outputs (BD, SD, TT, HH, CY+RD).
- The graph also contains ADTOF's input features: madmom's logarithmic filterbank (12 bands per octave, 20–20,000 Hz, normalised filters, 84 bands) and `log10(1 + x)`. The model's input is a magnitude spectrogram, shape `(1, frames, 1024)`. The input is framed at 100 fps from 44.1 kHz audio, with 2048-sample Hann windows. `DrumDetection.cs` computes it.
- dawsu! uses only the BD (kick) and SD (snare) outputs, with ADTOF's Frame_RNN peak-picking thresholds (0.22 and 0.24).

## Checks made during the conversion (2026-11-12)

- ONNX Runtime output against the same network built in TensorFlow from the checkpoint: largest difference 2×10⁻⁷.
- The filterbank against madmom's own `LogarithmicFilteredSpectrogramProcessor` on a song: largest difference 2×10⁻⁷.
- Kick and snare onsets from `DrumDetection.cs` against madmom's features and peak picking on the same audio. See `docs/drum-auto-fill.md` for the numbers.

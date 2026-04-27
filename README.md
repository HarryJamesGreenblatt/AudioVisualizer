# Audio Visualizer

A real-time audio spectrum visualizer for Windows built with WPF and .NET 9. Captures system audio output via WASAPI loopback and renders a mel-scale frequency bar visualization that responds to any audio playing through the default output device — Spotify, browsers, games, etc.

![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4) ![WPF](https://img.shields.io/badge/UI-WPF-178600) ![License](https://img.shields.io/badge/license-MIT-blue)

## Features

- **WASAPI Loopback Capture** — taps into the default audio output device with zero configuration
- **Mel-Scale FFT** — 64-band spectrum mapped to human-perceived frequency spacing via NWaves
- **Accent-Aware Theming** — bar gradient automatically matches your Windows accent color
- **Peak Hold with Gravity** — floating peak indicators rise instantly and fall with simulated gravity
- **Tear-Free Rendering** — lock-free double-buffered pipeline between the audio capture thread and WPF compositor
- **Low Latency** — 1024-sample FFT window (~21ms) with fast-attack smoothing for tight transient response

## Architecture

```
AudioVisualizer/
├── Services/
│   └── AudioCaptureService.cs    # WASAPI loopback capture via NAudio
├── Processing/
│   └── FftProcessor.cs           # Mel-scale FFT + per-band gain compensation
├── VisualizerElement.cs          # Custom WPF element: bar + peak rendering
├── MainWindow.xaml/.cs           # App shell, double-buffer bridge
└── App.xaml/.cs                  # Application entry point
```

### Data Pipeline

```
┌─────────────────┐     float[]      ┌────────────────┐    Interlocked     ┌───────────────────┐
│ WASAPI Loopback │ ──────────────►  │  FftProcessor  │ ────Exchange────►  │  WPF Compositor   │
│ (capture thread)│   PCM samples    │  mel bands +   │   double-buffer    │  VisualizerElement│
│                 │                  │  gain + sqrt   │                    │  bars + peaks     │
└─────────────────┘                  └────────────────┘                    └───────────────────┘
```

1. **AudioCaptureService** fires `AudioDataAvailable` on the WASAPI capture thread (~every 10ms)
2. **FftProcessor** windows the signal, runs a 1024-point FFT, maps bins to 64 mel bands, applies a treble gain curve and square-root compression
3. **MainWindow** writes the result to a back buffer and atomically publishes it via `Interlocked.Exchange`
4. **WPF CompositionTarget.Rendering** (~60fps) grabs the latest complete frame and pushes it to the visualizer
5. **VisualizerElement** draws gradient bars and peak-hold indicators with gravity physics

## Dependencies

| Package | Version | Purpose |
|---|---|---|
| [NAudio](https://github.com/naudio/NAudio) | 2.3.0 | WASAPI loopback audio capture |
| [NWaves](https://github.com/ar1st0crat/NWaves) | 0.9.6 | FFT engine and window functions |

## Getting Started

### Prerequisites

- Windows 10/11
- .NET 9 SDK
- A default audio output device configured in Windows Sound settings

### Build & Run

```bash
git clone https://github.com/HarryJamesGreenblatt/AudioVisualizer.git
cd AudioVisualizer
dotnet build
dotnet run --project AudioVisualizer
```

Play audio through your default output device, then click **Start**.

## License

MIT

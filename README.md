# Windows WavBall

![Cover](https://github.com/HarryJamesGreenblatt/WavBall/blob/main/WavBall/Assets/Images/Cover.png?raw=true)

A real-time audio spectrum visualizer and physics game for Windows built with WPF and .NET 9. Captures system audio output via WASAPI loopback and renders a mel-scale frequency bar visualization that responds to any audio playing through the default output device — Spotify, browsers, games, etc. Toggle on **Rain**, a **Ball**, or full **Game Mode** to layer interactive physics on top of the spectrum.

![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4) ![WPF](https://img.shields.io/badge/UI-WPF-178600) ![License](https://img.shields.io/badge/license-MIT-blue)

## Features

- **WASAPI Loopback Capture** — taps into the default audio output device with zero configuration
- **Mel-Scale FFT** — 64-band spectrum mapped to human-perceived frequency spacing via NWaves
- **Accent-Aware Theming** — bar gradient automatically matches your Windows accent color
- **Peak Hold with Gravity** — floating peak indicators rise instantly and fall with simulated gravity
- **Tear-Free Rendering** — lock-free double-buffered pipeline between the audio capture thread and WPF compositor
- **Low Latency** — 1024-sample FFT window (~21ms) with fast-attack smoothing for tight transient response
- **Interactive Ball Physics** — 7 unique ball types (beach ball → bowling ball) with Newtonian physics, drag-and-throw interaction, and collision against the live spectrum surface
- **Audio-Driven Rain** — variable-size raindrops with parallax depth, bass-driven wind, snare-burst spawning, and motion-blur trail rendering
- **Game Mode** — stage-based progression: guide each ball into a golden goal ring using the music itself as the playing field, with anti-cheat and particle burst scoring effects
- **Per-Band Thermal Luminosity** — bars glow brighter with sustained activity; treble bands charge faster

## Architecture

```
WavBall/
├── Components/
│   ├── Input.cs              # Mouse drag interaction
│   ├── Physics.cs            # Newtonian physics (ball, peak, particle)
│   ├── Reactivity.cs         # Audio-reactive behavior (bars, rain emitter)
│   └── Rendering.cs          # Visual rendering (bars, peaks, ball, particles, goal)
├── Configuration/
│   └── BallPreset.cs         # 7-stage ball catalog (kind, mass, COR, drag)
├── Entities/
│   ├── World.cs              # Base entity: position, velocity, component slots
│   ├── Ball.cs               # Draggable physics ball (7 types)
│   ├── Bar.cs                # Audio-reactive spectrum bars
│   ├── Goal.cs               # Golden ring target for game mode
│   ├── Peak.cs               # Gravity-driven peak indicators
│   ├── Rain.cs               # Rain emitter (spawns into particle pool)
│   └── ParticlePool.cs       # Object-pooled particles (rain, sparks)
├── Events/
│   ├── EventQueue.cs         # Lock-free MPSC queue
│   └── TransientEvent.cs     # Cross-thread event types
├── Models/
│   ├── CollisionInfo.cs      # Collision event data
│   └── MouseState.cs         # Input state snapshot
├── Services/
│   ├── AudioCaptureService.cs    # WASAPI loopback capture via NAudio
│   └── FftProcessingService.cs   # Mel-scale FFT + per-band gain compensation
├── Scene.cs                  # Game loop: fixed-timestep physics + render
├── VisualizerElement.cs      # WPF host: entity management, layer toggles
├── MainWindow.xaml/.cs       # App shell, double-buffer bridge
└── App.xaml/.cs              # Application entry point
```

### Data Pipeline

```
┌─────────────────┐     float[]      ┌─────────────────────┐  Interlocked   ┌───────────────────┐
│ WASAPI Loopback │ ──────────────►  │ FftProcessingService│ ───Exchange──►  │  WPF Compositor   │
│ (capture thread)│   PCM samples    │  mel bands + gain   │  double-buffer  │  VisualizerElement│
│                 │                  │  + sqrt compression │                 │  bars + peaks +   │
└─────────────────┘                  └─────────────────────┘                 │  ball + rain      │
                                                                            └───────────────────┘
```

1. **AudioCaptureService** fires `AudioDataAvailable` on the WASAPI capture thread (~every 10ms)
2. **FftProcessingService** windows the signal, runs a 1024-point FFT, maps bins to 64 mel bands, applies a treble gain curve and square-root compression
3. **MainWindow** writes the result to a back buffer and atomically publishes it via `Interlocked.Exchange`
4. **WPF CompositionTarget.Rendering** (~60fps) grabs the latest complete frame and pushes it to the visualizer
5. **VisualizerElement** runs a 120Hz fixed-timestep game loop — bars react to audio, physics entities collide against the live spectrum surface, and everything renders each frame

## Dependencies

| Package | Version | Purpose |
|---|---|---|
| [NAudio](https://github.com/naudio/NAudio) | 2.3.0 | WASAPI loopback audio capture |
| [NWaves](https://github.com/ar1st0crat/NWaves) | 0.9.6 | FFT engine and window functions |
| [WPF-UI](https://github.com/lepoco/wpfui) | 4.3.0 | Fluent dark theme and toggle controls |

## Getting Started

### Prerequisites

- Windows 10/11
- .NET 9 SDK
- A default audio output device configured in Windows Sound settings

### Build & Run

```bash
git clone https://github.com/HarryJamesGreenblatt/WavBall.git
cd WavBall
dotnet build
dotnet run --project WavBall
```

Play audio through your default output device, then click **Start**.

## License

MIT

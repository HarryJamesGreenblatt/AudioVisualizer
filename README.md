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
- **Autonomous Goal Agent** — the goal is a "mosquito" that hunts for musical energy: a two-dimensional appetite machine (Charge sensor + Satiety integrator) cycles between Feeding (proximity-weighted loudest band) and Sated (retreats to a randomized altitude above the action via anti-centroid X), so it migrates across the playing field on a polyrhythmic schedule rather than locking to one peak
- **Dual-Layer Goal Visual** — a cool cyan halo that grows with appetite-cycle state and pulses with bass kicks, around a warm gold ring that deforms into an oscilloscope-style squiggle on snare hits (high-frequency sinusoidal radial perturbation with exponential ring-out) — geometric shape encodes transient energy while brightness encodes collidability
- **Per-Band Thermal Luminosity** — bars glow brighter with sustained activity; treble bands charge faster
- **WMP9-Inspired Shell** — three-row Windows Media Player 9 ("Corona") chrome, with layout and sprite positions derived from the original `Corona.wms` skin layout spec. Blue metallic chrome frame (gradient sampled from `equalizer_middle.png`) around the visualizer with metallic bevel separators. Transport chrome panel uses the original `equalizer_left/middle/right` bitmaps as background, with the full `transports.png` atlas (136×30) rendered as a single image — per-button hover/down via clipped overlays. Pause is a 30×30 overlay at the play position. Rain toggle wired to the `>>` button with glow effect; mic toggle has a custom icon overlay. Palette sampled from the 2002 Microsoft sprite atlases.
- **Side Panel** — WMP9-style info panel: ball sprite icon (from game assets), physics stats (mass, restitution, radius in real-world units), LED round timer, and a pre-populated round history list (all 7 ball types visible from the start, "-" for uncompleted times, current ball highlighted amber). Now Playing section shows album art + track metadata from the active media session.
- **Goal Status HUD** — green LED strip below the visualizer shows real-time goal state: left indicator (◎ DISCHARGED / ◎ ARMED with gold blink) tracks collidability, right indicator (⚡ CHARGING / ⚡ OVERCHARGED with red blink) tracks the goal's feeding/sated appetite cycle.
- **System Volume Control** — master volume slider wired to the default audio endpoint via NAudio `MMDeviceEnumerator`; syncs bidirectionally with external changes (taskbar, hardware keys).
- **System Media-Key Integration** — the ◀ / ▶ buttons send `VK_MEDIA_PREV_TRACK` / `VK_MEDIA_NEXT_TRACK` globally, so they skip tracks in whatever app is currently playing (Spotify, browsers, Groove, foobar, etc.) without requiring focus or per-app integration.

## Architecture

```
WavBall/
├── Components/
│   ├── Input.cs              # Mouse drag interaction
│   ├── Physics.cs            # Newtonian physics (ball, peak, particle, goal trigger)
│   ├── Reactivity.cs         # Audio-reactive behavior (bars w/ Energy/SnareFlux/BassFlux/BandHeat, rain emitter)
│   ├── Rendering.cs          # Visual rendering (bars, peaks, ball, particles, two-layer goal)
│   ├── Steering.cs           # Autonomous-agent motion (goal two-dimensional appetite machine)
│   └── Charge.cs             # Spatial-audio sensor (Gaussian-KDE potential + asymmetric capacitor)
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
│   ├── MouseState.cs         # Input state snapshot
│   └── RoundRecord.cs        # Completed-round data (ball kind, time, PB flag)
├── Services/
│   ├── AudioCaptureService.cs    # WASAPI loopback capture via NAudio
│   ├── FftProcessingService.cs   # Mel-scale FFT + per-band gain compensation
│   ├── MediaKeyService.cs        # P/Invoke wrapper for VK_MEDIA_* virtual keys
│   ├── RoundHistoryStore.cs      # 1:1 per-kind history, PB tracking, ObservableCollection
│   ├── RoundTimerService.cs      # 4-state stopwatch (Idle/Running/Paused/Stopped)
│   └── SystemVolumeService.cs    # NAudio MMDevice master volume read/write + external change events
├── Themes/
│   └── Wmp9.xaml                 # WMP9 "Corona" palette + button styles + wedge volume slider
├── Assets/
│   └── Sprites/                  # Alpha-keyed PNGs from Corona skin BMPs (17 files)
│       ├── transports*.png       # Full 136×30 atlas + hover/down/pause overlays
│       ├── equalizer_*.png       # Transport chrome panel (left/middle/right)
│       ├── bottom_*.png          # Metadata strip with curved corners
│       ├── player_left/right.png # Inner frame rails (tiled vertically)
│       └── volume_*.png          # Volume slider sprites
├── Scripts/
│   └── Slice-WmpSprites.ps1     # BMP → magenta-to-alpha PNG converter
├── Scene.cs                  # Game loop: fixed-timestep physics + render
├── VisualizerElement.cs      # WPF host: entity management, layer toggles
├── MainWindow.xaml/.cs       # WMP9 shell (4-row Grid: chrome / visualizer+panel / metadata / transport)
└── App.xaml/.cs              # Application entry point (merges Wmp9.xaml resources)
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

Play audio through your default output device, then click the play button (the glossy blue bead with the amber triangle in the transport panel).

## License

MIT

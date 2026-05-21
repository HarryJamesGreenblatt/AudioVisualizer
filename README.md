# Windows WavBall

![Cover](https://github.com/HarryJamesGreenblatt/WavBall/blob/main/WavBall/Assets/Images/Cover.png?raw=true)

A real-time audio visualizer and physics game for Windows. WavBall captures whatever audio is playing on your PC — Spotify, YouTube, games, anything — and turns it into a playable spectrum. Bounce balls off the music, chase a goal that hunts for the loudest frequencies, and progress through 23 increasingly difficult stages.

Built with WPF and .NET 9. Styled after the classic Windows Media Player 9 "Corona" skin.

![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4) ![WPF](https://img.shields.io/badge/UI-WPF-178600) ![License](https://img.shields.io/badge/license-MIT-blue)

## How It Works

WavBall listens to your default audio output via WASAPI loopback — no mic, no configuration. The spectrum bars react to whatever's playing, and everything else builds on top of that:

- **23 ball types** — from Beach Ball to Cannonball, each with real-world-inspired physics (mass, bounce, drag). The music is the playing field.
- **An autonomous goal** — a golden ring that migrates across the spectrum, feeding on loud frequencies and retreating when full. Guide each ball into the goal to advance.
- **Audio-reactive rain** — bass drives the wind, snare bursts spawn drops, parallax layers add depth.
- **WMP9 shell** — transport controls, side panel with ball stats and round history, system volume and media key integration. Chrome and sprites derived from the original 2002 skin assets.

## Installation

Download the latest **WavBall-Installer.zip** from the [Releases](https://github.com/HarryJamesGreenblatt/WavBall/releases) page.

1. Extract the ZIP
2. Double-click **Setup.cmd**
3. Accept the UAC prompt (one-time certificate trust)
4. Click **Install** in the WavBall Setup dialog
5. Find **WavBall** in your Start Menu

> A portable single-file exe is also available as **WavBall-win-x64.zip** — extract and run, no installation needed.

## Building from Source

```bash
git clone https://github.com/HarryJamesGreenblatt/WavBall.git
cd WavBall
dotnet build
dotnet run --project WavBall
```

Requires Windows 10/11, .NET 9 SDK, and a default audio output device.

## Dependencies

| Package | Version | Purpose |
|---|---|---|
| [NAudio](https://github.com/naudio/NAudio) | 2.3.0 | WASAPI loopback audio capture |
| [NWaves](https://github.com/ar1st0crat/NWaves) | 0.9.6 | FFT and mel-scale frequency mapping |
| [WPF-UI](https://github.com/lepoco/wpfui) | 4.3.0 | Fluent dark theme |

## License

MIT

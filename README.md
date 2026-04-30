# Unity HandBrake Video Optimizer

![Unity 2021 LTS+](https://img.shields.io/badge/Unity-2021%20LTS%2B-black?logo=unity)
![Editor Tool](https://img.shields.io/badge/type-Unity%20Editor%20tool-blue)
![HandBrakeCLI](https://img.shields.io/badge/encoder-HandBrakeCLI-orange)
![License: MIT](https://img.shields.io/badge/license-MIT-green)

**Unity HandBrake Video Optimizer** is a Unity Editor extension for compressing and optimizing project video assets with **HandBrakeCLI**. It helps Unity developers reduce MP4, MOV, MKV, AVI, and WEBM file sizes while keeping visual quality, source timing, and editor workflow intact.

The tool runs from **Tools > Video Optimizer** and is designed for game projects, XR projects, mobile builds, WebGL/media-heavy scenes, training simulations, and Unity projects with large video libraries.

## Features

- Scan all supported video files under the Unity `Assets` folder.
- Batch optimize selected videos from a dedicated Unity Editor window.
- Use external **HandBrakeCLI** through `ProcessStartInfo`.
- Choose presets: **Balanced**, **High Quality**, **Small Size**, and **Mobile/Web**.
- Choose video codec: **H.264/x264** or **H.265/x265**.
- Use constant quality RF/CRF encoding for predictable quality.
- Preserve source frame timing with HandBrake `--vfr`.
- Select AAC audio bitrate, including 128k, 160k, and higher values.
- Write optimized copies with `_optimized` suffix to avoid name conflicts.
- Safely overwrite original MP4 files through a temporary encode file.
- Keep Unity asset references when overwriting the original MP4 asset.
- Show progress, stdout/stderr logs, failed files, old size, new size, and percentage savings.
- Support cancellation through `CancellationToken`.
- Refresh Unity assets after processing with `AssetDatabase.Refresh()`.
- Editor-only implementation under `Assets/Editor`, so it is not included in runtime builds.

## Supported Formats

Input files scanned under `Assets`:

- `.mp4`
- `.mov`
- `.mkv`
- `.avi`
- `.webm`

Optimized outputs are written as MP4 files for broad Unity, mobile, and web compatibility.

## Requirements

- Unity **2021 LTS or newer**
- Windows, macOS, or Linux Unity Editor
- External **HandBrakeCLI** installation

HandBrake is GPL-2.0 licensed software. This Unity plugin does **not** embed or redistribute HandBrake source code or binaries. It only calls a user-provided external `HandBrakeCLI` executable.

Expected executable names:

| Platform | Executable |
| --- | --- |
| Windows | `HandBrakeCLI.exe` |
| macOS | `HandBrakeCLI` |
| Linux | `HandBrakeCLI` |

## Installation

1. Install HandBrakeCLI on your machine.
   Official CLI documentation: <https://handbrake.fr/docs/en/latest/cli/cli-options.html>
2. Copy this folder into your Unity project:

   ```text
   Assets/Editor/VideoOptimizer/
   ```

3. Reopen or recompile Unity.
4. Open the tool from:

   ```text
   Tools > Video Optimizer
   ```

5. Set the `HandBrakeCLI` path manually or press **Auto Find**.

## Usage

1. Open **Tools > Video Optimizer**.
2. Press **Scan Project Videos**.
3. Select the videos you want to optimize.
4. Choose a preset and codec.
5. Adjust RF/CRF quality if needed.
6. Choose output folder or enable **Overwrite Original**.
7. Press **Optimize Selected**.

For transparent replacement of already assigned Unity video assets, enable **Overwrite Original**. The tool encodes to a temporary file first and replaces the original only after HandBrakeCLI succeeds.

## Recommended Quality Settings

| Goal | H.264 RF | H.265 RF | Audio |
| --- | ---: | ---: | ---: |
| High Quality | 20 | 22 | 160k |
| Balanced | 22 | 24 | 160k |
| Small Size | 23 | 26 | 128k |
| Mobile/Web | 23 | 25 | 128k |

Lower RF means higher quality and larger files. Higher RF means smaller files and lower quality.

## Unity Reference Safety

Unity asset references usually point to the GUID stored in the `.meta` file.

- **Overwrite Original ON for MP4:** existing references continue to point to the same asset because the `.meta` file is preserved.
- **Overwrite Original OFF:** the tool creates a new `_optimized.mp4` file with a new Unity asset GUID, so existing references are not changed automatically.
- **String URL paths:** if a project uses `VideoPlayer.url` or custom string paths, Unity GUID tracking does not apply.

## Example HandBrakeCLI Command

The generated command is equivalent to:

```bash
HandBrakeCLI -i input.mp4 -o output.mp4 -f av_mp4 -e x264 -q 22 --vfr --audio 1 -E av_aac -B 160 --optimize
```

## Project Structure

```text
Assets/Editor/VideoOptimizer/
  VideoOptimizerWindow.cs
  VideoOptimizerSettings.cs
  VideoFileInfo.cs
  HandBrakeRunner.cs
```

## SEO Keywords

Unity video optimizer, Unity video compression tool, Unity HandBrake plugin, Unity HandBrakeCLI integration, Unity MP4 compressor, Unity Editor video optimization, compress videos in Unity, Unity H.264 video optimizer, Unity H.265 HEVC video optimizer, Unity mobile video compression, Unity WebGL video optimization, game asset optimization, Unity media asset pipeline, HandBrakeCLI Unity editor tool.

## GitHub Topics

`unity`, `unity-editor`, `handbrake`, `handbrakecli`, `video-optimizer`, `video-compression`, `mp4`, `h264`, `h265`, `hevc`, `game-development`, `editor-tool`, `unity-tools`

## License

This Unity plugin is released under the MIT License.

HandBrake and HandBrakeCLI are separate external tools licensed by their own project. This repository does not include HandBrake binaries or source code.

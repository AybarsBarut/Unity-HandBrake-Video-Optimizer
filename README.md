# Unity HandBrake Video Optimizer

Unity HandBrake Video Optimizer is a powerful Unity Editor tool designed to streamline video compression and optimization directly within the Unity development environment. By integrating the HandBrakeCLI, this tool allows developers to reduce video file sizes, convert formats, and prepare media assets for various platforms without leaving the editor.

## Key Features

- Direct HandBrakeCLI Integration: Seamlessly connects Unity to the HandBrake command-line interface for professional-grade video encoding.
- Batch Video Processing: Select and optimize multiple video files simultaneously to save time during asset preparation.
- Optimized Presets: Includes pre-configured settings for Balanced performance, High Quality, Small Size, and Mobile/Web compatibility.
- Advanced Codec Support: Toggle between H.264 and H.265 (HEVC) codecs based on your project requirements and target hardware.
- Real-time Progress Tracking: Monitor the compression progress with a dedicated log system and progress bars within the Unity Editor.
- Automatic Path Detection: Automatically attempts to find the HandBrakeCLI installation on your system for a hassle-free setup.

## Supported Formats

The tool supports a wide range of common video containers including:
- MP4
- MOV
- MKV
- AVI
- WEBM

## Installation

1. Ensure you have HandBrakeCLI installed on your system;
   https://handbrake.fr/docs/en/1.9.0/cli/cli-options.html
2. Download or clone this repository into your Unity project's Assets folder.
3. The tool will be available under the Tools menu in the Unity Editor.

## Usage

1. Open the tool via Tools -> Video Optimizer.
2. Select the videos you wish to optimize from your project assets.
3. Choose your desired preset (Balanced, High Quality, Small Size, or Mobile/Web).
4. Select the target codec (H.264 or H.265).
5. Click the process button to start optimization.
6. Optimized files will be generated according to your settings, significantly reducing the storage footprint of your game assets.

## SEO Keywords

Unity Video Compression, Unity HandBrake Tool, Game Asset Optimization, Video Optimizer for Unity, HandBrake CLI Unity Integration, H.264 Unity, H.265 Unity, Video Format Converter Unity.

## License

This project is open-source and available under the MIT License.

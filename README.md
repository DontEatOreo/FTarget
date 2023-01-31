# FTarget
FTarget is a tool that enables you to set a specific file size for video or audio files, and ffmpeg will attempt to reach that size.

## Usage
To target a video file of 8 MB size:
```bash
-i /home/user/video.mp4 -s 8
```

To target a 16 MB file size with custom video and audio codecs, a different output directory, use optimized filters and change resolution to 480p
```bash
-i /home/user/video.mp4 -s 16 -v av1 -a opus -pr -of -r 480p -o /home/user/videos/
```

To target a 24 MB file size with default video and audio codecs and display the ffmpeg arguments used:
```bash
-i /home/user/video.mp4 -s 24 -p
```

To target a 32 MB file size with custom video and audio codecs and disable the progress bar:
```bash
-i /home/user/video.mp4 -s 32 -v hevc -a opus -np
```

## How to compile?
You use [dotnet publish](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-publish) to make a binary for your platform. 
For example, to make a binary for Linux, you can use the following command:
```bash
dotnet publish -c Release -r linux-x64 -p:PublishSingleFile=true --sc true
```

## Notes

- By default, FTarget places the compressed file in the same directory as the original file. You can specify a different directory using the -o flag.
- If no codec for video or audio is specified, FTarget uses the same codec as the original file (except for vorbis audio, which defaults to aac).
- Use the -p flag to display the ffmpeg arguments used during compression.
- Use the -np flag to disable the progress bar.
- Use the -of flag for optimized filters for better compression (only valid for vp9 and av1).
- The order of the arguments in the FTarget command line does not affect its functionality.
- Using AV1 or VP8/VP9 will force the output file to use libouput (opus) as the audio codec.

## NuGet Packages
```
CliWrap
Pastel
System.CommandLine
Xabe.FFmpeg
```
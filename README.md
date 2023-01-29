# FTarget
FTarget is a tool that enables you to set a specific file size for video or audio files, and ffmpeg will attempt to reach that size.

## Usage
To target a video file of 8 MB size:
```bash
-f /home/user/video.mp4 -s 8
```

To target a 16 MB file size with custom video and audio codecs, a different output directory, and display the progress bar:
```bash
-f /home/user/video.mp4 -s 16 -v libx264 -a aac -pr -o /home/user/videos/
```

To target a 24 MB file size with default video and audio codecs and display the ffmpeg arguments used:

```bash
-f /home/user/video.mp4 -s 24 -p
```

## Notes

- By default, FTarget places the compressed file in the same directory as the original file. You can specify a different directory using the -o flag.
- If no codec for video or audio is specified, FTarget uses the same codec as the original file (except for vorbis audio, which defaults to aac).
- Use the -p flag to display the ffmpeg arguments used during compression.
- Use the -pr flag to show a progress bar while ffmpeg compresses the file.
- The order of the arguments in the FTarget command line does not affect its functionality.

## NuGet Packages
```
CliWrap
Pastel
System.CommandLine
Xabe.FFmpeg
```
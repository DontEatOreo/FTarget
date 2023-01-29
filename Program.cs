using System.CommandLine;
using System.CommandLine.Invocation;
using System.Globalization;
using CliWrap;
using Pastel;
using Xabe.FFmpeg;

try
{
    await Cli.Wrap("ffmpeg")
        .WithArguments("-version")
        .WithValidation(CommandResultValidation.ZeroExitCode)
        .ExecuteAsync();
}
catch (Exception)
{
    Console.Error.WriteLine($"{"FFmpeg is not installed".Pastel(ConsoleColor.Red)}");
    Environment.Exit(1);
}

Option<string> filePathOption = new(new[] { "-f", "--file" }, "Path to file/s") { IsRequired = true };
filePathOption.AddValidator(context =>
{
    var value = context.GetValueOrDefault<string>();
    if (File.Exists(value))
        return;
    Console.Error.WriteLine($"{"File does not exist".Pastel(ConsoleColor.Red)}");
    Environment.Exit(1);
});

Option<string> outputFilePathOption = new(new[] { "-o", "--output" }, "Directory to output the file/s") { IsRequired = true };
outputFilePathOption.SetDefaultValue(Environment.CurrentDirectory);
outputFilePathOption.AddValidator(context =>
{
    var value = context.GetValueOrDefault<string>();
    if (Path.Exists(value))
        return;
    Console.Error.WriteLine($"{"Invalid path".Pastel(ConsoleColor.Red)}");
    Environment.Exit(1);
});

Option<string> sizeOption = new(new[] { "-s", "--size" }, "Target file size (MiB)") { IsRequired = true };
sizeOption.AddValidator(context =>
{
    var value = context.GetValueOrDefault<string>();
    if (double.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
    {
        if (parsed >= 0)
            return;
    }
    Console.Error.WriteLine($"{"Invalid size".Pastel(ConsoleColor.Red)}");
    Environment.Exit(1);
});


Option<string> videoCodecOption = new(new[] { "-v", "--video" }, "Video Codec to use. Available codecs:\n" +
                                                                 "h264 (libx264), h265 (libx265) (hevc), vp8 (libvpx), vp9 (libvpx-vp9), av1 (libaom-av1)");
videoCodecOption.AddValidator(context =>
{
    var value = context.GetValueOrDefault<string>();
    if (value is null)
        return;
    string[] validCodecs =
        { "h264", "libx264", "h265", "libx265", "hevc", "vp8", "libvpx", "vp9", "libvpx-vp9", "av1", "libaom-av1" };
    if (validCodecs.Any(value.Contains))
        return;
    Console.Error.WriteLine($"{"Invalid codec".Pastel(ConsoleColor.Red)}");
    Environment.Exit(1);
});
Option<string> audioCodecOption = new(new[] { "-a", "--audio" }, "Audio codec to use. Available codecs:\n" +
                                                                 "aac, mp3, opus (libopus)");
audioCodecOption.AddValidator(context =>
{
    var value = context.GetValueOrDefault<string>();
    if (value is null)
        return;
    if (value is "aac" or "mp3" or "opus" or "libopus")
        return;
    Console.Error.WriteLine($"{"Invalid codec".Pastel(ConsoleColor.Red)}");
    Environment.Exit(1);
});

Option<int> audioBitrateOption = new(new[] { "-b", "--bitrate" }, "Audio bitrate to use");
audioBitrateOption.SetDefaultValue(0);
audioBitrateOption.AddValidator(context =>
{
    var value = context.GetValueOrDefault<int>();
    switch (value)
    {
        case <= 0:
            return;
        case >= int.MaxValue:
            return;
    }
    Console.Error.WriteLine($"{"Invalid bitrate".Pastel(ConsoleColor.Red)}");
    Environment.Exit(1);
});

Option<bool> printArgumentsOption = new(new[] { "-p", "--print" }, "Prints FFmpeg arguments");
Option<bool> progressOption = new(new[] { "-pr", "--progress" }, "Prints progress");

Option[] options = { filePathOption, outputFilePathOption, sizeOption, videoCodecOption, audioCodecOption, audioBitrateOption, printArgumentsOption };
var rootCommand = new RootCommand("Converts a video/audio file to a specified file size");
foreach (var option in options)
    rootCommand.AddOption(option);
rootCommand.SetHandler(Handler);

async Task Handler(InvocationContext context)
{
    var filePathValue = context.ParseResult.GetValueForOption(filePathOption)!;
    var outputFilePathValue = Path.Combine(context.ParseResult.GetValueForOption(outputFilePathOption)!,
        Path.GetFileNameWithoutExtension(filePathValue) + "-target" + Path.GetExtension(filePathValue));
    var fileSizeValue = double.Parse(context.ParseResult.GetValueForOption(sizeOption)!, NumberStyles.Number, CultureInfo.InvariantCulture);
    var videoInputCodecValue = context.ParseResult.GetValueForOption(videoCodecOption);
    var audioInputCodecValue = context.ParseResult.GetValueForOption(audioCodecOption);
    var audioBitrateValue = context.ParseResult.GetValueForOption(audioBitrateOption);
    var printArgumentsValue = context.ParseResult.GetValueForOption(printArgumentsOption);
    var progressValue = context.ParseResult.GetValueForOption(progressOption);

    var mediaInfo = await FFmpeg.GetMediaInfo(filePathValue);
    var duration = mediaInfo.Duration.Seconds;
    var videoStream = mediaInfo.VideoStreams.FirstOrDefault();
    var audioStream = mediaInfo.AudioStreams.FirstOrDefault();

    var conversion = FFmpeg.Conversions.New()
        .SetOutput(outputFilePathValue);

    var videoCodec = SetVideoCodec(videoStream, videoInputCodecValue, conversion, ref outputFilePathValue);
    var audioBitrate = SetAudioCodec(audioStream, audioInputCodecValue, videoCodec, videoStream, conversion, audioBitrateValue, ref outputFilePathValue);

    if (File.Exists(outputFilePathValue))
    {
        Console.WriteLine($"{"File already exists. Overwrite? (y/n)".Pastel(ConsoleColor.Yellow)}");
        var answer = Console.ReadKey();
        if (answer.Key is ConsoleKey.Y)
            File.Delete(outputFilePathValue);
        else
            Environment.Exit(0);
        Console.WriteLine();
    }

    // Convert MiB to KiB
    fileSizeValue *= 1024; // 1 MiB = 1024 KiB

    var desiredBitrate = fileSizeValue * 8388.608 / duration; // 8388.608 = 8 * 1024 * 1024 / 1000
    var videoBitrate = desiredBitrate - (audioBitrateValue is not 0
        ? audioBitrateValue
        : audioBitrate ?? 0);
    conversion.SetVideoBitrate((int)videoBitrate);

    if (progressValue)
    {
        conversion.OnProgress += (_, eventArgs) =>
        {
            // VP8, VP9 and AV1 have two passes, so the progress is divided by 2.
            double percentage;
            TimeSpan eta;
            if (videoCodec is VideoCodec.vp8 or VideoCodec.vp9 or VideoCodec.av1)
            {
                percentage = eventArgs.Duration.TotalSeconds / duration * 50;
                eta = TimeSpan.FromSeconds(duration - eventArgs.Duration.TotalSeconds / 2);
            }
            else
            {
                percentage = eventArgs.Duration.TotalSeconds / duration * 100;
                eta = TimeSpan.FromSeconds(duration - eventArgs.Duration.TotalSeconds);
            }
            Console.Write($"\rProgress: {percentage.ToString("0.00", CultureInfo.InvariantCulture)}% | ETA: {eta:mm\\:ss}\t");
        };
        Console.WriteLine(); // new line after the progress bar
    }

    conversion.UseMultiThread(true);
    var ffmpegArgs = conversion.Build();
    if (printArgumentsValue)
        Console.WriteLine(ffmpegArgs.Pastel(ConsoleColor.Cyan));

    if (videoBitrate <= 0 || audioBitrate <= 0)
    {
        Console.Error.WriteLine("Target file size is too small.".Pastel(ConsoleColor.Red));
        Environment.Exit(1);
    }

    if (videoBitrate >= int.MaxValue || audioBitrate >= int.MaxValue)
    {
        Console.Error.WriteLine("Target file size is too large.".Pastel(ConsoleColor.Red));
        Environment.Exit(1);
    }

    await conversion.Start();
    Console.WriteLine($"{"Done".Pastel(ConsoleColor.Green)}\nOutput file: {outputFilePathValue.Pastel(ConsoleColor.Cyan)}");
}

VideoCodec? SetVideoCodec(IVideoStream? videoStream, string? videoInputCodec, IConversion conversion, ref string outputFilePathValue)
{
    if (videoStream is null)
        return null;

    conversion.SetPixelFormat(PixelFormat.yuv420p10le);

    var videoCodec = VideoCodec._012v; // dummy value
    if (videoInputCodec is null)
        videoStream.SetCodec(videoStream.Codec);
    else
    {
        videoCodec = videoInputCodec switch
        {
            "h264" or "libx264" => VideoCodec.libx264,
            "h265" or "libx265" or "hevc" => VideoCodec.hevc,
            "vp8" or "libvpx" => VideoCodec.vp8,
            "vp9" or "libvpx-vp9" => VideoCodec.vp9,
            "av1" or "libaom-av1" => VideoCodec.av1,
            _ => throw new ArgumentException("Invalid video codec")
        };
        videoStream.SetCodec(videoCodec);

        outputFilePathValue = Path.ChangeExtension(outputFilePathValue, videoCodec switch
        {
            VideoCodec.libx264 or VideoCodec.hevc => "mp4",
            VideoCodec.vp8 or VideoCodec.vp9 or VideoCodec.av1 => "webm",
            _ => throw new ArgumentException("Invalid video codec")
        });
        conversion.SetOutput(outputFilePathValue);
    }

    conversion.AddStream(videoStream);

    return videoCodec;
}

long? SetAudioCodec(IAudioStream? audioStream, string? audioInputCodec, VideoCodec? videoCodec, IVideoStream? videoStream,
    IConversion conversion, int audioBitrateValue, ref string outputFilePathValue1)
{
    var audioCodec = AudioCodec.aac; // dummy value
    var audioBitrate = audioStream?.Bitrate;
    if (audioStream is null)
        return audioBitrate;
    if (audioInputCodec is null)
        audioStream.SetCodec(audioStream.Codec);
    else
    {
        audioCodec = audioInputCodec switch
        {
            "aac" => AudioCodec.aac,
            "mp3" => AudioCodec.mp3,
            "opus" => AudioCodec.libopus,
            _ => throw new ArgumentException("Invalid audio codec")
        };
        audioStream.SetCodec(audioCodec);
    }

    if (videoCodec is VideoCodec.vp8 or VideoCodec.vp9)
        audioStream.SetCodec(AudioCodec.libopus);

    if (videoStream is null)
    {
        outputFilePathValue1 = Path.ChangeExtension(outputFilePathValue1, audioCodec switch
        {
            AudioCodec.aac => "m4a",
            AudioCodec.mp3 => "mp3",
            AudioCodec.libopus => "opus",
            _ => throw new ArgumentException("Invalid audio codec")
        });
        conversion.SetOutput(outputFilePathValue1);
    }

    if (audioStream.Codec is "vorbis")
        audioStream.SetCodec(AudioCodec.aac);

    audioStream.SetBitrate(audioBitrateValue is not 0
        ? audioBitrateValue
        : audioBitrate!.Value);
    conversion.AddStream(audioStream);

    return audioBitrate;
}

if (args.Length is 0)
    args = new[] { "-h" };
await rootCommand.InvokeAsync(args);
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Drawing;
using System.Globalization;
using System.Text;
using CliWrap;
using Pastel;
using Xabe.FFmpeg;

try
{
    await Cli.Wrap("ffmpeg")
        .WithArguments("-version")
        .ExecuteAsync();
}
catch (Exception)
{
    Console.Error.WriteLine($"{"FFmpeg is not installed".Pastel(ConsoleColor.Red)}");
    Environment.Exit(1);
}

string[] validVideoCodes = { "h264", "libx264", "h265", "libx265", "hevc", "vp8", "libvpx", "vp9", "libvpx-vp9", "av1", "libaom-av1" };
string[] validAudioCodecs = { "aac", "mp3", "opus", "libopus" };

string[] av1Args = {
    "-cpu-used 6",
    "-lag-in-frames 35",
    "-row-mt 1",
    "-tile-rows 0",
    "-tile-columns 1"
};
string[] vp9Args = {
    "-row-mt 1",
    "-lag-in-frames 25",
    "-cpu-used 4",
    "-auto-alt-ref 1",
    "-arnr-maxframes 7",
    "-arnr-strength 4",
    "-aq-mode 0",
    "-enable-tpl 1",
    "-row-mt 1"
};

string[] resolutionList =
{
    "144p",
    "240p",
    "360p",
    "480p",
    "720p",
    "1080p",
    "1440p",
    "2160p"
};

Option<string> filePathOption =
    new(new[] { "-i", "--input", "-f", "--file" },
    "Path to file") { IsRequired = true };
filePathOption.AddValidator(context =>
{
    var value = context.GetValueOrDefault<string>();
    if (File.Exists(value))
        return;
    Console.Error.WriteLine($"{"File does not exist".Pastel(ConsoleColor.Red)}");
    Environment.Exit(1);
});

Option<string> outputFilePathOption =
    new(new[] { "-o", "--output" },
        "Directory to output the file/s") { IsRequired = true };
outputFilePathOption.SetDefaultValue(Environment.CurrentDirectory);
outputFilePathOption.AddValidator(context =>
{
    var value = context.GetValueOrDefault<string>();
    if (Path.Exists(value))
        return;
    Console.Error.WriteLine($"{"Invalid path".Pastel(ConsoleColor.Red)}");
    Environment.Exit(1);
});

Option<string> sizeOption =
    new(new[] { "-s", "--size" },
        "Target file size (MiB)") { IsRequired = true };
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

Option<string> videoCodecOption =
    new(new[] { "-v", "--video" },
        "Video Codec to use.");
videoCodecOption.AddCompletions(validVideoCodes);
videoCodecOption.AddValidator(context =>
{
    var value = context.GetValueOrDefault<string>();
    if (value is null)
        return;
    if (validVideoCodes.Any(value.Contains))
        return;
    Console.Error.WriteLine($"{"Invalid codec".Pastel(ConsoleColor.Red)}");
    Environment.Exit(1);
});
Option<string> audioCodecOption =
    new(new[] { "-a", "--audio" },
    "Audio codec to use");
audioCodecOption.AddCompletions(validAudioCodecs);
audioCodecOption.AddValidator(context =>
{
    var value = context.GetValueOrDefault<string>();
    if (value is null)
        return;
    if (validAudioCodecs.Any(value.Contains))
        return;
    Console.Error.WriteLine($"{"Invalid codec".Pastel(ConsoleColor.Red)}");
    Environment.Exit(1);
});

Option<int> audioBitrateOption =
    new(new[] { "-b", "--bitrate" },
        "Audio bitrate to use");
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

Option<string> resolutionOption =
    new(new[] { "-r", "--resolution" },
        "Resolution");
resolutionOption.AddCompletions(resolutionList);
resolutionOption.AddValidator(validate =>
{
    var resolutionValue = validate.GetValueOrDefault<string>();
    if (resolutionValue is null)
        return;
    if (resolutionList.Contains(resolutionValue))
        return;

    Console.Error.WriteLine($"{"Invalid resolution".Pastel(ConsoleColor.Red)}");
    Environment.Exit(1);
});

Option<bool> printArgumentsOption =
    new(new[] { "-p", "--print" },
        "Prints FFmpeg arguments");
Option<bool> noProgressOption =
    new(new[] { "-np", "--no-progress" },
        "Disables progress bar");
Option<bool> optimizedFiltersOption =
    new(new[] { "-of", "--optimized-filters" },
        "Automatically applies optimized filters for the video");

Option[] options =
{
    filePathOption,
    outputFilePathOption,
    sizeOption,
    videoCodecOption,
    audioCodecOption,
    audioBitrateOption,
    printArgumentsOption,
    noProgressOption,
    optimizedFiltersOption,
    resolutionOption
};
RootCommand rootCommand = new("Converts a video/audio file to a specified file size");
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
    var resolutionValue = context.ParseResult.GetValueForOption(resolutionOption);

    var printArgumentsValue = context.ParseResult.GetValueForOption(printArgumentsOption);
    var noProgressValue = context.ParseResult.GetValueForOption(noProgressOption);
    var optimizedFilterValue = context.ParseResult.GetValueForOption(optimizedFiltersOption);

    var mediaInfo = await FFmpeg.GetMediaInfo(filePathValue);
    var duration = mediaInfo.Duration.Seconds;
    var videoStream = mediaInfo.VideoStreams.FirstOrDefault();
    var audioStream = mediaInfo.AudioStreams.FirstOrDefault();

    var conversion = FFmpeg.Conversions.New()
        .SetOutput(outputFilePathValue);

    var videoCodec = SetVideoCodec(videoStream,
        videoInputCodecValue,
        conversion,
        optimizedFilterValue,
        resolutionValue,
        ref outputFilePathValue);
    var audioBitrate = SetAudioCodec(audioStream,
        audioInputCodecValue,
        videoCodec,
        videoStream,
        conversion,
        audioBitrateValue,
        ref outputFilePathValue);

    FileCheck(outputFilePathValue);

    // Convert MiB to KiB
    fileSizeValue *= 1024; // 1 MiB = 1024 KiB

    var desiredBitrate = fileSizeValue * 8388.608 / duration; // 8388.608 = 8 * 1024 * 1024 / 1000
    var videoBitrate = desiredBitrate - (audioBitrateValue is not 0
        ? audioBitrateValue
        : audioBitrate ?? 0);
    conversion.SetVideoBitrate((int)videoBitrate);

    if (!noProgressValue)
        ProgressBar(conversion);

    conversion.UseMultiThread(true);
    var ffmpegArgs = conversion.Build();
    if (printArgumentsValue)
        Console.WriteLine(ffmpegArgs);

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
    Console.WriteLine($"\n{"Done".Pastel(ConsoleColor.DarkGreen)}\n" +
                      $"Output file: {outputFilePathValue}");
}

VideoCodec SetVideoCodec(IVideoStream? videoStream,
    string? videoInputCodec,
    IConversion conversion,
    bool optimizedFilter,
    string? resolutionValue,
    ref string outputFilePathValue)
{
    if (videoStream is null)
        return default;

    if (resolutionValue is not null)
        SetResolution(videoStream, resolutionValue);

    conversion.SetPreset(ConversionPreset.VerySlow);
    conversion.SetPixelFormat(PixelFormat.yuv420p10le);

    var videoCodec = VideoCodec._012v; // dummy value
    if (string.IsNullOrEmpty(videoInputCodec))
        videoStream.SetCodec(videoStream.Codec);
    else
    {
        videoCodec = GetVideoCodecFromInputCodec(videoInputCodec);
        videoStream.SetCodec(videoCodec);

        outputFilePathValue = GetOutputFilePathWithCorrectExtension(outputFilePathValue, videoCodec);
        conversion.SetOutput(outputFilePathValue);
    }

    conversion.AddStream(videoStream);

    if (!optimizedFilter)
        return videoCodec;

    AddOptimizedFilter(conversion, videoCodec);

    return videoCodec;
}

void SetResolution(IVideoStream videoStream, string resolutionValue)
{
    double originalWidth = videoStream.Width;
    double originalHeight = videoStream.Height;
    var resolutionValueInt = int.Parse(resolutionValue[..^1]);

    if (originalWidth > originalHeight)
    {
        var outputHeight = (int)Math.Round(originalHeight * (resolutionValueInt / originalWidth));
        var outputWidth = resolutionValueInt - resolutionValueInt % 2;
        outputHeight -= outputHeight % 2;
        videoStream.SetSize(outputWidth, outputHeight);
    }
    else if (originalWidth < originalHeight)
    {
        var outputWidth = (int)Math.Round(originalWidth * (resolutionValueInt / originalWidth));
        outputWidth -= outputWidth % 2;
        var outputHeight = resolutionValueInt - resolutionValueInt % 2;
        videoStream.SetSize(outputWidth, outputHeight);
    }
    else
    {
        var outputWidth = resolutionValueInt - resolutionValueInt % 2;
        videoStream.SetSize(outputWidth, outputWidth);
    }
}

VideoCodec GetVideoCodecFromInputCodec(string videoInputCodec)
{
    return videoInputCodec switch
    {
        "h264" => VideoCodec.libx264,
        "h265" or "libx265" => VideoCodec.hevc,
        "libvpx" => VideoCodec.vp8,
        "libvpx-vp9" => VideoCodec.vp9,
        "libaom-av1" => VideoCodec.av1,
        _ => Enum.Parse<VideoCodec>(videoInputCodec)
    };
}

string GetOutputFilePathWithCorrectExtension(string outputFilePath, VideoCodec videoCodec)
{
    return Path.ChangeExtension(outputFilePath, videoCodec switch
    {
        VideoCodec.libx264 or VideoCodec.hevc => "mp4",
        VideoCodec.vp8 or VideoCodec.vp9 or VideoCodec.av1 => "webm"
    });
}

void AddOptimizedFilter(IConversion conversion, VideoCodec videoCodec)
{
    switch (videoCodec)
    {
        case VideoCodec.av1:
            conversion.AddParameter(string.Join(" ", av1Args));
            break;
        case VideoCodec.vp9:
            conversion.AddParameter(string.Join(" ", vp9Args));
            break;
    }
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

    if (videoCodec is VideoCodec.vp8 or VideoCodec.vp9 or VideoCodec.av1)
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

void ProgressBar(IConversion conversion)
{
    conversion.OnProgress += (_, args) =>
    {
        const string startHex = "#05c880";
        const string endHex = "#02422a";
        var percent = args.Duration.TotalSeconds / args.TotalLength.TotalSeconds;
        var eta = args.TotalLength - args.Duration;
        var progress = (int)Math.Round(percent * 100);
        StringBuilder progressString = new();
        for (var i = 0; i < 100; i++)
        {
            var color = CalculateColor(startHex, endHex, i, progress);
            if (i < progress)
                progressString.Append($"{'█'}".Pastel(color));
            else if (i == progress)
                progressString.Append($"{'▓'}".Pastel(color));
            else
                progressString.Append('░');
        }
        Console.Write($"\rProgress: {progressString} {progress}% | ETA: {eta:hh\\:mm\\:ss}\t");
    };
}

static Color CalculateColor(string startHex, string endHex, int i, int progress)
{
    /*
     * The values of red, green, and blue are calculated based on the progress of the task and the start and end colors.
     * For each iteration, the values of red, green, and blue are determined by a weighted average of the start and end color values.
     * The weight of the start color decreases as the progress increases, while the weight of the end color increases.
     * The formula for each component (red, green, or blue) is as follows:
     * component = (1 - i / 100) * startColor.component + (i / 100) * endColor.component
     * where component is either R, G, or B depending on the component being calculated
     * and i is the current iteration, which ranges from 0 to 100.
    */
    var startColor = ColorTranslator.FromHtml(startHex);
    var endColor = ColorTranslator.FromHtml(endHex);
    var weight = (double)i / 100;
    if (i > progress)
        weight = 1 - weight;
    var red = (int)((1.0 - weight) * startColor.R + weight * endColor.R);
    var green = (int)((1.0 - weight) * startColor.G + weight * endColor.G);
    var blue = (int)((1.0 - weight) * startColor.B + weight * endColor.B);
    return Color.FromArgb(red, green, blue);
}

void FileCheck(string outputFilePathValue)
{
    if (!File.Exists(outputFilePathValue))
        return;

    Console.WriteLine($"{"File already exists. Overwrite? (y/n)".Pastel(ConsoleColor.Yellow)}");
    var answer = Console.ReadKey();
    if (answer.Key is ConsoleKey.Y)
        File.Delete(outputFilePathValue);
    else
        Environment.Exit(0);
    Console.WriteLine();
}

if (args.Length is 0)
    args = new[] { "-h" };
await rootCommand.InvokeAsync(args);
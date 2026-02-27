namespace InsightCast.Video;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
/// Exception thrown when the FFmpeg executable cannot be found.
/// </summary>
public class FFmpegNotFoundError : Exception
{
    public FFmpegNotFoundError()
        : base("FFmpeg executable was not found on this system.") { }

    public FFmpegNotFoundError(string message)
        : base(message) { }

    public FFmpegNotFoundError(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>
/// Wrapper around the FFmpeg command-line tool for video processing operations.
/// </summary>
public class FFmpegWrapper
{
    /// <summary>Full path to the ffmpeg executable.</summary>
    public string FfmpegPath { get; private set; }

    /// <summary>Full path to the ffprobe executable (derived from FfmpegPath directory).</summary>
    public string FfprobePath =>
        Path.Combine(Path.GetDirectoryName(FfmpegPath) ?? ".", "ffprobe.exe");

    /// <summary>
    /// Creates a new FFmpegWrapper instance.
    /// </summary>
    /// <param name="ffmpegPath">
    /// Explicit path to the ffmpeg executable. If null, auto-detection is performed.
    /// </param>
    /// <exception cref="FFmpegNotFoundError">Thrown when ffmpeg cannot be located.</exception>
    public FFmpegWrapper(string? ffmpegPath = null)
    {
        if (ffmpegPath != null)
        {
            if (!File.Exists(ffmpegPath))
            {
                throw new FFmpegNotFoundError(
                    $"Specified FFmpeg path does not exist: {ffmpegPath}");
            }
            FfmpegPath = ffmpegPath;
        }
        else
        {
            FfmpegPath = FindFfmpeg()
                ?? throw new FFmpegNotFoundError(
                    "FFmpeg executable was not found. Please install FFmpeg or specify its path.");
        }
    }

    /// <summary>
    /// Searches for the ffmpeg executable in common locations.
    /// Search order: PATH (via "where ffmpeg"), application-relative paths, common Windows paths.
    /// </summary>
    /// <returns>Full path to ffmpeg.exe if found; null otherwise.</returns>
    public static string? FindFfmpeg()
    {
        // --- 1. Search PATH using "where ffmpeg" ---
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "where",
                Arguments = "ffmpeg",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    string firstLine = output.Split(
                        new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                    if (File.Exists(firstLine))
                    {
                        return firstLine;
                    }
                }
            }
        }
        catch
        {
            // "where" command not available or failed; continue searching.
        }

        // --- 2. Application-relative and working-directory-relative paths ---
        string appDir = AppDomain.CurrentDomain.BaseDirectory;
        string cwd = Environment.CurrentDirectory;

        var relativePaths = new List<string>
        {
            // From application base directory (works in published builds)
            Path.Combine(appDir, "tools", "ffmpeg", "bin", "ffmpeg.exe"),
            Path.Combine(appDir, "tools", "ffmpeg", "ffmpeg.exe"),
            Path.Combine(appDir, "ffmpeg", "bin", "ffmpeg.exe"),
            Path.Combine(appDir, "ffmpeg", "ffmpeg.exe"),
            Path.Combine(appDir, "ffmpeg.exe"),
            Path.Combine(appDir, "..", "tools", "ffmpeg", "bin", "ffmpeg.exe"),
            // From current working directory (works with "dotnet run")
            Path.Combine(cwd, "tools", "ffmpeg", "bin", "ffmpeg.exe"),
            Path.Combine(cwd, "tools", "ffmpeg", "ffmpeg.exe"),
            Path.Combine(cwd, "ffmpeg", "bin", "ffmpeg.exe"),
            Path.Combine(cwd, "ffmpeg", "ffmpeg.exe"),
            Path.Combine(cwd, "ffmpeg.exe"),
            // build.ps1 publish output: publish/ffmpeg/bin/ffmpeg.exe
            Path.Combine(cwd, "publish", "ffmpeg", "bin", "ffmpeg.exe"),
            // build.ps1 publish output (from solution root when CWD is project dir)
            Path.Combine(cwd, "..", "publish", "ffmpeg", "bin", "ffmpeg.exe"),
        };

        // Walk up from appDir to find project/solution root
        string? dir = appDir;
        for (int i = 0; i < 6 && dir != null; i++)
        {
            dir = Path.GetDirectoryName(dir);
            if (dir != null)
            {
                relativePaths.Add(Path.Combine(dir, "tools", "ffmpeg", "bin", "ffmpeg.exe"));
                relativePaths.Add(Path.Combine(dir, "ffmpeg", "bin", "ffmpeg.exe"));
                relativePaths.Add(Path.Combine(dir, "publish", "ffmpeg", "bin", "ffmpeg.exe"));
            }
        }

        foreach (string path in relativePaths)
        {
            string fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        // --- 3. Common Windows installation paths ---
        string[] commonPaths =
        {
            @"C:\ffmpeg\bin\ffmpeg.exe",
            @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
            @"C:\Program Files (x86)\ffmpeg\bin\ffmpeg.exe",
            @"C:\tools\ffmpeg\bin\ffmpeg.exe",
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ffmpeg", "bin", "ffmpeg.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "ffmpeg", "bin", "ffmpeg.exe"),
        };

        foreach (string path in commonPaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks whether FFmpeg is available and can be executed.
    /// </summary>
    /// <returns>True if ffmpeg runs successfully with -version flag.</returns>
    public bool CheckAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = FfmpegPath,
                Arguments = "-version",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                // Read stdout asynchronously to prevent deadlock
                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                process.StandardError.ReadToEnd();
                stdoutTask.GetAwaiter().GetResult();
                process.WaitForExit();
                return process.ExitCode == 0;
            }
        }
        catch
        {
            // Process could not be started.
        }

        return false;
    }

    /// <summary>
    /// Gets the FFmpeg version string (first line of "ffmpeg -version" output).
    /// </summary>
    /// <returns>Version string, or null if unavailable.</returns>
    public string? GetVersion()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = FfmpegPath,
                Arguments = "-version",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                string output = process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    string[] lines = output.Split(
                        new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    return lines.Length > 0 ? lines[0].Trim() : null;
                }
            }
        }
        catch
        {
            // Ignore errors.
        }

        return null;
    }

    /// <summary>
    /// Runs an ffmpeg command with the given arguments.
    /// </summary>
    /// <param name="args">List of command-line arguments (without the ffmpeg executable itself).</param>
    /// <param name="showOutput">If true, writes stdout/stderr to the console.</param>
    /// <returns>True if the command exited with code 0.</returns>
    /// <summary>
    /// Quotes an argument for safe passing to a process command line.
    /// Prevents injection via user-controlled paths or text.
    /// </summary>
    private static string QuoteArg(string arg)
    {
        // If argument is already quoted or contains no special chars, return as-is
        if (arg.StartsWith('"') && arg.EndsWith('"')) return arg;
        if (!arg.Contains(' ') && !arg.Contains('"') && !arg.Contains('\\')) return arg;
        // Escape internal quotes and wrap
        return $"\"{arg.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
    }

    public bool RunCommand(List<string> args, bool showOutput = false)
    {
        try
        {
            string arguments = string.Join(" ", args.Select(QuoteArg));
            var psi = new ProcessStartInfo
            {
                FileName = FfmpegPath,
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                return false;
            }

            // Read stderr asynchronously to prevent deadlock
            // (FFmpeg writes large amounts of progress data to stderr)
            var stderrTask = process.StandardError.ReadToEndAsync();
            string stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            string stderr = stderrTask.GetAwaiter().GetResult();

            if (showOutput)
            {
                if (!string.IsNullOrEmpty(stdout))
                {
                    Console.WriteLine(stdout);
                }
                if (!string.IsNullOrEmpty(stderr))
                {
                    Console.Error.WriteLine(stderr);
                }
            }

            if (process.ExitCode != 0)
            {
                Debug.WriteLine($"FFmpeg failed (exit {process.ExitCode}): {stderr}");
            }

            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            if (showOutput)
            {
                Console.Error.WriteLine($"FFmpeg command failed: {ex.Message}");
            }
            return false;
        }
    }

    /// <summary>
    /// Retrieves basic information about a video file by running "ffmpeg -i".
    /// </summary>
    /// <param name="videoPath">Path to the video file.</param>
    /// <returns>Dictionary containing video metadata. Key "duration" holds the duration in seconds.</returns>
    public Dictionary<string, object> GetVideoInfo(string videoPath)
    {
        var info = new Dictionary<string, object>();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = FfmpegPath,
                Arguments = $"-i \"{videoPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                // Read stdout asynchronously to prevent deadlock
                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                string stderr = process.StandardError.ReadToEnd();
                stdoutTask.GetAwaiter().GetResult();
                process.WaitForExit();

                // Parse Duration: HH:MM:SS.CC
                var durationMatch = Regex.Match(
                    stderr,
                    @"Duration:\s*(\d{2}):(\d{2}):(\d{2})\.(\d{2})");

                if (durationMatch.Success)
                {
                    int hours = int.Parse(durationMatch.Groups[1].Value);
                    int minutes = int.Parse(durationMatch.Groups[2].Value);
                    int seconds = int.Parse(durationMatch.Groups[3].Value);
                    int centiseconds = int.Parse(durationMatch.Groups[4].Value);

                    double duration = hours * 3600.0
                        + minutes * 60.0
                        + seconds
                        + centiseconds / 100.0;

                    info["duration"] = duration;
                }

                // Parse resolution (e.g., "1920x1080")
                var resolutionMatch = Regex.Match(
                    stderr,
                    @"(\d{2,5})x(\d{2,5})");

                if (resolutionMatch.Success)
                {
                    info["width"] = int.Parse(resolutionMatch.Groups[1].Value);
                    info["height"] = int.Parse(resolutionMatch.Groups[2].Value);
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to get video info: {ex.Message}");
        }

        return info;
    }
}

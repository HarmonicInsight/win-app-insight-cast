namespace InsightCast.Video;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using InsightCast.Models;

/// <summary>
/// Generates individual video scenes from images, video clips, or blank backgrounds,
/// with optional subtitle overlays and audio tracks.
/// </summary>
public class SceneGenerator
{
    private readonly FFmpegWrapper _ffmpeg;
    private readonly string? _fontPath;

    /// <summary>
    /// Creates a new SceneGenerator.
    /// </summary>
    /// <param name="ffmpeg">FFmpeg wrapper instance for executing commands.</param>
    /// <param name="fontPath">
    /// Path to the font file for subtitles. If null, a default Japanese font is searched.
    /// </param>
    public SceneGenerator(FFmpegWrapper ffmpeg, string? fontPath = null)
    {
        _ffmpeg = ffmpeg;
        _fontPath = fontPath ?? FindDefaultFont();
    }

    /// <summary>
    /// Searches for a default Japanese font in the Windows Fonts directory.
    /// </summary>
    /// <returns>Path to a found font file, or null if none found.</returns>
    public static string? FindDefaultFont()
    {
        string fontsDir = @"C:\Windows\Fonts";
        string[] candidates =
        {
            "msgothic.ttc",
            "meiryo.ttc",
            "YuGothM.ttc",
            "YuGothR.ttc",
            "YuGothB.ttc",
            "msmincho.ttc",
            "arial.ttf",
            "segoeui.ttf",
        };

        foreach (string fontName in candidates)
        {
            string fullPath = Path.Combine(fontsDir, fontName);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return null;
    }

    /// <summary>
    /// Splits long subtitle text near the center at a punctuation boundary,
    /// inserting a newline for better readability.
    /// </summary>
    /// <param name="text">The subtitle text to split.</param>
    /// <param name="maxChars">Maximum characters per line before splitting.</param>
    /// <returns>The text with a newline inserted at the best split point.</returns>
    public static string SplitSubtitleText(string text, int maxChars = 18)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
        {
            return text;
        }

        // Punctuation characters that are good split points
        char[] splitChars =
        {
            '.', ',', '!', '?', ';', ':',
            '\u3001', // Japanese comma
            '\u3002', // Japanese period
            '\u3000', // Ideographic space
            '\u300C', // Left corner bracket
            '\u300D', // Right corner bracket
            '\u300E', // Left white corner bracket
            '\u300F', // Right white corner bracket
            '\uFF0C', // Fullwidth comma
            '\uFF0E', // Fullwidth full stop
            '\uFF01', // Fullwidth exclamation mark
            '\uFF1F', // Fullwidth question mark
        };

        int center = text.Length / 2;
        int bestSplit = -1;
        int bestDistance = int.MaxValue;

        // Search for a punctuation character closest to the center
        for (int i = 0; i < text.Length; i++)
        {
            if (splitChars.Contains(text[i]))
            {
                int distance = Math.Abs(i - center);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestSplit = i + 1; // Split after punctuation
                }
            }
        }

        // If no punctuation found, split at center
        if (bestSplit < 0 || bestSplit >= text.Length)
        {
            bestSplit = center;
        }

        string line1 = text[..bestSplit].TrimEnd();
        string line2 = text[bestSplit..].TrimStart();

        if (string.IsNullOrEmpty(line2))
        {
            return line1;
        }

        return $"{line1}\n{line2}";
    }

    /// <summary>
    /// Generates a complete scene video file from a Scene definition.
    /// </summary>
    /// <param name="scene">Scene definition with media and text data.</param>
    /// <param name="outputPath">Output video file path.</param>
    /// <param name="duration">Scene duration in seconds.</param>
    /// <param name="resolution">Video resolution as "WIDTHxHEIGHT" (e.g., "1080x1920").</param>
    /// <param name="fps">Frames per second.</param>
    /// <param name="audioPath">Optional audio file path to add to the scene.</param>
    /// <param name="textStyle">Optional text style settings for subtitle overlay.</param>
    /// <returns>True if the scene was generated successfully.</returns>
    public bool GenerateScene(
        Scene scene,
        string outputPath,
        double duration,
        string resolution = "1080x1920",
        int fps = 30,
        string? audioPath = null,
        TextStyle? textStyle = null,
        WatermarkSettings? watermark = null,
        int sceneIndex = 0,
        MotionIntensity motionIntensity = MotionIntensity.Medium)
    {
        try
        {
            // Parse resolution
            string[] resParts = resolution.Split('x');
            if (resParts.Length != 2
                || !int.TryParse(resParts[0], out int width) || width <= 0
                || !int.TryParse(resParts[1], out int height) || height <= 0)
            {
                return false;
            }

            // Step 1: Generate base video (from image, video, or blank)
            Console.Error.WriteLine($"[SceneGen] HasMedia={scene.HasMedia}, MediaType={scene.MediaType}, MediaPath={scene.MediaPath}");

            string tempBase = Path.Combine(
                Path.GetTempPath(),
                $"scene_base_{Guid.NewGuid():N}.mp4");

            bool baseSuccess;
            if (scene.HasMedia && scene.MediaType == MediaType.Image)
            {
                Console.Error.WriteLine("[SceneGen] Path: Image");
                var motion = scene.MotionType;
                if (motion == MotionType.Auto)
                    motion = ResolveAutoMotion(scene.MediaPath!, sceneIndex);
                baseSuccess = GenerateFromImage(
                    scene.MediaPath!, tempBase, duration, width, height, fps, motion, motionIntensity);
            }
            else if (scene.HasMedia && scene.MediaType == MediaType.Video)
            {
                Console.Error.WriteLine($"[SceneGen] Path: Video, file exists={File.Exists(scene.MediaPath!)}, loop={scene.KeepOriginalAudio == false}");
                baseSuccess = GenerateFromVideo(
                    scene.MediaPath!, tempBase, duration, width, height, fps,
                    loop: scene.KeepOriginalAudio == false);
                Console.Error.WriteLine($"[SceneGen] GenerateFromVideo result={baseSuccess}, output exists={File.Exists(tempBase)}, size={( File.Exists(tempBase) ? new FileInfo(tempBase).Length : 0 )}");
            }
            else
            {
                Console.Error.WriteLine("[SceneGen] Path: Blank");
                baseSuccess = GenerateBlankVideo(
                    tempBase, duration, width, height, fps, "black");
            }

            if (!baseSuccess)
            {
                Console.Error.WriteLine("[SceneGen] Base video FAILED");
                CleanupTempFile(tempBase);
                return false;
            }

            // Step 2: Add text overlays if present
            string currentFile = tempBase;
            string? tempOverlay = null;

            if (scene.HasTextOverlays)
            {
                tempOverlay = Path.Combine(
                    Path.GetTempPath(),
                    $"scene_overlay_{Guid.NewGuid():N}.mp4");

                bool overlaySuccess = AddTextOverlays(
                    currentFile, tempOverlay, scene.TextOverlays, width, height);

                if (overlaySuccess)
                {
                    CleanupTempFile(currentFile);
                    currentFile = tempOverlay;
                }
                else
                {
                    CleanupTempFile(tempOverlay);
                    tempOverlay = null;
                }
            }

            // Step 3: Add subtitle if present
            string? tempSubtitle = null;

            if (scene.HasSubtitle)
            {
                tempSubtitle = Path.Combine(
                    Path.GetTempPath(),
                    $"scene_sub_{Guid.NewGuid():N}.mp4");

                bool subSuccess = AddSubtitle(
                    currentFile, tempSubtitle, scene.SubtitleText!, width, height, textStyle);

                if (subSuccess)
                {
                    CleanupTempFile(currentFile);
                    currentFile = tempSubtitle;
                }
                else
                {
                    // Subtitle failed; continue without it
                    CleanupTempFile(tempSubtitle);
                    tempSubtitle = null;
                }
            }

            // Step 3.5: Add watermark if configured
            string? tempWatermark = null;

            if (watermark != null && watermark.HasWatermark)
            {
                tempWatermark = Path.Combine(
                    Path.GetTempPath(),
                    $"scene_wm_{Guid.NewGuid():N}.mp4");

                bool wmSuccess = AddWatermark(
                    currentFile, tempWatermark, watermark, width, height);

                if (wmSuccess)
                {
                    CleanupTempFile(currentFile);
                    currentFile = tempWatermark;
                }
                else
                {
                    CleanupTempFile(tempWatermark);
                    tempWatermark = null;
                }
            }

            // Step 4: Add audio (narration or silent track for concat compatibility)
            string? tempAudio = null;

            if (!string.IsNullOrEmpty(audioPath) && File.Exists(audioPath))
            {
                tempAudio = Path.Combine(
                    Path.GetTempPath(),
                    $"scene_audio_{Guid.NewGuid():N}.mp4");

                bool audioSuccess = AddAudio(currentFile, tempAudio, audioPath, duration);

                if (audioSuccess)
                {
                    CleanupTempFile(currentFile);
                    currentFile = tempAudio;
                }
                else
                {
                    CleanupTempFile(tempAudio);
                    tempAudio = null;
                }
            }
            else
            {
                // No narration: add silent audio track so concat/xfade works
                tempAudio = Path.Combine(
                    Path.GetTempPath(),
                    $"scene_silent_{Guid.NewGuid():N}.mp4");

                bool silentSuccess = AddSilentAudio(currentFile, tempAudio, duration);
                if (silentSuccess)
                {
                    CleanupTempFile(currentFile);
                    currentFile = tempAudio;
                }
                else
                {
                    CleanupTempFile(tempAudio);
                    tempAudio = null;
                }
            }

            // Step 5: Move final output into place
            if (currentFile != outputPath)
            {
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
                File.Move(currentFile, outputPath);
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Scene generation failed: {ex.Message}");
            return false;
        }
    }

    // -----------------------------------------------------------------------
    // Private helper methods
    // -----------------------------------------------------------------------

    /// <summary>
    /// Generates a video from a static image with the given duration,
    /// optionally applying Ken Burns motion (zoom/pan) with ease-in-out.
    /// </summary>
    private bool GenerateFromImage(
        string imagePath, string outputPath, double duration,
        int width, int height, int fps,
        MotionType motion = MotionType.None,
        MotionIntensity intensity = MotionIntensity.Medium)
    {
        string durationStr = duration.ToString("F2", CultureInfo.InvariantCulture);

        if (motion == MotionType.None)
        {
            // Static: simple scale+pad
            var args = new List<string>
            {
                "-y",
                "-loop", "1",
                "-i", $"\"{imagePath}\"",
                "-vf", $"\"scale={width}:{height}:force_original_aspect_ratio=decrease," +
                       $"pad={width}:{height}:(ow-iw)/2:(oh-ih)/2:black\"",
                "-c:v", "libx264",
                "-t", durationStr,
                "-pix_fmt", "yuv420p",
                "-r", fps.ToString(),
                "-an",
                $"\"{outputPath}\""
            };
            return _ffmpeg.RunCommand(args);
        }

        // Motion: use zoompan filter with ease-in-out
        int totalFrames = (int)(duration * fps);
        string zoompanFilter = BuildZoompanFilter(motion, width, height, totalFrames, fps, intensity);

        var motionArgs = new List<string>
        {
            "-y",
            "-loop", "1",
            "-i", $"\"{imagePath}\"",
            "-vf", $"\"{zoompanFilter}\"",
            "-c:v", "libx264",
            "-t", durationStr,
            "-pix_fmt", "yuv420p",
            "-r", fps.ToString(),
            "-an",
            $"\"{outputPath}\""
        };
        return _ffmpeg.RunCommand(motionArgs);
    }

    /// <summary>
    /// Resolves Auto motion by reading image dimensions.
    /// </summary>
    private MotionType ResolveAutoMotion(string imagePath, int sceneIndex)
    {
        try
        {
            var info = _ffmpeg.GetVideoInfo(imagePath);
            if (info != null
                && info.TryGetValue("width", out var ws) && int.TryParse(ws?.ToString(), out int w)
                && info.TryGetValue("height", out var hs) && int.TryParse(hs?.ToString(), out int h)
                && w > 0 && h > 0)
            {
                return MotionResolver.Resolve(w, h, sceneIndex);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SceneGen] ResolveAutoMotion failed: {ex.Message}");
        }
        // Fallback: alternate zoom in/out
        return sceneIndex % 2 == 0 ? MotionType.ZoomIn : MotionType.ZoomOut;
    }

    /// <summary>
    /// Builds FFmpeg zoompan filter string with ease-in-out easing.
    /// Zoom and pan values controlled by MotionIntensity.
    /// Pan also includes subtle micro-zoom (1.02→1.06) to reduce "sliding" feel.
    /// </summary>
    private static string BuildZoompanFilter(
        MotionType motion, int width, int height, int totalFrames, int fps = 30,
        MotionIntensity intensity = MotionIntensity.Medium)
    {
        // Ease-in-out function: smoothstep = 3*t^2 - 2*t^3
        string t = $"(on/{totalFrames})";
        string ease = $"(3*{t}*{t}-2*{t}*{t}*{t})";

        var (zoomMax, panPercent) = MotionIntensityParams.Get(intensity);
        string zoomRange = (zoomMax - 1.0).ToString("F4", CultureInfo.InvariantCulture);

        string panX = (width * panPercent).ToString("F1", CultureInfo.InvariantCulture);
        string panY = (height * panPercent).ToString("F1", CultureInfo.InvariantCulture);

        // Micro-zoom for pan: subtle zoom during pan to reduce "sliding" feel
        string microZoomStart = "1.02";
        string microZoomRange = "0.04";
        string panZoom = $"{microZoomStart}+{microZoomRange}*{ease}";

        string z, x, y;

        switch (motion)
        {
            case MotionType.ZoomIn:
                // Zoom 1.0 -> 1.08, centered
                z = $"1+{zoomRange}*{ease}";
                x = $"iw/2-(iw/zoom/2)";
                y = $"ih/2-(ih/zoom/2)";
                break;
            case MotionType.ZoomOut:
                // Zoom 1.08 -> 1.0, centered
                z = $"{zoomMax.ToString("F4", CultureInfo.InvariantCulture)}-{zoomRange}*{ease}";
                x = $"iw/2-(iw/zoom/2)";
                y = $"ih/2-(ih/zoom/2)";
                break;
            case MotionType.PanRight:
                // Pan left-to-right with micro-zoom
                z = panZoom;
                x = $"(iw/2-(iw/zoom/2))-{panX}/2+{panX}*{ease}";
                y = $"ih/2-(ih/zoom/2)";
                break;
            case MotionType.PanLeft:
                // Pan right-to-left with micro-zoom
                z = panZoom;
                x = $"(iw/2-(iw/zoom/2))+{panX}/2-{panX}*{ease}";
                y = $"ih/2-(ih/zoom/2)";
                break;
            case MotionType.PanDown:
                // Pan top-to-bottom with micro-zoom
                z = panZoom;
                x = $"iw/2-(iw/zoom/2)";
                y = $"(ih/2-(ih/zoom/2))-{panY}/2+{panY}*{ease}";
                break;
            case MotionType.PanUp:
                // Pan bottom-to-top with micro-zoom
                z = panZoom;
                x = $"iw/2-(iw/zoom/2)";
                y = $"(ih/2-(ih/zoom/2))+{panY}/2-{panY}*{ease}";
                break;
            default:
                z = "1";
                x = $"iw/2-(iw/zoom/2)";
                y = $"ih/2-(ih/zoom/2)";
                break;
        }

        return $"scale=8000:-1," +
               $"zoompan=z='{z}':x='{x}':y='{y}':" +
               $"d={totalFrames}:s={width}x{height}:fps={fps}";
    }

    /// <summary>
    /// Generates a video from a source video clip, optionally looping to fill the duration.
    /// </summary>
    private bool GenerateFromVideo(
        string videoPath, string outputPath, double duration,
        int width, int height, int fps, bool loop)
    {
        string durationStr = duration.ToString("F2", CultureInfo.InvariantCulture);
        var args = new List<string>();
        args.Add("-y");

        if (loop)
        {
            // Use stream_loop to loop the input
            args.AddRange(new[]
            {
                "-stream_loop", "-1",
                "-i", $"\"{videoPath}\"",
                "-t", durationStr
            });
        }
        else
        {
            args.AddRange(new[]
            {
                "-i", $"\"{videoPath}\"",
                "-t", durationStr
            });
        }

        args.AddRange(new[]
        {
            "-vf", $"\"scale={width}:{height}:force_original_aspect_ratio=decrease," +
                   $"pad={width}:{height}:(ow-iw)/2:(oh-ih)/2:black\"",
            "-c:v", "libx264",
            "-pix_fmt", "yuv420p",
            "-r", fps.ToString(),
            "-an",
            $"\"{outputPath}\""
        });

        return _ffmpeg.RunCommand(args);
    }

    /// <summary>
    /// Generates a blank (solid color) video with the given duration.
    /// </summary>
    private bool GenerateBlankVideo(
        string outputPath, double duration,
        int width, int height, int fps, string color = "black")
    {
        string durationStr = duration.ToString("F2", CultureInfo.InvariantCulture);

        var args = new List<string>
        {
            "-y",
            "-f", "lavfi",
            "-i", $"\"color=c={color}:s={width}x{height}:d={durationStr}:r={fps}\"",
            "-c:v", "libx264",
            "-pix_fmt", "yuv420p",
            "-t", durationStr,
            $"\"{outputPath}\""
        };

        return _ffmpeg.RunCommand(args);
    }

    /// <summary>
    /// Overlays multiple text layers onto a video using chained drawtext filters.
    /// Each TextOverlay specifies position (as percentage), font, color, and style.
    /// </summary>
    private bool AddTextOverlays(
        string inputPath, string outputPath, List<TextOverlay> overlays,
        int width, int height)
    {
        var activeOverlays = overlays.Where(o => o.HasText).ToList();
        if (activeOverlays.Count == 0)
            return false;

        string fontSpec = BuildFontSpec(new TextStyle()); // default font path

        var filterParts = new List<string>();

        foreach (var overlay in activeOverlays)
        {
            string escapedText = EscapeDrawText(overlay.Text);

            string textColorHex = ArrayToFfmpegColor(overlay.TextColor);
            string strokeColorHex = ArrayToFfmpegColor(overlay.StrokeColor);
            string shadowColorHex = ArrayToFfmpegColor(overlay.ShadowColor);

            // Calculate pixel position from percentage
            string xExpr = overlay.Alignment switch
            {
                Models.TextAlignment.Left => $"w*{(overlay.XPercent / 100.0).ToString("F4", CultureInfo.InvariantCulture)}",
                Models.TextAlignment.Right => $"w*{(overlay.XPercent / 100.0).ToString("F4", CultureInfo.InvariantCulture)}-text_w",
                _ => $"w*{(overlay.XPercent / 100.0).ToString("F4", CultureInfo.InvariantCulture)}-text_w/2"
            };
            string yExpr = $"h*{(overlay.YPercent / 100.0).ToString("F4", CultureInfo.InvariantCulture)}-text_h/2";

            // Shadow layer
            if (overlay.ShadowEnabled && overlay.ShadowOffset.Length >= 2 &&
                (overlay.ShadowOffset[0] != 0 || overlay.ShadowOffset[1] != 0))
            {
                filterParts.Add(
                    $"drawtext={fontSpec}" +
                    $"text='{escapedText}':" +
                    $"fontsize={overlay.FontSize}:" +
                    $"fontcolor={shadowColorHex}:" +
                    $"x={xExpr}+{overlay.ShadowOffset[0]}:" +
                    $"y={yExpr}+{overlay.ShadowOffset[1]}");
            }

            // Main text layer
            string mainDraw =
                $"drawtext={fontSpec}" +
                $"text='{escapedText}':" +
                $"fontsize={overlay.FontSize}:" +
                $"fontcolor={textColorHex}:" +
                $"borderw={overlay.StrokeWidth}:" +
                $"bordercolor={strokeColorHex}:" +
                $"x={xExpr}:" +
                $"y={yExpr}";

            filterParts.Add(mainDraw);
        }

        string filterChain = string.Join(",", filterParts);

        var args = new List<string>
        {
            "-y",
            "-i", $"\"{inputPath}\"",
            "-vf", $"\"{filterChain}\"",
            "-c:v", "libx264",
            "-pix_fmt", "yuv420p",
            "-c:a", "copy",
            $"\"{outputPath}\""
        };

        return _ffmpeg.RunCommand(args);
    }

    /// <summary>
    /// Overlays subtitle text onto a video using the ffmpeg drawtext filter.
    /// Applies text stroke, shadow, and optional background box.
    /// </summary>
    private bool AddSubtitle(
        string inputPath, string outputPath, string text,
        int width, int height, TextStyle? style = null)
    {
        style ??= new TextStyle();

        // Split long text for readability
        string displayText = SplitSubtitleText(text);

        // Escape text for ffmpeg drawtext filter
        string escapedText = EscapeDrawText(displayText);

        // Build font path for ffmpeg (escape Windows path separators)
        string fontSpec = BuildFontSpec(style);

        // Calculate vertical position (85% from top by default)
        int yPosition = (int)(height * 0.85);

        // Convert int[] color arrays to ffmpeg hex color strings
        string textColorHex = ArrayToFfmpegColor(style.TextColor);
        string strokeColorHex = ArrayToFfmpegColor(style.StrokeColor);
        string shadowColorHex = ArrayToFfmpegColor(style.ShadowColor);
        string bgColorWithAlpha = ArrayToFfmpegColorWithAlpha(
            style.BackgroundColor, style.BackgroundOpacity);

        // Build drawtext filter parts
        var filterParts = new List<string>();

        // Shadow layer (rendered first, offset behind main text)
        if (style.ShadowEnabled && style.ShadowOffset.Length >= 2 &&
            (style.ShadowOffset[0] != 0 || style.ShadowOffset[1] != 0))
        {
            filterParts.Add(
                $"drawtext={fontSpec}" +
                $"text='{escapedText}':" +
                $"fontsize={style.FontSize}:" +
                $"fontcolor={shadowColorHex}:" +
                $"x=(w-text_w)/2+{style.ShadowOffset[0]}:" +
                $"y={yPosition}+{style.ShadowOffset[1]}");
        }

        // Main text layer with border (stroke)
        string mainDraw =
            $"drawtext={fontSpec}" +
            $"text='{escapedText}':" +
            $"fontsize={style.FontSize}:" +
            $"fontcolor={textColorHex}:" +
            $"borderw={style.StrokeWidth}:" +
            $"bordercolor={strokeColorHex}:" +
            $"x=(w-text_w)/2:" +
            $"y={yPosition}";

        // Background box
        if (style.BackgroundOpacity > 0)
        {
            mainDraw += $":box=1:boxcolor={bgColorWithAlpha}:boxborderw=10";
        }

        filterParts.Add(mainDraw);

        string filterChain = string.Join(",", filterParts);

        var args = new List<string>
        {
            "-y",
            "-i", $"\"{inputPath}\"",
            "-vf", $"\"{filterChain}\"",
            "-c:v", "libx264",
            "-pix_fmt", "yuv420p",
            "-c:a", "copy",
            $"\"{outputPath}\""
        };

        return _ffmpeg.RunCommand(args);
    }

    /// <summary>
    /// Adds an audio track to a video, padding or trimming audio to match video duration.
    /// </summary>
    private bool AddAudio(
        string videoPath, string outputPath, string audioPath, double duration)
    {
        string durationStr = duration.ToString("F2", CultureInfo.InvariantCulture);

        // Generate silence-padded audio to match exact duration
        string tempAudioPadded = Path.Combine(
            Path.GetTempPath(),
            $"audio_padded_{Guid.NewGuid():N}.wav");

        try
        {
            // Pad audio with silence to match duration
            var padArgs = new List<string>
            {
                "-y",
                "-i", $"\"{audioPath}\"",
                "-af", $"\"apad=whole_dur={durationStr}\"",
                "-t", durationStr,
                "-ar", "44100",
                "-ac", "2",
                $"\"{tempAudioPadded}\""
            };

            if (!_ffmpeg.RunCommand(padArgs))
            {
                // Fallback: use audio as-is
                tempAudioPadded = audioPath;
            }

            // Merge video + audio
            var mergeArgs = new List<string>
            {
                "-y",
                "-i", $"\"{videoPath}\"",
                "-i", $"\"{tempAudioPadded}\"",
                "-c:v", "copy",
                "-c:a", "aac",
                "-b:a", "192k",
                "-t", durationStr,
                "-map", "0:v:0",
                "-map", "1:a:0",
                "-shortest",
                "-movflags", "+faststart",
                $"\"{outputPath}\""
            };

            return _ffmpeg.RunCommand(mergeArgs);
        }
        finally
        {
            // Clean up padded audio if it was a temp file
            if (tempAudioPadded != audioPath)
            {
                CleanupTempFile(tempAudioPadded);
            }
        }
    }

    /// <summary>
    /// Adds a silent audio track to a video so that concat/xfade works
    /// even when no narration is present.
    /// </summary>
    private bool AddSilentAudio(string videoPath, string outputPath, double duration)
    {
        string durationStr = duration.ToString("F2", CultureInfo.InvariantCulture);

        var args = new List<string>
        {
            "-y",
            "-i", $"\"{videoPath}\"",
            "-f", "lavfi", "-i", $"\"anullsrc=r=44100:cl=stereo\"",
            "-c:v", "copy",
            "-c:a", "aac",
            "-b:a", "192k",
            "-t", durationStr,
            "-map", "0:v:0",
            "-map", "1:a:0",
            "-shortest",
            "-movflags", "+faststart",
            $"\"{outputPath}\""
        };

        return _ffmpeg.RunCommand(args);
    }

    /// <summary>
    /// Overlays a watermark image onto the video at the specified position.
    /// </summary>
    private bool AddWatermark(
        string inputPath, string outputPath, WatermarkSettings watermark,
        int width, int height)
    {
        if (!File.Exists(watermark.ImagePath))
            return false;

        // Calculate watermark size
        int wmWidth = (int)(width * watermark.Scale);

        // Calculate position based on setting
        int margin = (int)(width * watermark.MarginPercent / 100.0);
        string overlayPos = watermark.Position switch
        {
            "top-left" => $"x={margin}:y={margin}",
            "top-right" => $"x=W-w-{margin}:y={margin}",
            "bottom-left" => $"x={margin}:y=H-h-{margin}",
            "center" => "x=(W-w)/2:y=(H-h)/2",
            _ => $"x=W-w-{margin}:y=H-h-{margin}" // bottom-right default
        };

        string opacityStr = watermark.Opacity.ToString("F2", CultureInfo.InvariantCulture);

        // Scale watermark and apply opacity, then overlay
        string filter =
            $"[1:v]scale={wmWidth}:-1,format=rgba," +
            $"colorchannelmixer=aa={opacityStr}[wm];" +
            $"[0:v][wm]overlay={overlayPos}";

        var args = new List<string>
        {
            "-y",
            "-i", $"\"{inputPath}\"",
            "-i", $"\"{watermark.ImagePath}\"",
            "-filter_complex", $"\"{filter}\"",
            "-c:v", "libx264",
            "-pix_fmt", "yuv420p",
            "-c:a", "copy",
            $"\"{outputPath}\""
        };

        return _ffmpeg.RunCommand(args);
    }

    /// <summary>
    /// Extracts a single frame from a video as a thumbnail image.
    /// </summary>
    public bool ExtractThumbnail(string videoPath, string outputImagePath, double timeSeconds = 0.5)
    {
        string timeStr = timeSeconds.ToString("F2", CultureInfo.InvariantCulture);

        var args = new List<string>
        {
            "-y",
            "-ss", timeStr,
            "-i", $"\"{videoPath}\"",
            "-vframes", "1",
            "-q:v", "2",
            $"\"{outputImagePath}\""
        };

        return _ffmpeg.RunCommand(args);
    }

    // -----------------------------------------------------------------------
    // Path and text escaping utilities
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds the fontfile/font specification string for ffmpeg drawtext filter.
    /// </summary>
    private string BuildFontSpec(TextStyle style)
    {
        // Try to find a system font file matching the FontFamily
        string? fontFilePath = _fontPath;

        if (!string.IsNullOrEmpty(fontFilePath) && File.Exists(fontFilePath))
        {
            string ffmpegFontPath = EscapeFontPath(fontFilePath);
            return $"fontfile='{ffmpegFontPath}':";
        }

        // Fallback: use font name directly (works if ffmpeg has fontconfig)
        if (!string.IsNullOrEmpty(style.FontFamily))
        {
            return $"font='{style.FontFamily}':";
        }

        return "";
    }

    /// <summary>
    /// Escapes a Windows file path for use in ffmpeg's drawtext fontfile parameter.
    /// Replaces backslashes with forward slashes and escapes colons.
    /// </summary>
    private static string EscapeFontPath(string path)
    {
        // Replace backslashes with forward slashes for ffmpeg
        string escaped = path.Replace('\\', '/');
        // Escape colons (e.g., C: -> C\\:)
        escaped = escaped.Replace(":", "\\:");
        return escaped;
    }

    /// <summary>
    /// Escapes text for use in the ffmpeg drawtext filter.
    /// </summary>
    private static string EscapeDrawText(string text)
    {
        string escaped = text;
        escaped = escaped.Replace("\\", "\\\\");
        escaped = escaped.Replace("'", "'\\''");
        escaped = escaped.Replace(":", "\\:");
        escaped = escaped.Replace("%", "%%");
        return escaped;
    }

    /// <summary>
    /// Converts an int[3] RGB array to an ffmpeg hex color string (e.g., "0xFFFFFF").
    /// </summary>
    private static string ArrayToFfmpegColor(int[] rgb)
    {
        if (rgb == null || rgb.Length < 3)
        {
            return "0xFFFFFF";
        }
        return $"0x{rgb[0]:X2}{rgb[1]:X2}{rgb[2]:X2}";
    }

    /// <summary>
    /// Converts an int[3] RGB array + alpha to an ffmpeg color string with alpha
    /// (e.g., "0x000000@0.7").
    /// </summary>
    private static string ArrayToFfmpegColorWithAlpha(int[] rgb, double alpha)
    {
        string baseColor = ArrayToFfmpegColor(rgb);
        string alphaStr = alpha.ToString("F2", CultureInfo.InvariantCulture);
        return $"{baseColor}@{alphaStr}";
    }

    /// <summary>
    /// Safely deletes a temporary file if it exists.
    /// </summary>
    private static void CleanupTempFile(string? path)
    {
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }
}

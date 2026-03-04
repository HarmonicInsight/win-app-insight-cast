using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using InsightCast.Models;
using InsightCast.Video;
using InsightCast.VoiceVox;

namespace InsightCast.Services
{
    public class ExportResult
    {
        public bool Success { get; set; }
        public string? VideoPath { get; set; }
        public string? ThumbnailPath { get; set; }
        public string? ChapterFilePath { get; set; }
        public string? MetadataFilePath { get; set; }
        public string? SubtitleFilePath { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class ExportService
    {
        private readonly FFmpegWrapper _ffmpeg;
        private readonly VoiceVoxClient _voiceVoxClient;
        private readonly AudioCache _audioCache;

        public ExportService(FFmpegWrapper ffmpeg, VoiceVoxClient voiceVoxClient, AudioCache audioCache)
        {
            _ffmpeg = ffmpeg;
            _voiceVoxClient = voiceVoxClient;
            _audioCache = audioCache;
        }

        public bool Export(
            Project project,
            string outputPath,
            string resolution,
            int fps,
            int defaultSpeakerId,
            Func<Scene, TextStyle> getStyleForScene,
            IProgress<string> progress,
            CancellationToken ct)
        {
            var result = ExportFull(project, outputPath, resolution, fps,
                defaultSpeakerId, getStyleForScene, progress, ct);
            return result.Success;
        }

        public ExportResult ExportFull(
            Project project,
            string outputPath,
            string resolution,
            int fps,
            int defaultSpeakerId,
            Func<Scene, TextStyle> getStyleForScene,
            IProgress<string> progress,
            CancellationToken ct)
        {
            var result = new ExportResult();
            progress.Report(LocalizationService.GetString("Export.Preparing"));

            if (!_ffmpeg.CheckAvailable())
            {
                var msg = LocalizationService.GetString("Export.NoFFmpeg");
                progress.Report(msg);
                result.ErrorMessage = msg;
                return result;
            }

            if (project.Scenes.Count == 0 || !project.Scenes.Any(s => s.HasMedia || s.HasNarration))
            {
                var msg = LocalizationService.GetString("Export.NoScenes");
                progress.Report(msg);
                result.ErrorMessage = msg;
                return result;
            }

            var sceneGen = new SceneGenerator(_ffmpeg);
            var composer = new VideoComposer(_ffmpeg);
            var tempDir = Path.Combine(Path.GetTempPath(), $"insightcast_build_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            var scenePaths = new List<string>();
            var transitions = new List<(TransitionType, double)>();
            var chapterTimes = new List<(double StartTime, string Title)>();
            double cumulativeDuration = 0;

            // Total steps: scenes + concat + finalize
            int totalSteps = project.Scenes.Count + 2;
            int currentStep = 0;

            // Step: Generate each content scene
            for (int i = 0; i < project.Scenes.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                currentStep++;
                var scene = project.Scenes[i];
                progress.Report($"[{currentStep}/{totalSteps}] {LocalizationService.GetString("Export.SceneAudio", i + 1, project.Scenes.Count)}");

                string? audioPath = null;
                if (scene.HasNarration && !scene.KeepOriginalAudio)
                {
                    var sid = scene.SpeakerId ?? defaultSpeakerId;
                    double speed = scene.SpeechSpeed;

                    // Use speed-aware cache key
                    string cacheKey = Math.Abs(speed - 1.0) > 0.01
                        ? $"{scene.NarrationText!}__spd{speed:F2}"
                        : scene.NarrationText!;

                    if (!_audioCache.Exists(cacheKey, sid))
                    {
                        var audioData = _voiceVoxClient
                            .GenerateAudioAsync(scene.NarrationText!, sid, speed)
                            .GetAwaiter().GetResult();
                        audioPath = _audioCache.Save(cacheKey, sid, audioData);
                    }
                    else
                    {
                        audioPath = _audioCache.GetCachePath(cacheKey, sid);
                    }
                    scene.AudioCachePath = audioPath;
                }

                progress.Report($"[{currentStep}/{totalSteps}] {LocalizationService.GetString("Export.SceneVideo", i + 1, project.Scenes.Count)}");

                double duration = scene.DurationMode == DurationMode.Fixed
                    ? scene.FixedSeconds
                    : (audioPath != null
                        ? (_audioCache.GetDuration(
                               Math.Abs(scene.SpeechSpeed - 1.0) > 0.01
                                   ? $"{scene.NarrationText!}__spd{scene.SpeechSpeed:F2}"
                                   : scene.NarrationText!,
                               scene.SpeakerId ?? defaultSpeakerId) ?? 1.0) + 2.0
                        : 3.0);

                // Chapter marker
                string chapterTitle = scene.HasNarration
                    ? (scene.NarrationText!.Length > 30
                        ? scene.NarrationText![..30] + "..."
                        : scene.NarrationText!)
                    : LocalizationService.GetString("Export.SceneLabel", i + 1);
                chapterTimes.Add((cumulativeDuration, chapterTitle));

                var scenePath = Path.Combine(tempDir, $"scene_{i:D4}.mp4");
                var style = getStyleForScene(scene);

                var success = sceneGen.GenerateScene(scene, scenePath, duration,
                    resolution, fps, audioPath, style, project.Watermark);

                if (!success)
                {
                    var msg = LocalizationService.GetString("Export.SceneFailed", i + 1);
                    progress.Report(msg);
                    result.ErrorMessage = msg;
                    return result;
                }

                scenePaths.Add(scenePath);
                cumulativeDuration += duration;

                // Add transition (use scene-level or project default)
                if (scenePaths.Count > 1)
                {
                    var transType = scene.TransitionType != TransitionType.None
                        ? scene.TransitionType
                        : project.DefaultTransition;
                    var transDur = scene.TransitionType != TransitionType.None
                        ? scene.TransitionDuration
                        : project.DefaultTransitionDuration;
                    transitions.Add((transType, transDur));
                }
            }

            // Step: Concatenate all scenes
            currentStep++;
            progress.Report($"[{currentStep}/{totalSteps}] {LocalizationService.GetString("Export.Combining")}");
            ct.ThrowIfCancellationRequested();

            bool concatOk;
            if (transitions.Any(t => t.Item1 != TransitionType.None))
            {
                concatOk = composer.ConcatWithTransitions(scenePaths, transitions, outputPath);
            }
            else
            {
                concatOk = composer.ConcatVideos(scenePaths, outputPath);
            }

            if (!concatOk)
            {
                var msg = LocalizationService.GetString("Export.CombineFailed");
                progress.Report(msg);
                result.ErrorMessage = msg;
                return result;
            }

            // Step: Add BGM
            if (project.Bgm?.HasBgm == true)
            {
                progress.Report(LocalizationService.GetString("Export.AddingBGM"));
                var withBgm = outputPath + ".bgm.mp4";
                var bgmOk = composer.AddBgm(outputPath, withBgm, project.Bgm);
                if (bgmOk)
                {
                    File.Delete(outputPath);
                    File.Move(withBgm, outputPath);
                }
            }

            result.Success = true;
            result.VideoPath = outputPath;

            // Step: Generate thumbnail
            if (project.GenerateThumbnail)
            {
                progress.Report(LocalizationService.GetString("Export.GeneratingThumbnail"));
                var thumbPath = Path.ChangeExtension(outputPath, ".jpg");
                if (sceneGen.ExtractThumbnail(outputPath, thumbPath, 1.0))
                {
                    result.ThumbnailPath = thumbPath;
                }
            }

            // Step: Generate chapter file
            if (project.GenerateChapters && chapterTimes.Count > 1)
            {
                progress.Report(LocalizationService.GetString("Export.GeneratingChapters"));
                var chapterPath = Path.ChangeExtension(outputPath, ".chapters.txt");
                WriteChapterFile(chapterPath, chapterTimes);
                result.ChapterFilePath = chapterPath;
            }

            // Step: Generate subtitle files (SRT + VTT)
            progress.Report(LocalizationService.GetString("Export.GeneratingSubtitles"));
            var srtPath = WriteSrtFile(outputPath, project, chapterTimes);
            if (srtPath != null)
            {
                result.SubtitleFilePath = srtPath;
                WriteVttFile(outputPath, project, chapterTimes);
            }

            // Step: Generate YouTube metadata
            progress.Report(LocalizationService.GetString("Export.GeneratingMetadata"));
            var metadataPath = Path.ChangeExtension(outputPath, ".metadata.txt");
            WriteYouTubeMetadata(metadataPath, project, chapterTimes);
            result.MetadataFilePath = metadataPath;

            // Clean up temp build directory
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Temp cleanup failed: {ex.Message}"); }

            progress.Report(LocalizationService.GetString("Export.Done"));
            return result;
        }

        /// <summary>
        /// Generates a preview for a single scene (no concat, no BGM).
        /// </summary>
        public bool GeneratePreview(
            Scene scene,
            string outputPath,
            string resolution,
            int fps,
            int defaultSpeakerId,
            TextStyle textStyle,
            IProgress<string> progress,
            CancellationToken ct)
        {
            progress.Report(LocalizationService.GetString("Export.Preview.Generating"));

            if (!_ffmpeg.CheckAvailable())
            {
                progress.Report(LocalizationService.GetString("Export.NoFFmpeg"));
                return false;
            }

            var sceneGen = new SceneGenerator(_ffmpeg);
            string? audioPath = null;

            if (scene.HasNarration && !scene.KeepOriginalAudio)
            {
                var sid = scene.SpeakerId ?? defaultSpeakerId;
                double speed = scene.SpeechSpeed;
                string cacheKey = Math.Abs(speed - 1.0) > 0.01
                    ? $"{scene.NarrationText!}__spd{speed:F2}"
                    : scene.NarrationText!;

                if (!_audioCache.Exists(cacheKey, sid))
                {
                    progress.Report(LocalizationService.GetString("Export.Preview.Audio"));
                    var audioData = _voiceVoxClient
                        .GenerateAudioAsync(scene.NarrationText!, sid, speed)
                        .GetAwaiter().GetResult();
                    audioPath = _audioCache.Save(cacheKey, sid, audioData);
                }
                else
                {
                    audioPath = _audioCache.GetCachePath(cacheKey, sid);
                }
            }

            double duration = scene.DurationMode == DurationMode.Fixed
                ? scene.FixedSeconds
                : (audioPath != null
                    ? (_audioCache.GetDuration(
                           Math.Abs(scene.SpeechSpeed - 1.0) > 0.01
                               ? $"{scene.NarrationText!}__spd{scene.SpeechSpeed:F2}"
                               : scene.NarrationText!,
                           scene.SpeakerId ?? defaultSpeakerId) ?? 1.0) + 2.0
                    : 3.0);

            progress.Report(LocalizationService.GetString("Export.Preview.Scene"));
            var success = sceneGen.GenerateScene(scene, outputPath, duration,
                resolution, fps, audioPath, textStyle);

            if (success)
                progress.Report(LocalizationService.GetString("Export.Preview.Done"));
            else
                progress.Report(LocalizationService.GetString("Export.Preview.Failed"));

            return success;
        }

        private static void WriteChapterFile(string path, List<(double StartTime, string Title)> chapters)
        {
            var sb = new StringBuilder();
            foreach (var (startTime, title) in chapters)
            {
                var ts = TimeSpan.FromSeconds(startTime);
                sb.AppendLine($"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2} {title}");
            }
            File.WriteAllText(path, sb.ToString());
        }

        private static void WriteYouTubeMetadata(
            string path, Project project, List<(double StartTime, string Title)> chapters)
        {
            var sb = new StringBuilder();
            sb.AppendLine(LocalizationService.GetString("Meta.Header"));
            sb.AppendLine();

            // Collect all narrations for analysis
            var allNarrations = project.Scenes
                .Where(s => s.HasNarration)
                .Select(s => s.NarrationText!)
                .ToList();

            // Title suggestion — smart extraction from narration content
            var firstNarration = allNarrations.FirstOrDefault()
                ?? LocalizationService.GetString("Meta.DefaultTitle");
            // Use scene title if available, otherwise extract from narration
            var titleScene = project.Scenes.FirstOrDefault(s => s.HasTitle);
            var titleSuggestion = titleScene?.Title ?? firstNarration;
            if (titleSuggestion.Length > 60)
                titleSuggestion = titleSuggestion[..60];
            sb.AppendLine(LocalizationService.GetString("Meta.TitleSuggestion"));
            sb.AppendLine(titleSuggestion);
            sb.AppendLine();

            // Description — auto-generated summary from narration
            sb.AppendLine(LocalizationService.GetString("Meta.Description"));
            var descriptionLines = new List<string>();
            foreach (var scene in project.Scenes.Where(s => s.HasNarration))
            {
                var text = scene.NarrationText!;
                // Extract first sentence as summary point
                var sentence = text.Split(new[] { '。', '！', '？', '.', '!', '?' },
                    StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
                if (!string.IsNullOrEmpty(sentence) && sentence.Length >= 5)
                    descriptionLines.Add(sentence);
            }
            if (descriptionLines.Count > 0)
            {
                // Use first narration sentence as lead
                sb.AppendLine(descriptionLines[0] + (descriptionLines[0].EndsWith('。') ? "" : "。"));
                sb.AppendLine();
                // Bullet points from subsequent scenes
                if (descriptionLines.Count > 1)
                {
                    foreach (var line in descriptionLines.Skip(1).Take(5))
                        sb.AppendLine($"- {line}");
                    sb.AppendLine();
                }
            }
            else
            {
                sb.AppendLine(LocalizationService.GetString("Meta.AutoGenerated"));
                sb.AppendLine();
            }

            // Chapter markers for YouTube
            if (chapters.Count > 1)
            {
                sb.AppendLine(LocalizationService.GetString("Meta.Chapters"));
                foreach (var (startTime, title) in chapters)
                {
                    var ts = TimeSpan.FromSeconds(startTime);
                    sb.AppendLine($"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2} {title}");
                }
                sb.AppendLine();
            }

            // Tags — smart keyword extraction
            sb.AppendLine(LocalizationService.GetString("Meta.Tags"));
            var tags = new List<string>
            {
                LocalizationService.GetString("Meta.Tag.Education"),
                LocalizationService.GetString("Meta.Tag.Training"),
                LocalizationService.GetString("Meta.Tag.Tutorial")
            };
            // Extract meaningful keywords from narrations (filter common particles/words)
            var commonWords = new HashSet<string>
            {
                "これ", "それ", "あれ", "この", "その", "ため", "こと", "もの",
                "する", "なる", "ある", "いる", "できる", "the", "and", "for", "with",
                "that", "this", "from", "have", "been", "will", "can", "are"
            };
            var narrations = allNarrations
                .SelectMany(s => s.Split(new[] { '、', '。', '！', '？', ' ', '　', '\n', '（', '）', '「', '」' },
                    StringSplitOptions.RemoveEmptyEntries))
                .Where(w => w.Length >= 2 && w.Length <= 15 && !commonWords.Contains(w))
                .GroupBy(w => w)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .Take(10);
            tags.AddRange(narrations);
            sb.AppendLine(string.Join(", ", tags));
            sb.AppendLine();

            // Hashtags for social media
            sb.AppendLine(LocalizationService.GetString("Meta.Hashtags"));
            var hashtags = tags.Take(8).Select(t => $"#{t.Replace(" ", "")}");
            sb.AppendLine(string.Join(" ", hashtags));
            sb.AppendLine();

            sb.AppendLine(LocalizationService.GetString("Meta.VideoInfo"));
            sb.AppendLine(LocalizationService.GetString("Meta.SceneCount", project.Scenes.Count));
            sb.AppendLine(LocalizationService.GetString("Meta.Resolution", project.Output.Resolution));
            sb.AppendLine(LocalizationService.GetString("Meta.GeneratedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm")));

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        /// <summary>
        /// Generates SRT subtitle file from scene narrations/subtitles with timing data.
        /// </summary>
        private static string? WriteSrtFile(
            string outputVideoPath, Project project,
            List<(double StartTime, string Title)> chapters)
        {
            var subtitleScenes = project.Scenes
                .Select((scene, index) => new { scene, index })
                .Where(s => s.scene.HasSubtitle || s.scene.HasNarration)
                .ToList();

            if (subtitleScenes.Count == 0)
                return null;

            var srtPath = Path.ChangeExtension(outputVideoPath, ".srt");
            var sb = new StringBuilder();
            int seqNum = 1;

            for (int i = 0; i < subtitleScenes.Count; i++)
            {
                var s = subtitleScenes[i];
                var text = s.scene.HasSubtitle ? s.scene.SubtitleText! : s.scene.NarrationText!;
                if (string.IsNullOrWhiteSpace(text)) continue;

                // Get timing from chapter markers
                double startTime = s.index < chapters.Count ? chapters[s.index].StartTime : 0;
                double endTime = (s.index + 1) < chapters.Count
                    ? chapters[s.index + 1].StartTime
                    : startTime + (s.scene.DurationMode == DurationMode.Fixed ? s.scene.FixedSeconds : 5.0);

                // Add small padding
                double padStart = startTime + 0.2;
                double padEnd = endTime - 0.3;
                if (padEnd <= padStart) padEnd = endTime;

                sb.AppendLine(seqNum.ToString());
                sb.AppendLine($"{FormatSrtTime(padStart)} --> {FormatSrtTime(padEnd)}");
                sb.AppendLine(text.Trim());
                sb.AppendLine();
                seqNum++;
            }

            File.WriteAllText(srtPath, sb.ToString(), Encoding.UTF8);
            return srtPath;
        }

        /// <summary>
        /// Generates VTT (WebVTT) subtitle file for web players.
        /// </summary>
        private static string? WriteVttFile(
            string outputVideoPath, Project project,
            List<(double StartTime, string Title)> chapters)
        {
            var subtitleScenes = project.Scenes
                .Select((scene, index) => new { scene, index })
                .Where(s => s.scene.HasSubtitle || s.scene.HasNarration)
                .ToList();

            if (subtitleScenes.Count == 0)
                return null;

            var vttPath = Path.ChangeExtension(outputVideoPath, ".vtt");
            var sb = new StringBuilder();
            sb.AppendLine("WEBVTT");
            sb.AppendLine();

            for (int i = 0; i < subtitleScenes.Count; i++)
            {
                var s = subtitleScenes[i];
                var text = s.scene.HasSubtitle ? s.scene.SubtitleText! : s.scene.NarrationText!;
                if (string.IsNullOrWhiteSpace(text)) continue;

                double startTime = s.index < chapters.Count ? chapters[s.index].StartTime : 0;
                double endTime = (s.index + 1) < chapters.Count
                    ? chapters[s.index + 1].StartTime
                    : startTime + (s.scene.DurationMode == DurationMode.Fixed ? s.scene.FixedSeconds : 5.0);

                double padStart = startTime + 0.2;
                double padEnd = endTime - 0.3;
                if (padEnd <= padStart) padEnd = endTime;

                sb.AppendLine($"{FormatVttTime(padStart)} --> {FormatVttTime(padEnd)}");
                sb.AppendLine(text.Trim());
                sb.AppendLine();
            }

            File.WriteAllText(vttPath, sb.ToString(), Encoding.UTF8);
            return vttPath;
        }

        private static string FormatSrtTime(double seconds)
        {
            var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2},{ts.Milliseconds:D3}";
        }

        private static string FormatVttTime(double seconds)
        {
            var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
        }

        private static bool IsVideoFile(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext is ".mp4" or ".avi" or ".mov" or ".wmv" or ".mkv";
        }
    }
}

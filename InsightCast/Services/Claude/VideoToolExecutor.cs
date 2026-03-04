using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using InsightCast.Models;
using InsightCast.Services;
using InsightCommon.AI;

namespace InsightCast.Services.Claude;

/// <summary>
/// Claude Tool Use のツール実行ロジック — シーンデータの読み書き + シーン構造変更 + 画像生成
/// </summary>
public class VideoToolExecutor : IToolExecutor
{
    private readonly Func<List<Scene>> _getScenes;
    private readonly Action<int, Action<Scene>> _updateScene;
    private readonly Dispatcher _dispatcher;
    private readonly ThumbnailService _thumbnailService;
    private readonly Action<int?> _addScene;
    private readonly Action<int> _removeScene;
    private readonly Action<int, int> _moveScene;
    private readonly Func<string>? _getOpenAIApiKey;

    /// <summary>
    /// 最後に生成されたサムネイルのパス（UI表示用）
    /// </summary>
    public string? LastGeneratedThumbnailPath { get; private set; }

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="getScenes">現在のシーン一覧を取得するデリゲート</param>
    /// <param name="updateScene">シーンをUIスレッドで更新するデリゲート (index, action)</param>
    /// <param name="dispatcher">UIスレッドディスパッチャー</param>
    /// <param name="thumbnailService">サムネイル生成サービス</param>
    /// <param name="addScene">シーン追加デリゲート (insert_at? null=末尾)</param>
    /// <param name="removeScene">シーン削除デリゲート (index)</param>
    /// <param name="moveScene">シーン移動デリゲート (from, to)</param>
    /// <param name="getOpenAIApiKey">OpenAI APIキー取得デリゲート</param>
    public VideoToolExecutor(
        Func<List<Scene>> getScenes,
        Action<int, Action<Scene>> updateScene,
        Dispatcher dispatcher,
        ThumbnailService thumbnailService,
        Action<int?> addScene,
        Action<int> removeScene,
        Action<int, int> moveScene,
        Func<string>? getOpenAIApiKey = null)
    {
        _getScenes = getScenes;
        _updateScene = updateScene;
        _dispatcher = dispatcher;
        _thumbnailService = thumbnailService;
        _addScene = addScene;
        _removeScene = removeScene;
        _moveScene = moveScene;
        _getOpenAIApiKey = getOpenAIApiKey;
    }

    /// <summary>
    /// IToolExecutor.ExecuteAsync — ツール名に応じて実行し、結果を返す
    /// </summary>
    public async Task<ToolExecutionResult> ExecuteAsync(string toolName, JsonElement input, CancellationToken ct)
    {
        try
        {
            var content = toolName switch
            {
                "get_scenes" => ExecuteGetScenes(),
                "set_scene_narration" => ExecuteSetSceneNarration(input),
                "set_scene_subtitle" => ExecuteSetSceneSubtitle(input),
                "get_project_summary" => ExecuteGetProjectSummary(),
                "set_multiple_scenes" => ExecuteSetMultipleScenes(input),
                "get_pptx_notes" => ExecuteGetPptxNotes(),
                "generate_thumbnail" => ExecuteGenerateThumbnail(input),
                "add_scene" => ExecuteAddScene(input),
                "remove_scene" => ExecuteRemoveScene(input),
                "move_scene" => ExecuteMoveScene(input),
                "set_scene_media" => ExecuteSetSceneMedia(input),
                "generate_scene_image" => await ExecuteGenerateSceneImageAsync(input, ct),
                "generate_ab_thumbnails" => ExecuteGenerateAbThumbnails(input),
                "add_cta_endcard" => ExecuteAddCtaEndcard(input),
                _ => JsonSerializer.Serialize(new { error = $"Unknown tool: {toolName}" }),
            };
            return new ToolExecutionResult { Content = content };
        }
        catch (Exception ex)
        {
            return new ToolExecutionResult
            {
                Content = JsonSerializer.Serialize(new { error = ex.Message }),
                IsError = true,
            };
        }
    }

    private string ExecuteGetScenes()
    {
        var scenes = _getScenes();
        var result = scenes.Select((s, i) => new
        {
            index = i,
            title = s.Title ?? $"Scene {i + 1}",
            narration = s.NarrationText ?? "",
            subtitle = s.SubtitleText ?? "",
            hasMedia = s.HasMedia,
            mediaPath = s.MediaPath ?? "",
            durationMode = s.DurationMode.ToString(),
            fixedSeconds = s.FixedSeconds,
        }).ToList();

        return JsonSerializer.Serialize(new { scenes = result });
    }

    private string ExecuteSetSceneNarration(JsonElement input)
    {
        var index = input.GetProperty("scene_index").GetInt32();
        var text = input.GetProperty("narration_text").GetString() ?? "";

        return _dispatcher.Invoke(() =>
        {
            var scenes = _getScenes();
            if (index < 0 || index >= scenes.Count)
                return JsonSerializer.Serialize(new { error = $"Scene index {index} out of range (0-{scenes.Count - 1})" });

            _updateScene(index, s => s.NarrationText = text);
            return JsonSerializer.Serialize(new { success = true, scene_index = index, narration_length = text.Length });
        });
    }

    private string ExecuteSetSceneSubtitle(JsonElement input)
    {
        var index = input.GetProperty("scene_index").GetInt32();
        var text = input.GetProperty("subtitle_text").GetString() ?? "";

        return _dispatcher.Invoke(() =>
        {
            var scenes = _getScenes();
            if (index < 0 || index >= scenes.Count)
                return JsonSerializer.Serialize(new { error = $"Scene index {index} out of range (0-{scenes.Count - 1})" });

            _updateScene(index, s => s.SubtitleText = text);
            return JsonSerializer.Serialize(new { success = true, scene_index = index, subtitle_length = text.Length });
        });
    }

    private string ExecuteGetProjectSummary()
    {
        var scenes = _getScenes();
        var result = new
        {
            totalScenes = scenes.Count,
            scenesWithMedia = scenes.Count(s => s.HasMedia),
            scenesWithNarration = scenes.Count(s => s.HasNarration),
            scenesWithSubtitle = scenes.Count(s => s.HasSubtitle),
            scenesWithOverlays = scenes.Count(s => s.HasTextOverlays),
        };

        return JsonSerializer.Serialize(result);
    }

    private string ExecuteSetMultipleScenes(JsonElement input)
    {
        var updates = input.GetProperty("updates");

        // Collect all updates first, then apply atomically on UI thread
        var updateList = new List<(int Index, string? Narration, string? Subtitle)>();
        foreach (var update in updates.EnumerateArray())
        {
            var index = update.GetProperty("scene_index").GetInt32();
            string? narr = update.TryGetProperty("narration_text", out var narrProp) ? narrProp.GetString() : null;
            string? sub = update.TryGetProperty("subtitle_text", out var subProp) ? subProp.GetString() : null;
            updateList.Add((index, narr, sub));
        }

        return _dispatcher.Invoke(() =>
        {
            var scenes = _getScenes();
            var results = new List<object>();

            foreach (var (index, narration, subtitle) in updateList)
            {
                if (index < 0 || index >= scenes.Count)
                {
                    results.Add(new { scene_index = index, error = "out of range" });
                    continue;
                }

                _updateScene(index, s =>
                {
                    if (narration != null) s.NarrationText = narration;
                    if (subtitle != null) s.SubtitleText = subtitle;
                });
                results.Add(new { scene_index = index, success = true });
            }

            return JsonSerializer.Serialize(new { updated = results.Count, results });
        });
    }

    private string ExecuteGetPptxNotes()
    {
        var scenes = _getScenes();
        var notes = scenes.Select((s, i) => new
        {
            index = i,
            title = s.Title ?? $"Scene {i + 1}",
            narration = s.NarrationText ?? "",
            additionalInstructions = s.AIGeneration?.AdditionalInstructions ?? "",
        }).ToList();

        return JsonSerializer.Serialize(new { scenes = notes });
    }

    private string ExecuteGenerateThumbnail(JsonElement input)
    {
        var patternStr = input.GetProperty("pattern").GetString() ?? "PowerWord";
        var mainText = input.GetProperty("main_text").GetString() ?? "";

        var styleStr = "BusinessImpact";
        if (input.TryGetProperty("style", out var styleProp))
            styleStr = styleProp.GetString() ?? "BusinessImpact";

        var subText = "";
        if (input.TryGetProperty("sub_text", out var subProp))
            subText = subProp.GetString() ?? "";

        var subSubText = "";
        if (input.TryGetProperty("sub_sub_text", out var subSubProp))
            subSubText = subSubProp.GetString() ?? "";

        if (!Enum.TryParse<ThumbnailPattern>(patternStr, true, out var pattern))
            return JsonSerializer.Serialize(new { error = $"Unknown pattern: {patternStr}" });

        if (!Enum.TryParse<ThumbnailStyle>(styleStr, true, out var style))
            return JsonSerializer.Serialize(new { error = $"Unknown style: {styleStr}" });

        var settings = new ThumbnailSettings
        {
            Pattern = pattern,
            MainText = mainText,
            SubText = subText,
            SubSubText = subSubText,
        };

        // Apply style preset
        ThumbnailService.ApplyStylePreset(settings, style);

        // Set background image from scene media if specified
        if (input.TryGetProperty("background_image_scene_index", out var bgProp))
        {
            var bgIndex = bgProp.GetInt32();
            var scenes = _getScenes();
            if (bgIndex >= 0 && bgIndex < scenes.Count && !string.IsNullOrEmpty(scenes[bgIndex].MediaPath))
                settings.BackgroundImagePath = scenes[bgIndex].MediaPath;
        }

        // Generate to temp folder
        var tempDir = Path.Combine(Path.GetTempPath(), "InsightCast", "Thumbnails");
        Directory.CreateDirectory(tempDir);
        var outputPath = Path.Combine(tempDir, $"thumb_{DateTime.Now:yyyyMMdd_HHmmss}_{patternStr}.png");

        _thumbnailService.GenerateThumbnail(settings, outputPath);
        LastGeneratedThumbnailPath = outputPath;

        return JsonSerializer.Serialize(new
        {
            success = true,
            output_path = outputPath,
            width = 1280,
            height = 720,
            pattern = patternStr,
            style = styleStr,
        });
    }

    // ── 新ツール: シーン構造変更 ──

    private string ExecuteAddScene(JsonElement input)
    {
        int? insertAt = null;
        if (input.TryGetProperty("insert_at", out var insertProp))
            insertAt = insertProp.GetInt32();

        _dispatcher.Invoke(() => _addScene(insertAt));

        var scenes = _getScenes();
        var newIndex = insertAt.HasValue
            ? Math.Min(insertAt.Value, scenes.Count - 1)
            : scenes.Count - 1;

        return JsonSerializer.Serialize(new
        {
            success = true,
            new_scene_index = newIndex,
            total_scenes = scenes.Count,
        });
    }

    private string ExecuteRemoveScene(JsonElement input)
    {
        var index = input.GetProperty("scene_index").GetInt32();

        return _dispatcher.Invoke(() =>
        {
            var scenes = _getScenes();
            if (index < 0 || index >= scenes.Count)
                return JsonSerializer.Serialize(new { error = $"Scene index {index} out of range (0-{scenes.Count - 1})" });
            if (scenes.Count <= 1)
                return JsonSerializer.Serialize(new { error = "Cannot remove the last scene" });

            _removeScene(index);
            return JsonSerializer.Serialize(new
            {
                success = true,
                removed_index = index,
                total_scenes = _getScenes().Count,
            });
        });
    }

    private string ExecuteMoveScene(JsonElement input)
    {
        var fromIndex = input.GetProperty("from_index").GetInt32();
        var toIndex = input.GetProperty("to_index").GetInt32();

        if (fromIndex == toIndex)
            return JsonSerializer.Serialize(new { error = "from_index and to_index are the same" });

        return _dispatcher.Invoke(() =>
        {
            var scenes = _getScenes();
            if (fromIndex < 0 || fromIndex >= scenes.Count)
                return JsonSerializer.Serialize(new { error = $"from_index {fromIndex} out of range (0-{scenes.Count - 1})" });
            if (toIndex < 0 || toIndex >= scenes.Count)
                return JsonSerializer.Serialize(new { error = $"to_index {toIndex} out of range (0-{scenes.Count - 1})" });

            _moveScene(fromIndex, toIndex);
            return JsonSerializer.Serialize(new
            {
                success = true,
                from_index = fromIndex,
                to_index = toIndex,
                total_scenes = _getScenes().Count,
            });
        });
    }

    private static readonly HashSet<string> AllowedMediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tiff", ".tif",
        ".mp4", ".avi", ".mov", ".wmv", ".mkv", ".webm",
    };

    private string ExecuteSetSceneMedia(JsonElement input)
    {
        var index = input.GetProperty("scene_index").GetInt32();
        var mediaPath = input.GetProperty("media_path").GetString() ?? "";

        // Validate file extension to prevent arbitrary file references
        var ext = Path.GetExtension(mediaPath);
        if (!AllowedMediaExtensions.Contains(ext))
            return JsonSerializer.Serialize(new { error = $"Unsupported file type: {ext}" });

        if (!File.Exists(mediaPath))
            return JsonSerializer.Serialize(new { error = $"File not found: {mediaPath}" });

        return _dispatcher.Invoke(() =>
        {
            var scenes = _getScenes();
            if (index < 0 || index >= scenes.Count)
                return JsonSerializer.Serialize(new { error = $"Scene index {index} out of range (0-{scenes.Count - 1})" });

            _updateScene(index, s =>
            {
                s.MediaPath = mediaPath;
                s.MediaType = MediaType.Image;
            });
            return JsonSerializer.Serialize(new
            {
                success = true,
                scene_index = index,
                media_path = mediaPath,
            });
        });
    }

    private async Task<string> ExecuteGenerateSceneImageAsync(JsonElement input, CancellationToken ct)
    {
        var index = input.GetProperty("scene_index").GetInt32();
        var prompt = input.GetProperty("prompt").GetString() ?? "";

        var size = "1024x1024";
        if (input.TryGetProperty("size", out var sizeProp))
            size = sizeProp.GetString() ?? "1024x1024";

        // Pre-validate scene index on UI thread before expensive API call
        var rangeError = _dispatcher.Invoke(() =>
        {
            var scenes = _getScenes();
            if (index < 0 || index >= scenes.Count)
                return $"Scene index {index} out of range (0-{scenes.Count - 1})";
            return (string?)null;
        });
        if (rangeError != null)
            return JsonSerializer.Serialize(new { error = rangeError });

        var apiKey = _getOpenAIApiKey?.Invoke();
        if (string.IsNullOrEmpty(apiKey))
            return JsonSerializer.Serialize(new { error = "OpenAI API key is not set. Please configure it in the AI Assistant panel." });

        using var dalle = new DalleService(apiKey);
        var imagePath = await dalle.GenerateImageAsync(prompt, "dall-e-3", size, ct);

        // Set generated image as scene media (re-validate on UI thread)
        return _dispatcher.Invoke(() =>
        {
            var scenes = _getScenes();
            if (index < 0 || index >= scenes.Count)
                return JsonSerializer.Serialize(new { error = $"Scene index {index} no longer valid after image generation" });

            _updateScene(index, s =>
            {
                s.MediaPath = imagePath;
                s.MediaType = MediaType.Image;
            });
            return JsonSerializer.Serialize(new
            {
                success = true,
                scene_index = index,
                media_path = imagePath,
                prompt,
                size,
            });
        });
    }

    private string ExecuteGenerateAbThumbnails(JsonElement input)
    {
        var mainText = input.GetProperty("main_text").GetString() ?? "";
        var subText = input.TryGetProperty("sub_text", out var st) ? st.GetString() ?? "" : "";
        var subSubText = input.TryGetProperty("sub_sub_text", out var sst) ? sst.GetString() ?? "" : "";

        var settings = new ThumbnailSettings
        {
            MainText = mainText,
            SubText = subText,
            SubSubText = subSubText,
        };

        // Use scene 0 media as background if available
        var scenes = _getScenes();
        if (scenes.Count > 0 && scenes[0].HasMedia && scenes[0].MediaType == MediaType.Image)
            settings.BackgroundImagePath = scenes[0].MediaPath;

        var outputDir = Path.Combine(Path.GetTempPath(), "InsightCast", "ab_thumbnails", Guid.NewGuid().ToString("N")[..8]);
        var results = _thumbnailService.GenerateQuickAbTest(settings, outputDir, "thumbnail");

        // Set last generated for UI preview
        if (results.Count > 0)
            LastGeneratedThumbnailPath = results[0];

        return JsonSerializer.Serialize(new
        {
            success = true,
            count = results.Count,
            output_directory = outputDir,
            files = results,
        });
    }

    private string ExecuteAddCtaEndcard(JsonElement input)
    {
        var ctaText = input.TryGetProperty("cta_text", out var ct2) ? ct2.GetString() : null;
        var linkText = input.TryGetProperty("link_text", out var lt) ? lt.GetString() : null;

        return _dispatcher.Invoke(() =>
        {
            _addScene(null); // Add at end
            var scenes = _getScenes();
            var lastIndex = scenes.Count - 1;

            _updateScene(lastIndex, s =>
            {
                s.Title = LocalizationService.GetString("CTA.ThankYou");
                s.DurationMode = DurationMode.Fixed;
                s.FixedSeconds = 5.0;
                s.TextOverlays = TextOverlay.CreateEndcardSet(ctaText, linkText);
            });

            return JsonSerializer.Serialize(new
            {
                success = true,
                scene_index = lastIndex,
                overlays = scenes[lastIndex].TextOverlays.Count,
            });
        });
    }
}

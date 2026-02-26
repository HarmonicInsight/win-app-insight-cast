using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using InsightCast.Models;
using InsightCast.Services.OpenAI;
using InsightCast.Video;
using InsightCast.VoiceVox;

namespace InsightCast.Services.Batch
{
    /// <summary>
    /// バッチエクスポート処理を行うサービス。
    /// </summary>
    public class BatchExportService : IBatchExportService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly ExportService _exportService;
        private readonly IOpenAIService? _openAIService;

        /// <inheritdoc />
        public event EventHandler<BatchProgress>? ProgressChanged;

        /// <inheritdoc />
        public event EventHandler<BatchProjectResult>? ProjectCompleted;

        /// <summary>
        /// BatchExportServiceを作成します。
        /// </summary>
        public BatchExportService(
            FFmpegWrapper ffmpeg,
            VoiceVoxClient voiceVoxClient,
            AudioCache audioCache,
            IOpenAIService? openAIService = null)
        {
            _exportService = new ExportService(ffmpeg, voiceVoxClient, audioCache);
            _openAIService = openAIService;
        }

        /// <inheritdoc />
        public async Task<BatchResult> ExecuteBatchAsync(
            BatchConfig config,
            int defaultSpeakerId,
            Func<Scene, TextStyle> getStyleForScene,
            IProgress<BatchProgress> progress,
            CancellationToken ct)
        {
            var result = new BatchResult
            {
                BatchName = config.BatchName,
                StartTime = DateTime.Now,
                TotalProjects = config.Projects.Count
            };

            // OpenAI APIキー設定
            if (_openAIService != null && !string.IsNullOrEmpty(config.GlobalSettings.OpenAIApiKey))
            {
                var apiKey = ApiKeyManager.ResolveApiKeyReference(config.GlobalSettings.OpenAIApiKey);
                if (!string.IsNullOrEmpty(apiKey))
                {
                    await _openAIService.ConfigureAsync(apiKey, ct);
                }
            }

            // 出力ディレクトリ作成
            if (!string.IsNullOrEmpty(config.GlobalSettings.OutputDirectory))
            {
                Directory.CreateDirectory(config.GlobalSettings.OutputDirectory);
            }

            for (int i = 0; i < config.Projects.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var projectItem = config.Projects[i];
                var projectStart = DateTime.Now;

                var batchProgress = new BatchProgress
                {
                    CurrentProjectIndex = i,
                    TotalProjects = config.Projects.Count,
                    CurrentProjectName = Path.GetFileNameWithoutExtension(projectItem.ProjectFile),
                    Phase = BatchPhase.Loading
                };
                progress.Report(batchProgress);
                ProgressChanged?.Invoke(this, batchProgress);

                try
                {
                    // プロジェクトJSONをロード
                    var project = ImportProjectFromJson(projectItem.ProjectFile);

                    // オーバーライド適用
                    ApplyOverrides(project, projectItem.Overrides);

                    // AI生成が必要なシーンを処理
                    if (_openAIService?.IsConfigured == true && project.Scenes.Any(s => s.RequiresAIGeneration))
                    {
                        batchProgress.Phase = BatchPhase.AIGeneration;
                        batchProgress.CurrentMessage = LocalizationService.GetString("Batch.AIGeneration");
                        progress.Report(batchProgress);
                        ProgressChanged?.Invoke(this, batchProgress);

                        await ProcessAIGenerationAsync(project, ct);
                    }

                    // 出力パス決定
                    var outputPath = DetermineOutputPath(config, projectItem);

                    // エクスポート実行
                    batchProgress.Phase = BatchPhase.Exporting;
                    batchProgress.CurrentMessage = LocalizationService.GetString("Batch.Exporting");
                    progress.Report(batchProgress);
                    ProgressChanged?.Invoke(this, batchProgress);

                    var exportProgress = new Progress<string>(msg =>
                    {
                        batchProgress.CurrentMessage = msg;
                        progress.Report(batchProgress);
                        ProgressChanged?.Invoke(this, batchProgress);
                    });

                    var exportResult = _exportService.ExportFull(
                        project,
                        outputPath,
                        project.Output.Resolution,
                        project.Output.Fps,
                        defaultSpeakerId,
                        getStyleForScene,
                        exportProgress,
                        ct);

                    var projectResult = new BatchProjectResult
                    {
                        ProjectFile = projectItem.ProjectFile,
                        OutputPath = outputPath,
                        Success = exportResult.Success,
                        ErrorMessage = exportResult.ErrorMessage,
                        Duration = DateTime.Now - projectStart
                    };

                    if (exportResult.Success)
                    {
                        result.SuccessCount++;
                        result.SuccessfulProjects.Add(projectResult);
                    }
                    else
                    {
                        result.FailCount++;
                        result.FailedProjects.Add(projectResult);

                        if (!config.GlobalSettings.ContinueOnError)
                            break;
                    }

                    ProjectCompleted?.Invoke(this, projectResult);
                }
                catch (Exception ex)
                {
                    var projectResult = new BatchProjectResult
                    {
                        ProjectFile = projectItem.ProjectFile,
                        Success = false,
                        ErrorMessage = ex.Message,
                        Duration = DateTime.Now - projectStart
                    };

                    result.FailCount++;
                    result.FailedProjects.Add(projectResult);
                    ProjectCompleted?.Invoke(this, projectResult);

                    if (!config.GlobalSettings.ContinueOnError)
                        break;
                }
            }

            result.EndTime = DateTime.Now;
            return result;
        }

        /// <inheritdoc />
        public async Task<ExportResult> ImportAndExportAsync(
            string projectJsonPath,
            string outputPath,
            int defaultSpeakerId,
            Func<Scene, TextStyle> getStyleForScene,
            IProgress<string> progress,
            CancellationToken ct)
        {
            progress.Report(LocalizationService.GetString("Batch.LoadingProject"));
            var project = ImportProjectFromJson(projectJsonPath);

            // AI生成処理
            if (_openAIService?.IsConfigured == true && project.Scenes.Any(s => s.RequiresAIGeneration))
            {
                progress.Report(LocalizationService.GetString("Batch.AIGeneration"));
                await ProcessAIGenerationAsync(project, ct);
            }

            progress.Report(LocalizationService.GetString("Batch.Exporting"));
            return _exportService.ExportFull(
                project,
                outputPath,
                project.Output.Resolution,
                project.Output.Fps,
                defaultSpeakerId,
                getStyleForScene,
                progress,
                ct);
        }

        /// <inheritdoc />
        public Project ImportProjectFromJson(string jsonPath)
        {
            if (!File.Exists(jsonPath))
                throw new FileNotFoundException(
                    LocalizationService.GetString("Batch.FileNotFound", jsonPath), jsonPath);

            var json = File.ReadAllText(jsonPath);
            var project = JsonSerializer.Deserialize<Project>(json, JsonOptions);

            if (project == null)
                throw new InvalidOperationException(
                    LocalizationService.GetString("Batch.DeserializeFailed"));

            // ファイルパスの解決（相対パス対応）
            var baseDir = Path.GetDirectoryName(Path.GetFullPath(jsonPath))!;
            ResolveFilePaths(project, baseDir);

            // シーンが空の場合はデフォルトを追加
            if (project.Scenes.Count == 0)
            {
                project.InitializeDefaultScenes();
            }

            return project;
        }

        /// <inheritdoc />
        public BatchConfig LoadBatchConfig(string configPath)
        {
            if (!File.Exists(configPath))
                throw new FileNotFoundException(
                    LocalizationService.GetString("Batch.FileNotFound", configPath), configPath);

            var json = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<BatchConfig>(json, JsonOptions);

            if (config == null)
                throw new InvalidOperationException(
                    LocalizationService.GetString("Batch.DeserializeFailed"));

            // プロジェクトファイルパスの解決（相対パス対応）
            var baseDir = Path.GetDirectoryName(Path.GetFullPath(configPath))!;
            foreach (var item in config.Projects)
            {
                if (!Path.IsPathRooted(item.ProjectFile))
                {
                    item.ProjectFile = Path.GetFullPath(Path.Combine(baseDir, item.ProjectFile));
                }
            }

            // 出力ディレクトリの解決
            if (!string.IsNullOrEmpty(config.GlobalSettings.OutputDirectory) &&
                !Path.IsPathRooted(config.GlobalSettings.OutputDirectory))
            {
                config.GlobalSettings.OutputDirectory = Path.GetFullPath(
                    Path.Combine(baseDir, config.GlobalSettings.OutputDirectory));
            }

            return config;
        }

        private async Task ProcessAIGenerationAsync(Project project, CancellationToken ct)
        {
            if (_openAIService == null || !_openAIService.IsConfigured)
                return;

            foreach (var scene in project.Scenes)
            {
                ct.ThrowIfCancellationRequested();

                var aiSettings = scene.AIGeneration;
                if (aiSettings == null)
                    continue;

                // ナレーション生成
                if (aiSettings.CanGenerateNarration)
                {
                    var request = new TextGenerationRequest
                    {
                        Topic = aiSettings.NarrationTopic!,
                        Style = aiSettings.NarrationStyle,
                        TargetDurationSeconds = aiSettings.TargetDurationSeconds,
                        AdditionalInstructions = aiSettings.AdditionalInstructions
                    };

                    var result = await _openAIService.GenerateNarrationAsync(request, ct);
                    if (result.Success)
                    {
                        scene.NarrationText = result.Text;
                        if (string.IsNullOrEmpty(scene.SubtitleText))
                            scene.SubtitleText = result.Text;
                    }
                    else
                    {
                        Debug.WriteLine($"AI narration generation failed: {result.ErrorMessage}");
                    }
                }

                // 画像生成
                if (aiSettings.CanGenerateImage)
                {
                    var request = new ImageGenerationRequest
                    {
                        Description = aiSettings.ImageDescription!,
                        Style = aiSettings.ImageStyle
                    };

                    var result = await _openAIService.GenerateImageAsync(request, ct);
                    if (result.Success)
                    {
                        scene.MediaPath = result.ImagePath;
                        scene.MediaType = MediaType.Image;
                    }
                    else
                    {
                        Debug.WriteLine($"AI image generation failed: {result.ErrorMessage}");
                    }
                }
            }
        }

        private static void ResolveFilePaths(Project project, string baseDir)
        {
            // シーンのメディアパス
            foreach (var scene in project.Scenes)
            {
                if (!string.IsNullOrEmpty(scene.MediaPath) && !Path.IsPathRooted(scene.MediaPath))
                {
                    scene.MediaPath = Path.GetFullPath(Path.Combine(baseDir, scene.MediaPath));
                }
            }

            // BGMパス
            if (project.Bgm?.HasBgm == true && !string.IsNullOrEmpty(project.Bgm.FilePath) &&
                !Path.IsPathRooted(project.Bgm.FilePath))
            {
                project.Bgm.FilePath = Path.GetFullPath(Path.Combine(baseDir, project.Bgm.FilePath));
            }

            // ウォーターマークパス
            if (project.Watermark?.HasWatermark == true && !string.IsNullOrEmpty(project.Watermark.ImagePath) &&
                !Path.IsPathRooted(project.Watermark.ImagePath))
            {
                project.Watermark.ImagePath = Path.GetFullPath(Path.Combine(baseDir, project.Watermark.ImagePath));
            }

        }

        private static void ApplyOverrides(Project project, BatchOverrides? overrides)
        {
            if (overrides == null)
                return;

            if (!string.IsNullOrEmpty(overrides.Resolution))
                project.Output.Resolution = overrides.Resolution;

            if (overrides.Fps.HasValue)
                project.Output.Fps = overrides.Fps.Value;
        }

        private static string DetermineOutputPath(BatchConfig config, BatchProjectItem projectItem)
        {
            var outputName = projectItem.OutputName
                ?? $"{Path.GetFileNameWithoutExtension(projectItem.ProjectFile)}.mp4";

            if (!string.IsNullOrEmpty(config.GlobalSettings.OutputDirectory))
            {
                return Path.Combine(config.GlobalSettings.OutputDirectory, outputName);
            }

            var projectDir = Path.GetDirectoryName(projectItem.ProjectFile)!;
            return Path.Combine(projectDir, outputName);
        }
    }
}

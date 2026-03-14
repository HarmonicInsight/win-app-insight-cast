using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using InsightCast.Models;
using InsightCast.TTS;
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

        /// <inheritdoc />
        public event EventHandler<BatchProgress>? ProgressChanged;

        /// <inheritdoc />
        public event EventHandler<BatchProjectResult>? ProjectCompleted;

        /// <summary>
        /// BatchExportServiceを作成します。
        /// </summary>
        public BatchExportService(
            FFmpegWrapper ffmpeg,
            ITtsEngine ttsEngine,
            AudioCache audioCache,
            NarrationDictionary? narrationDictionary = null)
        {
            _exportService = new ExportService(ffmpeg, ttsEngine, audioCache);
            _exportService.NarrationDictionary = narrationDictionary;
        }

        // 既存コード互換
        public BatchExportService(
            FFmpegWrapper ffmpeg,
            VoiceVoxClient voiceVoxClient,
            AudioCache audioCache)
            : this(ffmpeg, new VoiceVoxTtsAdapter(voiceVoxClient), audioCache)
        {
        }

        /// <inheritdoc />
        public Task<BatchResult> ExecuteBatchAsync(
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
            return Task.FromResult(result);
        }

        /// <inheritdoc />
        public Task<ExportResult> ImportAndExportAsync(
            string projectJsonPath,
            string outputPath,
            int defaultSpeakerId,
            Func<Scene, TextStyle> getStyleForScene,
            IProgress<string> progress,
            CancellationToken ct)
        {
            progress.Report(LocalizationService.GetString("Batch.LoadingProject"));
            var project = ImportProjectFromJson(projectJsonPath);

            progress.Report(LocalizationService.GetString("Batch.Exporting"));
            return Task.FromResult(_exportService.ExportFull(
                project,
                outputPath,
                project.Output.Resolution,
                project.Output.Fps,
                defaultSpeakerId,
                getStyleForScene,
                progress,
                ct));
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

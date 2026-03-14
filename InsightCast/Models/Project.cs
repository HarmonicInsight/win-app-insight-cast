using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace InsightCast.Models
{
    public class OutputSettings
    {
        [JsonPropertyName("resolution")]
        public string Resolution { get; set; } = "1920x1080";

        [JsonPropertyName("fps")]
        public int Fps { get; set; } = 30;

        [JsonPropertyName("outputPath")]
        public string? OutputPath { get; set; }
    }

    public class ProjectSettings
    {
        [JsonPropertyName("voicevoxBaseUrl")]
        public string VoicevoxBaseUrl { get; set; } = "http://127.0.0.1:50021";

        [JsonPropertyName("voicevoxRunExe")]
        public string? VoicevoxRunExe { get; set; }

        [JsonPropertyName("ffmpegPath")]
        public string? FfmpegPath { get; set; }

        [JsonPropertyName("fontPath")]
        public string? FontPath { get; set; }
    }

    public class Project
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        [JsonPropertyName("projectPath")]
        public string? ProjectPath { get; set; }

        [JsonPropertyName("scenes")]
        public List<Scene> Scenes { get; set; } = new();

        [JsonPropertyName("output")]
        public OutputSettings Output { get; set; } = new();

        [JsonPropertyName("settings")]
        public ProjectSettings Settings { get; set; } = new();

        [JsonPropertyName("bgm")]
        public BGMSettings Bgm { get; set; } = new();

        [JsonPropertyName("watermark")]
        public WatermarkSettings Watermark { get; set; } = new();

        [JsonPropertyName("defaultTransition")]
        public TransitionType DefaultTransition { get; set; } = TransitionType.Fade;

        [JsonPropertyName("defaultTransitionDuration")]
        public double DefaultTransitionDuration { get; set; } = 0.5;

        [JsonPropertyName("motionIntensity")]
        public MotionIntensity MotionIntensity { get; set; } = MotionIntensity.Medium;

        [JsonPropertyName("generateThumbnail")]
        public bool GenerateThumbnail { get; set; } = true;

        [JsonPropertyName("generateChapters")]
        public bool GenerateChapters { get; set; } = true;

        [JsonPropertyName("generateSubtitleFile")]
        public bool GenerateSubtitleFile { get; set; } = true;

        [JsonPropertyName("generateMetadata")]
        public bool GenerateMetadata { get; set; } = true;

        [JsonPropertyName("thumbnailGenerator")]
        public ThumbnailGeneratorSettings ThumbnailGenerator { get; set; } = new();

        [JsonPropertyName("workingFolderPath")]
        public string? WorkingFolderPath { get; set; }

        [JsonPropertyName("checkedFiles")]
        public List<string>? CheckedFiles { get; set; }

        [JsonPropertyName("defaultSubtitleFontSize")]
        public int DefaultSubtitleFontSize { get; set; } = 28;

        /// <summary>字幕を黒帯（レターボックス）に表示するか。false=映像上に重ねる。</summary>
        [JsonPropertyName("subtitleLetterbox")]
        public bool SubtitleLetterbox { get; set; } = true;

        /// <summary>プロジェクトのデフォルトナレーター（話者 StyleId）。null の場合はアプリ設定を使用。</summary>
        [JsonPropertyName("defaultSpeakerId")]
        public int? DefaultSpeakerId { get; set; }

        /// <summary>インポート元のファイルパス（PPTX, DOCX 等）。</summary>
        [JsonPropertyName("sourcePath")]
        public string? SourcePath { get; set; }

        [JsonPropertyName("aiMemory")]
        public InsightCommon.AI.AiMemoryHotCache? AiMemory { get; set; }

        public Project()
        {
        }

        [JsonIgnore]
        public int TotalScenes => Scenes.Count;

        [JsonIgnore]
        public bool IsValid => Scenes.Count > 0 && Scenes.Any(s => s.HasMedia || s.HasNarration);

        public void InitializeDefaultScenes()
        {
            Scenes.Clear();
            Scenes.Add(new Scene());
            Scenes.Add(new Scene());
        }

        public Scene AddScene(int? index = null)
        {
            var scene = new Scene();
            if (index.HasValue && index.Value >= 0 && index.Value <= Scenes.Count)
            {
                Scenes.Insert(index.Value, scene);
            }
            else
            {
                Scenes.Add(scene);
            }
            return scene;
        }

        public bool RemoveScene(int index)
        {
            if (Scenes.Count <= 1)
                return false;

            if (index < 0 || index >= Scenes.Count)
                return false;

            Scenes.RemoveAt(index);
            return true;
        }

        public bool MoveScene(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || fromIndex >= Scenes.Count)
                return false;
            if (toIndex < 0 || toIndex >= Scenes.Count)
                return false;
            if (fromIndex == toIndex)
                return false;

            var scene = Scenes[fromIndex];
            Scenes.RemoveAt(fromIndex);
            Scenes.Insert(toIndex, scene);
            return true;
        }

        public Scene? GetScene(int index)
        {
            if (index < 0 || index >= Scenes.Count)
                return null;
            return Scenes[index];
        }

        /// <summary>
        /// Creates a deep copy of this project for thread-safe background processing.
        /// </summary>
        public Project Clone()
        {
            var json = JsonSerializer.Serialize(this, SerializerOptions);
            var clone = JsonSerializer.Deserialize<Project>(json, SerializerOptions)
                ?? throw new InvalidOperationException("Failed to clone project.");
            clone.ProjectPath = ProjectPath;
            return clone;
        }

        public void Save(string? path = null)
        {
            var savePath = path ?? ProjectPath;
            if (string.IsNullOrEmpty(savePath))
                throw new InvalidOperationException("Save path is not specified.");

            ProjectPath = savePath;

            // 一時ディレクトリにある素材ファイルをプロジェクトフォルダにコピー
            ConsolidateMedia(savePath);

            var json = JsonSerializer.Serialize(this, SerializerOptions);
            Core.Config.AtomicWriteText(savePath, json);
        }

        /// <summary>
        /// 一時フォルダ（Temp, LocalAppData\cache）にある素材ファイルを
        /// プロジェクトファイルと同じディレクトリの media サブフォルダにコピーし、
        /// パスを更新する。
        /// </summary>
        private void ConsolidateMedia(string projectPath)
        {
            var projectDir = Path.GetDirectoryName(projectPath);
            if (string.IsNullOrEmpty(projectDir)) return;

            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            var mediaDir = Path.Combine(projectDir, $"{projectName}_media");

            var tempBase = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            foreach (var scene in Scenes)
            {
                if (string.IsNullOrEmpty(scene.MediaPath) || !File.Exists(scene.MediaPath))
                    continue;

                var mediaPath = Path.GetFullPath(scene.MediaPath);

                // 素材が一時ディレクトリにある場合のみコピー
                bool isTemp = mediaPath.StartsWith(tempBase, StringComparison.OrdinalIgnoreCase)
                           || mediaPath.StartsWith(Path.Combine(localAppData, "InsightCast", "cache"), StringComparison.OrdinalIgnoreCase);

                if (!isTemp) continue;

                // 既にプロジェクトフォルダにある場合はスキップ
                if (mediaPath.StartsWith(projectDir, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    if (!Directory.Exists(mediaDir))
                        Directory.CreateDirectory(mediaDir);

                    var fileName = Path.GetFileName(mediaPath);
                    var destPath = Path.Combine(mediaDir, fileName);

                    // ファイル名の衝突回避
                    if (File.Exists(destPath) && !FilesAreEqual(mediaPath, destPath))
                    {
                        var baseName = Path.GetFileNameWithoutExtension(fileName);
                        var ext = Path.GetExtension(fileName);
                        int counter = 1;
                        do
                        {
                            destPath = Path.Combine(mediaDir, $"{baseName}_{counter}{ext}");
                            counter++;
                        } while (File.Exists(destPath));
                    }

                    if (!File.Exists(destPath))
                        File.Copy(mediaPath, destPath);

                    scene.MediaPath = destPath;
                }
                catch
                {
                    // コピー失敗時は元のパスを維持（ベストエフォート）
                }
            }
        }

        private static bool FilesAreEqual(string path1, string path2)
        {
            var info1 = new FileInfo(path1);
            var info2 = new FileInfo(path2);
            return info1.Length == info2.Length;
        }

        public static Project Load(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Project file not found: {path}");

            var json = File.ReadAllText(path);
            var project = JsonSerializer.Deserialize<Project>(json, SerializerOptions);

            if (project == null)
                throw new InvalidOperationException("Failed to deserialize project file.");

            project.ProjectPath = path;

            if (project.Scenes.Count == 0)
            {
                project.InitializeDefaultScenes();
            }

            return project;
        }
    }
}

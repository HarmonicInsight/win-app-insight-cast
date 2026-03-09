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

            var json = JsonSerializer.Serialize(this, SerializerOptions);
            Core.Config.AtomicWriteText(savePath, json);
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

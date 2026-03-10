using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using InsightCast.Models;

namespace InsightCast.Services
{
    public class ProjectTemplate
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [JsonPropertyName("bgm")]
        public BGMSettings Bgm { get; set; } = new();

        [JsonPropertyName("watermark")]
        public WatermarkSettings Watermark { get; set; } = new();

        [JsonPropertyName("output")]
        public OutputSettings Output { get; set; } = new();

        [JsonPropertyName("defaultTransition")]
        public TransitionType DefaultTransition { get; set; } = TransitionType.Fade;

        [JsonPropertyName("defaultTransitionDuration")]
        public double DefaultTransitionDuration { get; set; } = 0.5;

        [JsonPropertyName("generateThumbnail")]
        public bool GenerateThumbnail { get; set; } = true;

        [JsonPropertyName("generateChapters")]
        public bool GenerateChapters { get; set; } = true;

        [JsonPropertyName("defaultSubtitleStyleId")]
        public string? DefaultSubtitleStyleId { get; set; }

        [JsonPropertyName("thumbnailGenerator")]
        public ThumbnailGeneratorSettings ThumbnailGenerator { get; set; } = new();
    }

    public static class TemplateService
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public static string TemplateDirectory
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "InsightCast", "Templates");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        private static T DeepCopy<T>(T obj) where T : class
        {
            var json = JsonSerializer.Serialize(obj);
            return JsonSerializer.Deserialize<T>(json)!;
        }

        public static ProjectTemplate CreateFromProject(Project project, string name, string description = "")
        {
            return new ProjectTemplate
            {
                Name = name,
                Description = description,
                CreatedAt = DateTime.Now,
                Bgm = DeepCopy(project.Bgm),
                Watermark = DeepCopy(project.Watermark),
                Output = DeepCopy(project.Output),
                DefaultTransition = project.DefaultTransition,
                DefaultTransitionDuration = project.DefaultTransitionDuration,
                GenerateThumbnail = project.GenerateThumbnail,
                GenerateChapters = project.GenerateChapters,
                ThumbnailGenerator = DeepCopy(project.ThumbnailGenerator)
            };
        }

        public static void ApplyToProject(ProjectTemplate template, Project project)
        {
            project.Bgm = DeepCopy(template.Bgm);
            project.Watermark = DeepCopy(template.Watermark);
            project.Output = DeepCopy(template.Output);
            project.DefaultTransition = template.DefaultTransition;
            project.DefaultTransitionDuration = template.DefaultTransitionDuration;
            project.GenerateThumbnail = template.GenerateThumbnail;
            project.GenerateChapters = template.GenerateChapters;
            project.ThumbnailGenerator = DeepCopy(template.ThumbnailGenerator);
        }

        public static void SaveTemplate(ProjectTemplate template)
        {
            var safeName = string.Join("_", template.Name.Split(Path.GetInvalidFileNameChars()));
            var path = Path.Combine(TemplateDirectory, $"{safeName}.json");
            var json = JsonSerializer.Serialize(template, Options);
            File.WriteAllText(path, json);
        }

        public static List<ProjectTemplate> LoadAllTemplates()
        {
            var templates = new List<ProjectTemplate>();
            if (!Directory.Exists(TemplateDirectory))
                return templates;

            foreach (var file in Directory.GetFiles(TemplateDirectory, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var template = JsonSerializer.Deserialize<ProjectTemplate>(json, Options);
                    if (template != null)
                        templates.Add(template);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.TraceWarning($"[WARN] Template load failed: {file}: {ex.Message}");
                }
            }

            return templates.OrderByDescending(t => t.CreatedAt).ToList();
        }

        public static bool DeleteTemplate(string name)
        {
            var safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
            var path = Path.Combine(TemplateDirectory, $"{safeName}.json");
            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }
            return false;
        }
    }
}

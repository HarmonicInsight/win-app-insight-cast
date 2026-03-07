using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace InsightCast.Models
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum MediaType
    {
        Image,
        Video,
        None
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DurationMode
    {
        Auto,
        Fixed
    }

    public class Scene
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("mediaPath")]
        public string? MediaPath { get; set; }

        [JsonPropertyName("mediaType")]
        public MediaType MediaType { get; set; } = MediaType.None;

        [JsonPropertyName("narrationText")]
        public string? NarrationText { get; set; }

        [JsonPropertyName("subtitleText")]
        public string? SubtitleText { get; set; }

        [JsonPropertyName("speakerId")]
        public int? SpeakerId { get; set; }

        [JsonPropertyName("subtitleFontSize")]
        public int? SubtitleFontSize { get; set; }

        [JsonPropertyName("keepOriginalAudio")]
        public bool KeepOriginalAudio { get; set; } = false;

        [JsonPropertyName("subtitleStyleId")]
        public string? SubtitleStyleId { get; set; }

        [JsonPropertyName("transitionType")]
        public TransitionType TransitionType { get; set; } = TransitionType.None;

        [JsonPropertyName("transitionDuration")]
        public double TransitionDuration { get; set; } = TransitionSettings.DEFAULT_TRANSITION_DURATION;

        [JsonPropertyName("durationMode")]
        public DurationMode DurationMode { get; set; } = DurationMode.Auto;

        [JsonPropertyName("fixedSeconds")]
        public double FixedSeconds { get; set; } = 3.0;

        [JsonPropertyName("audioCachePath")]
        public string? AudioCachePath { get; set; }

        [JsonPropertyName("textOverlays")]
        public List<TextOverlay> TextOverlays { get; set; } = new();

        [JsonPropertyName("speechSpeed")]
        public double SpeechSpeed { get; set; } = 1.2;

        [JsonPropertyName("motionType")]
        public MotionType MotionType { get; set; } = MotionType.None;

        /// <summary>
        /// シーンのタイトル（企画段階での見出し）。
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        /// <summary>
        /// AI生成設定。ナレーションや画像をAIで生成する場合に使用。
        /// </summary>
        [JsonPropertyName("aiGeneration")]
        public AIGenerationSettings? AIGeneration { get; set; }

        [JsonIgnore]
        public bool HasMedia => !string.IsNullOrEmpty(MediaPath);

        [JsonIgnore]
        public bool HasTextOverlays => TextOverlays.Count > 0 && TextOverlays.Any(o => o.HasText);

        [JsonIgnore]
        public bool HasNarration => !string.IsNullOrEmpty(NarrationText);

        [JsonIgnore]
        public bool HasSubtitle => !string.IsNullOrEmpty(SubtitleText);

        [JsonIgnore]
        public bool HasTitle => !string.IsNullOrEmpty(Title);

        /// <summary>
        /// AI生成が必要かどうかを判定します。
        /// </summary>
        [JsonIgnore]
        public bool RequiresAIGeneration => AIGeneration?.RequiresGeneration == true;

        public Scene()
        {
        }

    }
}

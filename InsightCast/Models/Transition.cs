using System.Collections.Generic;
using System.Text.Json.Serialization;
using InsightCast.Services;

namespace InsightCast.Models
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TransitionType
    {
        None,
        Fade,
        Dissolve,
        WipeLeft,
        WipeRight,
        SlideLeft,
        SlideRight,
        ZoomIn
    }

    public static class TransitionNames
    {
        public static Dictionary<TransitionType, string> DisplayNames => new()
        {
            { TransitionType.None, LocalizationService.GetString("Transition.Type.None") },
            { TransitionType.Fade, LocalizationService.GetString("Transition.Type.Fade") },
            { TransitionType.Dissolve, LocalizationService.GetString("Transition.Type.Dissolve") },
            { TransitionType.WipeLeft, LocalizationService.GetString("Transition.Type.WipeLeft") },
            { TransitionType.WipeRight, LocalizationService.GetString("Transition.Type.WipeRight") },
            { TransitionType.SlideLeft, LocalizationService.GetString("Transition.Type.SlideLeft") },
            { TransitionType.SlideRight, LocalizationService.GetString("Transition.Type.SlideRight") },
            { TransitionType.ZoomIn, LocalizationService.GetString("Transition.Type.ZoomIn") }
        };
    }

    public static class FfmpegTransitionMap
    {
        public static readonly Dictionary<TransitionType, string?> FilterNames = new()
        {
            { TransitionType.None, null },
            { TransitionType.Fade, "fade" },
            { TransitionType.Dissolve, "dissolve" },
            { TransitionType.WipeLeft, "wipeleft" },
            { TransitionType.WipeRight, "wiperight" },
            { TransitionType.SlideLeft, "slideleft" },
            { TransitionType.SlideRight, "slideright" },
            { TransitionType.ZoomIn, "zoomin" }
        };
    }

    public class TransitionSettings
    {
        public const double DEFAULT_TRANSITION_DURATION = 0.5;

        [JsonPropertyName("type")]
        public TransitionType Type { get; set; } = TransitionType.None;

        [JsonPropertyName("duration")]
        public double Duration { get; set; } = DEFAULT_TRANSITION_DURATION;

        public TransitionSettings()
        {
        }

        public TransitionSettings(TransitionType type, double duration = DEFAULT_TRANSITION_DURATION)
        {
            Type = type;
            Duration = duration;
        }

        [JsonIgnore]
        public string DisplayName =>
            TransitionNames.DisplayNames.TryGetValue(Type, out var name) ? name : Type.ToString();

        [JsonIgnore]
        public string? FfmpegName =>
            FfmpegTransitionMap.FilterNames.TryGetValue(Type, out var name) ? name : null;

        [JsonIgnore]
        public bool HasTransition => Type != TransitionType.None;

    }

    public static class PresetTransitions
    {
        public static List<(TransitionType Type, string Name, string Description)> PRESET_TRANSITIONS => new()
        {
            (TransitionType.None, LocalizationService.GetString("Transition.Preset.None.Name"), LocalizationService.GetString("Transition.Preset.None.Desc")),
            (TransitionType.Fade, LocalizationService.GetString("Transition.Preset.Fade.Name"), LocalizationService.GetString("Transition.Preset.Fade.Desc")),
            (TransitionType.Dissolve, LocalizationService.GetString("Transition.Preset.Dissolve.Name"), LocalizationService.GetString("Transition.Preset.Dissolve.Desc")),
            (TransitionType.WipeLeft, LocalizationService.GetString("Transition.Preset.WipeLeft.Name"), LocalizationService.GetString("Transition.Preset.WipeLeft.Desc")),
            (TransitionType.WipeRight, LocalizationService.GetString("Transition.Preset.WipeRight.Name"), LocalizationService.GetString("Transition.Preset.WipeRight.Desc")),
            (TransitionType.SlideLeft, LocalizationService.GetString("Transition.Preset.SlideLeft.Name"), LocalizationService.GetString("Transition.Preset.SlideLeft.Desc")),
            (TransitionType.SlideRight, LocalizationService.GetString("Transition.Preset.SlideRight.Name"), LocalizationService.GetString("Transition.Preset.SlideRight.Desc")),
            (TransitionType.ZoomIn, LocalizationService.GetString("Transition.Preset.ZoomIn.Name"), LocalizationService.GetString("Transition.Preset.ZoomIn.Desc"))
        };
    }
}

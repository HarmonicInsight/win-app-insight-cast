using System.Collections.ObjectModel;
using InsightCast.Models;
using InsightCast.Services.Claude;
using InsightCommon.AI;

namespace InsightCast.ViewModels;

public class PromptDisplayItem
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool IsUserPrompt { get; set; }
    public int UseCount { get; set; }
    public System.DateTime? LastUsedAt { get; set; }
    public UserPrompt? Source { get; set; }

    public static PromptDisplayItem FromPreset(PresetPrompt p, string lang)
    {
        return new PromptDisplayItem
        {
            Id = p.Id,
            Label = p.GetLabel(lang),
            Icon = p.Icon,
            Prompt = p.GetPrompt(lang),
            Category = p.GetCategory(lang),
            IsUserPrompt = false
        };
    }

    public static PromptDisplayItem FromUserPrompt(UserPrompt u)
    {
        return new PromptDisplayItem
        {
            Id = u.Id,
            Label = u.Label,
            Icon = u.Icon,
            Prompt = u.Prompt,
            Category = u.Category,
            IsUserPrompt = true,
            UseCount = u.UseCount,
            LastUsedAt = u.LastUsedAt,
            Source = u
        };
    }
}

/// <summary>
/// Group of preset prompts by category for ToggleButton-based collapsible sections.
/// </summary>
public class PresetPromptGroupVm
{
    public string CategoryName { get; set; } = string.Empty;
    public ObservableCollection<PresetPromptVm> Prompts { get; set; } = new();
}

/// <summary>
/// Group of user prompts by category.
/// </summary>
public class UserPromptGroupVm
{
    public string CategoryName { get; set; } = string.Empty;
    public ObservableCollection<UserPromptVm> Prompts { get; set; } = new();
}

/// <summary>
/// Individual preset prompt chip display item.
/// </summary>
public class PresetPromptVm
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Tooltip { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public int RecommendedModelIndex { get; set; }
    public string ModelDisplay { get; set; } = string.Empty;
    public InsightCastPresetPrompt? Source { get; set; }
}

/// <summary>
/// Individual user prompt chip display item.
/// </summary>
public class UserPromptVm
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Tooltip { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public bool IsFavorite { get; set; }
    public string ModelDisplay { get; set; } = string.Empty;
    public UserPrompt? Source { get; set; }
}

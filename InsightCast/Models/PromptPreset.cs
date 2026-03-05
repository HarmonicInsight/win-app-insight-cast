using System;

namespace InsightCast.Models;

/// <summary>
/// AIアシスタント用プロンプトプリセットのデータモデル。
/// </summary>
public class PromptPreset
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string SystemPrompt { get; set; } = "";
    public string Description { get; set; } = "";
    public string Author { get; set; } = "";
    public string Category { get; set; } = "";
    public string? Mode { get; set; }
    public bool IsDefault { get; set; }
    public bool IsPinned { get; set; }
    public int UsageCount { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime ModifiedAt { get; set; } = DateTime.Now;

    public bool HasCategory => !string.IsNullOrWhiteSpace(Category);

    public PromptPreset Duplicate()
    {
        return new PromptPreset
        {
            Id = Services.PromptPresetService.GenerateId(),
            Name = Name + " (Copy)",
            SystemPrompt = SystemPrompt,
            Description = Description,
            Author = Author,
            Category = Category,
            IsDefault = false,
            CreatedAt = DateTime.Now,
            ModifiedAt = DateTime.Now,
        };
    }
}

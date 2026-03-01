using System;
using System.Text.Json.Serialization;

namespace InsightCast.Models;

public class UserPrompt
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("icon")]
    public string Icon { get; set; } = "\U0001F4CC"; // 📌

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    [JsonPropertyName("useCount")]
    public int UseCount { get; set; }

    [JsonPropertyName("lastUsedAt")]
    public DateTime? LastUsedAt { get; set; }

    [JsonPropertyName("sourcePresetId")]
    public string? SourcePresetId { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "check";

    [JsonPropertyName("recommendedPersonaId")]
    public string RecommendedPersonaId { get; set; } = "megumi";

    [JsonPropertyName("requiresContextData")]
    public bool RequiresContextData { get; set; } = true;

    [JsonPropertyName("isImageMode")]
    public bool IsImageMode { get; set; }

    [JsonPropertyName("imageSize")]
    public string ImageSize { get; set; } = "1024x1024";
}

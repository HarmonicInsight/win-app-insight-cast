using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using InsightCast.Models.Report;

namespace InsightCast.Services.Report;

/// <summary>
/// 埋め込みリソースから report-config.json を読み込み、テンプレート一覧・プラン制限等を提供する。
/// </summary>
public class ReportConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private ReportConfig? _config;
    private bool _loaded;

    public ReportConfig GetConfig()
    {
        if (_loaded) return _config ?? new ReportConfig();

        _loaded = true;
        try
        {
            var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("report-config.json", StringComparison.OrdinalIgnoreCase));

            if (resourceName == null) return _config = new ReportConfig();

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) return _config = new ReportConfig();

            _config = JsonSerializer.Deserialize<ReportConfig>(stream, JsonOptions) ?? new ReportConfig();
        }
        catch
        {
            _config = new ReportConfig();
        }

        return _config;
    }

    public List<ReportTemplate> GetTemplates() => GetConfig().Templates;

    public ReportTemplate? GetTemplateById(string templateId)
        => GetConfig().Templates.FirstOrDefault(t => t.Id == templateId);

    public ReportPlanLimits? GetLimits(string plan)
    {
        var config = GetConfig();
        if (config.LimitsByPlan != null && config.LimitsByPlan.TryGetValue(plan, out var limits))
            return limits;
        return null;
    }

    public string GetSystemPromptExtension(string locale = "ja")
    {
        var config = GetConfig();
        return locale == "en"
            ? (config.SystemPromptExtension?.En ?? "")
            : (config.SystemPromptExtension?.Ja ?? "");
    }

    public string GetRevisionPromptExtension(string locale = "ja")
    {
        var config = GetConfig();
        return locale == "en"
            ? (config.RevisionPromptExtension?.En ?? "")
            : (config.RevisionPromptExtension?.Ja ?? "");
    }

    public List<ReportOutputDestination> GetOutputDestinations()
    {
        var config = GetConfig();
        return (config.OutputDestinations ?? [])
            .Where(d => d.Availability == "always")
            .ToList();
    }
}

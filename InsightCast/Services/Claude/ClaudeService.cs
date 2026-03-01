using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InsightCast.Core;
using InsightCommon.AI;

namespace InsightCast.Services.Claude;

/// <summary>
/// ClaudeApiClient のラッパーサービス — Config からの初期化・モデル永続化
/// </summary>
public class ClaudeService : IClaudeService
{
    private readonly ClaudeApiClient _client = new();
    private readonly Config _config;

    public bool IsConfigured => _client.IsConfigured;
    public string CurrentModel => _client.CurrentModel;

    public ClaudeService(Config config)
    {
        _config = config;

        var apiKey = config.ClaudeApiKey;
        if (!string.IsNullOrEmpty(apiKey))
            _client.SetApiKey(apiKey);

        _client.SetModelByIndex(config.ClaudeModelIndex);
    }

    public void SetApiKey(string apiKey)
    {
        _client.SetApiKey(apiKey);
        _config.ClaudeApiKey = apiKey;
    }

    public void SetModelByIndex(int index)
    {
        _client.SetModelByIndex(index);
        _config.ClaudeModelIndex = index;
    }

    public Task<string> SendMessageAsync(string userMessage, string? systemContext = null, CancellationToken ct = default)
        => _client.SendMessageAsync(userMessage, systemContext, ct);

    public Task<string> SendChatAsync(List<AiMessage> history, string userMessage, string? systemContext = null, CancellationToken ct = default)
        => _client.SendChatAsync(history, userMessage, systemContext, ct);

    public Task<ClaudeToolResponse> SendMessageWithToolsAsync(List<object> messages, List<ToolDefinition> tools, string? systemContext = null, CancellationToken ct = default)
        => _client.SendMessageWithToolsAsync(messages, tools, systemContext, ct);
}

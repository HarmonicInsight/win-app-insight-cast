using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InsightCast.Core;
using InsightCommon.AI;

namespace InsightCast.Services.Claude;

/// <summary>
/// ClaudeApiClient のラッパーサービス — AiService に委譲。
/// IClaudeService インターフェースは後方互換のため変更しない。
/// </summary>
public class ClaudeService : IClaudeService
{
    private readonly AiService _aiService;
    private readonly Config _config;

    public bool IsConfigured => _aiService.IsConfigured;
    public string CurrentModel => _aiService.CurrentModel;

    /// <summary>共通 AiService インスタンス（設定ダイアログ等で利用）</summary>
    public AiService AiService => _aiService;

    public ClaudeService(Config config)
    {
        _config = config;
        _aiService = new AiService("INMV");

        // 旧 Config から API キーを移行（AiProviderConfig に未設定の場合のみ）
        if (!_aiService.IsConfigured)
        {
            var legacyKey = config.ClaudeApiKey;
            if (!string.IsNullOrEmpty(legacyKey))
                _aiService.SetApiKey(legacyKey);
        }

        // 旧 Config のモデルインデックスを適用（AiProviderConfig にモデル未設定の場合のみ）
        if (string.IsNullOrEmpty(_aiService.Config.ModelId))
            _aiService.SetModelByIndex(config.ClaudeModelIndex);
    }

    public void SetApiKey(string apiKey)
    {
        _aiService.SetApiKey(apiKey);
        _config.ClaudeApiKey = apiKey;
    }

    public void SetModelByIndex(int index)
    {
        _aiService.SetModelByIndex(index);
        _config.ClaudeModelIndex = index;
    }

    public Task<string> SendMessageAsync(string userMessage, string? systemContext = null, CancellationToken ct = default)
        => _aiService.SendMessageAsync(userMessage, systemContext, ct);

    public Task<string> SendChatAsync(List<AiMessage> history, string userMessage, string? systemContext = null, CancellationToken ct = default)
        => _aiService.SendChatAsync(history, userMessage, systemContext, ct);

    public async Task<ClaudeToolResponse> SendMessageWithToolsAsync(List<object> messages, List<ToolDefinition> tools, string? systemContext = null, CancellationToken ct = default)
    {
        var response = await _aiService.SendWithToolsAsync(messages, tools, systemContext, null, ct);
        return response.ToClaudeToolResponse();
    }
}

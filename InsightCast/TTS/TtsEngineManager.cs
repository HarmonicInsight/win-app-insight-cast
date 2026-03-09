namespace InsightCast.TTS;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using InsightCast.Core;
using InsightCast.VoiceVox;

/// <summary>
/// TTS エンジンの選択・切替・フォールバックを管理するクラス。
///
/// 優先順位:
/// 1. ユーザーが設定で選択したエンジン
/// 2. 選択エンジンが利用不可の場合、自動フォールバック:
///    EdgeNeural → WindowsOneCore
///    VoiceVox → EdgeNeural → WindowsOneCore
/// </summary>
public class TtsEngineManager : IDisposable
{
    private readonly Dictionary<TtsEngineType, ITtsEngine> _engines = new();
    private ITtsEngine _activeEngine;
    private readonly Config _config;
    private EngineLauncher? _engineLauncher;

    public TtsEngineManager(Config config, VoiceVoxClient? voiceVoxClient = null)
    {
        _config = config;

        // 全エンジンを登録
        _engines[TtsEngineType.EdgeNeural] = new EdgeTtsClient();
        _engines[TtsEngineType.WindowsOneCore] = new WindowsTtsClient();

        if (voiceVoxClient != null)
            _engines[TtsEngineType.VoiceVox] = new VoiceVoxTtsAdapter(voiceVoxClient);

        // 設定からエンジンを選択
        var preferred = config.TtsEngineType;
        _activeEngine = _engines.TryGetValue(preferred, out var engine) ? engine : _engines[TtsEngineType.EdgeNeural];
    }

    /// <summary>現在アクティブなエンジン</summary>
    public ITtsEngine ActiveEngine => _activeEngine;

    /// <summary>利用可能な全エンジン</summary>
    public IReadOnlyDictionary<TtsEngineType, ITtsEngine> Engines => _engines;

    /// <summary>
    /// エンジンを切替え。設定にも保存する。
    /// </summary>
    public void SwitchEngine(TtsEngineType type)
    {
        if (_engines.TryGetValue(type, out var engine))
        {
            _activeEngine = engine;
            _config.TtsEngineType = type;
        }
    }

    /// <summary>
    /// アクティブエンジンの接続チェック。失敗時は自動フォールバック。
    /// </summary>
    /// <returns>(接続結果文字列, 最終的に使用するエンジン種別)</returns>
    public async Task<(string Status, TtsEngineType FinalEngine)> CheckAndFallbackAsync()
    {
        // まずアクティブエンジンを試行
        var result = await _activeEngine.CheckConnectionAsync();
        if (result != null)
            return (result, _activeEngine.EngineType);

        // フォールバックチェーン
        var fallbackOrder = _activeEngine.EngineType switch
        {
            TtsEngineType.VoiceVox => new[] { TtsEngineType.EdgeNeural, TtsEngineType.WindowsOneCore },
            TtsEngineType.EdgeNeural => new[] { TtsEngineType.WindowsOneCore },
            _ => Array.Empty<TtsEngineType>(),
        };

        foreach (var fallbackType in fallbackOrder)
        {
            if (!_engines.TryGetValue(fallbackType, out var fallbackEngine))
                continue;

            result = await fallbackEngine.CheckConnectionAsync();
            if (result != null)
            {
                _activeEngine = fallbackEngine;
                return (result, fallbackType);
            }
        }

        // 全エンジン利用不可
        return ("TTS エンジンが利用できません", _activeEngine.EngineType);
    }

    /// <summary>
    /// VOICEVOX エンジンが起動していなければ自動起動を試みる。
    /// 接続チェック → 失敗なら EngineLauncher で起動。
    /// </summary>
    /// <returns>true: 接続可能, false: 起動失敗</returns>
    public async Task<bool> EnsureVoiceVoxRunningAsync()
    {
        if (!_engines.ContainsKey(TtsEngineType.VoiceVox))
            return false;

        // 既に接続できる場合は何もしない
        var status = await _engines[TtsEngineType.VoiceVox].CheckConnectionAsync();
        if (status != null)
            return true;

        // エンジンを自動起動
        try
        {
            _engineLauncher?.Dispose();
            _engineLauncher = new EngineLauncher(_config.EnginePath);
            return await _engineLauncher.Launch();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// VoiceVox アダプターから VoiceVoxClient を取得（既存コードとの互換用）。
    /// </summary>
    public VoiceVoxClient? GetVoiceVoxClient()
    {
        if (_engines.TryGetValue(TtsEngineType.VoiceVox, out var engine) && engine is VoiceVoxTtsAdapter adapter)
            return adapter.InnerClient;
        return null;
    }

    public void Dispose()
    {
        _engineLauncher?.Dispose();
        foreach (var engine in _engines.Values)
        {
            if (engine is IDisposable d)
                d.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}

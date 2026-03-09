namespace InsightCast.TTS;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using InsightCast.VoiceVox;

/// <summary>
/// 既存の VoiceVoxClient を ITtsEngine インターフェースに適合させるアダプター。
///
/// 【企業向け制約事項】
/// - VOICEVOX エンジン（約 1GB）の別途インストールが必要
/// - キャラクター音声のため、音声ごとに利用規約が異なる（商用利用可否を要確認）
/// - テキストデータは一切外部に送信されない（完全ローカル処理）
/// - GPU 搭載 PC では高速合成が可能（CPU のみでも動作）
/// - 30 以上の音声キャラクター・感情バリエーション対応
/// </summary>
public class VoiceVoxTtsAdapter : ITtsEngine
{
    private readonly VoiceVoxClient _client;

    public VoiceVoxTtsAdapter(VoiceVoxClient client)
    {
        _client = client;
    }

    /// <summary>アダプター内部の VoiceVoxClient（既存コードとの互換用）</summary>
    public VoiceVoxClient InnerClient => _client;

    public TtsEngineType EngineType => TtsEngineType.VoiceVox;

    public string DisplayName => "VOICEVOX (高品質・オフライン)";

    public IReadOnlyList<string> Constraints { get; } = new[]
    {
        "VOICEVOX エンジン（約 1GB）の別途インストールが必要です",
        "音声キャラクターごとに利用規約が異なります（商用利用時は各キャラクターの規約を確認してください）",
        "テキストデータは一切外部に送信されません（完全ローカル処理）",
        "GPU 搭載 PC では高速合成、CPU のみでも動作します",
        "30 以上の音声キャラクター・感情バリエーションに対応しています",
    };

    public async Task<string?> CheckConnectionAsync()
    {
        return await _client.CheckConnectionAsync();
    }

    public async Task<List<TtsSpeaker>> GetSpeakersAsync()
    {
        var speakers = new List<TtsSpeaker>();
        var rawSpeakers = await _client.GetSpeakersAsync();

        foreach (var speaker in rawSpeakers)
        {
            if (!speaker.TryGetProperty("name", out var nameProp)) continue;
            var rawName = nameProp.GetString() ?? "Unknown";
            var displaySpeakerName = VoiceVoxClient.GetLocalizedSpeakerName(rawName);

            if (!speaker.TryGetProperty("styles", out var styles)) continue;

            foreach (var style in styles.EnumerateArray())
            {
                if (!style.TryGetProperty("id", out var idProp)) continue;
                var styleId = idProp.GetInt32();
                var rawStyleName = style.TryGetProperty("name", out var snProp)
                    ? snProp.GetString() ?? "ノーマル" : "ノーマル";
                var styleName = VoiceVoxClient.GetLocalizedStyleName(rawStyleName);

                speakers.Add(new TtsSpeaker
                {
                    Id = styleId.ToString(),
                    DisplayName = $"{displaySpeakerName} ({styleName})",
                    Gender = "",
                    StyleId = styleId,
                });
            }
        }

        return speakers;
    }

    public async Task<byte[]> GenerateAudioAsync(string text, string speakerId, double speedScale = 1.0)
    {
        if (!int.TryParse(speakerId, out var sid))
            sid = 13; // デフォルト: 青山龍星

        return await _client.GenerateAudioAsync(text, sid, speedScale);
    }
}

namespace InsightCast.TTS;

using System;
using System.Collections.Generic;
using System.IO;
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

        if (!NarrationPauseHelper.HasPauseMarkers(text))
            return await _client.GenerateAudioAsync(text, sid, speedScale);

        // ポーズ記法がある場合: テキストを分割し、無音を挿入して連結
        var segments = NarrationPauseHelper.Parse(text);
        return await GenerateWithPausesAsync(segments, sid, speedScale);
    }

    private async Task<byte[]> GenerateWithPausesAsync(
        List<NarrationPauseHelper.Segment> segments, int speakerId, double speedScale)
    {
        using var ms = new MemoryStream();
        bool headerWritten = false;

        foreach (var seg in segments)
        {
            byte[] audio;

            if (seg.IsPause)
            {
                audio = GenerateSilentWav(seg.PauseSeconds);
            }
            else
            {
                audio = await _client.GenerateAudioAsync(seg.Text, speakerId, speedScale);
            }

            if (audio.Length < 44) continue;

            if (!headerWritten)
            {
                ms.Write(audio, 0, audio.Length);
                headerWritten = true;
            }
            else
            {
                // WAV ヘッダー(44バイト)をスキップして data 部分のみ追加
                ms.Write(audio, 44, audio.Length - 44);
            }
        }

        var result = ms.ToArray();
        if (result.Length > 44)
            UpdateWavHeader(result);
        return result;
    }

    private static byte[] GenerateSilentWav(double seconds)
    {
        int sampleRate = 24000;
        int bitsPerSample = 16;
        int channels = 1;
        int numSamples = (int)(sampleRate * seconds);
        int dataSize = numSamples * channels * (bitsPerSample / 8);

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + dataSize);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);
        bw.Write((short)1);
        bw.Write((short)channels);
        bw.Write(sampleRate);
        bw.Write(sampleRate * channels * (bitsPerSample / 8));
        bw.Write((short)(channels * (bitsPerSample / 8)));
        bw.Write((short)bitsPerSample);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        bw.Write(dataSize);
        bw.Write(new byte[dataSize]);

        return ms.ToArray();
    }

    private static void UpdateWavHeader(byte[] wav)
    {
        if (wav.Length < 44) return;
        var fileSize = wav.Length - 8;
        var dataSize = wav.Length - 44;
        BitConverter.GetBytes(fileSize).CopyTo(wav, 4);
        BitConverter.GetBytes(dataSize).CopyTo(wav, 40);
    }
}

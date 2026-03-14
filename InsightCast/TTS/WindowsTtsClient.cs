namespace InsightCast.TTS;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Speech.Synthesis;
using System.Text;
using System.Threading.Tasks;

/// <summary>
/// Windows 標準 TTS クライアント。System.Speech.Synthesis (SAPI) を使用。
/// 追加インストール不要・完全オフライン動作。
///
/// 【企業向け制約事項】
/// - 音声品質は Edge Neural / VOICEVOX に比べて劣る（規則合成方式）
/// - 日本語音声は Ayumi / Haruka / Ichiro / Sayaka の 4 種（Windows バージョンにより異なる）
/// - テキストデータは一切外部に送信されない（完全ローカル処理）
/// - 追加ソフトウェアのインストールは不要
/// - オフライン環境で利用可能
/// </summary>
public class WindowsTtsClient : ITtsEngine
{
    public TtsEngineType EngineType => TtsEngineType.WindowsOneCore;

    public string DisplayName => "Windows 標準音声 (オフライン)";

    public IReadOnlyList<string> Constraints { get; } = new[]
    {
        "音声品質は Edge Neural や VOICEVOX に比べて自然さが劣ります",
        "日本語音声は環境により 1〜4 種類です（多くの場合 Haruka のみ）",
        "テキストデータは一切外部に送信されません（完全ローカル処理）",
        "追加ソフトウェアのインストールは不要です",
        "インターネット接続なしで利用できます",
    };

    public Task<string?> CheckConnectionAsync()
    {
        try
        {
            using var synth = new SpeechSynthesizer();
            var voices = synth.GetInstalledVoices()
                .Where(v => v.Enabled && v.VoiceInfo.Culture.Name.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
                .ToList();
            return Task.FromResult<string?>(voices.Count > 0 ? $"Windows TTS ({voices.Count} voices)" : null);
        }
        catch
        {
            return Task.FromResult<string?>(null);
        }
    }

    public Task<List<TtsSpeaker>> GetSpeakersAsync()
    {
        var speakers = new List<TtsSpeaker>();

        try
        {
            using var synth = new SpeechSynthesizer();
            var voices = synth.GetInstalledVoices()
                .Where(v => v.Enabled && v.VoiceInfo.Culture.Name.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
                .ToList();

            for (int i = 0; i < voices.Count; i++)
            {
                var info = voices[i].VoiceInfo;
                var genderJa = info.Gender == VoiceGender.Female ? "女性"
                    : info.Gender == VoiceGender.Male ? "男性" : "";
                speakers.Add(new TtsSpeaker
                {
                    Id = info.Name,
                    DisplayName = $"{info.Name} ({genderJa})",
                    Gender = info.Gender.ToString(),
                    StyleId = i,
                });
            }
        }
        catch
        {
            // SpeechSynthesizer が使えない環境
        }

        return Task.FromResult(speakers);
    }

    public Task<byte[]> GenerateAudioAsync(string text, string speakerId, double speedScale = 1.0)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Task.FromResult(GenerateSilentWav(0.5));

        try
        {
            using var synth = new SpeechSynthesizer();

            // 話者を選択
            try
            {
                synth.SelectVoice(speakerId);
            }
            catch
            {
                // 指定話者が見つからない場合はデフォルトの日本語音声を試行
                var jaVoice = synth.GetInstalledVoices()
                    .FirstOrDefault(v => v.Enabled &&
                        v.VoiceInfo.Culture.Name.StartsWith("ja", StringComparison.OrdinalIgnoreCase));
                if (jaVoice != null)
                    synth.SelectVoice(jaVoice.VoiceInfo.Name);
            }

            // 速度設定 (-10 ～ +10 の範囲、1.0 → 0, 1.5 → +5, 0.5 → -5)
            int rate = Math.Clamp((int)((speedScale - 1.0) * 10), -10, 10);
            synth.Rate = rate;

            using var ms = new MemoryStream();
            synth.SetOutputToWaveStream(ms);

            // ポーズ記法対応: SSML の <break> タグを使用
            if (NarrationPauseHelper.HasPauseMarkers(text))
            {
                var segments = NarrationPauseHelper.Parse(text);
                var ssml = BuildSsmlForSapi(segments, speakerId, rate);
                synth.SpeakSsml(ssml);
            }
            else
            {
                synth.Speak(text);
            }

            return Task.FromResult(ms.ToArray());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"WindowsTtsClient.GenerateAudioAsync failed: {ex.Message}");
            return Task.FromResult(GenerateSilentWav(1.0));
        }
    }

    /// <summary>
    /// SAPI 用の SSML を構築する（break タグ対応）。
    /// </summary>
    private static string BuildSsmlForSapi(List<NarrationPauseHelper.Segment> segments, string voiceName, int rate)
    {
        var sb = new StringBuilder();
        sb.Append("<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='ja-JP'>");

        foreach (var seg in segments)
        {
            if (seg.IsPause)
            {
                var ms = (int)(seg.PauseSeconds * 1000);
                sb.Append($"<break time=\"{ms}ms\"/>");
            }
            else
            {
                sb.Append(System.Security.SecurityElement.Escape(seg.Text));
            }
        }

        sb.Append("</speak>");
        return sb.ToString();
    }

    private static byte[] GenerateSilentWav(double seconds)
    {
        int sampleRate = 22050;
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
}

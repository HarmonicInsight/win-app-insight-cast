namespace InsightCast.TTS;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Microsoft Edge Neural TTS クライアント。
/// Edge ブラウザの読み上げ機能と同じ Azure Neural Voice を無料で利用する。
///
/// 【企業向け制約事項】
/// - インターネット接続が必須（Microsoft のサーバーと通信）
/// - 音声データは Microsoft のクラウドで生成される（テキストが送信される）
/// - APIキー・アカウント登録は不要
/// - 商用利用可能（Edge ブラウザと同等の利用規約）
/// - 利用量の明示的な上限は公開されていないが、大量一括処理時は注意
/// - オフライン環境では利用不可（WindowsOneCore にフォールバック推奨）
/// </summary>
public class EdgeTtsClient : ITtsEngine, IDisposable
{
    private const string TRUSTED_CLIENT_TOKEN = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";
    private const string CHROMIUM_FULL_VERSION = "143.0.3650.75";
    private const string SEC_MS_GEC_VERSION = "1-" + CHROMIUM_FULL_VERSION;

    // Windows epoch: seconds between 1601-01-01 and 1970-01-01
    private const long WIN_EPOCH = 11644473600L;
    private const long S_TO_NS = 10_000_000L; // 100ns ticks per second (Windows FILETIME)

    private const string BASE_URL =
        "speech.platform.bing.com/consumer/speech/synthesize/readaloud";

    private static string EDGE_VOICE_LIST_URL =>
        $"https://{BASE_URL}/voices/list?trustedclienttoken={TRUSTED_CLIENT_TOKEN}";

    private readonly System.Net.Http.HttpClient _httpClient = new();
    private List<TtsSpeaker>? _cachedSpeakers;

    public TtsEngineType EngineType => TtsEngineType.EdgeNeural;

    public string DisplayName => "Microsoft Neural TTS";

    public IReadOnlyList<string> Constraints { get; } = new[]
    {
        "インターネット接続が必要です",
        "ナレーションのテキストが Microsoft のクラウドサーバーに送信されます",
        "APIキー・アカウント登録は不要です（無料）",
        "Microsoft Edge ブラウザと同等の音声品質です",
        "オフライン環境では利用できません（Windows 標準音声に自動切替されます）",
    };

    /// <summary>
    /// Sec-MS-GEC トークンを生成する（Microsoft の DRM 認証用）。
    /// SHA-256(ticks + TRUSTED_CLIENT_TOKEN) のアッパーケース16進文字列。
    /// </summary>
    private static string GenerateSecMsGec()
    {
        var unixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var ticks = (unixSeconds + WIN_EPOCH);
        // 300秒（5分）ウィンドウに丸める
        ticks -= ticks % 300;
        // Windows FILETIME スケール（100ns 単位）
        ticks *= S_TO_NS;

        var strToHash = $"{ticks}{TRUSTED_CLIENT_TOKEN}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(strToHash));
        return Convert.ToHexString(hashBytes);
    }

    /// <summary>WebSocket 接続 URL を Sec-MS-GEC 付きで生成</summary>
    private static string BuildWssUrl()
    {
        var secMsGec = GenerateSecMsGec();
        var connectionId = Guid.NewGuid().ToString("N");
        return $"wss://{BASE_URL}/edge/v1" +
               $"?TrustedClientToken={TRUSTED_CLIENT_TOKEN}" +
               $"&Sec-MS-GEC={secMsGec}" +
               $"&Sec-MS-GEC-Version={SEC_MS_GEC_VERSION}" +
               $"&ConnectionId={connectionId}";
    }

    public async Task<string?> CheckConnectionAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var response = await _httpClient.GetAsync(EDGE_VOICE_LIST_URL, cts.Token);
            return response.IsSuccessStatusCode ? "Edge Neural TTS" : null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<TtsSpeaker>> GetSpeakersAsync()
    {
        if (_cachedSpeakers != null)
            return _cachedSpeakers;

        var speakers = new List<TtsSpeaker>();

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var json = await _httpClient.GetStringAsync(EDGE_VOICE_LIST_URL, cts.Token);
            var voices = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement[]>(json);

            if (voices != null)
            {
                foreach (var voice in voices)
                {
                    var locale = voice.GetProperty("Locale").GetString() ?? "";
                    if (!locale.StartsWith("ja-JP", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var shortName = voice.GetProperty("ShortName").GetString() ?? "";
                    var displayName = voice.GetProperty("FriendlyName").GetString() ?? shortName;
                    var gender = voice.GetProperty("Gender").GetString() ?? "";

                    var genderJa = gender == "Female" ? "女性" : gender == "Male" ? "男性" : gender;

                    speakers.Add(new TtsSpeaker
                    {
                        Id = shortName,
                        DisplayName = $"{displayName} ({genderJa})",
                        Gender = gender,
                        StyleId = speakers.Count,
                    });
                }
            }
        }
        catch
        {
            // API 取得失敗時はハードコードのフォールバック
        }

        // フォールバック: API から取得できなかった場合
        if (speakers.Count == 0)
        {
            speakers.Add(new TtsSpeaker { Id = "ja-JP-NanamiNeural", DisplayName = "Microsoft Nanami (女性)", Gender = "Female", StyleId = 0 });
            speakers.Add(new TtsSpeaker { Id = "ja-JP-KeitaNeural", DisplayName = "Microsoft Keita (男性)", Gender = "Male", StyleId = 1 });
        }

        _cachedSpeakers = speakers;
        return speakers;
    }

    public async Task<byte[]> GenerateAudioAsync(string text, string speakerId, double speedScale = 1.0)
    {
        if (string.IsNullOrWhiteSpace(text))
            return GenerateSilentWav(0.5);

        // 長文は句読点で分割して連結
        var chunks = SplitText(text, 300);
        if (chunks.Count == 1)
            return await SynthesizeSingleAsync(chunks[0], speakerId, speedScale);

        // 各チャンクを合成後、WAV データ部分のみ連結
        using var ms = new MemoryStream();
        bool headerWritten = false;

        foreach (var chunk in chunks)
        {
            var audio = await SynthesizeSingleAsync(chunk, speakerId, speedScale);
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

    private async Task<byte[]> SynthesizeSingleAsync(string text, string speakerId, double speedScale)
    {
        const int maxRetries = 3;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                return await SynthesizeViaWebSocketAsync(text, speakerId, speedScale);
            }
            catch when (attempt < maxRetries)
            {
                await Task.Delay(500 * attempt);
            }
        }

        return await SynthesizeViaWebSocketAsync(text, speakerId, speedScale);
    }

    private static async Task<byte[]> SynthesizeViaWebSocketAsync(string text, string voiceName, double speedScale)
    {
        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("User-Agent",
            $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{CHROMIUM_FULL_VERSION} Safari/537.36 Edg/{CHROMIUM_FULL_VERSION}");
        ws.Options.SetRequestHeader("Origin", "chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold");

        var wssUrl = BuildWssUrl();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await ws.ConnectAsync(new Uri(wssUrl), cts.Token);

        var requestId = Guid.NewGuid().ToString("N");

        // 速度を SSML prosody rate に変換 (e.g., 1.2 → "+20%", 0.8 → "-20%")
        var ratePercent = (int)((speedScale - 1.0) * 100);
        var rateStr = ratePercent >= 0 ? $"+{ratePercent}%" : $"{ratePercent}%";

        // Config メッセージ送信
        var configMessage =
            $"X-Timestamp:{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ}\r\n" +
            "Content-Type:application/json; charset=utf-8\r\n" +
            $"Path:speech.config\r\n\r\n" +
            "{\"context\":{\"synthesis\":{\"audio\":{\"metadataoptions\":{\"sentenceBoundaryEnabled\":\"false\",\"wordBoundaryEnabled\":\"false\"},\"outputFormat\":\"audio-24khz-48kbitrate-mono-mp3\"}}}}";

        await SendTextMessage(ws, configMessage, cts.Token);

        // SSML メッセージ送信
        var escapedText = System.Security.SecurityElement.Escape(text);
        var ssml =
            $"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='ja-JP'>" +
            $"<voice name='{voiceName}'>" +
            $"<prosody rate='{rateStr}'>{escapedText}</prosody>" +
            $"</voice></speak>";

        var ssmlMessage =
            $"X-RequestId:{requestId}\r\n" +
            "Content-Type:application/ssml+xml\r\n" +
            $"X-Timestamp:{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ}\r\n" +
            $"Path:ssml\r\n\r\n{ssml}";

        await SendTextMessage(ws, ssmlMessage, cts.Token);

        // 音声データ受信（MP3）
        using var audioStream = new MemoryStream();
        var buffer = new byte[8192];

        while (true)
        {
            cts.Token.ThrowIfCancellationRequested();
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);

            if (result.MessageType == WebSocketMessageType.Close)
                break;

            if (result.MessageType == WebSocketMessageType.Binary)
            {
                // バイナリメッセージ: 2バイトのヘッダー長 + ヘッダー + 音声データ
                int headerLen = (buffer[0] << 8) | buffer[1];
                int dataOffset = 2 + headerLen;
                if (dataOffset < result.Count)
                    audioStream.Write(buffer, dataOffset, result.Count - dataOffset);
            }
            else if (result.MessageType == WebSocketMessageType.Text)
            {
                var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                if (msg.Contains("Path:turn.end"))
                    break;
            }
        }

        var mp3Data = audioStream.ToArray();
        if (mp3Data.Length == 0)
            return GenerateSilentWav(0.5);

        return ConvertMp3ToWav(mp3Data);
    }

    private static async Task SendTextMessage(ClientWebSocket ws, string message, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }

    private static void UpdateWavHeader(byte[] wav)
    {
        if (wav.Length < 44) return;
        var fileSize = wav.Length - 8;
        var dataSize = wav.Length - 44;
        BitConverter.GetBytes(fileSize).CopyTo(wav, 4);
        BitConverter.GetBytes(dataSize).CopyTo(wav, 40);
    }

    /// <summary>
    /// FFmpeg を使って MP3 データを WAV (PCM 16bit) に変換する。
    /// AudioCache やプレビュー再生が WAV 形式を前提としているため必須。
    /// </summary>
    private static byte[] ConvertMp3ToWav(byte[] mp3Data)
    {
        var ffmpegPath = Video.FFmpegWrapper.FindFfmpeg();
        if (ffmpegPath == null)
        {
            // FFmpeg がない場合は MP3 をそのまま返す（エクスポート時は FFmpeg が処理する）
            return mp3Data;
        }

        var tempMp3 = Path.Combine(Path.GetTempPath(), $"edge_tts_{Guid.NewGuid():N}.mp3");
        var tempWav = Path.ChangeExtension(tempMp3, ".wav");

        try
        {
            File.WriteAllBytes(tempMp3, mp3Data);

            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"-y -i \"{tempMp3}\" -acodec pcm_s16le -ar 24000 -ac 1 \"{tempWav}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var process = Process.Start(psi);
            if (process == null)
                return mp3Data;

            process.StandardError.ReadToEnd();
            process.WaitForExit(10000);

            if (process.ExitCode == 0 && File.Exists(tempWav))
                return File.ReadAllBytes(tempWav);

            return mp3Data;
        }
        catch
        {
            return mp3Data;
        }
        finally
        {
            try { if (File.Exists(tempMp3)) File.Delete(tempMp3); } catch { }
            try { if (File.Exists(tempWav)) File.Delete(tempWav); } catch { }
        }
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

        // RIFF header
        bw.Write(Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + dataSize);
        bw.Write(Encoding.ASCII.GetBytes("WAVE"));

        // fmt chunk
        bw.Write(Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);
        bw.Write((short)1); // PCM
        bw.Write((short)channels);
        bw.Write(sampleRate);
        bw.Write(sampleRate * channels * (bitsPerSample / 8));
        bw.Write((short)(channels * (bitsPerSample / 8)));
        bw.Write((short)bitsPerSample);

        // data chunk (silence)
        bw.Write(Encoding.ASCII.GetBytes("data"));
        bw.Write(dataSize);
        bw.Write(new byte[dataSize]);

        return ms.ToArray();
    }

    private static List<string> SplitText(string text, int maxLength)
    {
        if (text.Length <= maxLength)
            return new List<string> { text };

        var chunks = new List<string>();
        var delimiters = new[] { '。', '！', '？', '!', '?', '\n', '、', ',' };
        var remaining = text;

        while (remaining.Length > 0)
        {
            if (remaining.Length <= maxLength)
            {
                chunks.Add(remaining);
                break;
            }

            int splitAt = -1;
            for (int i = Math.Min(maxLength - 1, remaining.Length - 1); i >= maxLength / 2; i--)
            {
                if (delimiters.Contains(remaining[i]))
                {
                    splitAt = i + 1;
                    break;
                }
            }

            if (splitAt < 0)
                splitAt = maxLength;

            chunks.Add(remaining[..splitAt]);
            remaining = remaining[splitAt..];
        }

        return chunks;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}

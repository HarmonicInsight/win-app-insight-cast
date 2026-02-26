namespace InsightCast.VoiceVox;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

/// <summary>
/// Holds connection information for a discovered VOICEVOX engine instance.
/// </summary>
public class EngineInfo
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Constructs the base URL from Host and Port.
    /// </summary>
    public string BaseUrl => $"http://{Host}:{Port}";
}

/// <summary>
/// HTTP client for communicating with the VOICEVOX engine REST API.
/// Provides discovery, speaker lookup, audio query creation, and speech synthesis.
/// </summary>
public class VoiceVoxClient : IDisposable
{
    public const string DEFAULT_HOST = "127.0.0.1";
    public const int DEFAULT_PORT = 50021;
    public static readonly (int Start, int End) PORT_SCAN_RANGE = (50020, 50100);

    private static readonly string[] DefaultSpeakerPriority =
    {
        "青山龍星",
        "四国めたん",
        "ずんだもん",
        "春日部つむぎ"
    };

    /// <summary>
    /// Romanized display names for VOICEVOX speakers (used in English locale).
    /// Format: "Romaji (原名)" — proper nouns kept recognizable.
    /// </summary>
    private static readonly Dictionary<string, string> SpeakerRomanization = new(StringComparer.Ordinal)
    {
        ["四国めたん"]   = "Shikoku Metan",
        ["ずんだもん"]   = "Zundamon",
        ["春日部つむぎ"] = "Kasukabe Tsumugi",
        ["雨晴はう"]     = "Amehare Hau",
        ["波音リツ"]     = "Namine Ritsu",
        ["玄野武宏"]     = "Kurono Takehiro",
        ["白上虎太郎"]   = "Shirakami Kotaro",
        ["青山龍星"]     = "Aoyama Ryusei",
        ["冥鳴ひまり"]   = "Meimei Himari",
        ["九州そら"]     = "Kyushu Sora",
        ["もち子さん"]   = "Mochiko-san",
        ["剣崎雌雄"]     = "Kenzaki Mesuo",
        ["WhiteCUL"]     = "WhiteCUL",
        ["後鬼"]         = "Goki",
        ["No.7"]         = "No.7",
        ["ちび式じい"]   = "Chibishiki Jii",
        ["櫻歌ミコ"]     = "Ohka Miko",
        ["小夜/SAYO"]    = "Sayo",
        ["ナースロボ＿タイプＴ"] = "Nurse Robot Type T",
        ["†聖騎士 紅桜†"] = "Holy Knight Kouzakura",
        ["雀松朱司"]     = "Wakamatsu Akashi",
        ["麒ヶ島宗麟"]   = "Kigashima Sorin",
        ["春歌ナナ"]     = "Haruka Nana",
        ["猫使アル"]     = "Nekotsukai Aru",
        ["猫使ビィ"]     = "Nekotsukai Bii",
        ["中国うさぎ"]   = "Chugoku Usagi",
        ["栗田まろん"]   = "Kurita Maron",
        ["あいえるたん"] = "Aiel-tan",
        ["満別花丸"]     = "Manbetsu Hanamaru",
        ["琴詠ニア"]     = "Kotoyomi Nia",
    };

    /// <summary>
    /// Romanized style names for VOICEVOX voice styles (used in English locale).
    /// </summary>
    private static readonly Dictionary<string, string> StyleRomanization = new(StringComparer.Ordinal)
    {
        ["ノーマル"]   = "Normal",
        ["あまあま"]   = "Sweet",
        ["ツンツン"]   = "Tsundere",
        ["セクシー"]   = "Sexy",
        ["ささやき"]   = "Whisper",
        ["ヒソヒソ"]   = "Hushed",
        ["怒り"]       = "Angry",
        ["悲しみ"]     = "Sad",
        ["喜び"]       = "Happy",
        ["やさしい"]   = "Gentle",
        ["かわいい"]   = "Cute",
        ["かっこいい"] = "Cool",
        ["ものまね"]   = "Mimic",
        ["へろへろ"]   = "Exhausted",
        ["びえーん"]   = "Crying",
    };

    /// <summary>
    /// Returns a localized display name for a VOICEVOX speaker.
    /// In English locale: "Romaji (原名)"; in Japanese locale: original name.
    /// </summary>
    public static string GetLocalizedSpeakerName(string japaneseName)
    {
        if (!Services.LocalizationService.IsEnglish())
            return japaneseName;

        if (SpeakerRomanization.TryGetValue(japaneseName, out var romaji))
            return $"{romaji} ({japaneseName})";

        return japaneseName;
    }

    /// <summary>
    /// Returns a localized display name for a VOICEVOX style.
    /// In English locale: English name; in Japanese locale: original name.
    /// </summary>
    public static string GetLocalizedStyleName(string japaneseName)
    {
        if (!Services.LocalizationService.IsEnglish())
            return japaneseName;

        if (StyleRomanization.TryGetValue(japaneseName, out var english))
            return english;

        return japaneseName;
    }

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private string _baseUrl;
    private EngineInfo? _engineInfo;

    /// <summary>
    /// Creates a new VoiceVoxClient.
    /// </summary>
    /// <param name="baseUrl">
    /// Optional base URL of the VOICEVOX engine.
    /// Defaults to http://127.0.0.1:50021 if not specified.
    /// </param>
    /// <param name="httpClient">
    /// Optional HttpClient instance to use. If not provided, a new one is created
    /// and will be disposed when this client is disposed.
    /// </param>
    public VoiceVoxClient(string? baseUrl = null, HttpClient? httpClient = null)
    {
        _baseUrl = baseUrl ?? $"http://{DEFAULT_HOST}:{DEFAULT_PORT}";

        if (httpClient != null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            _httpClient = new HttpClient();
            _ownsHttpClient = true;
        }
    }

    /// <summary>
    /// The base URL currently configured for API requests.
    /// </summary>
    public string BaseUrl
    {
        get => _baseUrl;
        set => _baseUrl = value;
    }

    /// <summary>
    /// Information about the discovered engine, or null if discovery has not been performed.
    /// </summary>
    public EngineInfo? EngineInfo => _engineInfo;

    /// <summary>
    /// Scans a range of ports on localhost to discover a running VOICEVOX engine.
    /// </summary>
    /// <param name="fastCheckFirst">
    /// When true, checks the default port (50021) first before scanning the full range.
    /// </param>
    /// <returns>An <see cref="EngineInfo"/> if an engine is found; otherwise null.</returns>
    public async Task<EngineInfo?> DiscoverEngineAsync(bool fastCheckFirst = true)
    {
        if (fastCheckFirst)
        {
            var info = await TryConnectAsync(DEFAULT_HOST, DEFAULT_PORT);
            if (info != null)
            {
                _engineInfo = info;
                _baseUrl = info.BaseUrl;
                return info;
            }
        }

        for (int port = PORT_SCAN_RANGE.Start; port <= PORT_SCAN_RANGE.End; port++)
        {
            if (fastCheckFirst && port == DEFAULT_PORT)
                continue;

            var info = await TryConnectAsync(DEFAULT_HOST, port);
            if (info != null)
            {
                _engineInfo = info;
                _baseUrl = info.BaseUrl;
                return info;
            }
        }

        return null;
    }

    /// <summary>
    /// Attempts to connect to a specific host and port, returning engine info on success.
    /// Uses a short timeout suitable for port scanning.
    /// </summary>
    private async Task<EngineInfo?> TryConnectAsync(string host, int port)
    {
        try
        {
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(0.5));
            var url = $"http://{host}:{port}/version";
            var response = await _httpClient.GetAsync(url, cts.Token);

            if (response.IsSuccessStatusCode)
            {
                var version = await response.Content.ReadAsStringAsync();
                // The version endpoint returns a JSON string like "0.14.7"
                version = version.Trim().Trim('"');

                return new EngineInfo
                {
                    Host = host,
                    Port = port,
                    Version = version
                };
            }
        }
        catch (Exception)
        {
            // Connection failed or timed out - this port is not available
        }

        return null;
    }

    /// <summary>
    /// Checks if the VOICEVOX engine is reachable at the current base URL.
    /// </summary>
    /// <returns>The version string if connected; otherwise null.</returns>
    public async Task<string?> CheckConnectionAsync()
    {
        try
        {
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(2));
            var response = await _httpClient.GetAsync($"{_baseUrl}/version", cts.Token);

            if (response.IsSuccessStatusCode)
            {
                var version = await response.Content.ReadAsStringAsync();
                return version.Trim().Trim('"');
            }
        }
        catch (Exception)
        {
            // Connection failed
        }

        return null;
    }

    /// <summary>
    /// Retrieves the list of available speakers from the engine.
    /// </summary>
    /// <returns>A list of speaker JSON elements.</returns>
    public async Task<List<JsonElement>> GetSpeakersAsync()
    {
        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var response = await _httpClient.GetAsync($"{_baseUrl}/speakers", cts.Token);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var speakers = JsonSerializer.Deserialize<List<JsonElement>>(json);

        return speakers ?? new List<JsonElement>();
    }

    /// <summary>
    /// Searches for a speaker by name (partial match, case-insensitive).
    /// </summary>
    /// <param name="name">The speaker name to search for.</param>
    /// <returns>
    /// A tuple of (speakerElement, styleId) if found; otherwise (null, -1).
    /// The styleId is the first style's ID for the matched speaker.
    /// </returns>
    public async Task<(JsonElement? Speaker, int StyleId)> FindSpeakerByNameAsync(string name)
    {
        var speakers = await GetSpeakersAsync();

        foreach (var speaker in speakers)
        {
            if (speaker.TryGetProperty("name", out var nameElement))
            {
                var speakerName = nameElement.GetString() ?? string.Empty;
                if (speakerName.Contains(name, StringComparison.OrdinalIgnoreCase))
                {
                    int styleId = 0;
                    if (speaker.TryGetProperty("styles", out var styles) &&
                        styles.GetArrayLength() > 0)
                    {
                        var firstStyle = styles[0];
                        if (firstStyle.TryGetProperty("id", out var idElement))
                        {
                            styleId = idElement.GetInt32();
                        }
                    }

                    return (speaker, styleId);
                }
            }
        }

        return (null, -1);
    }

    /// <summary>
    /// Finds a default speaker from a priority list of known speaker names.
    /// Priority order: 青山龍星, 四国めたん, ずんだもん, 春日部つむぎ.
    /// Falls back to the first available speaker if none of the preferred speakers are found.
    /// </summary>
    /// <returns>
    /// A tuple of (speakerElement, styleId) if any speaker is available; otherwise (null, -1).
    /// </returns>
    public async Task<(JsonElement? Speaker, int StyleId)> GetDefaultSpeakerAsync()
    {
        var speakers = await GetSpeakersAsync();

        if (speakers.Count == 0)
            return (null, -1);

        // Try each preferred speaker in priority order
        foreach (var preferredName in DefaultSpeakerPriority)
        {
            foreach (var speaker in speakers)
            {
                if (speaker.TryGetProperty("name", out var nameElement))
                {
                    var speakerName = nameElement.GetString() ?? string.Empty;
                    if (speakerName.Contains(preferredName, StringComparison.OrdinalIgnoreCase))
                    {
                        int styleId = 0;
                        if (speaker.TryGetProperty("styles", out var styles) &&
                            styles.GetArrayLength() > 0)
                        {
                            var firstStyle = styles[0];
                            if (firstStyle.TryGetProperty("id", out var idElement))
                            {
                                styleId = idElement.GetInt32();
                            }
                        }

                        return (speaker, styleId);
                    }
                }
            }
        }

        // Fall back to first available speaker
        var fallback = speakers[0];
        int fallbackStyleId = 0;
        if (fallback.TryGetProperty("styles", out var fallbackStyles) &&
            fallbackStyles.GetArrayLength() > 0)
        {
            var firstStyle = fallbackStyles[0];
            if (firstStyle.TryGetProperty("id", out var idElement))
            {
                fallbackStyleId = idElement.GetInt32();
            }
        }

        return (fallback, fallbackStyleId);
    }

    /// <summary>
    /// Creates an audio query from the given text and speaker ID.
    /// The audio query contains prosody and phoneme information used for synthesis.
    /// </summary>
    /// <param name="text">The text to convert to speech.</param>
    /// <param name="speakerId">The speaker/style ID to use.</param>
    /// <returns>The audio query as a <see cref="JsonElement"/>.</returns>
    public async Task<JsonElement> CreateAudioQueryAsync(string text, int speakerId)
    {
        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
        var encodedText = Uri.EscapeDataString(text);
        var url = $"{_baseUrl}/audio_query?text={encodedText}&speaker={speakerId}";

        using var response = await _httpClient.PostAsync(url, null, cts.Token);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    /// <summary>
    /// Synthesizes speech audio from an audio query.
    /// </summary>
    /// <param name="query">The audio query obtained from <see cref="CreateAudioQueryAsync"/>.</param>
    /// <param name="speakerId">The speaker/style ID to use.</param>
    /// <returns>The synthesized audio data as a WAV byte array.</returns>
    public async Task<byte[]> SynthesizeAsync(JsonElement query, int speakerId)
    {
        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(30));
        var url = $"{_baseUrl}/synthesis?speaker={speakerId}";

        var queryJson = JsonSerializer.Serialize(query);
        var content = new StringContent(queryJson, Encoding.UTF8, "application/json");

        using var response = await _httpClient.PostAsync(url, content, cts.Token);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsByteArrayAsync();
    }

    /// <summary>
    /// Performs one-step text-to-speech: creates an audio query and synthesizes it.
    /// </summary>
    /// <param name="text">The text to convert to speech.</param>
    /// <param name="speakerId">The speaker/style ID to use.</param>
    /// <param name="speedScale">Speech speed multiplier (0.5 = slow, 1.0 = normal, 2.0 = fast).</param>
    /// <returns>The synthesized audio data as a WAV byte array.</returns>
    public async Task<byte[]> GenerateAudioAsync(string text, int speakerId, double speedScale = 1.0)
    {
        const int maxRetries = 3;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var query = await CreateAudioQueryAsync(text, speakerId);

                if (Math.Abs(speedScale - 1.0) > 0.01)
                {
                    query = ModifyQuerySpeed(query, speedScale);
                }

                return await SynthesizeAsync(query, speakerId);
            }
            catch (Exception) when (attempt < maxRetries)
            {
                await Task.Delay(1000 * attempt); // backoff: 1s, 2s
            }
        }

        // Final attempt without catch — let exception propagate
        var finalQuery = await CreateAudioQueryAsync(text, speakerId);
        if (Math.Abs(speedScale - 1.0) > 0.01)
            finalQuery = ModifyQuerySpeed(finalQuery, speedScale);
        return await SynthesizeAsync(finalQuery, speakerId);
    }

    /// <summary>
    /// Modifies the speedScale property in an audio query JSON.
    /// </summary>
    private static JsonElement ModifyQuerySpeed(JsonElement query, double speedScale)
    {
        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(query.GetRawText())!;
        var modified = new Dictionary<string, object?>();

        foreach (var kv in dict)
        {
            if (kv.Key == "speedScale")
                modified[kv.Key] = speedScale;
            else
                modified[kv.Key] = kv.Value;
        }

        var json = JsonSerializer.Serialize(modified);
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    /// <summary>
    /// Releases resources used by this client.
    /// </summary>
    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}

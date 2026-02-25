using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace InsightCast.Services.OpenAI
{
    /// <summary>
    /// OpenAI APIとの連携を行うサービス実装。
    /// </summary>
    public class OpenAIService : IOpenAIService
    {
        private const string CHAT_COMPLETIONS_ENDPOINT = "https://api.openai.com/v1/chat/completions";
        private const string IMAGES_GENERATIONS_ENDPOINT = "https://api.openai.com/v1/images/generations";

        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;
        private string? _apiKey;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// OpenAIServiceを作成します。
        /// </summary>
        /// <param name="httpClient">使用するHttpClient（nullの場合は内部で作成）。</param>
        public OpenAIService(HttpClient? httpClient = null)
        {
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

        /// <inheritdoc />
        public string NarrationModel { get; set; } = "gpt-4o";

        /// <inheritdoc />
        public string ImageModel { get; set; } = "dall-e-3";

        /// <inheritdoc />
        public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);

        /// <inheritdoc />
        public async Task<bool> ConfigureAsync(string apiKey, CancellationToken ct = default)
        {
            _apiKey = apiKey;
            UpdateAuthHeader();

            // 簡単な接続テスト
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(15));

                var testRequest = new
                {
                    model = "gpt-4o-mini",
                    messages = new[] { new { role = "user", content = "test" } },
                    max_tokens = 5
                };

                var response = await _httpClient.PostAsJsonAsync(
                    CHAT_COMPLETIONS_ENDPOINT, testRequest, JsonOptions, cts.Token);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <inheritdoc />
        public async Task<TextGenerationResult> GenerateNarrationAsync(
            TextGenerationRequest request,
            CancellationToken ct = default)
        {
            if (!IsConfigured)
                return TextGenerationResult.Error("OpenAI APIキーが設定されていません。");

            var prompt = PromptTemplates.BuildNarrationPrompt(request);

            var requestBody = new
            {
                model = request.Model ?? NarrationModel,
                messages = new object[]
                {
                    new { role = "system", content = PromptTemplates.NARRATION_SYSTEM_PROMPT },
                    new { role = "user", content = prompt }
                },
                max_tokens = request.MaxTokens,
                temperature = request.Temperature
            };

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(60));

                var response = await _httpClient.PostAsJsonAsync(
                    CHAT_COMPLETIONS_ENDPOINT, requestBody, JsonOptions, cts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(cts.Token);
                    return TextGenerationResult.Error($"API エラー ({response.StatusCode}): {errorBody}");
                }

                var json = await response.Content.ReadAsStringAsync(cts.Token);
                var result = JsonSerializer.Deserialize<ChatCompletionResponse>(json, JsonOptions);

                if (result?.Choices == null || result.Choices.Length == 0)
                    return TextGenerationResult.Error("APIからの応答が空でした。");

                return new TextGenerationResult
                {
                    Success = true,
                    Text = result.Choices[0].Message?.Content?.Trim() ?? string.Empty,
                    TokensUsed = result.Usage?.TotalTokens ?? 0
                };
            }
            catch (OperationCanceledException)
            {
                return TextGenerationResult.Error("リクエストがタイムアウトしました。");
            }
            catch (Exception ex)
            {
                return TextGenerationResult.Error($"テキスト生成に失敗しました: {ex.Message}");
            }
        }

        /// <inheritdoc />
        public async Task<ImageGenerationResult> GenerateImageAsync(
            ImageGenerationRequest request,
            CancellationToken ct = default)
        {
            if (!IsConfigured)
                return ImageGenerationResult.Error("OpenAI APIキーが設定されていません。");

            var prompt = PromptTemplates.BuildImagePrompt(request);

            var requestBody = new
            {
                model = request.Model ?? ImageModel,
                prompt = prompt,
                size = request.Size ?? "1792x1024",
                quality = request.Quality ?? "standard",
                n = 1
            };

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(120)); // 画像生成は時間がかかる

                var response = await _httpClient.PostAsJsonAsync(
                    IMAGES_GENERATIONS_ENDPOINT, requestBody, JsonOptions, cts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(cts.Token);
                    return ImageGenerationResult.Error($"API エラー ({response.StatusCode}): {errorBody}");
                }

                var json = await response.Content.ReadAsStringAsync(cts.Token);
                var result = JsonSerializer.Deserialize<ImageGenerationResponse>(json, JsonOptions);

                if (result?.Data == null || result.Data.Length == 0)
                    return ImageGenerationResult.Error("APIからの応答が空でした。");

                var imageUrl = result.Data[0].Url;
                if (string.IsNullOrEmpty(imageUrl))
                    return ImageGenerationResult.Error("画像URLが取得できませんでした。");

                // 画像をダウンロードして一時ファイルに保存
                var imageBytes = await _httpClient.GetByteArrayAsync(imageUrl, cts.Token);
                var cacheDir = Path.Combine(Path.GetTempPath(), "InsightCast", "ai_images");
                Directory.CreateDirectory(cacheDir);

                var tempPath = Path.Combine(cacheDir, $"dalle_{Guid.NewGuid():N}.png");
                await File.WriteAllBytesAsync(tempPath, imageBytes, cts.Token);

                return new ImageGenerationResult
                {
                    Success = true,
                    ImagePath = tempPath,
                    OriginalUrl = imageUrl
                };
            }
            catch (OperationCanceledException)
            {
                return ImageGenerationResult.Error("リクエストがタイムアウトしました。");
            }
            catch (Exception ex)
            {
                return ImageGenerationResult.Error($"画像生成に失敗しました: {ex.Message}");
            }
        }

        private void UpdateAuthHeader()
        {
            _httpClient.DefaultRequestHeaders.Remove("Authorization");
            if (!string.IsNullOrEmpty(_apiKey))
            {
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_ownsHttpClient)
            {
                _httpClient.Dispose();
            }
            GC.SuppressFinalize(this);
        }

        #region Response Models

        private class ChatCompletionResponse
        {
            [JsonPropertyName("choices")]
            public ChatChoice[]? Choices { get; set; }

            [JsonPropertyName("usage")]
            public UsageInfo? Usage { get; set; }
        }

        private class ChatChoice
        {
            [JsonPropertyName("message")]
            public ChatMessage? Message { get; set; }
        }

        private class ChatMessage
        {
            [JsonPropertyName("content")]
            public string? Content { get; set; }
        }

        private class UsageInfo
        {
            [JsonPropertyName("total_tokens")]
            public int TotalTokens { get; set; }
        }

        private class ImageGenerationResponse
        {
            [JsonPropertyName("data")]
            public ImageData[]? Data { get; set; }
        }

        private class ImageData
        {
            [JsonPropertyName("url")]
            public string? Url { get; set; }
        }

        #endregion
    }
}

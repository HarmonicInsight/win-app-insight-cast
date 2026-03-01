using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace InsightCast.Services.Claude;

/// <summary>
/// OpenAI DALL-E API client for image generation.
/// </summary>
public sealed class DalleService : IDisposable
{
    private readonly HttpClient _http;
    private const string Endpoint = "https://api.openai.com/v1/images/generations";

    public DalleService(string apiKey)
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    }

    /// <summary>
    /// Generate an image using DALL-E and save it to a temporary file.
    /// </summary>
    /// <param name="prompt">Image generation prompt</param>
    /// <param name="model">DALL-E model name (e.g. "dall-e-3")</param>
    /// <param name="size">Image size: "1024x1024", "1792x1024", or "1024x1792"</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Absolute path to the generated image file</returns>
    public async Task<string> GenerateImageAsync(
        string prompt, string model = "dall-e-3", string size = "1024x1024", CancellationToken ct = default)
    {
        var requestBody = new
        {
            model,
            prompt,
            n = 1,
            size,
            response_format = "b64_json"
        };

        var json = JsonSerializer.Serialize(requestBody);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http.PostAsync(Endpoint, content, ct);
        var responseJson = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            string? errorMsg = null;
            try
            {
                using var errorDoc = JsonDocument.Parse(responseJson);
                if (errorDoc.RootElement.TryGetProperty("error", out var errorObj) &&
                    errorObj.TryGetProperty("message", out var msgProp))
                    errorMsg = msgProp.GetString();
            }
            catch { /* ignore JSON parse failures */ }

            throw new HttpRequestException($"DALL-E API error ({(int)response.StatusCode}): {errorMsg ?? response.ReasonPhrase}");
        }

        using var doc = JsonDocument.Parse(responseJson);
        var b64 = doc.RootElement
            .GetProperty("data")[0]
            .GetProperty("b64_json")
            .GetString()
            ?? throw new InvalidOperationException("No image data in response");

        var bytes = Convert.FromBase64String(b64);
        var tempDir = Path.Combine(Path.GetTempPath(), "InsightCast", "dalle");
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, $"dalle_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        await File.WriteAllBytesAsync(filePath, bytes, ct);

        return filePath;
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}

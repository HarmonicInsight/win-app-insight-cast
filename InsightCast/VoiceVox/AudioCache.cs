namespace InsightCast.VoiceVox;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// File-based cache for synthesized audio data.
/// Stores WAV files keyed by a hash of the text and speaker ID to avoid
/// redundant synthesis requests.
/// </summary>
public class AudioCache
{
    private readonly string _cacheDir;

    /// <summary>
    /// Creates a new AudioCache.
    /// </summary>
    /// <param name="cacheDir">
    /// Optional cache directory path. Defaults to %TEMP%\insightcast_cache\audio.
    /// The directory is created automatically if it does not exist.
    /// </param>
    public AudioCache(string? cacheDir = null)
    {
        _cacheDir = cacheDir
            ?? Path.Combine(Path.GetTempPath(), "insightcast_cache", "audio");

        Directory.CreateDirectory(_cacheDir);
    }

    /// <summary>
    /// The directory where cached audio files are stored.
    /// </summary>
    public string CacheDirectory => _cacheDir;

    /// <summary>
    /// Computes a deterministic cache key from the text and speaker ID using MD5.
    /// </summary>
    /// <param name="text">The input text.</param>
    /// <param name="speakerId">The speaker/style ID.</param>
    /// <returns>A hex-encoded MD5 hash string.</returns>
    public static string GetCacheKey(string text, int speakerId)
    {
        var input = $"{text}_{speakerId}";
        var inputBytes = Encoding.UTF8.GetBytes(input);

#pragma warning disable CA5351 // MD5 is used only for cache key generation, not for security
        var hashBytes = MD5.HashData(inputBytes);
#pragma warning restore CA5351

        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Returns the full file path where the cached audio for the given text and
    /// speaker would be stored.
    /// </summary>
    /// <param name="text">The input text.</param>
    /// <param name="speakerId">The speaker/style ID.</param>
    /// <returns>The full path to the .wav cache file.</returns>
    public string GetCachePath(string text, int speakerId)
    {
        var key = GetCacheKey(text, speakerId);
        return Path.Combine(_cacheDir, $"{key}.wav");
    }

    /// <summary>
    /// Checks whether a cached audio file exists for the given text and speaker.
    /// </summary>
    /// <param name="text">The input text.</param>
    /// <param name="speakerId">The speaker/style ID.</param>
    /// <returns>True if the cache file exists; otherwise false.</returns>
    public bool Exists(string text, int speakerId)
    {
        var path = GetCachePath(text, speakerId);
        return File.Exists(path);
    }

    /// <summary>
    /// Saves audio data to the cache.
    /// </summary>
    /// <param name="text">The input text used to generate the audio.</param>
    /// <param name="speakerId">The speaker/style ID used to generate the audio.</param>
    /// <param name="audioData">The WAV audio data to cache.</param>
    /// <returns>The full path to the saved cache file.</returns>
    /// <summary>Maximum cache size in bytes (500 MB).</summary>
    private const long MaxCacheSizeBytes = 500L * 1024 * 1024;

    public string Save(string text, int speakerId, byte[] audioData)
    {
        var path = GetCachePath(text, speakerId);
        File.WriteAllBytes(path, audioData);
        TrimCacheIfNeeded();
        return path;
    }

    /// <summary>
    /// Removes oldest cache files when total size exceeds the limit.
    /// </summary>
    private void TrimCacheIfNeeded()
    {
        try
        {
            if (!Directory.Exists(_cacheDir)) return;

            var files = new DirectoryInfo(_cacheDir)
                .GetFiles("*.wav")
                .OrderBy(f => f.LastAccessTime)
                .ToList();

            long totalSize = files.Sum(f => f.Length);

            while (totalSize > MaxCacheSizeBytes && files.Count > 1)
            {
                var oldest = files[0];
                totalSize -= oldest.Length;
                oldest.Delete();
                files.RemoveAt(0);
            }
        }
        catch (Exception ex) { System.Diagnostics.Trace.TraceWarning($"Cache trim failed: {ex.Message}"); }
    }

    /// <summary>
    /// Loads cached audio data for the given text and speaker.
    /// </summary>
    /// <param name="text">The input text.</param>
    /// <param name="speakerId">The speaker/style ID.</param>
    /// <returns>The cached WAV byte array, or null if no cache entry exists.</returns>
    public byte[]? Load(string text, int speakerId)
    {
        var path = GetCachePath(text, speakerId);

        if (!File.Exists(path))
            return null;

        return File.ReadAllBytes(path);
    }

    /// <summary>
    /// Computes the duration of a cached audio file by reading its WAV header.
    /// </summary>
    /// <param name="text">The input text.</param>
    /// <param name="speakerId">The speaker/style ID.</param>
    /// <returns>The duration in seconds, or null if the cache entry does not exist.</returns>
    public double? GetDuration(string text, int speakerId)
    {
        var data = Load(text, speakerId);
        if (data == null)
            return null;

        return GetAudioDurationFromBytes(data);
    }

    /// <summary>
    /// Deletes all .wav files from the cache directory.
    /// </summary>
    /// <returns>The number of files deleted.</returns>
    public int ClearCache()
    {
        int count = 0;

        if (!Directory.Exists(_cacheDir))
            return count;

        foreach (var file in Directory.GetFiles(_cacheDir, "*.wav"))
        {
            try
            {
                File.Delete(file);
                count++;
            }
            catch (IOException)
            {
                // File may be locked; skip it
            }
            catch (UnauthorizedAccessException)
            {
                // Insufficient permissions; skip it
            }
        }

        return count;
    }

    /// <summary>
    /// Parses a WAV byte array to compute the audio duration in seconds.
    /// Reads the RIFF/WAV header to extract sample rate, number of channels,
    /// and bits per sample, then locates the data chunk to determine size.
    /// </summary>
    /// <param name="wavData">The raw WAV file bytes.</param>
    /// <returns>The audio duration in seconds.</returns>
    /// <exception cref="ArgumentException">Thrown if the data is not a valid WAV file.</exception>
    public static double GetAudioDurationFromBytes(byte[] wavData)
    {
        if (wavData == null || wavData.Length < 44)
            throw new ArgumentException("Data is too short to be a valid WAV file.", nameof(wavData));

        // Validate RIFF header
        var riff = Encoding.ASCII.GetString(wavData, 0, 4);
        if (riff != "RIFF")
            throw new ArgumentException("Not a valid WAV file: missing RIFF header.", nameof(wavData));

        var wave = Encoding.ASCII.GetString(wavData, 8, 4);
        if (wave != "WAVE")
            throw new ArgumentException("Not a valid WAV file: missing WAVE format.", nameof(wavData));

        // Parse format chunk - walk through chunks to find "fmt " and "data"
        int numChannels = 0;
        int sampleRate = 0;
        int bitsPerSample = 0;
        int dataSize = 0;
        bool foundFmt = false;
        bool foundData = false;

        int offset = 12; // Start after "RIFF" + size + "WAVE"

        while (offset + 8 <= wavData.Length)
        {
            var chunkId = Encoding.ASCII.GetString(wavData, offset, 4);
            var chunkSize = BitConverter.ToInt32(wavData, offset + 4);

            if (chunkId == "fmt ")
            {
                if (offset + 8 + 16 > wavData.Length)
                    throw new ArgumentException("Truncated fmt chunk.", nameof(wavData));

                numChannels = BitConverter.ToInt16(wavData, offset + 10);
                sampleRate = BitConverter.ToInt32(wavData, offset + 12);
                // offset + 16: byte rate (4 bytes)
                // offset + 20: block align (2 bytes)
                bitsPerSample = BitConverter.ToInt16(wavData, offset + 22);
                foundFmt = true;
            }
            else if (chunkId == "data")
            {
                dataSize = chunkSize;
                foundData = true;
            }

            if (foundFmt && foundData)
                break;

            // Move to next chunk (chunk header is 8 bytes + chunk data)
            offset += 8 + chunkSize;

            // Ensure word alignment (chunks are padded to even sizes)
            if (chunkSize % 2 != 0)
                offset++;
        }

        if (!foundFmt)
            throw new ArgumentException("WAV file missing fmt chunk.", nameof(wavData));

        if (!foundData)
            throw new ArgumentException("WAV file missing data chunk.", nameof(wavData));

        if (sampleRate <= 0 || numChannels <= 0 || bitsPerSample <= 0)
            throw new ArgumentException("Invalid WAV format parameters.", nameof(wavData));

        int bytesPerSample = bitsPerSample / 8;
        int bytesPerSecond = sampleRate * numChannels * bytesPerSample;

        if (bytesPerSecond == 0)
            throw new ArgumentException("Computed bytes per second is zero.", nameof(wavData));

        return (double)dataSize / bytesPerSecond;
    }
}

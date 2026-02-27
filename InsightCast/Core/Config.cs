namespace InsightCast.Core;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

public class Config
{
    private static readonly string ConfigDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InsightCast");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    private Dictionary<string, JsonElement> _data = new();
    private int _batchDepth;
    private bool _dirty;

    /// <summary>
    /// True if the config file existed but could not be parsed (corrupted).
    /// </summary>
    public bool LoadFailed { get; private set; }

    public Config()
    {
        Load();
    }

    public void Load()
    {
        if (!File.Exists(ConfigPath))
        {
            _data = new Dictionary<string, JsonElement>();
            return;
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            _data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)
                    ?? new Dictionary<string, JsonElement>();
        }
        catch (Exception ex)
        {
            _data = new Dictionary<string, JsonElement>();
            Debug.WriteLine($"Config.Load failed (file may be corrupted): {ex.Message}");
            LoadFailed = true;
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(_data, options);
            AtomicWriteText(ConfigPath, json);
            _dirty = false;
        }
        catch (Exception ex)
        {
            // Prevent I/O errors (disk full, permissions) from crashing the app
            // when Save is called implicitly from property setters.
            Debug.WriteLine($"Config.Save failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Writes text to a file atomically: write to temp file, then rename.
    /// Prevents corruption if the process crashes mid-write.
    /// </summary>
    internal static void AtomicWriteText(string path, string content)
    {
        var dir = Path.GetDirectoryName(path) ?? ".";
        var tempPath = Path.Combine(dir, Path.GetRandomFileName());
        try
        {
            File.WriteAllText(tempPath, content);
            // Create backup of existing file
            if (File.Exists(path))
            {
                var backupPath = path + ".backup";
                File.Copy(path, backupPath, overwrite: true);
            }
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            // Clean up temp file on failure
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            throw;
        }
    }

    /// <summary>
    /// Begin a batch update. Calls to Set will not auto-save until EndUpdate is called.
    /// </summary>
    public void BeginUpdate()
    {
        _batchDepth++;
    }

    /// <summary>
    /// End a batch update. If any values were changed, saves once.
    /// </summary>
    public void EndUpdate()
    {
        if (_batchDepth > 0) _batchDepth--;
        if (_batchDepth == 0 && _dirty)
            Save();
    }

    public T? Get<T>(string key, T? defaultValue = default)
    {
        if (!_data.TryGetValue(key, out var element))
            return defaultValue;

        try
        {
            return element.Deserialize<T>();
        }
        catch
        {
            return defaultValue;
        }
    }

    public void Set<T>(string key, T value)
    {
        var element = JsonSerializer.SerializeToElement(value);
        _data[key] = element;
        _dirty = true;

        if (_batchDepth == 0)
            Save();
    }

    public bool IsFirstRun
    {
        get => Get<bool>("is_first_run", true);
        set => Set("is_first_run", value);
    }

    public string? EngineUrl
    {
        get => Get<string?>("engine_url", null);
        set => Set("engine_url", value);
    }

    public string? EnginePath
    {
        get => Get<string?>("engine_path", null);
        set => Set("engine_path", value);
    }

    public int? DefaultSpeakerId
    {
        get => Get<int?>("default_speaker_id", null);
        set => Set("default_speaker_id", value);
    }

    public string? LicenseKey
    {
        get => Get<string?>("license_key", null);
        set => Set("license_key", value);
    }

    public string? LicenseEmail
    {
        get
        {
            var encrypted = Get<string?>("license_email", null);
            if (string.IsNullOrEmpty(encrypted)) return null;
            try
            {
                var bytes = Convert.FromBase64String(encrypted);
                var decrypted = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decrypted);
            }
            catch
            {
                // Fallback: treat as plain text (migration from old format)
                return encrypted;
            }
        }
        set
        {
            if (value == null)
            {
                Set<string?>("license_email", null);
                return;
            }
            try
            {
                var bytes = Encoding.UTF8.GetBytes(value);
                var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
                Set("license_email", Convert.ToBase64String(encrypted));
            }
            catch
            {
                Set("license_email", value); // Fallback to plain
            }
        }
    }

    public void MarkSetupCompleted()
    {
        BeginUpdate();
        IsFirstRun = false;
        EndUpdate();
    }

    public string Language
    {
        get => Get<string>("language", "ja") ?? "ja";
        set => Set("language", value);
    }

    public void ClearLicense()
    {
        BeginUpdate();
        LicenseKey = null;
        LicenseEmail = null;
        EndUpdate();
    }

    private const int MaxRecentFiles = 5;

    public List<string> RecentFiles
    {
        get => Get<List<string>>("recent_files", new List<string>()) ?? new List<string>();
        set => Set("recent_files", value);
    }

    /// <summary>
    /// OpenAI APIキー（暗号化して保存）。
    /// </summary>
    public string? OpenAIApiKey
    {
        get => Get<string?>("openai_api_key", null);
        set => Set("openai_api_key", value);
    }

    /// <summary>
    /// OpenAI ナレーション生成モデル。
    /// </summary>
    public string OpenAINarrationModel
    {
        get => Get<string>("openai_narration_model", "gpt-4o") ?? "gpt-4o";
        set => Set("openai_narration_model", value);
    }

    /// <summary>
    /// OpenAI 画像生成モデル。
    /// </summary>
    public string OpenAIImageModel
    {
        get => Get<string>("openai_image_model", "dall-e-3") ?? "dall-e-3";
        set => Set("openai_image_model", value);
    }

    public void AddRecentFile(string path)
    {
        var files = RecentFiles;
        files.Remove(path);
        files.Insert(0, path);
        if (files.Count > MaxRecentFiles)
            files.RemoveRange(MaxRecentFiles, files.Count - MaxRecentFiles);
        RecentFiles = files;
    }
}

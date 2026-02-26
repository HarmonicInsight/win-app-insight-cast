namespace InsightCast.Services;

using System;
using System.Windows;

/// <summary>
/// Provides runtime language switching via merged ResourceDictionaries.
/// Default language is Japanese ("ja"). Supported: "ja", "en".
/// </summary>
public static class LocalizationService
{
    private static string _currentLanguage = "ja";
    private static ResourceDictionary? _currentDictionary;

    public static string CurrentLanguage => _currentLanguage;

    public static event Action? LanguageChanged;

    public static void Initialize(string language)
    {
        SetLanguage(language, notify: false);
    }

    public static void SetLanguage(string language)
    {
        SetLanguage(language, notify: true);
    }

    private static void SetLanguage(string language, bool notify)
    {
        if (language != "ja" && language != "en")
            language = "ja";

        _currentLanguage = language;

        var dict = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Strings/{language}.xaml")
        };

        var mergedDicts = Application.Current.Resources.MergedDictionaries;

        if (_currentDictionary != null)
            mergedDicts.Remove(_currentDictionary);

        mergedDicts.Add(dict);
        _currentDictionary = dict;

        if (notify)
            LanguageChanged?.Invoke();
    }

    /// <summary>
    /// Get a localized string by key. Falls back to key if not found.
    /// </summary>
    public static string GetString(string key)
    {
        var value = Application.Current.TryFindResource(key);
        return value as string ?? key;
    }

    /// <summary>
    /// Get a localized string with format arguments.
    /// </summary>
    public static string GetString(string key, params object[] args)
    {
        var template = GetString(key);
        try
        {
            return string.Format(template, args);
        }
        catch
        {
            return template;
        }
    }

    /// <summary>
    /// Returns true if the current language is English.
    /// </summary>
    public static bool IsEnglish() => _currentLanguage == "en";

    /// <summary>
    /// Toggle between ja and en.
    /// </summary>
    public static string ToggleLanguage()
    {
        var next = _currentLanguage == "ja" ? "en" : "ja";
        SetLanguage(next);
        return next;
    }
}

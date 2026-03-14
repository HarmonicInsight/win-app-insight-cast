using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using InsightCast.Core;
using InsightCast.TTS;

namespace InsightCast.Views;

public partial class NarrationDictionaryDialog : Window
{
    private readonly Config _config;
    public ObservableCollection<DictionaryDisplayItem> Items { get; } = new();

    public NarrationDictionaryDialog(Config config)
    {
        InitializeComponent();
        _config = config;
        LoadEntries();
        EntriesListView.ItemsSource = Items;
        UpdateCountLabel();
    }

    private void LoadEntries()
    {
        Items.Clear();
        var dict = _config.LoadNarrationDictionary();
        var customFromSet = new System.Collections.Generic.HashSet<string>(
            dict.CustomEntries.Select(e => e.From), StringComparer.Ordinal);

        // カスタムエントリ
        foreach (var entry in dict.CustomEntries)
        {
            Items.Add(new DictionaryDisplayItem
            {
                From = entry.From,
                To = entry.To,
                IsPreset = false,
                IsEnabled = true,
            });
        }

        // プリセットエントリ（カスタムと完全一致する From はスキップ）
        foreach (var preset in NarrationDictionary.Presets)
        {
            if (customFromSet.Contains(preset.From))
                continue;

            Items.Add(new DictionaryDisplayItem
            {
                From = preset.From,
                To = preset.To,
                IsPreset = true,
                IsEnabled = !dict.DisabledPresets.Contains(preset.From),
            });
        }
    }

    private void UpdateCountLabel()
    {
        int enabled = Items.Count(i => i.IsEnabled);
        EntryCountLabel.Text = $"合計 {Items.Count} 件（有効: {enabled} 件）";
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var from = NewFromTextBox.Text.Trim();
        var to = NewToTextBox.Text.Trim();

        if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
        {
            MessageBox.Show("変換元と変換先の両方を入力してください。", "入力エラー",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 既存エントリの上書きチェック（完全一致）
        var existing = Items.FirstOrDefault(i => i.From == from);
        if (existing != null)
        {
            if (existing.IsPreset)
            {
                // プリセットをカスタムに置き換え：プリセット行を削除して新規カスタム追加
                Items.Remove(existing);
                Items.Insert(0, new DictionaryDisplayItem
                {
                    From = from,
                    To = to,
                    IsPreset = false,
                    IsEnabled = true,
                });
            }
            else
            {
                existing.To = to;
                existing.IsEnabled = true;
            }
        }
        else
        {
            Items.Insert(0, new DictionaryDisplayItem
            {
                From = from,
                To = to,
                IsPreset = false,
                IsEnabled = true,
            });
        }

        NewFromTextBox.Text = string.Empty;
        NewToTextBox.Text = string.Empty;
        NewFromTextBox.Focus();
        UpdateCountLabel();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is DictionaryDisplayItem item)
        {
            Items.Remove(item);
            UpdateCountLabel();
        }
    }

    private void ResetPresetsButton_Click(object sender, RoutedEventArgs e)
    {
        // プリセットを全て有効に戻す
        foreach (var item in Items.Where(i => i.IsPreset))
        {
            item.IsEnabled = true;
        }

        // 不足しているプリセットを追加（カスタムにもプリセットにもない場合）
        var existingFroms = new System.Collections.Generic.HashSet<string>(
            Items.Select(i => i.From), StringComparer.Ordinal);
        foreach (var preset in NarrationDictionary.Presets)
        {
            if (!existingFroms.Contains(preset.From))
            {
                Items.Add(new DictionaryDisplayItem
                {
                    From = preset.From,
                    To = preset.To,
                    IsPreset = true,
                    IsEnabled = true,
                });
            }
        }

        UpdateCountLabel();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var dict = new NarrationDictionary
        {
            CustomEntries = Items
                .Where(i => !i.IsPreset)
                .Select(i => new DictionaryEntry(i.From, i.To))
                .ToList(),
            DisabledPresets = Items
                .Where(i => i.IsPreset && !i.IsEnabled)
                .Select(i => i.From)
                .ToList(),
        };

        _config.SaveNarrationDictionary(dict);
        DialogResult = true;
        Close();
    }
}

public class DictionaryDisplayItem : INotifyPropertyChanged
{
    private string _from = string.Empty;
    private string _to = string.Empty;
    private bool _isPreset;
    private bool _isEnabled = true;

    public string From
    {
        get => _from;
        set { _from = value; OnPropertyChanged(nameof(From)); }
    }

    public string To
    {
        get => _to;
        set { _to = value; OnPropertyChanged(nameof(To)); }
    }

    public bool IsPreset
    {
        get => _isPreset;
        set { _isPreset = value; OnPropertyChanged(nameof(IsPreset)); OnPropertyChanged(nameof(TypeLabel)); OnPropertyChanged(nameof(DeleteVisible)); }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; OnPropertyChanged(nameof(IsEnabled)); }
    }

    public string TypeLabel => IsPreset ? "プリセット" : "カスタム";
    public Visibility DeleteVisible => IsPreset ? Visibility.Collapsed : Visibility.Visible;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using InsightCast.Services;
using InsightCast.Services.Claude;
using InsightCommon.AI;

namespace InsightCast.Views;

/// <summary>
/// AI実行ダイアログ — InsightCastPresetPrompts（ツール対応プリセット）を使用
/// </summary>
public partial class AIPromptExecuteDialog : Window
{
    private List<PromptItemVm> _allPrompts;
    private string _currentLang;

    /// <summary>
    /// 選択されたプリセット（実行用）
    /// </summary>
    public InsightCastPresetPrompt? SelectedPreset { get; private set; }

    public AIPromptExecuteDialog()
    {
        InitializeComponent();
        _currentLang = LocalizationService.CurrentLanguage;
        _allPrompts = BuildPromptList();
        RefreshList();

        PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && PromptList.SelectedItem is PromptItemVm item)
            {
                ExecutePrompt(item);
                e.Handled = true;
            }
        };

        Loaded += (s, e) => SearchBox.Focus();
    }

    private List<PromptItemVm> BuildPromptList()
    {
        var lang = _currentLang;
        var list = new List<PromptItemVm>();

        // InsightCastPresetPrompts から全プリセットを取得
        foreach (var preset in InsightCastPresetPrompts.All)
        {
            list.Add(new PromptItemVm
            {
                Id = preset.Id,
                Name = lang == "ja" ? preset.LabelJa : preset.LabelEn,
                Description = lang == "ja" ? preset.CategoryJa : preset.CategoryEn,
                Category = lang == "ja" ? preset.CategoryJa : preset.CategoryEn,
                Icon = preset.Icon,
                IsToolEnabled = true,
                Source = preset,
            });
        }

        return list;
    }

    private void RefreshList()
    {
        var searchText = SearchBox?.Text?.Trim() ?? "";

        IEnumerable<PromptItemVm> result = _allPrompts;

        if (!string.IsNullOrEmpty(searchText))
        {
            result = result.Where(p =>
                p.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                p.Category.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                p.Description.Contains(searchText, StringComparison.OrdinalIgnoreCase));
        }

        // カテゴリ順 → 名前順でソート
        var sorted = result
            .OrderBy(p => p.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        PromptList.ItemsSource = null;
        PromptList.ItemsSource = sorted;

        EmptyState.Visibility = sorted.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PromptList.Visibility = sorted.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshList();
    }

    private void PromptList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ExecuteBtn.IsEnabled = PromptList.SelectedItem is PromptItemVm;
    }

    private void PromptList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PromptList.SelectedItem is PromptItemVm item)
        {
            ExecutePrompt(item);
        }
    }

    private void Execute_Click(object sender, RoutedEventArgs e)
    {
        if (PromptList.SelectedItem is PromptItemVm item)
        {
            ExecutePrompt(item);
        }
    }

    private void ExecutePrompt(PromptItemVm item)
    {
        SelectedPreset = item.Source;
        DialogResult = true;
        Close();
    }

    private void ManagePrompts_Click(object sender, RoutedEventArgs e)
    {
        // プロンプト管理ダイアログ（ユーザープロンプト用）
        var dialog = new PromptPresetManagerDialog { Owner = this };
        dialog.ShowDialog();
        // ユーザープロンプトは現時点では別システムなので、このダイアログには影響なし
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

/// <summary>
/// プロンプトリスト表示用ビューモデル
/// </summary>
public class PromptItemVm
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public string Icon { get; set; } = "";
    public bool IsToolEnabled { get; set; }
    public InsightCastPresetPrompt? Source { get; set; }
}

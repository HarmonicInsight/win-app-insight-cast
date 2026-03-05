using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using InsightCast.Models;
using InsightCast.Services;

namespace InsightCast.Views;

public partial class PromptPresetManagerDialog : Window
{
    private List<PromptPreset> _presets;
    public bool Changed { get; private set; }

    public PromptPresetManagerDialog()
    {
        InitializeComponent();
        _presets = PromptPresetService.LoadAll();
        RebuildCategoryFilter();
        RefreshList();

        PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = Changed;
                Close();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && PresetList.SelectedItem is PromptPreset)
            {
                EditPreset_Click(s, e);
                e.Handled = true;
            }
        };
    }

    private void RebuildCategoryFilter()
    {
        var selected = CategoryFilterCombo.SelectedItem as string;
        CategoryFilterCombo.Items.Clear();

        var allLabel = TryGetResource("AIPrompt.AllCategories") ?? "All";
        CategoryFilterCombo.Items.Add(allLabel);

        foreach (var cat in _presets
                     .Select(p => p.Category)
                     .Where(c => !string.IsNullOrWhiteSpace(c))
                     .Distinct()
                     .OrderBy(c => c))
        {
            CategoryFilterCombo.Items.Add(cat);
        }

        CategoryFilterCombo.SelectedItem = selected != null && CategoryFilterCombo.Items.Contains(selected)
            ? selected
            : allLabel;
    }

    private string? TryGetResource(string key)
    {
        return Application.Current.TryFindResource(key) as string;
    }

    private void RefreshList()
    {
        var filtered = GetFilteredPresets();
        PresetList.ItemsSource = null;
        PresetList.ItemsSource = filtered;
    }

    private List<PromptPreset> GetFilteredPresets()
    {
        var allLabel = TryGetResource("AIPrompt.AllCategories") ?? "All";
        var categoryFilter = CategoryFilterCombo.SelectedItem as string;
        var searchText = SearchBox?.Text?.Trim() ?? "";

        var showBuiltIn = PresetsTab?.IsChecked == true;
        IEnumerable<PromptPreset> result = _presets.Where(p =>
            showBuiltIn ? p.Id.StartsWith("builtin_", StringComparison.Ordinal) : !p.Id.StartsWith("builtin_", StringComparison.Ordinal));

        if (!string.IsNullOrEmpty(categoryFilter) && categoryFilter != allLabel)
            result = result.Where(p => (p.Category ?? "") == categoryFilter);

        if (!string.IsNullOrEmpty(searchText))
            result = result.Where(p =>
                (p.Name ?? "").Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                (p.Description ?? "").Contains(searchText, StringComparison.OrdinalIgnoreCase));

        return result
            .OrderByDescending(p => p.IsPinned)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IEnumerable<string> GetExistingCategories()
    {
        var showBuiltIn = PresetsTab?.IsChecked == true;
        return _presets
            .Where(p => showBuiltIn ? p.Id.StartsWith("builtin_", StringComparison.Ordinal) : !p.Id.StartsWith("builtin_", StringComparison.Ordinal))
            .Select(p => p.Category)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct();
    }

    private void TabChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        RebuildCategoryFilter();
        RefreshList();
    }

    private void CategoryFilter_Changed(object sender, SelectionChangedEventArgs e) => RefreshList();
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshList();

    private void PresetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = PresetList.SelectedItem is PromptPreset;
        EditBtn.IsEnabled = selected;
        DeleteBtn.IsEnabled = selected;
        DefaultBtn.IsEnabled = selected;
        DuplicateBtn.IsEnabled = selected;
        PinBtn.IsEnabled = selected;

        if (PresetList.SelectedItem is PromptPreset preset)
        {
            PinBtn.Content = preset.IsPinned
                ? TryGetResource("AIPrompt.Unpin") ?? "Unpin"
                : TryGetResource("AIPrompt.Pin") ?? "Pin";
        }
    }

    private void PresetList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PresetList.SelectedItem is PromptPreset)
            EditPreset_Click(sender, e);
    }

    private void NewPreset_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PromptPresetDialog(null, GetExistingCategories()) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            if (dialog.Result.IsDefault)
                PromptPresetService.SetDefault("");
            PromptPresetService.Add(dialog.Result);
            if (dialog.Result.IsDefault)
                PromptPresetService.SetDefault(dialog.Result.Id);
            _presets = PromptPresetService.LoadAll();
            RebuildCategoryFilter();
            RefreshList();
            Changed = true;
        }
    }

    private void DuplicatePreset_Click(object sender, RoutedEventArgs e)
    {
        if (PresetList.SelectedItem is not PromptPreset selected) return;

        var duplicate = selected.Duplicate();
        PromptPresetService.Add(duplicate);
        _presets = PromptPresetService.LoadAll();
        RebuildCategoryFilter();
        RefreshList();
        Changed = true;
    }

    private void EditPreset_Click(object sender, RoutedEventArgs e)
    {
        if (PresetList.SelectedItem is not PromptPreset selected) return;

        var dialog = new PromptPresetDialog(selected, GetExistingCategories()) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            if (dialog.Result.IsDefault)
                PromptPresetService.SetDefault(dialog.Result.Id);
            PromptPresetService.Update(selected.Id, dialog.Result);
            _presets = PromptPresetService.LoadAll();
            RebuildCategoryFilter();
            RefreshList();
            Changed = true;
        }
    }

    private void DeletePreset_Click(object sender, RoutedEventArgs e)
    {
        if (PresetList.SelectedItem is not PromptPreset selected) return;

        var title = TryGetResource("AIPrompt.Delete") ?? "Delete";
        var msg = $"{title}: {selected.Name}?";
        var result = MessageBox.Show(msg, title, MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            PromptPresetService.Remove(selected.Id);
            _presets = PromptPresetService.LoadAll();
            RebuildCategoryFilter();
            RefreshList();
            Changed = true;
        }
    }

    private void SetDefault_Click(object sender, RoutedEventArgs e)
    {
        if (PresetList.SelectedItem is not PromptPreset selected) return;

        PromptPresetService.SetDefault(selected.Id);
        _presets = PromptPresetService.LoadAll();
        RefreshList();
        Changed = true;
    }

    private void PinPreset_Click(object sender, RoutedEventArgs e)
    {
        if (PresetList.SelectedItem is not PromptPreset selected) return;

        PromptPresetService.TogglePin(selected.Id);
        _presets = PromptPresetService.LoadAll();
        RefreshList();
        Changed = true;
    }

    private void ExportPresets_Click(object sender, RoutedEventArgs e)
    {
        var presetsToExport = _presets;
        if (PresetList.SelectedItem is PromptPreset selected)
            presetsToExport = [selected];

        var dialog = new SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json",
            FileName = "prompt_presets_export.json"
        };
        if (dialog.ShowDialog() == true)
        {
            PromptPresetService.Export(presetsToExport, dialog.FileName);
        }
    }

    private void ImportPresets_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "JSON files (*.json)|*.json"
        };
        if (dialog.ShowDialog() != true) return;

        var imported = PromptPresetService.Import(dialog.FileName);
        if (imported.Count == 0) return;

        foreach (var preset in imported)
            PromptPresetService.Add(preset);

        _presets = PromptPresetService.LoadAll();
        RebuildCategoryFilter();
        RefreshList();
        Changed = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = Changed;
        Close();
    }
}

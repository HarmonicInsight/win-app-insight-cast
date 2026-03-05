using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using InsightCast.Models;
using InsightCast.Services;

namespace InsightCast.Views;

public partial class PromptPresetDialog : Window
{
    public PromptPreset? Result { get; private set; }

    private readonly PromptPreset? _existingPreset;

    public PromptPresetDialog() : this(null, null) { }

    public PromptPresetDialog(PromptPreset? existing) : this(existing, null) { }

    public PromptPresetDialog(PromptPreset? existing, IEnumerable<string>? existingCategories)
    {
        InitializeComponent();
        _existingPreset = existing;

        if (existingCategories != null)
        {
            foreach (var cat in existingCategories.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct())
                CategoryCombo.Items.Add(cat);
        }

        if (existing != null)
        {
            PresetNameBox.Text = existing.Name;
            SystemPromptBox.Text = existing.SystemPrompt;
            DescriptionBox.Text = existing.Description;
            AuthorBox.Text = existing.Author;
            CategoryCombo.Text = existing.Category;
            IsDefaultCheck.IsChecked = existing.IsDefault;
        }

        UpdateCharCounts();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = PresetNameBox.Text?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            var msg = Application.Current.TryFindResource("AIPrompt.EnterName") as string ?? "Please enter a name.";
            MessageBox.Show(msg, "", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = new PromptPreset
        {
            Id = _existingPreset?.Id ?? PromptPresetService.GenerateId(),
            Name = name,
            SystemPrompt = SystemPromptBox.Text?.Trim() ?? "",
            Description = DescriptionBox.Text?.Trim() ?? "",
            Author = AuthorBox.Text?.Trim() ?? "",
            Category = CategoryCombo.Text?.Trim() ?? "",
            IsDefault = IsDefaultCheck.IsChecked == true,
            CreatedAt = _existingPreset?.CreatedAt ?? DateTime.Now,
            ModifiedAt = DateTime.Now,
        };

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void DescriptionBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateCharCounts();
    private void SystemPromptBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateCharCounts();

    private void UpdateCharCounts()
    {
        var descLen = DescriptionBox?.Text?.Length ?? 0;
        if (DescriptionCharCount != null)
            DescriptionCharCount.Text = $"{descLen}/200";

        var promptLen = SystemPromptBox?.Text?.Length ?? 0;
        if (PromptCharCount != null)
            PromptCharCount.Text = $"{promptLen:N0}";
    }
}

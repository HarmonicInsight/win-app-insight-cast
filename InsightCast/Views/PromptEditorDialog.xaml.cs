using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using InsightCast.Models;
using InsightCast.Services.Claude;
using InsightCommon.AI;
using Microsoft.Win32;

namespace InsightCast.Views;

public partial class PromptEditorDialog : Window
{
    private readonly List<UserPrompt> _userPrompts;
    private readonly string _lang;
    private readonly HashSet<AiProviderType> _configuredProviders;
    private UserPrompt? _selectedCustom;
    private bool _suppressOutputToggle = true; // suppress during InitializeComponent

    public bool HasChanges { get; private set; }

    /// <summary>実行リクエスト: プロンプトテキスト</summary>
    public string? ExecutePromptText { get; private set; }

    /// <summary>実行リクエスト: モデルID</summary>
    public string? ExecuteModelId { get; private set; }

    /// <summary>実行リクエスト: 画像生成モードか</summary>
    public bool ExecuteIsImageMode { get; private set; }

    /// <summary>実行リクエスト: 画像サイズ</summary>
    public string ExecuteImageSize { get; private set; } = "1024x1024";

    public PromptEditorDialog(List<UserPrompt> userPrompts, string lang,
        AiProviderConfig? providerConfig = null)
    {
        InitializeComponent();
        _suppressOutputToggle = false;
        _userPrompts = userPrompts;
        _lang = lang;

        // 設定済みプロバイダーを判定
        _configuredProviders = BuildConfiguredProviders(providerConfig);

        // モデルComboBox構築（Chat + ImageGeneration 対応モデル、プロバイダー別 API キー状態付き）
        var chatModels = AiModelRegistry.GetModelsWithCapability(AiCapability.Chat)
            .Select(m => new ModelItem(m.Id, m.DisplayName, _configuredProviders.Contains(m.Provider)));
        var imageModels = AiModelRegistry.GetModelsWithCapability(AiCapability.ImageGeneration)
            .Select(m => new ModelItem(m.Id, m.DisplayName + " [IMG]", _configuredProviders.Contains(m.Provider)));
        var models = chatModels.Concat(imageModels).ToList();

        ModelComboBox.ItemsSource = models;
        ModelComboBox.DisplayMemberPath = "DisplayName";
        ModelComboBox.SelectedValuePath = "ModelId";

        // 画像サイズComboBox
        ImageSizeComboBox.ItemsSource = new[]
        {
            new ImageSizeItem("1024x1024", "1024 x 1024 (正方形)"),
            new ImageSizeItem("1792x1024", "1792 x 1024 (横長 16:9)"),
            new ImageSizeItem("1024x1792", "1024 x 1792 (縦長 9:16)"),
        };
        ImageSizeComboBox.DisplayMemberPath = "DisplayName";
        ImageSizeComboBox.SelectedValuePath = "Size";
        ImageSizeComboBox.SelectedIndex = 0;

        // カテゴリComboBox
        RefreshCategoryComboBox();

        ApplyLanguage();
        BuildTree();
    }

    private static HashSet<AiProviderType> BuildConfiguredProviders(AiProviderConfig? config)
    {
        var set = new HashSet<AiProviderType>();
        if (config == null) return set;
        foreach (var provider in AiModelRegistry.GetAvailableProviders())
        {
            if (!string.IsNullOrEmpty(config.GetApiKey(provider)))
                set.Add(provider);
        }
        return set;
    }

    private string Res(string key, string fallback)
    {
        return Application.Current.TryFindResource(key) as string ?? fallback;
    }

    private void ApplyLanguage()
    {
        Title = Res("PE.Title", "プロンプト管理");
        ListHeaderText.Text = Res("PE.PromptList", "プロンプト一覧");
        NameLabel.Text = Res("PE.Name", "タイトル");
        CategoryLabel.Text = Res("PE.Category", "カテゴリ");
        ModelLabel.Text = Res("PE.Model", "モデル");
        PromptLabel.Text = Res("PE.PromptText", "プロンプト");
        RequiresContextDataCheckBox.Content = Res("PE.RequiresContextData", "シーンデータ必須");
        SaveButton.Content = Res("PE.Save", "保存");
        ExecuteButton.Content = "\u25B6 " + Res("PE.Execute", "実行");
        OutputTypeLabel.Text = Res("PE.OutputType", "出力") + ":";
        OutputText.Content = Res("PE.OutputText", "テキスト");
        OutputImage.Content = Res("PE.OutputImage", "画像");
        ImageSizeLabel.Text = Res("PE.ImageSize", "画像サイズ") + ":";
        AddButton.ToolTip = Res("PE.Add", "新規追加");
        DeleteButton.ToolTip = Res("PE.Delete", "削除");
        ExportButton.Content = "\uE898 " + Res("PE.Export", "エクスポート");
        ExportButton.ToolTip = Res("PE.Export.Tooltip", "プロンプトをJSONファイルにエクスポート");
        ImportButton.Content = "\uE896 " + Res("PE.Import", "インポート");
        ImportButton.ToolTip = Res("PE.Import.Tooltip", "JSONファイルからプロンプトをインポート");
    }

    private void RefreshCategoryComboBox()
    {
        var categories = _userPrompts
            .Select(p => p.Category)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .OrderBy(c => c)
            .ToList();
        CategoryComboBox.ItemsSource = categories;
    }

    private void BuildTree()
    {
        PromptTree.Items.Clear();

        // Preset prompts grouped by category
        var presetHeader = new TreeViewItem
        {
            Header = Res("Ai.PresetPrompts", "プリセットプロンプト"),
            IsExpanded = true,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
        };

        var presetCategories = new Dictionary<string, TreeViewItem>();
        var presetCategoryOrder = new List<string>();

        foreach (var preset in InsightCastPresetPrompts.All)
        {
            var catName = preset.GetCategory(_lang);
            if (!presetCategories.TryGetValue(catName, out var catItem))
            {
                catItem = new TreeViewItem
                {
                    Header = catName,
                    IsExpanded = false,
                    FontWeight = FontWeights.Normal,
                    FontSize = 11,
                };
                presetCategories[catName] = catItem;
                presetCategoryOrder.Add(catName);
            }
            var presetTypeTag = preset.IsImageMode ? " [IMG]" : " [TXT]";
            catItem.Items.Add(new TreeViewItem
            {
                Header = preset.Icon + " " + preset.GetLabel(_lang) + presetTypeTag,
                Tag = preset,
                FontWeight = FontWeights.Normal,
                FontSize = 11,
            });
        }

        foreach (var catName in presetCategoryOrder)
        {
            presetHeader.Items.Add(presetCategories[catName]);
        }
        PromptTree.Items.Add(presetHeader);

        // Custom (user) prompts
        var customHeader = new TreeViewItem
        {
            Header = Res("Ai.MyPrompts", "マイプロンプト"),
            IsExpanded = true,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
        };
        foreach (var entry in _userPrompts)
        {
            var typeTag = entry.IsImageMode ? " [IMG]" : " [TXT]";
            var item = new TreeViewItem
            {
                Header = entry.Icon + " " + entry.Label + typeTag,
                Tag = entry,
                FontWeight = FontWeights.Normal,
                FontSize = 11,
            };
            customHeader.Items.Add(item);
        }
        PromptTree.Items.Add(customHeader);
    }

    private void PromptTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not TreeViewItem tvi) return;

        if (tvi.Tag is InsightCastPresetPrompt preset)
        {
            _selectedCustom = null;

            EditorPanel.IsEnabled = true;
            NameBox.Text = preset.GetLabel(_lang);
            NameBox.IsReadOnly = true;
            CategoryComboBox.Text = preset.GetCategory(_lang);
            CategoryComboBox.IsEnabled = false;

            // モデル（ペルソナ → モデルID解決）
            var persona = AiPersona.FindById(preset.RecommendedPersonaId);
            ModelComboBox.SelectedValue = persona?.ModelId ?? ClaudeModels.DefaultModel;
            ModelComboBox.IsEnabled = false;

            _suppressOutputToggle = true;
            OutputText.IsChecked = !preset.IsImageMode;
            OutputImage.IsChecked = preset.IsImageMode;
            _suppressOutputToggle = false;
            OutputText.IsEnabled = false;
            OutputImage.IsEnabled = false;
            RequiresContextDataCheckBox.IsChecked = preset.RequiresContextData;
            RequiresContextDataCheckBox.IsEnabled = false;
            ApplyImageModeUI(preset.IsImageMode);
            ImageSizeComboBox.SelectedValue = "1024x1024";
            ImageSizeComboBox.IsEnabled = false;

            PromptBox.Text = preset.GetPrompt(_lang);
            PromptBox.IsReadOnly = true;

            SaveButton.IsEnabled = false;
            DeleteButton.IsEnabled = false;
            ExecuteButton.IsEnabled = true;
            CustomizeButton.IsEnabled = true;
        }
        else if (tvi.Tag is UserPrompt custom)
        {
            _selectedCustom = custom;

            EditorPanel.IsEnabled = true;
            NameBox.Text = custom.Label;
            NameBox.IsReadOnly = false;
            CategoryComboBox.Text = custom.Category;
            CategoryComboBox.IsEnabled = true;

            // モデル（ModelId 優先、なければペルソナ → モデルID解決でフォールバック）
            var customModelId = custom.RecommendedModelId;
            if (string.IsNullOrEmpty(customModelId))
            {
                var p = AiPersona.FindById(custom.RecommendedPersonaId);
                customModelId = p?.ModelId ?? ClaudeModels.DefaultModel;
            }
            ModelComboBox.SelectedValue = customModelId;
            if (ModelComboBox.SelectedItem == null)
                ModelComboBox.SelectedValue = ClaudeModels.DefaultModel;
            ModelComboBox.IsEnabled = true;

            _suppressOutputToggle = true;
            OutputText.IsChecked = !custom.IsImageMode;
            OutputImage.IsChecked = custom.IsImageMode;
            _suppressOutputToggle = false;
            OutputText.IsEnabled = true;
            OutputImage.IsEnabled = true;
            RequiresContextDataCheckBox.IsChecked = custom.RequiresContextData;
            RequiresContextDataCheckBox.IsEnabled = true;
            ApplyImageModeUI(custom.IsImageMode);
            ImageSizeComboBox.SelectedValue = custom.ImageSize ?? "1024x1024";
            ImageSizeComboBox.IsEnabled = true;

            PromptBox.Text = custom.Prompt;
            PromptBox.IsReadOnly = false;

            SaveButton.IsEnabled = true;
            DeleteButton.IsEnabled = true;
            ExecuteButton.IsEnabled = true;
            CustomizeButton.IsEnabled = false;
        }
        else
        {
            _selectedCustom = null;
            EditorPanel.IsEnabled = false;
            CustomizeButton.IsEnabled = false;
            DeleteButton.IsEnabled = false;
            ExecuteButton.IsEnabled = false;
        }
    }

    private void Customize_Click(object sender, RoutedEventArgs e)
    {
        // プリセットのプロンプトを修正してカスタム登録
        var promptText = PromptBox.Text?.Trim() ?? "";
        var name = NameBox.Text?.Trim() ?? "";
        var category = (CategoryComboBox.Text ?? "").Trim();
        var suffix = _lang == "ja" ? "カスタム" : "Custom";

        var selectedModelId = ModelComboBox.SelectedValue as string ?? ClaudeModels.DefaultModel;
        var entry = new UserPrompt
        {
            Label = name + " (" + suffix + ")",
            Category = category,
            Prompt = promptText,
            RecommendedModelId = selectedModelId,
            RecommendedPersonaId = AiPersona.FindByModelId(selectedModelId)?.Id ?? "megumi",
            IsImageMode = OutputImage.IsChecked == true,
            ImageSize = ImageSizeComboBox.SelectedValue as string ?? "1024x1024",
            RequiresContextData = RequiresContextDataCheckBox.IsChecked == true,
        };
        _userPrompts.Add(entry);
        HasChanges = true;
        RefreshCategoryComboBox();
        BuildTree();
        SelectCustomEntry(entry);
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var entry = new UserPrompt
        {
            Label = Res("PE.NewPrompt", "新しいプロンプト"),
        };
        _userPrompts.Add(entry);
        HasChanges = true;
        RefreshCategoryComboBox();
        BuildTree();
        SelectCustomEntry(entry);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCustom == null) return;

        var message = string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            Res("PE.ConfirmDelete", "{0} を削除しますか？"), _selectedCustom.Label);
        var result = MessageBox.Show(message, Res("PE.ConfirmTitle", "確認"),
            MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _userPrompts.Remove(_selectedCustom);
            _selectedCustom = null;
            HasChanges = true;
            RefreshCategoryComboBox();
            BuildTree();
            EditorPanel.IsEnabled = false;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCustom == null) return;

        var savedModelId = ModelComboBox.SelectedValue as string ?? ClaudeModels.DefaultModel;
        _selectedCustom.Label = NameBox.Text.Trim();
        _selectedCustom.Category = (CategoryComboBox.Text ?? string.Empty).Trim();
        _selectedCustom.RecommendedModelId = savedModelId;
        _selectedCustom.RecommendedPersonaId = AiPersona.FindByModelId(savedModelId)?.Id ?? "megumi";
        _selectedCustom.IsImageMode = OutputImage.IsChecked == true;
        _selectedCustom.ImageSize = ImageSizeComboBox.SelectedValue as string ?? "1024x1024";
        _selectedCustom.RequiresContextData = RequiresContextDataCheckBox.IsChecked == true;
        _selectedCustom.Prompt = PromptBox.Text.Trim();
        _selectedCustom.UpdatedAt = DateTime.Now;

        HasChanges = true;
        RefreshCategoryComboBox();

        var saved = _selectedCustom;
        BuildTree();
        SelectCustomEntry(saved);
    }

    private void SelectCustomEntry(UserPrompt entry)
    {
        if (PromptTree.Items.Count >= 2 && PromptTree.Items[1] is TreeViewItem customRoot)
        {
            foreach (TreeViewItem child in customRoot.Items)
            {
                if (child.Tag == entry)
                {
                    child.IsSelected = true;
                    child.BringIntoView();
                    break;
                }
            }
        }
    }

    private void Execute_Click(object sender, RoutedEventArgs e)
    {
        ExecutePromptText = PromptBox.Text.Trim();
        ExecuteModelId = ModelComboBox.SelectedValue as string ?? ClaudeModels.DefaultModel;
        ExecuteIsImageMode = OutputImage.IsChecked == true;
        ExecuteImageSize = ImageSizeComboBox.SelectedValue as string ?? "1024x1024";
        Close();
    }

    // ── Export / Import ──

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_userPrompts.Count == 0)
        {
            MessageBox.Show(
                Res("PE.Export.Empty", "エクスポートするプロンプトがありません。"),
                Res("PE.Export", "エクスポート"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "JSON|*.json",
            FileName = $"prompts_{DateTime.Now:yyyyMMdd_HHmmss}.json",
            Title = Res("PE.Export", "エクスポート"),
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            var json = JsonSerializer.Serialize(_userPrompts, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
            File.WriteAllText(dlg.FileName, json);
            MessageBox.Show(
                string.Format(Res("PE.Export.Success", "{0} 件のプロンプトをエクスポートしました。"), _userPrompts.Count),
                Res("PE.Export", "エクスポート"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Res("PE.Export", "エクスポート"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "JSON|*.json",
            Title = Res("PE.Import", "インポート"),
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            var json = File.ReadAllText(dlg.FileName);
            var imported = JsonSerializer.Deserialize<List<UserPrompt>>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
            });
            if (imported == null || imported.Count == 0)
            {
                MessageBox.Show(
                    Res("PE.Import.Empty", "インポートできるプロンプトがありませんでした。"),
                    Res("PE.Import", "インポート"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Merge: skip duplicates by Id
            var existingIds = new HashSet<string>(_userPrompts.Select(p => p.Id));
            int added = 0;
            foreach (var item in imported)
            {
                if (string.IsNullOrEmpty(item.Id))
                    item.Id = Guid.NewGuid().ToString("N");
                if (!existingIds.Contains(item.Id))
                {
                    _userPrompts.Add(item);
                    existingIds.Add(item.Id);
                    added++;
                }
            }

            if (added > 0)
            {
                HasChanges = true;
                RefreshCategoryComboBox();
                BuildTree();
            }

            MessageBox.Show(
                string.Format(Res("PE.Import.Success", "{0} 件のプロンプトをインポートしました。（{1} 件は重複スキップ）"),
                    added, imported.Count - added),
                Res("PE.Import", "インポート"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Res("PE.Import", "インポート"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Output Type Toggle ──

    private void OutputImage_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressOutputToggle) return;
        ApplyImageModeUI(true);

        // Auto-select an image model for custom prompts
        if (_selectedCustom != null)
        {
            // Pick first available image generation model
            var imageModels = AiModelRegistry.GetModelsWithCapability(AiCapability.ImageGeneration);
            var firstAvailable = imageModels.FirstOrDefault(m => _configuredProviders.Contains(m.Provider));
            if (firstAvailable != null)
                ModelComboBox.SelectedValue = firstAvailable.Id;
            else if (imageModels.Count > 0)
                ModelComboBox.SelectedValue = imageModels[0].Id;

            RequiresContextDataCheckBox.IsChecked = false;
            ImageSizeComboBox.SelectedValue = "1024x1024";
        }
    }

    private void OutputText_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressOutputToggle) return;
        ApplyImageModeUI(false);

        // Restore text defaults for custom prompts
        if (_selectedCustom != null)
        {
            ModelComboBox.SelectedValue = ClaudeModels.DefaultModel;
            RequiresContextDataCheckBox.IsChecked = true;
        }
    }

    private void ApplyImageModeUI(bool isImage)
    {
        ImageSizePanel.Visibility = isImage ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Font Size Control ──

    private void FontSizeUp_Click(object sender, RoutedEventArgs e)
    {
        var size = Math.Min(PromptBox.FontSize + 1, 24);
        PromptBox.FontSize = size;
        FontSizeLabel.Text = size.ToString();
    }

    private void FontSizeDown_Click(object sender, RoutedEventArgs e)
    {
        var size = Math.Max(PromptBox.FontSize - 1, 9);
        PromptBox.FontSize = size;
        FontSizeLabel.Text = size.ToString();
    }

    private sealed record ModelItem(string ModelId, string DisplayName, bool IsProviderConfigured);
    private sealed record ImageSizeItem(string Size, string DisplayName);
}

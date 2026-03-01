using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using InsightCast.Models;
using InsightCast.Services.Claude;
using InsightCommon.AI;

namespace InsightCast.Views;

public partial class PromptEditorDialog : Window
{
    private readonly List<UserPrompt> _userPrompts;
    private readonly string _lang;
    private UserPrompt? _selectedCustom;
    private bool _suppressOutputToggle = true; // suppress during InitializeComponent

    public bool HasChanges { get; private set; }

    /// <summary>実行リクエスト: プロンプトテキスト</summary>
    public string? ExecutePromptText { get; private set; }

    /// <summary>実行リクエスト: モード (check/advice)</summary>
    public string? ExecuteMode { get; private set; }

    /// <summary>実行リクエスト: ペルソナID</summary>
    public string? ExecutePersonaId { get; private set; }

    /// <summary>実行リクエスト: 画像生成モードか</summary>
    public bool ExecuteIsImageMode { get; private set; }

    /// <summary>実行リクエスト: 画像サイズ</summary>
    public string ExecuteImageSize { get; private set; } = "1024x1024";

    public PromptEditorDialog(List<UserPrompt> userPrompts, string lang)
    {
        InitializeComponent();
        _suppressOutputToggle = false;
        _userPrompts = userPrompts;
        _lang = lang;

        // モデルComboBox構築 (strip "Claude" prefix: 俊、恵、学 + model name)
        var models = ClaudeModels.Registry
            .Where(m => m.IsActive)
            .Select(m =>
            {
                var persona = AiPersona.FindByModelId(m.Id);
                var name = persona?.GetName(lang)?.Replace("Claude", "").Trim() ?? m.Family;
                return new ModelItem(
                    persona?.Id ?? m.Family,
                    $"{name} ({m.Label} {m.CostIndicator})");
            })
            .ToList();

        // OpenAI models
        models.Add(new ModelItem("dalle3", "DALL-E 3 (OpenAI $$$)"));
        models.Add(new ModelItem("gpt-image-1", "GPT Image 1 (OpenAI $$)"));

        ModelComboBox.ItemsSource = models;
        ModelComboBox.DisplayMemberPath = "DisplayName";
        ModelComboBox.SelectedValuePath = "PersonaId";

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
        ModeLabel.Text = Res("PE.Mode", "モード") + ":";
        PromptLabel.Text = Res("PE.PromptText", "プロンプト");
        RequiresContextDataCheckBox.Content = Res("PE.RequiresContextData", "シーンデータ必須");
        SaveButton.Content = Res("PE.Save", "保存");
        ExecuteButton.Content = "\u25B6 " + Res("PE.Execute", "実行");
        ModeDescriptionText.Text = Res("PE.ModeDesc",
            "Check: シーンデータをAIが修正提案します\nAdvice: アドバイスをテキストで返します");
        OutputTypeLabel.Text = Res("PE.OutputType", "出力") + ":";
        OutputText.Content = Res("PE.OutputText", "テキスト");
        OutputImage.Content = Res("PE.OutputImage", "画像");
        ImageSizeLabel.Text = Res("PE.ImageSize", "画像サイズ") + ":";
        AddButton.ToolTip = Res("PE.Add", "新規追加");
        DeleteButton.ToolTip = Res("PE.Delete", "削除");
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

        if (tvi.Tag is PresetPrompt preset)
        {
            _selectedCustom = null;

            EditorPanel.IsEnabled = true;
            NameBox.Text = preset.GetLabel(_lang);
            NameBox.IsReadOnly = true;
            CategoryComboBox.Text = preset.GetCategory(_lang);
            CategoryComboBox.IsEnabled = false;

            // モデル
            var persona = AiPersona.FindById(preset.RecommendedPersonaId);
            ModelComboBox.SelectedValue = persona?.Id ?? "megumi";
            ModelComboBox.IsEnabled = false;

            ModeCheck.IsChecked = preset.Mode == "check";
            ModeAdvice.IsChecked = preset.Mode != "check";
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
        }
        else if (tvi.Tag is UserPrompt custom)
        {
            _selectedCustom = custom;

            EditorPanel.IsEnabled = true;
            NameBox.Text = custom.Label;
            NameBox.IsReadOnly = false;
            CategoryComboBox.Text = custom.Category;
            CategoryComboBox.IsEnabled = true;

            ModelComboBox.SelectedValue = custom.RecommendedPersonaId;
            if (ModelComboBox.SelectedItem == null)
                ModelComboBox.SelectedValue = "megumi";
            ModelComboBox.IsEnabled = true;

            ModeCheck.IsChecked = custom.Mode == "check";
            ModeAdvice.IsChecked = custom.Mode != "check";
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
        }
        else
        {
            _selectedCustom = null;
            EditorPanel.IsEnabled = false;
            DeleteButton.IsEnabled = false;
            ExecuteButton.IsEnabled = false;
        }
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

        _selectedCustom.Label = NameBox.Text.Trim();
        _selectedCustom.Category = (CategoryComboBox.Text ?? string.Empty).Trim();
        _selectedCustom.RecommendedPersonaId = ModelComboBox.SelectedValue as string ?? "megumi";
        _selectedCustom.Mode = ModeCheck.IsChecked == true ? "check" : "advice";
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
        ExecuteMode = ModeCheck.IsChecked == true ? "check" : "advice";
        ExecutePersonaId = ModelComboBox.SelectedValue as string ?? "megumi";
        ExecuteIsImageMode = OutputImage.IsChecked == true;
        ExecuteImageSize = ImageSizeComboBox.SelectedValue as string ?? "1024x1024";
        Close();
    }

    // ── Output Type Toggle ──

    private void OutputImage_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressOutputToggle) return;
        ApplyImageModeUI(true);

        // Auto-fill image defaults for custom prompts
        if (_selectedCustom != null)
        {
            ModelComboBox.SelectedValue = "dalle3";
            RequiresContextDataCheckBox.IsChecked = false;
            ModeAdvice.IsChecked = true;
            ImageSizeComboBox.SelectedValue = "1024x1024";

            // Hide mode (not relevant for image generation)
            ModePanel.Visibility = Visibility.Collapsed;
            ModeDescriptionText.Visibility = Visibility.Collapsed;
        }
    }

    private void OutputText_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressOutputToggle) return;
        ApplyImageModeUI(false);

        // Restore text defaults for custom prompts
        if (_selectedCustom != null)
        {
            ModelComboBox.SelectedValue = "megumi";
            RequiresContextDataCheckBox.IsChecked = true;
            ModeCheck.IsChecked = true;

            ModePanel.Visibility = Visibility.Visible;
            ModeDescriptionText.Visibility = Visibility.Visible;
        }
    }

    private void ApplyImageModeUI(bool isImage)
    {
        ImageSizePanel.Visibility = isImage ? Visibility.Visible : Visibility.Collapsed;
        ModePanel.Visibility = isImage ? Visibility.Collapsed : Visibility.Visible;
        ModeDescriptionText.Visibility = isImage ? Visibility.Collapsed : Visibility.Visible;
    }

    private sealed record ModelItem(string PersonaId, string DisplayName);
    private sealed record ImageSizeItem(string Size, string DisplayName);
}

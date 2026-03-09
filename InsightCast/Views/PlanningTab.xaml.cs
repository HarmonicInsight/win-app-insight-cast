using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using InsightCast.Core;
using InsightCast.Models;
using InsightCast.Services;
using InsightCast.Services.Claude;
using InsightCast.ViewModels;
using InsightCommon.AI;
using InsightCommon.UI.Reference;
using PromptPresetService = InsightCast.Services.PromptPresetService;

namespace InsightCast.Views;

/// <summary>
/// PPTX作成タブ — Doc の DocumentFactory を移植。
/// ① Input (参考資料) | ② Process (プロンプト一覧+エディタ) | ③ Output (成果物) | ④ AI Chat (コンシェルジュ)
/// の4パネル構成 + 下部アクションボタン。
/// </summary>
public partial class PlanningTab : UserControl
{
    private PlanningViewModel? _viewModel;
    private ReferencePanelViewModel? _referencePanelVm;
    private ChatPanelViewModel? _chatVm;
    private Config? _config;

    private bool _isPresetSelected;
    private UserPromptPreset? _selectedPreset;
    private HashSet<AiProviderType> _configuredProviders = new();

    // Last AI output for Apply/Confirm
    private string? _lastAiOutput;

    /// <summary>実行ボタン押下時に発火。プロンプトテキスト + 参考資料コンテキスト + モデルID。</summary>
    public event Action<string /*promptText*/, string? /*referenceContext*/, string /*modelId*/>? ExecuteRequested;

    public PlanningTab()
    {
        InitializeComponent();
    }

    /// <summary>Gets the PlanningViewModel for this tab.</summary>
    public PlanningViewModel? ViewModel => _viewModel;

    /// <summary>Gets the ReferencePanelViewModel for this tab.</summary>
    public ReferencePanelViewModel? ReferencePanelViewModel => _referencePanelVm;

    /// <summary>Gets the ChatPanelViewModel for the embedded chat.</summary>
    public ChatPanelViewModel? ChatViewModel => _chatVm;

    public void Initialize(Config config, Project project, AiProviderConfig? providerConfig = null)
    {
        _viewModel = new PlanningViewModel(config, project);
        _config = config;
        DataContext = _viewModel;

        _configuredProviders = BuildConfiguredProviders(providerConfig);

        // Initialize reference panel (shared InsightCommon control)
        _referencePanelVm = new ReferencePanelViewModel(new CastReferenceMaterialParser());
        ReferencePanelControl.DataContext = _referencePanelVm;

        // Load working folder if project has one
        if (!string.IsNullOrEmpty(project.WorkingFolderPath) && System.IO.Directory.Exists(project.WorkingFolderPath))
        {
            _referencePanelVm.LoadFromFolder(project.WorkingFolderPath);
        }

        // Model ComboBox
        var models = AiModelRegistry.GetModelsWithCapability(AiCapability.Chat)
            .Select(m => new ModelItem(
                m.Id,
                m.DisplayName,
                _configuredProviders.Contains(m.Provider)))
            .ToList();
        ModelCombo.ItemsSource = models;
        ModelCombo.DisplayMemberPath = "DisplayName";
        ModelCombo.SelectedValuePath = "ModelId";
        ModelCombo.SelectedValue = ClaudeModels.DefaultModel;

        RefreshCategoryComboBox();
        BuildTree();
    }

    /// <summary>Chat パネルを初期化する。MainWindow から ClaudeService 作成後に呼び出す。</summary>
    public void InitializeChatPanel(ChatPanelViewModel chatVm)
    {
        _chatVm = chatVm;
        PlanningChatPanel.DataContext = chatVm;

        // AI の応答を Output パネルに反映するためイベント購読
        chatVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChatPanelViewModel.LastAiResult))
            {
                Dispatcher.Invoke(() => OnAiResultReceived(chatVm.LastAiResult));
            }
        };

        // Chat panel の Close/PopOut はここでは無視（PlanningTab 内では常時表示）
        PlanningChatPanel.CloseRequested += () => { /* noop in planning tab */ };
    }

    /// <summary>プロバイダー設定が更新された時に呼ぶ。</summary>
    public void UpdateProviderConfig(AiProviderConfig? providerConfig)
    {
        _configuredProviders = BuildConfiguredProviders(providerConfig);
    }

    public void RefreshScenes()
    {
        _viewModel?.RefreshSceneList();
    }

    /// <summary>サムネイル設定をProjectから再読み込み（テンプレート適用後に呼び出す）</summary>
    public void ReloadThumbnailSettings()
    {
        _viewModel?.LoadThumbnailSettingsFromProject();
    }

    /// <summary>Raised when scenes are added, removed, or modified in the planning tab.</summary>
    public event Action? ScenesChanged
    {
        add { if (_viewModel != null) _viewModel.ScenesChanged += value; }
        remove { if (_viewModel != null) _viewModel.ScenesChanged -= value; }
    }

    /// <summary>参考資料パネルの表示/非表示を切り替える。</summary>
    public void ToggleReferencePanel()
    {
        var isOpen = RefBorder.Visibility == Visibility.Visible;
        if (isOpen)
        {
            RefCol.Width = new GridLength(0);
            RefCol.MinWidth = 0;
            RefCol.MaxWidth = 0;
            RefSplitterCol.Width = new GridLength(0);
            RefBorder.Visibility = Visibility.Collapsed;
            RefSplitter.Visibility = Visibility.Collapsed;
        }
        else
        {
            RefCol.Width = new GridLength(200);
            RefCol.MinWidth = 120;
            RefCol.MaxWidth = 320;
            RefSplitterCol.Width = GridLength.Auto;
            RefBorder.Visibility = Visibility.Visible;
            RefSplitter.Visibility = Visibility.Visible;
        }
    }

    // ── Output Panel ──

    private void OnAiResultReceived(string result)
    {
        if (string.IsNullOrWhiteSpace(result)) return;

        _lastAiOutput = result;
        ApplyButton.IsEnabled = true;
        ConfirmButton.IsEnabled = true;

        // Add output item to the panel
        OutputEmptyText.Visibility = Visibility.Collapsed;
        AddOutputItem(result);
    }

    private void AddOutputItem(string content)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");

        // Doc Factory style: green-bordered success card
        var border = new Border
        {
            BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x16, 0xA3, 0x4A)), // #16A34A
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 8),
            Background = System.Windows.Media.Brushes.White,
        };

        var stack = new StackPanel();

        // Success header with checkmark (Doc Factory style)
        var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        headerPanel.Children.Add(new TextBlock
        {
            Text = "\uE73E",
            FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
            FontSize = 14,
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x16, 0xA3, 0x4A)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        });
        headerPanel.Children.Add(new TextBlock
        {
            Text = $"AI Output [{timestamp}]",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x16, 0xA3, 0x4A)),
            VerticalAlignment = VerticalAlignment.Center,
        });
        stack.Children.Add(headerPanel);

        // Content area with background (Doc Factory style)
        var contentBorder = new Border
        {
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xFA, 0xF8, 0xF5)), // #FAF8F5
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 4),
        };
        var displayContent = content.Length > 500 ? content[..500] + "..." : content;
        var textBox = new TextBox
        {
            Text = displayContent,
            TextWrapping = TextWrapping.Wrap,
            IsReadOnly = true,
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 11,
            MaxHeight = 200,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Cursor = System.Windows.Input.Cursors.Arrow,
        };
        contentBorder.Child = textBox;
        stack.Children.Add(contentBorder);

        border.Child = stack;
        OutputPanel.Children.Add(border);
    }

    // ── Ribbon Delegation (called from MainWindow) ──

    public void RibbonAddFile()
    {
        ReferencePanelControl.TriggerAddFile();
    }

    public void RibbonAddFolder()
    {
        ReferencePanelControl.TriggerAddFolder();
    }

    public void RibbonClearAllReferences()
    {
        _referencePanelVm?.ClearAll();
    }

    public void RibbonFocusPromptTree()
    {
        PromptTree?.Focus();
    }

    // ── Action Buttons ──

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastAiOutput)) return;

        // AI の出力をチャットパネル経由でシーンに反映
        // ExecuteRequested で MainWindow に通知
        ExecuteRequested?.Invoke(_lastAiOutput, null, "apply");
    }

    private void Register_Click(object sender, RoutedEventArgs e)
    {
        // 現在のプロンプトをカスタムとして登録
        var promptText = PromptBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(promptText))
        {
            // チャットパネルの入力テキストを使用
            promptText = _chatVm?.AiInput?.Trim();
        }

        if (string.IsNullOrWhiteSpace(promptText)) return;

        var newPreset = new UserPromptPreset
        {
            Id = PromptPresetService.GenerateId(),
            Name = LocalizationService.GetString("PE.NewPrompt"),
            SystemPrompt = promptText,
            Category = CategoryCombo.Text?.Trim() ?? "",
        };
        PromptPresetService.Add(newPreset);
        RefreshCategoryComboBox();
        BuildTree();
        SelectPresetInTree(newPreset.Id);
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastAiOutput)) return;

        // 成果物の確認ダイアログを表示
        var dialog = new Window
        {
            Title = LocalizationService.GetString("PE.Confirm"),
            Width = 700,
            Height = 500,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
        };

        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(12),
        };

        var textBox = new TextBox
        {
            Text = _lastAiOutput,
            TextWrapping = TextWrapping.Wrap,
            IsReadOnly = true,
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 12,
        };

        scrollViewer.Content = textBox;
        dialog.Content = scrollViewer;
        dialog.ShowDialog();
    }

    // ── Tree Building ──

    private void BuildTree()
    {
        PromptTree.Items.Clear();

        var allPresets = PromptPresetService.LoadAll();

        // Built-in presets by category
        var presetHeader = new TreeViewItem
        {
            Header = LocalizationService.GetString("PE.Presets"),
            IsExpanded = true,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
        };

        var builtIn = allPresets.Where(p => p.Id.StartsWith("builtin_", StringComparison.Ordinal)).ToList();
        var categories = builtIn.Select(p => p.Category).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct();

        foreach (var category in categories)
        {
            var catItem = new TreeViewItem
            {
                Header = category,
                IsExpanded = false,
                FontWeight = FontWeights.Normal,
                FontSize = 11,
            };
            foreach (var preset in builtIn.Where(p => p.Category == category))
            {
                catItem.Items.Add(new TreeViewItem
                {
                    Header = preset.Name,
                    Tag = preset,
                    FontWeight = FontWeights.Normal,
                    FontSize = 11,
                });
            }
            presetHeader.Items.Add(catItem);
        }
        PromptTree.Items.Add(presetHeader);

        // Custom prompts — group-based
        var customHeader = new TreeViewItem
        {
            Header = LocalizationService.GetString("PE.Custom"),
            IsExpanded = true,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
        };
        var custom = allPresets.Where(p => !p.Id.StartsWith("builtin_", StringComparison.Ordinal)).ToList();

        var grouped = custom.GroupBy(p => string.IsNullOrWhiteSpace(p.Category) ? "" : p.Category)
            .OrderBy(g => g.Key == "" ? 1 : 0)
            .ThenBy(g => g.Key);

        foreach (var group in grouped)
        {
            if (string.IsNullOrEmpty(group.Key))
            {
                foreach (var preset in group)
                {
                    customHeader.Items.Add(CreatePresetTreeItem(preset));
                }
            }
            else
            {
                var groupItem = new TreeViewItem
                {
                    Header = "\uD83D\uDCC1 " + group.Key,
                    IsExpanded = true,
                    FontWeight = FontWeights.Normal,
                    FontSize = 11,
                };
                foreach (var preset in group)
                {
                    groupItem.Items.Add(CreatePresetTreeItem(preset));
                }
                customHeader.Items.Add(groupItem);
            }
        }
        PromptTree.Items.Add(customHeader);
    }

    private static TreeViewItem CreatePresetTreeItem(UserPromptPreset preset)
    {
        var label = preset.Name;
        if (preset.IsDefault) label = "\u2605 " + label;
        if (preset.IsPinned) label = "\uD83D\uDCCC " + label;
        return new TreeViewItem
        {
            Header = label,
            Tag = preset,
            FontWeight = FontWeights.Normal,
            FontSize = 11,
        };
    }

    // ── Tree Selection ──

    private void PromptTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not TreeViewItem tvi || tvi.Tag is not UserPromptPreset preset)
        {
            _isPresetSelected = false;
            _selectedPreset = null;
            EditorPanel.IsEnabled = false;
            DeleteButton.IsEnabled = false;
            CustomizeButton.IsEnabled = false;
            return;
        }

        _selectedPreset = preset;
        EditorPanel.IsEnabled = true;
        NameBox.Text = preset.Name;
        PromptBox.Text = preset.SystemPrompt;
        CategoryCombo.Text = preset.Category ?? "";
        ExecuteButton.IsEnabled = true;

        var modelId = preset.ModelId;
        if (string.IsNullOrEmpty(modelId))
            modelId = ClaudeModels.DefaultModel;
        ModelCombo.SelectedValue = modelId;
        if (ModelCombo.SelectedItem == null)
            ModelCombo.SelectedValue = ClaudeModels.DefaultModel;

        if (preset.Id.StartsWith("builtin_", StringComparison.Ordinal))
        {
            _isPresetSelected = true;
            NameBox.IsReadOnly = true;
            PromptBox.IsReadOnly = true;
            CategoryCombo.IsEnabled = false;
            ModelCombo.IsEnabled = false;
            SaveButton.IsEnabled = false;
            DeleteButton.IsEnabled = false;
            CustomizeButton.IsEnabled = true;
        }
        else
        {
            _isPresetSelected = false;
            NameBox.IsReadOnly = false;
            PromptBox.IsReadOnly = false;
            CategoryCombo.IsEnabled = true;
            ModelCombo.IsEnabled = true;
            SaveButton.IsEnabled = true;
            DeleteButton.IsEnabled = true;
            CustomizeButton.IsEnabled = false;
        }
    }

    // ── CRUD ──

    private void Customize_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPreset == null) return;
        var selectedModelId = ModelCombo.SelectedValue as string ?? ClaudeModels.DefaultModel;
        var newPreset = new UserPromptPreset
        {
            Id = PromptPresetService.GenerateId(),
            Name = _selectedPreset.Name + " (" + LocalizationService.GetString("PE.CustomSuffix") + ")",
            SystemPrompt = _selectedPreset.SystemPrompt,
            Description = _selectedPreset.Description,
            Author = "",
            Category = _selectedPreset.Category,
            ModelId = selectedModelId,
        };
        PromptPresetService.Add(newPreset);
        RefreshCategoryComboBox();
        BuildTree();
        SelectPresetInTree(newPreset.Id);
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var newPreset = new UserPromptPreset
        {
            Id = PromptPresetService.GenerateId(),
            Name = LocalizationService.GetString("PE.NewPrompt"),
            SystemPrompt = "",
            Category = "",
        };
        PromptPresetService.Add(newPreset);
        BuildTree();
        SelectPresetInTree(newPreset.Id);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPreset == null || _isPresetSelected) return;
        PromptPresetService.Remove(_selectedPreset.Id);
        _selectedPreset = null;
        RefreshCategoryComboBox();
        EditorPanel.IsEnabled = false;
        BuildTree();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPreset == null || _isPresetSelected) return;
        var savedModelId = ModelCombo.SelectedValue as string ?? ClaudeModels.DefaultModel;
        var updated = new UserPromptPreset
        {
            Id = _selectedPreset.Id,
            Name = NameBox.Text.Trim(),
            SystemPrompt = PromptBox.Text.Trim(),
            Description = _selectedPreset.Description,
            Author = _selectedPreset.Author,
            Category = (CategoryCombo.Text ?? "").Trim(),
            ModelId = savedModelId,
            IsDefault = _selectedPreset.IsDefault,
            IsPinned = _selectedPreset.IsPinned,
            UsageCount = _selectedPreset.UsageCount,
            LastUsedAt = _selectedPreset.LastUsedAt,
            CreatedAt = _selectedPreset.CreatedAt,
            ModifiedAt = DateTime.Now,
        };
        PromptPresetService.Update(_selectedPreset.Id, updated);
        _selectedPreset = updated;
        RefreshCategoryComboBox();
        var savedId = updated.Id;
        BuildTree();
        SelectPresetInTree(savedId);
    }

    private void Execute_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPreset == null) return;
        var text = _isPresetSelected ? _selectedPreset.SystemPrompt : PromptBox.Text.Trim();

        if (!_isPresetSelected)
        {
            Save_Click(sender, e);
        }

        var referenceContext = _referencePanelVm?.BuildReferenceContext();
        var modelId = ModelCombo.SelectedValue as string ?? ClaudeModels.DefaultModel;
        PromptPresetService.IncrementUsage(_selectedPreset.Id);

        // Send to embedded chat panel if available
        if (_chatVm != null)
        {
            // Build the full prompt with reference context
            var fullPrompt = text;
            if (!string.IsNullOrWhiteSpace(referenceContext))
            {
                fullPrompt = $"以下の参考資料を元に処理してください。\n\n【参考資料】\n{referenceContext}\n\n【指示】\n{text}";
            }

            _chatVm.AiInput = fullPrompt;
            if (_chatVm.ExecutePromptCommand.CanExecute(null))
            {
                _chatVm.ExecutePromptCommand.Execute(null);
            }
        }

        // Also fire legacy event for MainWindow
        ExecuteRequested?.Invoke(text, referenceContext, modelId);
    }

    // ── Font Size ──

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

    // ── Group Management ──

    private void AddGroup_Click(object sender, RoutedEventArgs e)
    {
        var newPreset = new UserPromptPreset
        {
            Id = PromptPresetService.GenerateId(),
            Name = LocalizationService.GetString("PE.NewPrompt"),
            SystemPrompt = "",
            Category = CategoryCombo.Text?.Trim() ?? "",
        };
        PromptPresetService.Add(newPreset);
        BuildTree();
        SelectPresetInTree(newPreset.Id);
    }

    // ── Helpers ──

    private void SelectPresetInTree(string id)
    {
        foreach (TreeViewItem root in PromptTree.Items)
        {
            foreach (object child in root.Items)
            {
                if (child is TreeViewItem tvi && tvi.Tag is UserPromptPreset p && p.Id == id)
                {
                    tvi.IsSelected = true;
                    tvi.BringIntoView();
                    return;
                }
                if (child is TreeViewItem catItem)
                {
                    foreach (object grandchild in catItem.Items)
                    {
                        if (grandchild is TreeViewItem gvi && gvi.Tag is UserPromptPreset gp && gp.Id == id)
                        {
                            catItem.IsExpanded = true;
                            gvi.IsSelected = true;
                            gvi.BringIntoView();
                            return;
                        }
                    }
                }
            }
        }
    }

    private void RefreshCategoryComboBox()
    {
        var allPresets = PromptPresetService.LoadAll();
        var categories = allPresets
            .Select(p => p.Category)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .OrderBy(c => c)
            .ToList();
        CategoryCombo.ItemsSource = categories;
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

    private sealed record ModelItem(string ModelId, string DisplayName, bool IsProviderConfigured);
}

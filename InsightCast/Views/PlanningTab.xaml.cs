using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
    #pragma warning disable CS0067
    public event Action<string /*promptText*/, string? /*referenceContext*/, string /*modelId*/>? ExecuteRequested;
    #pragma warning restore CS0067

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
        ModelCombo.SelectedValue = ClaudeModels.DefaultPremiumModel;

        // Artifact filter combo
        ArtifactFilterCombo.ItemsSource = new[]
        {
            LocalizationService.GetString("PE.OutputAll"),
        };
        ArtifactFilterCombo.SelectedIndex = 0;

        RefreshCategoryComboBox();
        BuildTree();
    }

    /// <summary>Chat パネルを初期化する。MainWindow から ClaudeService 作成後に呼び出す。</summary>
    public void InitializeChatPanel(ChatPanelViewModel chatVm)
    {
        _chatVm = chatVm;
        PlanningChatPanel.DataContext = chatVm;

        // ── Context Providers（参考資料 + プロジェクトサマリーをシステムプロンプトに注入）──
        chatVm.GetReferenceContext = () => _referencePanelVm?.BuildReferenceContext();
        chatVm.GetProjectSummary = () => BuildProjectSummary();

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

        // ウェルカムメッセージ（プッシュ型）
        SendWelcomeMessage(chatVm);
    }

    /// <summary>プロジェクトのシーン概要をテキスト化（システムプロンプト注入用）</summary>
    private string? BuildProjectSummary()
    {
        var project = _viewModel?.Project;
        if (project == null || project.Scenes.Count == 0) return null;

        var lang = _config?.Language == "EN" ? "EN" : "JA";
        var sb = new System.Text.StringBuilder();

        var scenesWithNarration = project.Scenes.Count(s => !string.IsNullOrWhiteSpace(s.NarrationText));
        var scenesWithMedia = project.Scenes.Count(s => s.MediaType != MediaType.None && !string.IsNullOrEmpty(s.MediaPath));
        var scenesWithSubtitle = project.Scenes.Count(s => !string.IsNullOrWhiteSpace(s.SubtitleText));

        if (lang == "EN")
        {
            sb.AppendLine($"- Total scenes: {project.Scenes.Count}");
            sb.AppendLine($"- Scenes with narration: {scenesWithNarration}");
            sb.AppendLine($"- Scenes with media: {scenesWithMedia}");
            sb.AppendLine($"- Scenes with subtitles: {scenesWithSubtitle}");
            sb.AppendLine($"- Resolution: {project.Output.Resolution}");
            sb.AppendLine($"- Default transition: {project.DefaultTransition}");
        }
        else
        {
            sb.AppendLine($"- シーン数: {project.Scenes.Count}");
            sb.AppendLine($"- ナレーションあり: {scenesWithNarration}");
            sb.AppendLine($"- メディアあり: {scenesWithMedia}");
            sb.AppendLine($"- 字幕あり: {scenesWithSubtitle}");
            sb.AppendLine($"- 解像度: {project.Output.Resolution}");
            sb.AppendLine($"- デフォルトトランジション: {project.DefaultTransition}");
        }

        return sb.ToString();
    }

    private void SendWelcomeMessage(ChatPanelViewModel chatVm)
    {
        if (chatVm.ChatMessages.Count > 0) return; // 既にメッセージがある場合はスキップ

        var welcome = LocalizationService.GetString("PE.WelcomeMessage");
        chatVm.ChatMessages.Add(new InsightCommon.AI.ChatMessageVm
        {
            Role = InsightCommon.AI.ChatRole.Assistant,
            Content = welcome,
            IsWelcome = true,
        });
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
            FontSize = 11,
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
        RefreshArtifactCount();
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

    // ── Artifact Panel ──

    private void OpenArtifactFolder_Click(object sender, RoutedEventArgs e)
    {
        var project = _viewModel?.Project;
        if (project == null) return;
        var folder = project.WorkingFolderPath;
        if (!string.IsNullOrEmpty(folder) && System.IO.Directory.Exists(folder))
        {
            System.Diagnostics.Process.Start("explorer.exe", folder);
        }
    }

    private void ArtifactFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        ApplyArtifactFilters();
    }

    private void ArtifactSearch_Changed(object sender, TextChangedEventArgs e)
    {
        ApplyArtifactFilters();
    }

    private void ApplyArtifactFilters()
    {
        if (OutputPanel == null) return;

        var filterTag = (ArtifactFilterCombo?.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        var searchText = ArtifactSearchBox?.Text?.Trim() ?? "";

        foreach (UIElement child in OutputPanel.Children)
        {
            if (child is not FrameworkElement fe) continue;
            if (fe == OutputEmptyText) continue;

            bool matchesFilter = string.IsNullOrEmpty(filterTag) ||
                (fe.Tag is string tag && tag.Equals(filterTag, StringComparison.OrdinalIgnoreCase));

            bool matchesSearch = string.IsNullOrEmpty(searchText) ||
                (fe is ContentControl cc && cc.Content?.ToString()?.Contains(searchText, StringComparison.OrdinalIgnoreCase) == true) ||
                (fe.ToolTip?.ToString()?.Contains(searchText, StringComparison.OrdinalIgnoreCase) == true);

            fe.Visibility = matchesFilter && matchesSearch ? Visibility.Visible : Visibility.Collapsed;
        }

        RefreshArtifactCount();
    }

    private void RefreshArtifactCount()
    {
        var count = OutputPanel.Children.Count;
        if (OutputEmptyText.Visibility == Visibility.Visible) count = 0;
        ArtifactCountBadge.Text = count > 0 ? $"({count})" : "";
    }

    // ── Tree Building ──

    private void BuildTree()
    {
        PromptTree.Items.Clear();

        var allPresets = PromptPresetService.LoadAll();
        var custom = allPresets.Where(p => !p.Id.StartsWith("builtin_", StringComparison.Ordinal)).ToList();
        var builtIn = allPresets.Where(p => p.Id.StartsWith("builtin_", StringComparison.Ordinal)).ToList();

        // ── マイプロンプト（上） ──
        var customHeader = new TreeViewItem
        {
            Header = LocalizationService.GetString("PE.Custom"),
            IsExpanded = true,
            FontWeight = FontWeights.Medium,
            FontSize = 11,
        };

        if (custom.Count == 0)
        {
            customHeader.Items.Add(new TreeViewItem
            {
                Header = LocalizationService.GetString("PE.CustomEmpty"),
                FontStyle = FontStyles.Italic,
                FontSize = 10,
                Foreground = System.Windows.Media.Brushes.Gray,
                IsEnabled = false,
            });
        }
        else
        {
            var grouped = custom.GroupBy(p => string.IsNullOrWhiteSpace(p.Category) ? "" : p.Category)
                .OrderBy(g => g.Key == "" ? 1 : 0)
                .ThenBy(g => g.Key);

            foreach (var group in grouped)
            {
                if (string.IsNullOrEmpty(group.Key))
                {
                    foreach (var preset in group)
                        customHeader.Items.Add(CreatePresetTreeItem(preset));
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
                        groupItem.Items.Add(CreatePresetTreeItem(preset));
                    customHeader.Items.Add(groupItem);
                }
            }
        }
        PromptTree.Items.Add(customHeader);

        // ── 区切り ──
        PromptTree.Items.Add(new Separator { Margin = new Thickness(4, 6, 4, 6) });

        // ── プリセット（下） ──
        var presetHeader = new TreeViewItem
        {
            Header = LocalizationService.GetString("PE.Presets"),
            IsExpanded = false,
            FontWeight = FontWeights.Medium,
            FontSize = 11,
        };

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
        ConfirmToChatButton.IsEnabled = true;

        var modelId = preset.ModelId;
        if (string.IsNullOrEmpty(modelId))
            modelId = ClaudeModels.DefaultModel;
        ModelCombo.SelectedValue = modelId;
        if (ModelCombo.SelectedItem == null)
            ModelCombo.SelectedValue = ClaudeModels.DefaultPremiumModel;

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

    private void ConfirmToChat_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPreset == null) return;
        var text = _isPresetSelected ? _selectedPreset.SystemPrompt : PromptBox.Text.Trim();

        if (!_isPresetSelected)
        {
            Save_Click(sender, e);
        }

        var referenceContext = _referencePanelVm?.BuildReferenceContext();
        PromptPresetService.IncrementUsage(_selectedPreset.Id);

        // Copy prompt to AI concierge text field (do NOT execute)
        if (_chatVm != null)
        {
            var fullPrompt = text;
            if (!string.IsNullOrWhiteSpace(referenceContext))
            {
                fullPrompt = $"以下の参考資料を元に処理してください。\n\n【参考資料】\n{referenceContext}\n\n【指示】\n{text}";
            }

            _chatVm.AiInput = fullPrompt;
        }
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

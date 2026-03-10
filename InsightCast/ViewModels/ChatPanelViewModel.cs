using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using InsightCast.Core;
using InsightCast.Infrastructure;
using InsightCast.Models;
using InsightCast.Services;
using InsightCast.Services.Claude;
using InsightCommon.AI;
using ChatMsg = InsightCommon.AI.ChatMessageVm;
using PromptPresetSvc = InsightCast.Services.PromptPresetService;
using AiMemoryHotCache = InsightCommon.AI.AiMemoryHotCache;
using AiMemoryService = InsightCommon.AI.AiMemoryService;
using MemoryExtractor = InsightCommon.AI.MemoryExtractor;
using ArtifactManager = InsightCommon.AI.Artifact.ArtifactManager;
using ArtifactParser = InsightCommon.AI.Artifact.ArtifactParser;

namespace InsightCast.ViewModels;

/// <summary>
/// AI アシスタントパネルの ViewModel（InsightSlide 方式: プロンプト → 結果）
/// </summary>
public class ChatPanelViewModel : ViewModelBase
{
    private readonly IClaudeService _claude;
    internal IClaudeService ClaudeService => _claude;
    private readonly VideoToolExecutor _toolExecutor;
    private readonly Func<string> _getLang;
    private readonly Config _config;

    private const int MaxToolLoops = 10;

    // ── Context Providers (set by PlanningTab) ──
    /// <summary>参考資料コンテキストを取得するデリゲート（ReferencePanelViewModel.BuildReferenceContext）</summary>
    public Func<string?>? GetReferenceContext { get; set; }

    /// <summary>プロジェクトサマリーを取得するデリゲート（シーン情報等）</summary>
    public Func<string?>? GetProjectSummary { get; set; }

    // ── Output Format Control ──
    private string _pendingOutputFormat = "auto";
    /// <summary>出力フォーマット指示: auto / word / excel / pptx / html</summary>
    public string PendingOutputFormat
    {
        get => _pendingOutputFormat;
        set => SetProperty(ref _pendingOutputFormat, value);
    }

    // ── Prompt Input ──
    private string _aiInput = string.Empty;
    public string AiInput
    {
        get => _aiInput;
        set => SetProperty(ref _aiInput, value);
    }

    // ── Result ──
    private string _lastAiResult = string.Empty;
    public string LastAiResult
    {
        get => _lastAiResult;
        set
        {
            if (SetProperty(ref _lastAiResult, value))
                OnPropertyChanged(nameof(IsLastAiResultVisible));
        }
    }

    public bool IsLastAiResultVisible => !string.IsNullOrEmpty(LastAiResult);

    // ── Thumbnail Preview ──
    private string? _lastGeneratedThumbnailPath;
    public string? LastGeneratedThumbnailPath
    {
        get => _lastGeneratedThumbnailPath;
        set
        {
            if (SetProperty(ref _lastGeneratedThumbnailPath, value))
                OnPropertyChanged(nameof(HasGeneratedThumbnail));
        }
    }

    public bool HasGeneratedThumbnail => !string.IsNullOrEmpty(LastGeneratedThumbnailPath);

    // ── Loaded Prompt Source (for model switching & usage tracking) ──
    private PresetPromptVm? _loadedPreset;
    private UserPromptVm? _loadedUserPrompt;

    // ── Processing State ──
    private bool _isAiProcessing;
    public bool IsAiProcessing
    {
        get => _isAiProcessing;
        set => SetProperty(ref _isAiProcessing, value);
    }

    private string _aiProcessingText = string.Empty;
    public string AiProcessingText
    {
        get => _aiProcessingText;
        set => SetProperty(ref _aiProcessingText, value);
    }

    private string _aiProcessingModelName = string.Empty;
    public string AiProcessingModelName
    {
        get => _aiProcessingModelName;
        set => SetProperty(ref _aiProcessingModelName, value);
    }

    // ── Panel State ──
    private bool _isChatOpen;
    public bool IsChatOpen
    {
        get => _isChatOpen;
        set => SetProperty(ref _isChatOpen, value);
    }

    private bool _isPoppedOut;
    public bool IsPoppedOut
    {
        get => _isPoppedOut;
        set => SetProperty(ref _isPoppedOut, value);
    }

    // ── API Key ──
    private string _apiKeyInput = string.Empty;
    public string ApiKeyInput
    {
        get => _apiKeyInput;
        set
        {
            SetProperty(ref _apiKeyInput, value);
            OnPropertyChanged(nameof(CanSetApiKey));
        }
    }

    public bool IsApiKeySet => _claude.IsConfigured;
    public bool CanSetApiKey => !string.IsNullOrWhiteSpace(ApiKeyInput);

    // ── AI Concierge ──
    private string _panelTitle = "AI コンシェルジュ";
    public string PanelTitle
    {
        get => _panelTitle;
        private set => SetProperty(ref _panelTitle, value);
    }

    private string _welcomeMessage = string.Empty;
    public string WelcomeMessage
    {
        get => _welcomeMessage;
        private set => SetProperty(ref _welcomeMessage, value);
    }

    public bool HasWelcomeMessage => !string.IsNullOrEmpty(WelcomeMessage);

    private string? _conciergeSystemPromptExtension;

    // ── AI Memory ──
    private AiMemoryService? _memoryService;
    private AiMemoryHotCache? _memoryHotCache;

    // ── Artifact ──
    private readonly ArtifactManager _artifactManager;

    // ── Chat Messages ──
    public ObservableCollection<ChatMsg> ChatMessages { get; } = new();
    public bool IsChatEmpty => ChatMessages.Count == 0;

    private bool _isApiKeyPanelOpen;
    public bool IsApiKeyPanelOpen
    {
        get => _isApiKeyPanelOpen;
        set => SetProperty(ref _isApiKeyPanelOpen, value);
    }

    // ── OpenAI API Key ──
    private string _openAIApiKeyInput = string.Empty;
    public string OpenAIApiKeyInput
    {
        get => _openAIApiKeyInput;
        set
        {
            SetProperty(ref _openAIApiKeyInput, value);
            OnPropertyChanged(nameof(CanSetOpenAIApiKey));
        }
    }

    public bool IsOpenAIApiKeySet => !string.IsNullOrEmpty(_config.OpenAIApiKey);
    public bool CanSetOpenAIApiKey => !string.IsNullOrWhiteSpace(OpenAIApiKeyInput);

    // ── Image Mode ──
    private bool _isImageMode;
    public bool IsImageMode
    {
        get => _isImageMode;
        set
        {
            if (SetProperty(ref _isImageMode, value))
            {
                OnPropertyChanged(nameof(ActiveModelDisplay));
                BuildPresetPromptGroups();
            }
        }
    }

    /// <summary>テキストモード時は選択中の Claude モデル名、画像モード時は "DALL-E 3 (OpenAI)" を返す</summary>
    public string ActiveModelDisplay => _isImageMode
        ? "DALL-E 3 (OpenAI)"
        : CurrentModelDisplay;

    private string _selectedDalleSize = "1024x1024";
    public string SelectedDalleSize
    {
        get => _selectedDalleSize;
        set => SetProperty(ref _selectedDalleSize, value);
    }

    public string[] DalleSizeLabels { get; } = { "1024x1024", "1792x1024", "1024x1792" };

    // ── Model Selection ──
    private int _selectedModelIndex;
    public int SelectedModelIndex
    {
        get => _selectedModelIndex;
        set
        {
            if (SetProperty(ref _selectedModelIndex, value) && value >= 0)
            {
                _claude.SetModelByIndex(value);
                OnPropertyChanged(nameof(CurrentModelDisplay));
                OnPropertyChanged(nameof(ActiveModelDisplay));
            }
        }
    }

    public string[] ModelLabels { get; } = ClaudeModels.Registry
        .Select(m => ClaudeModels.GetDisplayName(m.Index))
        .ToArray();

    public string CurrentModelDisplay => ClaudeModels.GetDisplayName(_selectedModelIndex);

    // ── Preset Prompts (grouped by category) ──
    public ObservableCollection<PresetPromptGroupVm> PresetPromptGroups { get; } = new();

    private bool _isPresetPromptsVisible;
    public bool IsPresetPromptsVisible
    {
        get => _isPresetPromptsVisible;
        set => SetProperty(ref _isPresetPromptsVisible, value);
    }

    // ── User Prompts (grouped by category) ──
    public ObservableCollection<UserPromptGroupVm> UserPromptGroups { get; } = new();

    private bool _isUserPromptsVisible = true;
    public bool IsUserPromptsVisible
    {
        get => _isUserPromptsVisible;
        set => SetProperty(ref _isUserPromptsVisible, value);
    }

    public bool HasUserPrompts => UserPromptGroups.Count > 0;

    // ── Save Prompt Flow ──
    private bool _isSavePromptPanelOpen;
    public bool IsSavePromptPanelOpen
    {
        get => _isSavePromptPanelOpen;
        set => SetProperty(ref _isSavePromptPanelOpen, value);
    }

    private string _savePromptLabel = string.Empty;
    public string SavePromptLabel
    {
        get => _savePromptLabel;
        set => SetProperty(ref _savePromptLabel, value);
    }

    private string _savePromptCategory = string.Empty;
    public string SavePromptCategory
    {
        get => _savePromptCategory;
        set => SetProperty(ref _savePromptCategory, value);
    }

    private string? _savePromptText;
    public string? SavePromptText
    {
        get => _savePromptText;
        set => SetProperty(ref _savePromptText, value);
    }

    // ── Edit Prompt Flow ──
    private bool _isEditingPrompt;
    public bool IsEditingPrompt
    {
        get => _isEditingPrompt;
        set => SetProperty(ref _isEditingPrompt, value);
    }

    private UserPromptVm? _editingUserPrompt;

    private string _editLabel = string.Empty;
    public string EditLabel
    {
        get => _editLabel;
        set => SetProperty(ref _editLabel, value);
    }

    private string _editPrompt = string.Empty;
    public string EditPrompt
    {
        get => _editPrompt;
        set => SetProperty(ref _editPrompt, value);
    }

    private string _editCategory = string.Empty;
    public string EditCategory
    {
        get => _editCategory;
        set => SetProperty(ref _editCategory, value);
    }

    private string _editIcon = "\U0001F4CC"; // 📌
    public string EditIcon
    {
        get => _editIcon;
        set => SetProperty(ref _editIcon, value);
    }

    // ── Usage Tracking ──
    private int _apiCalls;
    public int ApiCalls
    {
        get => _apiCalls;
        set => SetProperty(ref _apiCalls, value);
    }

    private int _inputTokens;
    public int InputTokens
    {
        get => _inputTokens;
        set => SetProperty(ref _inputTokens, value);
    }

    private int _outputTokens;
    public int OutputTokens
    {
        get => _outputTokens;
        set => SetProperty(ref _outputTokens, value);
    }

    private decimal _estimatedCost;
    public decimal EstimatedCost
    {
        get => _estimatedCost;
        set => SetProperty(ref _estimatedCost, value);
    }

    public string CostDisplay
    {
        get => $"${EstimatedCost:F4}";
        set { } // WPF binding compatibility
    }

    private void OpenAiSettings()
    {
        if (_claude is not ClaudeService concrete) return;

        var owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                    ?? Application.Current.MainWindow;
        if (owner == null) return;

        var theme = InsightCommon.Theme.InsightTheme.Create();
        var locale = Services.LocalizationService.CurrentLanguage == "EN" ? "en" : "ja";

        concrete.AiService.ShowSettingsDialog(owner, theme, "Insight Training Studio", locale);

        // 設定変更後に UI を更新
        OnPropertyChanged(nameof(IsApiKeySet));
    }

    // ── Cancellation ──
    private CancellationTokenSource? _cts;

    // ── Commands ──
    public ICommand ExecutePromptCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand SetApiKeyCommand { get; }
    public ICommand ToggleChatCommand { get; }
    public ICommand ToggleApiKeyPanelCommand { get; }
    public ICommand TogglePresetPromptsCommand { get; }
    public ICommand ToggleUserPromptsCommand { get; }
    public ICommand LoadPresetToEditorCommand { get; }
    public ICommand LoadUserPromptToEditorCommand { get; }
    public ICommand SaveAsUserPromptCommand { get; }
    public ICommand ConfirmSavePromptCommand { get; }
    public ICommand CancelSavePromptCommand { get; }
    public ICommand PopOutCommand { get; }
    public ICommand CopyResultCommand { get; }

    // ── Thumbnail Commands ──
    public ICommand SaveThumbnailCommand { get; }

    // ── OpenAI / DALL-E Commands ──
    public ICommand SetOpenAIApiKeyCommand { get; }

    // ── Prompt Library Commands ──
    public ICommand EditPromptCommand { get; }
    public ICommand ConfirmEditCommand { get; }
    public ICommand CancelEditCommand { get; }
    public ICommand DeletePromptCommand { get; }
    public ICommand DuplicatePresetCommand { get; }

    // ── Prompt Editor Dialog ──
    public ICommand OpenPromptEditorCommand { get; }

    // ── Prompt Export / Import ──
    public ICommand ExportPromptsCommand { get; }
    public ICommand ImportPromptsCommand { get; }

    // ── AI Settings Dialog ──
    public ICommand OpenAiSettingsCommand { get; }

    public ICommand ClearChatCommand { get; }

    // ── Artifact Commands ──
    public ICommand OpenArtifactCommand => _openArtifactCommand ??=
        new RelayCommand(id => { if (id is string s && !string.IsNullOrEmpty(s)) _artifactManager.OpenInBrowser(s); });
    private ICommand? _openArtifactCommand;

    public ICommand ShowArtifactListCommand => _showArtifactListCommand ??=
        new RelayCommand(_ =>
        {
            var owner = Application.Current?.MainWindow;
            var locale = _getLang() == "EN" ? "en" : "ja";
            InsightCommon.AI.Artifact.ArtifactListDialog.Show(_artifactManager, owner, locale);
        });
    private ICommand? _showArtifactListCommand;

    public int ArtifactCount => _artifactManager?.GetCount() ?? 0;

    public ChatPanelViewModel(
        IClaudeService claude,
        VideoToolExecutor toolExecutor,
        Func<string> getLang,
        Config config,
        int initialModelIndex = 1)
    {
        _claude = claude;
        _toolExecutor = toolExecutor;
        _getLang = getLang;
        _config = config;
        _selectedModelIndex = initialModelIndex;

        // AI コンシェルジュ初期化
        var locale = getLang() == "EN" ? "en" : "ja";
        PanelTitle = AiConciergeConfig.GetPanelTitle("INMV", locale);
        WelcomeMessage = AiConciergeConfig.GetWelcomeMessage("INMV", locale);
        _conciergeSystemPromptExtension = AiConciergeConfig.GetSystemPromptExtension("INMV", locale);

        // Artifact 初期化
        _artifactManager = new ArtifactManager("INMV", "Insight Training Studio");
        _artifactManager.Cleanup();
        _artifactManager.SeedSamplesIfEmpty(locale);

        ExecutePromptCommand = new AsyncRelayCommand(
            () => ExecutePromptAsync(),
            () => !IsAiProcessing && !string.IsNullOrWhiteSpace(AiInput) && (IsApiKeySet || IsImageMode));

        CancelCommand = new RelayCommand(
            _ => CancelProcessing(),
            _ => IsAiProcessing);

        SetApiKeyCommand = new RelayCommand(
            _ => SetApiKey(),
            _ => CanSetApiKey);

        ToggleChatCommand = new RelayCommand(_ => IsChatOpen = !IsChatOpen);
        ToggleApiKeyPanelCommand = new RelayCommand(_ => IsApiKeyPanelOpen = !IsApiKeyPanelOpen);
        TogglePresetPromptsCommand = new RelayCommand(_ => IsPresetPromptsVisible = !IsPresetPromptsVisible);
        ToggleUserPromptsCommand = new RelayCommand(_ => IsUserPromptsVisible = !IsUserPromptsVisible);

        LoadPresetToEditorCommand = new RelayCommand(o =>
        {
            if (o is PresetPromptVm preset && preset.Source != null)
            {
                var lang = _getLang();
                AiInput = preset.Source.GetPrompt(lang);
                IsImageMode = preset.Source.IsImageMode;
                _loadedPreset = preset;
                _loadedUserPrompt = null;
            }
        });

        LoadUserPromptToEditorCommand = new RelayCommand(o =>
        {
            if (o is UserPromptVm userPrompt && userPrompt.Source != null)
            {
                AiInput = userPrompt.Source.SystemPrompt;
                _loadedUserPrompt = userPrompt;
                _loadedPreset = null;
            }
        });

        SaveAsUserPromptCommand = new RelayCommand(_ => BeginSaveCurrentPrompt());

        ConfirmSavePromptCommand = new RelayCommand(
            _ => ConfirmSavePrompt(),
            _ => !string.IsNullOrWhiteSpace(SavePromptLabel));

        CancelSavePromptCommand = new RelayCommand(_ => CancelSavePrompt());

        PopOutCommand = new RelayCommand(_ => { }); // Handled in code-behind

        CopyResultCommand = new RelayCommand(_ =>
        {
            if (!string.IsNullOrEmpty(LastAiResult))
                Clipboard.SetText(LastAiResult);
        });

        SaveThumbnailCommand = new RelayCommand(_ => SaveThumbnail());

        // OpenAI / DALL-E Commands
        SetOpenAIApiKeyCommand = new RelayCommand(
            _ => SetOpenAIApiKey(),
            _ => CanSetOpenAIApiKey);

        // Prompt Library Commands
        EditPromptCommand = new RelayCommand(o => BeginEditPrompt(o as UserPromptVm));

        ConfirmEditCommand = new RelayCommand(
            _ => ConfirmEdit(),
            _ => !string.IsNullOrWhiteSpace(EditLabel));

        CancelEditCommand = new RelayCommand(_ => CancelEdit());

        DeletePromptCommand = new RelayCommand(o => DeleteUserPrompt(o as UserPromptVm));

        DuplicatePresetCommand = new RelayCommand(o => DuplicatePreset(o as PresetPromptVm));

        OpenPromptEditorCommand = new RelayCommand(_ => OpenPromptEditor());

        ExportPromptsCommand = new RelayCommand(_ => ExportPrompts());
        ImportPromptsCommand = new RelayCommand(_ => ImportPrompts());

        OpenAiSettingsCommand = new RelayCommand(_ => OpenAiSettings());

        ClearChatCommand = new RelayCommand(_ =>
        {
            ChatMessages.Clear();
            AiInput = string.Empty;
            LastAiResult = string.Empty;
            LastGeneratedThumbnailPath = null;
            OnPropertyChanged(nameof(IsChatEmpty));
        });

        // Build prompt groups
        BuildPresetPromptGroups();
        RefreshUserPromptGroups();

        // Auto-open API key panel when no key is set
        if (!IsApiKeySet)
            IsApiKeyPanelOpen = true;
    }

    /// <summary>
    /// 言語変更時にプリセットカテゴリを再通知
    /// </summary>
    public void RefreshForLanguageChange()
    {
        BuildPresetPromptGroups();
        OnPropertyChanged(nameof(CurrentModelDisplay));
    }

    // ── Format persona + model display (strip "Claude" prefix) ──

    private static string FormatModelDisplay(string personaId, string lang)
    {
        var persona = AiPersona.FindById(personaId);
        if (persona == null) return string.Empty;
        var model = ClaudeModels.GetModel(persona.ModelId);
        // Strip "Claude" prefix from persona name
        var name = persona.GetName(lang).Replace("Claude", "").Trim();
        return model != null ? $"{name} ({model.Label})" : name;
    }

    // ── Build Preset Prompt Groups ──

    private void BuildPresetPromptGroups()
    {
        PresetPromptGroups.Clear();
        var lang = _getLang();
        // Filter presets by current mode (text vs image)
        var filtered = InsightCastPresetPrompts.All.Where(p => p.IsImageMode == _isImageMode).ToArray();
        var categories = filtered.Select(p => p.GetCategory(lang)).Distinct().ToArray();

        foreach (var category in categories)
        {
            var group = new PresetPromptGroupVm { CategoryName = category };
            var presets = filtered.Where(p => p.GetCategory(lang) == category).ToArray();
            foreach (var p in presets)
            {
                var promptText = p.GetPrompt(lang);
                var tooltip = promptText.Length > 100 ? promptText[..100] + "..." : promptText;
                group.Prompts.Add(new PresetPromptVm
                {
                    Id = p.Id,
                    Label = p.GetLabel(lang),
                    Icon = p.Icon,
                    Tooltip = tooltip,
                    RecommendedModelIndex = p.RecommendedModelIndex,
                    ModelDisplay = p.IsImageMode ? "DALL-E 3 (OpenAI)" : FormatModelDisplay(p.RecommendedPersonaId, lang),
                    Source = p
                });
            }
            PresetPromptGroups.Add(group);
        }
    }

    // ── Refresh User Prompt Groups ──

    public void RefreshUserPromptGroups()
    {
        UserPromptGroups.Clear();
        var lang = _getLang();
        var all = PromptPresetSvc.LoadAll();

        var grouped = all
            .GroupBy(p => string.IsNullOrWhiteSpace(p.Category) ? "General" : p.Category)
            .OrderBy(g => g.Key);

        foreach (var g in grouped)
        {
            var group = new UserPromptGroupVm { CategoryName = g.Key };
            foreach (var u in g.OrderByDescending(p => p.LastUsedAt ?? DateTime.MinValue))
            {
                var tooltip = u.SystemPrompt.Length > 100 ? u.SystemPrompt[..100] + "..." : u.SystemPrompt;
                group.Prompts.Add(new UserPromptVm
                {
                    Id = u.Id,
                    Label = u.Name,
                    Icon = u.Icon ?? "\U0001F4CC",
                    Tooltip = tooltip,
                    IsFavorite = u.IsPinned,
                    ModelDisplay = "",
                    Source = u
                });
            }
            UserPromptGroups.Add(group);
        }

        OnPropertyChanged(nameof(HasUserPrompts));
    }

    // ── Execute Prompt (tool loop) ──

    private async Task ExecutePromptAsync()
    {
        if (string.IsNullOrWhiteSpace(AiInput)) return;

        // Route to DALL-E if image mode is active
        if (IsImageMode)
        {
            await ExecuteDallePromptAsync(AiInput.Trim());
            return;
        }

        if (!IsApiKeySet) return;

        var prompt = AiInput.Trim();

        // Switch to recommended model for preset prompts
        int? prevModelIndex = null;
        if (_loadedPreset != null)
        {
            prevModelIndex = _selectedModelIndex;
            SelectedModelIndex = _loadedPreset.RecommendedModelIndex;
        }

        // Track usage for user prompts
        if (_loadedUserPrompt?.Source != null)
        {
            PromptPresetSvc.IncrementUsage(_loadedUserPrompt.Source.Id);
        }

        // チャットにユーザーメッセージを追加
        ChatMessages.Add(new ChatMsg { Role = ChatRole.User, Content = prompt });
        OnPropertyChanged(nameof(IsChatEmpty));

        IsAiProcessing = true;
        AiProcessingModelName = CurrentModelDisplay;
        AiProcessingText = Application.Current.TryFindResource("Ai.Processing") as string ?? "Processing...";
        LastAiResult = string.Empty;
        LastGeneratedThumbnailPath = null;
        var cts = new CancellationTokenSource();
        _cts = cts;

        try
        {
            await SendWithToolLoopAsync(prompt, cts.Token);
            LastGeneratedThumbnailPath = _toolExecutor.LastGeneratedThumbnailPath;

            // チャットにアシスタント応答を追加（Artifact 自動検出・保存 + ツールステップ付き）
            if (!string.IsNullOrEmpty(LastAiResult))
            {
                var artifactResult = _artifactManager.ProcessResponse(
                    LastAiResult,
                    modelId: _claude.CurrentModel,
                    prompt: prompt);

                var msg = new ChatMsg
                {
                    Role = ChatRole.Assistant,
                    Content = artifactResult.HasArtifacts ? artifactResult.DisplayText : LastAiResult,
                };

                // ツール実行ステップを添付
                if (_lastToolSteps != null)
                {
                    foreach (var step in _lastToolSteps)
                        msg.ToolSteps.Add(step);
                    _lastToolSteps = null;
                }

                foreach (var entry in artifactResult.Entries)
                {
                    msg.Artifacts.Add(new InsightCommon.AI.ArtifactLink
                    {
                        Id = entry.Id,
                        Title = entry.Title,
                        Icon = ArtifactParser.GetTypeIcon(entry.Type),
                        Type = entry.Type,
                    });
                }

                if (artifactResult.HasArtifacts)
                    LastAiResult = artifactResult.DisplayText;

                ChatMessages.Add(msg);
                OnPropertyChanged(nameof(IsChatEmpty));
                OnPropertyChanged(nameof(ArtifactCount));
            }
        }
        catch (OperationCanceledException)
        {
            LastAiResult = Application.Current.TryFindResource("Ai.Cancelled") as string ?? "Cancelled.";
            ChatMessages.Add(new ChatMsg { Role = ChatRole.System, Content = LastAiResult });
        }
        catch (Exception ex)
        {
            LastAiResult = $"Error: {ex.Message}";
            ChatMessages.Add(new ChatMsg { Role = ChatRole.System, Content = LastAiResult });
        }
        finally
        {
            // Restore model after preset execution
            if (prevModelIndex.HasValue)
                SelectedModelIndex = prevModelIndex.Value;

            // Clear loaded prompt references
            _loadedPreset = null;
            _loadedUserPrompt = null;

            IsAiProcessing = false;
            cts.Dispose();
            if (_cts == cts) _cts = null;
        }
    }

    private async Task ExecuteDallePromptAsync(string prompt)
    {
        if (!IsOpenAIApiKeySet)
        {
            LastAiResult = Application.Current.TryFindResource("Ai.DalleError") as string ?? "Image generation failed";
            return;
        }

        IsAiProcessing = true;
        AiProcessingModelName = _config.OpenAIImageModel;
        AiProcessingText = Application.Current.TryFindResource("Ai.Processing") as string ?? "Processing...";
        LastAiResult = string.Empty;
        LastGeneratedThumbnailPath = null;
        var cts = new CancellationTokenSource();
        _cts = cts;

        try
        {
            using var dalle = new DalleService(_config.OpenAIApiKey!);
            var imagePath = await dalle.GenerateImageAsync(
                prompt, _config.OpenAIImageModel, SelectedDalleSize, cts.Token);
            LastGeneratedThumbnailPath = imagePath;
            LastAiResult = Application.Current.TryFindResource("Ai.ImageGenerated") as string ?? "Image generated";
        }
        catch (OperationCanceledException)
        {
            LastAiResult = Application.Current.TryFindResource("Ai.Cancelled") as string ?? "Cancelled.";
        }
        catch (Exception ex)
        {
            var errorLabel = Application.Current.TryFindResource("Ai.DalleError") as string ?? "Image generation failed";
            LastAiResult = $"{errorLabel}: {ex.Message}";
        }
        finally
        {
            IsAiProcessing = false;
            cts.Dispose();
            if (_cts == cts) _cts = null;
        }
    }

    private async Task SendWithToolLoopAsync(string userPrompt, CancellationToken ct)
    {
        var tools = VideoToolDefinitions.GetAll();
        var lang = _getLang();
        var systemContext = BuildMemoryAwareSystemPrompt(BuildSystemContext(lang));

        // 出力フォーマットをリセット（1回のリクエストで消費）
        _pendingOutputFormat = "auto";

        var apiMessages = new List<object>
        {
            new Dictionary<string, object>
            {
                ["role"] = "user",
                ["content"] = userPrompt
            }
        };

        var resultBuilder = new StringBuilder();
        var toolSteps = new List<string>();

        for (int loop = 0; loop < MaxToolLoops; loop++)
        {
            var response = await _claude.SendMessageWithToolsAsync(
                apiMessages, tools, systemContext, ct);

            ApiCalls++;
            InputTokens += response.InputTokens;
            OutputTokens += response.OutputTokens;
            EstimatedCost += IClaudeService.CalculateCost(
                response.InputTokens, response.OutputTokens, _claude.CurrentModel);
            OnPropertyChanged(nameof(CostDisplay));

            // Extract text and tool_use blocks
            var textParts = new StringBuilder();
            var toolCalls = new List<ContentBlock>();

            foreach (var block in response.Content)
            {
                if (block.Type == "text" && !string.IsNullOrEmpty(block.Text))
                    textParts.AppendLine(block.Text);
                else if (block.Type == "tool_use")
                    toolCalls.Add(block);
            }

            // Accumulate text response
            if (textParts.Length > 0)
                resultBuilder.AppendLine(textParts.ToString().Trim());

            // Tool Use ループ: StopReason が "tool_use" の場合のみツールを実行
            // (insight-common AgenticSessionManager と同じパターン)
            if (response.StopReason != "tool_use" || toolCalls.Count == 0)
                break;

            // Execute tool calls and build tool_result messages
            var assistantContent = response.Content.Select(b =>
            {
                if (b.Type == "text")
                    return new Dictionary<string, object?> { ["type"] = "text", ["text"] = b.Text ?? "" };
                if (b.Type == "tool_use")
                    return new Dictionary<string, object?>
                    {
                        ["type"] = "tool_use",
                        ["id"] = b.Id,
                        ["name"] = b.Name,
                        ["input"] = b.Input
                    };
                return null;
            }).Where(x => x != null).Cast<object>().ToList();

            apiMessages.Add(new Dictionary<string, object>
            {
                ["role"] = "assistant",
                ["content"] = assistantContent
            });

            var toolResults = new List<object>();
            foreach (var tc in toolCalls)
            {
                if (tc.Name == null || tc.Id == null)
                    continue;

                // ツール実行ステップを記録
                toolSteps.Add($"\U0001F527 {tc.Name}");
                Application.Current?.Dispatcher.Invoke(() =>
                    AiProcessingText = $"\U0001F527 {tc.Name}...");

                var execResult = await _toolExecutor.ExecuteAsync(tc.Name, tc.Input ?? default, ct);

                if (execResult.IsError)
                    toolSteps.Add($"\u274C {tc.Name} failed");

                var toolResult = new Dictionary<string, object>
                {
                    ["type"] = "tool_result",
                    ["tool_use_id"] = tc.Id,
                    ["content"] = execResult.Content,
                };
                if (execResult.IsError)
                    toolResult["is_error"] = true;

                toolResults.Add(toolResult);
            }

            apiMessages.Add(new Dictionary<string, object>
            {
                ["role"] = "user",
                ["content"] = toolResults
            });
        }

        LastAiResult = ExtractAndMergeMemory(resultBuilder.ToString().Trim());
        _lastToolSteps = toolSteps.Count > 0 ? toolSteps : null;
    }

    /// <summary>直前のツール実行ステップ（チャットメッセージに添付用）</summary>
    private List<string>? _lastToolSteps;

    private string BuildSystemContext(string lang)
    {
        var sb = new StringBuilder();

        // ── 1. コアペルソナ ──
        if (lang == "EN")
        {
            sb.Append("You are the AI co-producer for Insight Training Studio — you help users create professional training videos that captivate audiences and deliver measurable learning outcomes. ");
            sb.Append("Your mission: turn rough ideas into polished, viewer-ready content in the shortest time possible. ");
            sb.Append("You have full control over narration, subtitles, scene structure, thumbnails, and AI image generation via the provided tools. ");
            sb.Append("Always think from the viewer's perspective: Is the message clear? Does the flow keep attention? Will the thumbnail get clicked? ");
            sb.Append("Use set_multiple_scenes for batch updates to maximize efficiency. ");
            sb.Append("You can read text data (titles, narration, subtitles, media paths) but cannot see image/video content. ");
            sb.Append("Respond in English.");
        }
        else
        {
            sb.Append("あなたは「Insight Training Studio」のAI共同プロデューサーです。");
            sb.Append("ユーザーがプロ品質の研修動画を最短で完成させるのが、あなたのミッションです。");
            sb.Append("視聴者目線で常に考えてください — メッセージは伝わるか？構成は飽きさせないか？サムネイルはクリックされるか？");
            sb.Append("ナレーション作成、多言語字幕、構成レビュー、CTR最適化サムネイル、AI画像生成を駆使して、動画の訴求力を最大化します。");
            sb.Append("提供されたツールでシーンの読み書き・追加・削除・並べ替え・画像生成が可能です。");
            sb.Append("参照できるのはテキスト情報（タイトル、ナレーション、字幕、メディアパス）のみで、画像・動画の内容は参照できません。");
            sb.Append("ナレーション・字幕の一括更新にはset_multiple_scenesを使い、効率を最大化してください。");
            sb.Append("日本語で回答してください。");
        }

        // ── 2. コンシェルジュ + プリセット拡張 ──
        var concierge = _conciergeSystemPromptExtension ?? "";
        if (_loadedPreset?.Source?.HasSystemPromptExtension == true)
        {
            var presetExt = _loadedPreset.Source.GetSystemPromptExtension(lang);
            if (!string.IsNullOrEmpty(presetExt))
                concierge = (string.IsNullOrEmpty(concierge) ? "" : concierge + "\n\n") + presetExt;
        }
        if (!string.IsNullOrEmpty(concierge))
            sb.Append("\n\n").Append(concierge);

        // ── 3. 出力フォーマット指示 ──
        var formatInstruction = GetOutputFormatInstruction(_pendingOutputFormat, lang);
        if (!string.IsNullOrEmpty(formatInstruction))
            sb.Append("\n\n").Append(formatInstruction);

        // ── 4. プロジェクトコンテキスト ──
        try
        {
            var projectSummary = GetProjectSummary?.Invoke();
            if (!string.IsNullOrWhiteSpace(projectSummary))
            {
                sb.Append("\n\n");
                sb.Append(lang == "EN"
                    ? "## Current Project Context\n"
                    : "## 現在のプロジェクト情報\n");
                sb.Append(projectSummary);
            }
        }
        catch { /* ignore summary errors */ }

        // ── 5. 参考資料コンテキスト ──
        try
        {
            var refContext = GetReferenceContext?.Invoke();
            if (!string.IsNullOrWhiteSpace(refContext))
            {
                sb.Append("\n\n");
                sb.Append(lang == "EN"
                    ? "## Reference Materials\nThe user has provided the following reference materials. Use them to improve the quality of your output.\n\n"
                    : "## 参考資料\nユーザーが以下の参考資料を提供しています。出力の品質向上に活用してください。\n\n");
                sb.Append(refContext);
            }
        }
        catch { /* ignore reference errors */ }

        return sb.ToString();
    }

    /// <summary>出力フォーマットに応じた指示テキストを返す</summary>
    private static string GetOutputFormatInstruction(string format, string lang)
    {
        if (lang == "EN")
        {
            return format switch
            {
                "word" => "[Output Format] Use the generate_report tool to create a Word document.",
                "excel" => "[Output Format] Use the generate_spreadsheet tool to create an Excel spreadsheet.",
                "pptx" => "[Output Format] Use the generate_presentation tool to create a PowerPoint presentation.",
                "html" => "[Output Format] Respond with a rich HTML Artifact for visual output.",
                _ => "" // auto: no explicit instruction
            };
        }
        return format switch
        {
            "word" => "[出力形式] generate_report ツールを使用して Word ドキュメントを生成してください。",
            "excel" => "[出力形式] generate_spreadsheet ツールを使用して Excel スプレッドシートを生成してください。",
            "pptx" => "[出力形式] generate_presentation ツールを使用して PowerPoint プレゼンテーションを生成してください。",
            "html" => "[出力形式] HTML Artifact で視覚的に回答してください。",
            _ => "" // auto: 指示なし
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // AI Memory
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// プロジェクトから AI メモリを読み込み、AiMemoryService を初期化する
    /// </summary>
    public void LoadMemoryFromProject(AiMemoryHotCache? hotCache, string planCode)
    {
        _memoryHotCache = hotCache;
        _memoryService = new AiMemoryService(planCode, _memoryHotCache);
        _memoryHotCache = _memoryService.HotCache;

        var (current, max) = _memoryService.GetCapacity();
        if (current > 0)
            System.Diagnostics.Debug.WriteLine($"AI Memory loaded: {current}/{max} entries");
    }

    /// <summary>
    /// AI メモリホットキャッシュを返す（プロジェクト保存時に呼ばれる）
    /// </summary>
    public AiMemoryHotCache? GetMemoryHotCache() => _memoryHotCache;

    /// <summary>
    /// AI 応答からメモリエントリを抽出し、ホットキャッシュにマージする。
    /// 表示用のクリーンテキストを返す。
    /// </summary>
    private string ExtractAndMergeMemory(string responseText)
    {
        if (_memoryService == null || string.IsNullOrEmpty(responseText))
            return responseText;

        var result = MemoryExtractor.Extract(responseText);
        if (result.ExtractedEntries.Count > 0)
        {
            var mergeResult = _memoryService.MergeEntries(result.ExtractedEntries);
            _memoryHotCache = _memoryService.HotCache;

            if (mergeResult.Added > 0 || mergeResult.Updated > 0)
                System.Diagnostics.Debug.WriteLine(
                    $"AI Memory: +{mergeResult.Added} new, ~{mergeResult.Updated} updated, x{mergeResult.Skipped} skipped");
        }

        return result.CleanText;
    }

    /// <summary>
    /// メモリ注入済みシステムプロンプトを構築する
    /// </summary>
    private string BuildMemoryAwareSystemPrompt(string basePrompt)
    {
        var locale = _getLang() == "EN" ? "en" : "ja";
        var prompt = MemoryExtractor.BuildSystemPrompt(basePrompt, _memoryHotCache, locale);

        // Artifact 出力指示を付加
        prompt += "\n\n" + ArtifactParser.GetSystemPromptExtension(locale);

        return prompt;
    }

    // ── Save Prompt ──

    private void BeginSaveCurrentPrompt()
    {
        if (string.IsNullOrWhiteSpace(AiInput)) return;
        SavePromptText = AiInput.Trim();
        SavePromptLabel = SavePromptText.Length > 20 ? SavePromptText[..20] + "..." : SavePromptText;
        SavePromptCategory = string.Empty;
        IsSavePromptPanelOpen = true;
    }

    private void DuplicatePreset(PresetPromptVm? preset)
    {
        if (preset?.Source == null) return;
        var lang = _getLang();
        SavePromptText = preset.Source.GetPrompt(lang);
        SavePromptLabel = preset.Source.GetLabel(lang);
        SavePromptCategory = preset.Source.GetCategory(lang);
        IsSavePromptPanelOpen = true;
    }

    private void ConfirmSavePrompt()
    {
        if (string.IsNullOrWhiteSpace(SavePromptLabel) || string.IsNullOrWhiteSpace(SavePromptText))
            return;

        var preset = new UserPromptPreset
        {
            Id = PromptPresetSvc.GenerateId(),
            Name = SavePromptLabel.Trim(),
            SystemPrompt = SavePromptText.Trim(),
            Category = SavePromptCategory?.Trim() ?? string.Empty,
        };

        PromptPresetSvc.Add(preset);
        IsSavePromptPanelOpen = false;
        SavePromptLabel = string.Empty;
        SavePromptCategory = string.Empty;
        SavePromptText = null;
        RefreshUserPromptGroups();
    }

    private void CancelSavePrompt()
    {
        IsSavePromptPanelOpen = false;
        SavePromptLabel = string.Empty;
        SavePromptCategory = string.Empty;
        SavePromptText = null;
    }

    // ── Edit Prompt ──

    private void BeginEditPrompt(UserPromptVm? item)
    {
        if (item?.Source == null) return;
        _editingUserPrompt = item;
        EditLabel = item.Source.Name;
        EditPrompt = item.Source.SystemPrompt;
        EditCategory = item.Source.Category;
        EditIcon = item.Source.Icon ?? "\U0001F4CC";
        IsEditingPrompt = true;
    }

    private void ConfirmEdit()
    {
        if (_editingUserPrompt?.Source == null || string.IsNullOrWhiteSpace(EditLabel)) return;

        var source = _editingUserPrompt.Source;
        source.Name = EditLabel.Trim();
        source.SystemPrompt = EditPrompt.Trim();
        source.Category = EditCategory?.Trim() ?? string.Empty;
        source.Icon = string.IsNullOrWhiteSpace(EditIcon) ? "\U0001F4CC" : EditIcon.Trim();
        source.ModifiedAt = DateTime.Now;

        PromptPresetSvc.Update(source.Id, source);
        IsEditingPrompt = false;
        _editingUserPrompt = null;
        RefreshUserPromptGroups();
    }

    private void CancelEdit()
    {
        IsEditingPrompt = false;
        _editingUserPrompt = null;
    }

    private void DeleteUserPrompt(UserPromptVm? item)
    {
        if (item?.Source == null) return;
        PromptPresetSvc.Remove(item.Source.Id);
        IsEditingPrompt = false;
        _editingUserPrompt = null;
        RefreshUserPromptGroups();
    }

    // ── Save Thumbnail ──

    private void SaveThumbnail()
    {
        if (string.IsNullOrEmpty(LastGeneratedThumbnailPath) || !System.IO.File.Exists(LastGeneratedThumbnailPath))
            return;

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG Image|*.png",
            DefaultExt = ".png",
            FileName = System.IO.Path.GetFileName(LastGeneratedThumbnailPath),
        };

        if (dlg.ShowDialog() == true)
        {
            System.IO.File.Copy(LastGeneratedThumbnailPath, dlg.FileName, overwrite: true);
        }
    }

    // ── Prompt Editor ──

    private void OpenPromptEditor()
    {
        // PromptEditorDialog は旧 UserPrompt 型を使用 → 変換して渡す
        var presets = PromptPresetSvc.LoadAll();
        var userPrompts = presets.Select(ToLegacyUserPrompt).ToList();
        var lang = _getLang();
        var dialog = new Views.PromptEditorDialog(userPrompts, lang);

        try { dialog.Owner = Application.Current.MainWindow; }
        catch { /* popped out */ }

        dialog.ShowDialog();

        // Persist changes — 旧 UserPrompt → UserPromptPreset に変換して保存
        if (dialog.HasChanges)
        {
            foreach (var existing in PromptPresetSvc.LoadAll())
                PromptPresetSvc.Remove(existing.Id);
            foreach (var up in userPrompts)
                PromptPresetSvc.Add(FromLegacyUserPrompt(up));

            RefreshUserPromptGroups();
        }

        // Handle execute request
        if (!string.IsNullOrEmpty(dialog.ExecutePromptText))
        {
            AiInput = dialog.ExecutePromptText;
            IsImageMode = dialog.ExecuteIsImageMode;

            if (dialog.ExecuteIsImageMode)
            {
                SelectedDalleSize = dialog.ExecuteImageSize;
            }
            else if (!string.IsNullOrEmpty(dialog.ExecuteModelId))
            {
                var modelIndex = InsightCommon.AI.ClaudeModels.GetModelIndex(dialog.ExecuteModelId);
                if (modelIndex >= 0)
                    SelectedModelIndex = modelIndex;
            }

            if (ExecutePromptCommand.CanExecute(null))
                ExecutePromptCommand.Execute(null);
        }
    }

    // ── Export / Import ──

    private void ExportPrompts()
    {
        var all = PromptPresetSvc.LoadAll();
        if (all.Count == 0)
        {
            MessageBox.Show(
                Application.Current.TryFindResource("PromptLib.Export.Empty") as string ?? "No prompts to export.",
                "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "JSON|*.json",
            DefaultExt = ".json",
            FileName = "prompts_export.json",
        };

        if (dlg.ShowDialog() == true)
        {
            PromptPresetSvc.Export(all, dlg.FileName);
            MessageBox.Show(
                string.Format(
                    Application.Current.TryFindResource("PromptLib.Export.Success") as string ?? "Exported {0} prompts.",
                    all.Count),
                "Export", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void ImportPrompts()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "JSON|*.json",
            DefaultExt = ".json",
        };

        if (dlg.ShowDialog() != true) return;

        var imported = PromptPresetSvc.Import(dlg.FileName);
        if (imported.Count == 0)
        {
            MessageBox.Show(
                Application.Current.TryFindResource("PromptLib.Import.Empty") as string ?? "No prompts found in file.",
                "Import", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // マージして保存
        var existing = PromptPresetSvc.LoadAll();
        var existingIds = new HashSet<string>(existing.Select(p => p.Id), StringComparer.Ordinal);
        var added = 0;
        foreach (var preset in imported)
        {
            if (existingIds.Contains(preset.Id))
            {
                PromptPresetSvc.Update(preset.Id, preset);
            }
            else
            {
                PromptPresetSvc.Add(preset);
                added++;
            }
        }

        RefreshUserPromptGroups();
        MessageBox.Show(
            string.Format(
                Application.Current.TryFindResource("PromptLib.Import.Success") as string ?? "Imported {0} prompts ({1} new).",
                imported.Count, added),
            "Import", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ── 旧 UserPrompt ⇔ UserPromptPreset 変換（PromptEditorDialog 互換用）──

    private static UserPrompt ToLegacyUserPrompt(UserPromptPreset p) => new()
    {
        Id = p.Id,
        Label = p.Name,
        Prompt = p.SystemPrompt,
        Category = p.Category,
        Icon = p.Icon ?? "\U0001F4CC",
        UseCount = p.UsageCount,
        LastUsedAt = p.LastUsedAt,
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.ModifiedAt,
    };

    private static UserPromptPreset FromLegacyUserPrompt(UserPrompt u) => new()
    {
        Id = u.Id,
        Name = u.Label,
        SystemPrompt = u.Prompt,
        Category = u.Category,
        Icon = u.Icon,
        UsageCount = u.UseCount,
        LastUsedAt = u.LastUsedAt,
        CreatedAt = u.CreatedAt,
        ModifiedAt = u.UpdatedAt,
    };

    // ── Cancel / API Key ──

    private void CancelProcessing()
    {
        _cts?.Cancel();
    }

    private void SetApiKey()
    {
        if (string.IsNullOrWhiteSpace(ApiKeyInput)) return;
        _claude.SetApiKey(ApiKeyInput.Trim());
        ApiKeyInput = string.Empty;
        IsApiKeyPanelOpen = false;
        OnPropertyChanged(nameof(IsApiKeySet));
    }

    private void SetOpenAIApiKey()
    {
        if (string.IsNullOrWhiteSpace(OpenAIApiKeyInput)) return;
        _config.OpenAIApiKey = OpenAIApiKeyInput.Trim();
        OpenAIApiKeyInput = string.Empty;
        OnPropertyChanged(nameof(IsOpenAIApiKeySet));
    }
}

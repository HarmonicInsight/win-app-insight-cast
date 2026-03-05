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

namespace InsightCast.ViewModels;

/// <summary>
/// AI アシスタントパネルの ViewModel（InsightSlide 方式: プロンプト → 結果）
/// </summary>
public class ChatPanelViewModel : ViewModelBase
{
    private readonly IClaudeService _claude;
    private readonly VideoToolExecutor _toolExecutor;
    private readonly Func<string> _getLang;
    private readonly Config _config;

    private const int MaxToolLoops = 10;

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
                AiInput = userPrompt.Source.Prompt;
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
        var all = PromptLibraryService.LoadAllPrompts();

        var grouped = all
            .GroupBy(p => string.IsNullOrWhiteSpace(p.Category) ? "General" : p.Category)
            .OrderBy(g => g.Key);

        foreach (var g in grouped)
        {
            var group = new UserPromptGroupVm { CategoryName = g.Key };
            foreach (var u in g.OrderByDescending(p => p.LastUsedAt ?? DateTime.MinValue))
            {
                var tooltip = u.Prompt.Length > 100 ? u.Prompt[..100] + "..." : u.Prompt;
                group.Prompts.Add(new UserPromptVm
                {
                    Id = u.Id,
                    Label = u.Label,
                    Icon = u.Icon,
                    Tooltip = tooltip,
                    IsFavorite = false,
                    ModelDisplay = FormatModelDisplay(u.RecommendedPersonaId, lang),
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
            PromptLibraryService.IncrementUseCount(_loadedUserPrompt.Source.Id);
        }

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
        }
        catch (OperationCanceledException)
        {
            LastAiResult = Application.Current.TryFindResource("Ai.Cancelled") as string ?? "Cancelled.";
        }
        catch (Exception ex)
        {
            LastAiResult = $"Error: {ex.Message}";
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
        var systemContext = BuildSystemContext(lang);

        var apiMessages = new List<object>
        {
            new Dictionary<string, object>
            {
                ["role"] = "user",
                ["content"] = userPrompt
            }
        };

        var resultBuilder = new StringBuilder();

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

                var execResult = await _toolExecutor.ExecuteAsync(tc.Name, tc.Input ?? default, ct);
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

        LastAiResult = resultBuilder.ToString().Trim();
    }

    private string BuildSystemContext(string lang)
    {
        if (lang == "EN")
        {
            return "You are an AI assistant for Insight Training Studio, an educational video creation tool. " +
                   "You help with narration, subtitles, video structure, thumbnails, scene management, and image generation. " +
                   "Use the provided tools to read/modify scene data, add/remove/reorder scenes, and generate images with DALL-E. " +
                   "You can only read text data (titles, narration, subtitles, media paths). You cannot see image or video content. " +
                   "Use set_multiple_scenes to batch-update narration/subtitles efficiently (saves API calls vs updating one by one). " +
                   "Respond in English.";
        }

        return "あなたは教育動画作成ツール「Insight Training Studio」のAIアシスタントです。" +
               "ナレーション、字幕、動画構成、サムネイル、シーン管理、画像生成を支援します。" +
               "提供されたツールでシーンデータの読み書き、シーンの追加・削除・並べ替え、DALL-Eでの画像生成が可能です。" +
               "参照できるのはテキスト情報（タイトル、ナレーション、字幕、メディアパス）のみで、画像・動画の内容は参照できません。" +
               "ナレーション・字幕の一括更新にはset_multiple_scenesを使い、APIコールを節約してください。" +
               "日本語で回答してください。";
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

        var prompt = new UserPrompt
        {
            Label = SavePromptLabel.Trim(),
            Prompt = SavePromptText.Trim(),
            Category = SavePromptCategory?.Trim() ?? string.Empty,
        };

        PromptLibraryService.SavePrompt(prompt);
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
        EditLabel = item.Source.Label;
        EditPrompt = item.Source.Prompt;
        EditCategory = item.Source.Category;
        EditIcon = item.Source.Icon;
        IsEditingPrompt = true;
    }

    private void ConfirmEdit()
    {
        if (_editingUserPrompt?.Source == null || string.IsNullOrWhiteSpace(EditLabel)) return;

        var source = _editingUserPrompt.Source;
        source.Label = EditLabel.Trim();
        source.Prompt = EditPrompt.Trim();
        source.Category = EditCategory?.Trim() ?? string.Empty;
        source.Icon = string.IsNullOrWhiteSpace(EditIcon) ? "\U0001F4CC" : EditIcon.Trim();
        source.UpdatedAt = DateTime.Now;

        PromptLibraryService.SavePrompt(source);
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
        PromptLibraryService.DeletePrompt(item.Source.Id);
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
        var userPrompts = PromptLibraryService.LoadAllPrompts();
        var lang = _getLang();
        var dialog = new Views.PromptEditorDialog(userPrompts, lang);

        // Try to set owner to current window
        try
        {
            dialog.Owner = Application.Current.MainWindow;
        }
        catch
        {
            // Ignore if owner can't be set (e.g., popped out)
        }

        dialog.ShowDialog();

        // Persist changes to user prompts
        if (dialog.HasChanges)
        {
            // Delete all existing, then re-save the modified list
            var existing = PromptLibraryService.LoadAllPrompts();
            foreach (var old in existing)
                PromptLibraryService.DeletePrompt(old.Id);
            foreach (var prompt in userPrompts)
                PromptLibraryService.SavePrompt(prompt);

            RefreshUserPromptGroups();
        }

        // Handle execute request
        if (!string.IsNullOrEmpty(dialog.ExecutePromptText))
        {
            AiInput = dialog.ExecutePromptText;
            IsImageMode = dialog.ExecuteIsImageMode;

            if (dialog.ExecuteIsImageMode)
            {
                // Image mode: set DALL-E size
                SelectedDalleSize = dialog.ExecuteImageSize;
            }
            else if (!string.IsNullOrEmpty(dialog.ExecutePersonaId))
            {
                // Text mode: set Claude model based on persona
                var persona = InsightCommon.AI.AiPersona.FindById(dialog.ExecutePersonaId);
                if (persona != null)
                {
                    var modelIndex = InsightCommon.AI.ClaudeModels.GetModelIndex(persona.ModelId);
                    if (modelIndex >= 0)
                        SelectedModelIndex = modelIndex;
                }
            }

            // Auto-execute
            if (ExecutePromptCommand.CanExecute(null))
                ExecutePromptCommand.Execute(null);
        }
    }

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

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using InsightCast.Core;
using InsightCast.Infrastructure;
using InsightCast.Models;
using InsightCast.Services;
using InsightCast.Video;
using InsightCast.VoiceVox;

namespace InsightCast.ViewModels
{
    public class SpeakerItem
    {
        public string DisplayName { get; set; } = string.Empty;
        public int StyleId { get; set; }
        public override string ToString() => DisplayName;
    }

    public class MainWindowViewModel : ViewModelBase
    {
        #region Services

        private readonly VoiceVoxClient _voiceVoxClient;
        private readonly int _defaultSpeakerId;
        private readonly FFmpegWrapper? _ffmpegWrapper;
        private readonly Config _config;
        private readonly AudioCache _audioCache;
        private readonly IAppLogger _logger;
        private IDialogService? _dialogService;

        #endregion

        #region Observable State

        private Project _project;
        private Timer? _autoSaveTimer;
        private bool _isDirty;
        private int _selectedSceneIndex = -1;
        private Scene? _currentScene;
        private bool _isLoadingScene;
        private bool _isExporting;
        private CancellationTokenSource? _exportCts;

        private string _windowTitle = LocalizationService.GetString("VM.NewProject");
        private string _statusText = string.Empty;
        private string _mediaName = LocalizationService.GetString("Common.Unselected");
        private string _narrationText = string.Empty;
        private string _subtitleText = string.Empty;
        private bool _keepOriginalAudio;
        private bool _isAutoDuration = true;
        private bool _isFixedDuration;
        private string _durationSeconds = "3.0";
        private int _selectedTransitionIndex;
        private string _transitionDuration = "0.5";
        private int _selectedSceneSpeakerIndex;
        private string _bgmStatusText = LocalizationService.GetString("BGM.NotSet");
        private bool _bgmActive;
        private string _fpsText = "30";
        private int _selectedResolutionIndex = 0; // Default: 1920x1080 (横動画)
        private int _selectedExportSpeakerIndex;
        private bool _exportProgressVisible;
        private double _exportProgressValue;

        // License state
        private LicenseInfo? _licenseInfo;
        private PlanCode _currentPlan = PlanCode.Free;
        private string _planDisplayName = License.GetPlanDisplayName(PlanCode.Free);
        private bool _canSubtitle;
        private bool _canSubtitleStyle;
        private bool _canTransition;
        private bool _canPptx;
        private string _subtitlePlaceholder = LocalizationService.GetString("Scene.Subtitle.Default");

        // Style
        private TextStyle _defaultSubtitleStyle;
        private readonly Dictionary<string, TextStyle> _sceneSubtitleStyles = new();
        private Dictionary<int, string> _speakerStyles = new();

        // Watermark
        private string _watermarkFilePath = string.Empty;
        private int _selectedWatermarkPosIndex = 3; // bottom-right

        // Overlay
        private int _selectedOverlayIndex = -1;
        private string _overlayText = string.Empty;
        private string _overlayXPercent = "50.0";
        private string _overlayYPercent = "50.0";
        private string _overlayFontSize = "64";
        private double _overlayFontSizeSlider = 64;
        private double _overlayOpacitySlider = 100;
        private int _selectedAlignmentIndex;
        private int _selectedOverlayColorIndex;
        private bool _isLoadingOverlay;

        #endregion

        #region Collections

        public ObservableCollection<SceneListItem> SceneItems { get; } = new();
        public ObservableCollection<SpeakerItem> ExportSpeakers { get; } = new();
        public ObservableCollection<SpeakerItem> SceneSpeakers { get; } = new();
        public ObservableCollection<OverlayListItem> OverlayItems { get; } = new();
        public ObservableCollection<OverlayPreviewItem> OverlayPreviewItems { get; } = new();

        #endregion

        #region Properties

        public string WindowTitle
        {
            get => _windowTitle;
            set => SetProperty(ref _windowTitle, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public int SelectedSceneIndex
        {
            get => _selectedSceneIndex;
            set
            {
                if (SetProperty(ref _selectedSceneIndex, value))
                    OnSceneSelected();
            }
        }

        public string MediaName
        {
            get => _mediaName;
            set => SetProperty(ref _mediaName, value);
        }

        public string NarrationText
        {
            get => _narrationText;
            set
            {
                if (SetProperty(ref _narrationText, value))
                    OnNarrationChanged();
            }
        }

        public bool NarrationPlaceholderVisible => string.IsNullOrEmpty(_narrationText);

        public string SubtitleText
        {
            get => _subtitleText;
            set
            {
                if (SetProperty(ref _subtitleText, value))
                    OnSubtitleChanged();
            }
        }

        public bool SubtitlePlaceholderVisible => string.IsNullOrEmpty(_subtitleText);

        public string SubtitlePlaceholder
        {
            get => _subtitlePlaceholder;
            set => SetProperty(ref _subtitlePlaceholder, value);
        }

        public bool KeepOriginalAudio
        {
            get => _keepOriginalAudio;
            set
            {
                if (SetProperty(ref _keepOriginalAudio, value) && !_isLoadingScene && _currentScene != null)
                    _currentScene.KeepOriginalAudio = value;
            }
        }

        public bool IsAutoDuration
        {
            get => _isAutoDuration;
            set
            {
                if (SetProperty(ref _isAutoDuration, value))
                    OnDurationModeChanged();
            }
        }

        public bool IsFixedDuration
        {
            get => _isFixedDuration;
            set => SetProperty(ref _isFixedDuration, value);
        }

        public string DurationSeconds
        {
            get => _durationSeconds;
            set
            {
                if (SetProperty(ref _durationSeconds, value))
                    OnDurationSecondsChanged();
            }
        }

        public int SelectedTransitionIndex
        {
            get => _selectedTransitionIndex;
            set
            {
                if (SetProperty(ref _selectedTransitionIndex, value))
                    OnTransitionChanged();
            }
        }

        public string TransitionDuration
        {
            get => _transitionDuration;
            set
            {
                if (SetProperty(ref _transitionDuration, value))
                    OnTransitionDurationChanged();
            }
        }

        public int SelectedSceneSpeakerIndex
        {
            get => _selectedSceneSpeakerIndex;
            set
            {
                if (SetProperty(ref _selectedSceneSpeakerIndex, value))
                    OnSceneSpeakerChanged();
            }
        }

        public int SelectedExportSpeakerIndex
        {
            get => _selectedExportSpeakerIndex;
            set => SetProperty(ref _selectedExportSpeakerIndex, value);
        }

        public int SelectedResolutionIndex
        {
            get => _selectedResolutionIndex;
            set => SetProperty(ref _selectedResolutionIndex, value);
        }

        public string FpsText
        {
            get => _fpsText;
            set => SetProperty(ref _fpsText, value);
        }

        public string BgmStatusText
        {
            get => _bgmStatusText;
            set => SetProperty(ref _bgmStatusText, value);
        }

        public bool BgmActive
        {
            get => _bgmActive;
            set => SetProperty(ref _bgmActive, value);
        }

        public bool IsExporting
        {
            get => _isExporting;
            private set => SetProperty(ref _isExporting, value);
        }

        public bool ExportProgressVisible
        {
            get => _exportProgressVisible;
            set => SetProperty(ref _exportProgressVisible, value);
        }

        public double ExportProgressValue
        {
            get => _exportProgressValue;
            set => SetProperty(ref _exportProgressValue, value);
        }

        // License plan display
        public string PlanDisplayName
        {
            get => _planDisplayName;
            set => SetProperty(ref _planDisplayName, value);
        }

        // License feature flags
        public bool CanSubtitle
        {
            get => _canSubtitle;
            set => SetProperty(ref _canSubtitle, value);
        }

        public bool CanSubtitleStyle
        {
            get => _canSubtitleStyle;
            set => SetProperty(ref _canSubtitleStyle, value);
        }

        public bool CanTransition
        {
            get => _canTransition;
            set => SetProperty(ref _canTransition, value);
        }

        public bool CanPptx
        {
            get => _canPptx;
            set => SetProperty(ref _canPptx, value);
        }

        // UI Mode toggle (Simple/Detail)
        private bool _isSimpleMode = true;
        public bool IsSimpleMode
        {
            get => _isSimpleMode;
            set
            {
                if (SetProperty(ref _isSimpleMode, value))
                {
                    OnPropertyChanged(nameof(IsDetailMode));
                }
            }
        }

        public bool IsDetailMode => !_isSimpleMode;

        // Overlay properties
        public int SelectedOverlayIndex
        {
            get => _selectedOverlayIndex;
            set
            {
                if (SetProperty(ref _selectedOverlayIndex, value))
                    OnOverlaySelected();
            }
        }

        public string OverlayText
        {
            get => _overlayText;
            set
            {
                if (SetProperty(ref _overlayText, value))
                    OnOverlayTextChanged();
            }
        }

        public string OverlayXPercent
        {
            get => _overlayXPercent;
            set
            {
                if (SetProperty(ref _overlayXPercent, value))
                    OnOverlayPositionChanged();
            }
        }

        public string OverlayYPercent
        {
            get => _overlayYPercent;
            set
            {
                if (SetProperty(ref _overlayYPercent, value))
                    OnOverlayPositionChanged();
            }
        }

        public string OverlayFontSize
        {
            get => _overlayFontSize;
            set
            {
                if (SetProperty(ref _overlayFontSize, value))
                    OnOverlayFontSizeChanged();
            }
        }

        public double OverlayFontSizeSlider
        {
            get => _overlayFontSizeSlider;
            set
            {
                if (SetProperty(ref _overlayFontSizeSlider, value))
                {
                    OverlayFontSize = ((int)value).ToString();
                }
            }
        }

        public double OverlayOpacitySlider
        {
            get => _overlayOpacitySlider;
            set
            {
                if (SetProperty(ref _overlayOpacitySlider, value))
                {
                    OnPropertyChanged(nameof(OverlayOpacityDisplay));
                    OnOverlayOpacityChanged();
                }
            }
        }

        public string OverlayOpacityDisplay => $"{(int)_overlayOpacitySlider}%";

        public int SelectedAlignmentIndex
        {
            get => _selectedAlignmentIndex;
            set
            {
                if (SetProperty(ref _selectedAlignmentIndex, value))
                    OnOverlayAlignmentChanged();
            }
        }

        public int SelectedOverlayColorIndex
        {
            get => _selectedOverlayColorIndex;
            set
            {
                if (SetProperty(ref _selectedOverlayColorIndex, value))
                    OnOverlayColorChanged();
            }
        }

        public bool OverlayListVisible => OverlayItems.Count > 0;
        public bool OverlayEditorVisible => _selectedOverlayIndex >= 0 && _selectedOverlayIndex < OverlayItems.Count;

        public List<string> AlignmentOptions { get; } = new() { LocalizationService.GetString("Align.Center"), LocalizationService.GetString("Align.Left"), LocalizationService.GetString("Align.Right") };

        public List<string> OverlayColorOptions { get; } = new()
        {
            LocalizationService.GetString("Color.White"), LocalizationService.GetString("Color.Black"), LocalizationService.GetString("Color.Red"), LocalizationService.GetString("Color.Blue"), LocalizationService.GetString("Color.Yellow"), LocalizationService.GetString("Color.Gold"), LocalizationService.GetString("Color.Pink"), LocalizationService.GetString("Color.LightBlue")
        };

        private static readonly int[][] OverlayColorValues =
        {
            new[] { 255, 255, 255 }, // 白
            new[] { 0, 0, 0 },       // 黒
            new[] { 255, 0, 0 },     // 赤
            new[] { 0, 0, 255 },     // 青
            new[] { 255, 255, 0 },   // 黄
            new[] { 212, 175, 55 },  // 金
            new[] { 255, 105, 180 }, // ピンク
            new[] { 0, 191, 255 },   // 水色
        };

        // Watermark properties
        public string WatermarkFilePath
        {
            get => _watermarkFilePath;
            set => SetProperty(ref _watermarkFilePath, value);
        }

        public bool HasWatermark => !string.IsNullOrEmpty(_watermarkFilePath);
        public string WatermarkFileName => string.IsNullOrEmpty(_watermarkFilePath)
            ? LocalizationService.GetString("Common.None") : Path.GetFileName(_watermarkFilePath);

        public int SelectedWatermarkPosIndex
        {
            get => _selectedWatermarkPosIndex;
            set => SetProperty(ref _selectedWatermarkPosIndex, value);
        }

        public List<string> WatermarkPositionOptions { get; } = new()
        {
            LocalizationService.GetString("WmPos.TopLeft"), LocalizationService.GetString("WmPos.TopRight"), LocalizationService.GetString("WmPos.BottomLeft"), LocalizationService.GetString("WmPos.BottomRight"), LocalizationService.GetString("Align.Center")
        };

        private static readonly string[] WatermarkPositionValues =
            { "top-left", "top-right", "bottom-left", "bottom-right", "center" };

        // Speech speed
        public List<string> SpeechSpeedOptions { get; } = new()
        {
            "0.8x", "1.0x", "1.2x", "1.5x"
        };

        private static readonly double[] SpeechSpeedValues = { 0.8, 1.0, 1.2, 1.5 };

        private int _selectedSpeechSpeedIndex = 1;
        public int SelectedSpeechSpeedIndex
        {
            get => _selectedSpeechSpeedIndex;
            set
            {
                if (SetProperty(ref _selectedSpeechSpeedIndex, value))
                    OnSpeechSpeedChanged();
            }
        }

        private void OnSpeechSpeedChanged()
        {
            if (_isLoadingScene || _currentScene == null) return;
            if (_selectedSpeechSpeedIndex >= 0 && _selectedSpeechSpeedIndex < SpeechSpeedValues.Length)
                _currentScene.SpeechSpeed = SpeechSpeedValues[_selectedSpeechSpeedIndex];
        }

        // Current scene for UI binding
        public Scene? CurrentScene => _currentScene;
        public SceneListItem? CurrentSceneItem =>
            _selectedSceneIndex >= 0 && _selectedSceneIndex < SceneItems.Count
                ? SceneItems[_selectedSceneIndex] : null;
        public Project Project => _project;

        #endregion

        #region Events (for UI-specific actions the ViewModel can't handle directly)

        /// <summary>Raised when audio preview should play. Args: (path, speed).</summary>
        public event Action<string, double>? PlayAudioRequested;

        /// <summary>Raised when audio preview should stop.</summary>
        public event Action? StopAudioRequested;

        /// <summary>Raised when thumbnail should update. Args: media path or null.</summary>
        public event Action<string?>? ThumbnailUpdateRequested;

        /// <summary>Raised when subtitle style preview should update. Args: TextStyle.</summary>
        public event Action<TextStyle>? StylePreviewUpdateRequested;

        /// <summary>Raised when user wants to open exported file. Args: file path.</summary>
        public event Action<string>? OpenFileRequested;

        /// <summary>Raised when scene preview video is ready. Args: video file path.</summary>
        public event Action<string>? PreviewVideoReady;

        /// <summary>Raised when scenes are added, removed, or modified.</summary>
        public event Action? ScenesChanged;
        public event Action? TemplateApplied;

        /// <summary>外部からシーン変更を通知する（AI ツールから使用）</summary>
        public void NotifyScenesChanged()
        {
            RefreshSceneList();
            ScenesChanged?.Invoke();
        }

        #endregion

        #region Commands

        public ICommand NewProjectCommand { get; }
        public ICommand OpenProjectCommand { get; }
        public ICommand SaveProjectCommand { get; }
        public ICommand SaveProjectAsCommand { get; }
        public ICommand ImportPptxCommand { get; }
        public ICommand AddSceneCommand { get; }
        public ICommand RemoveSceneCommand { get; }
        public ICommand MoveSceneUpCommand { get; }
        public ICommand MoveSceneDownCommand { get; }
        public ICommand SelectMediaCommand { get; }
        public ICommand ClearMediaCommand { get; }
        public ICommand OpenStyleDialogCommand { get; }
        public ICommand PreviewAudioCommand { get; }
        public ICommand PreviewSceneCommand { get; }
        public ICommand StopPreviewCommand { get; }
        public ICommand ExportVideoCommand { get; }
        public ICommand AddOverlayCommand { get; }
        public ICommand RemoveOverlayCommand { get; }
        public ICommand AddCoverTemplateCommand { get; }
        public ICommand MoveOverlayUpCommand { get; }
        public ICommand MoveOverlayDownCommand { get; }
        public ICommand SetOverlayPositionCommand { get; }
        public ICommand SelectWatermarkCommand { get; }
        public ICommand ClearWatermarkCommand { get; }
        public ICommand SaveTemplateCommand { get; }
        public ICommand LoadTemplateCommand { get; }
        public ICommand OpenOutputFolderCommand { get; }
        public ICommand BgmSettingsCommand { get; }
        public ICommand CopyNarrationToSubtitleCommand { get; }
        public ICommand ApplyTransitionToAllCommand { get; }
        public ICommand ShowTutorialCommand { get; }
        public ICommand ShowFaqCommand { get; }
        public ICommand ShowHelpCommand { get; }
        public ICommand ShowLicenseManagerCommand { get; }
        public ICommand ShowLicenseInfoCommand { get; }
        public ICommand ShowAboutCommand { get; }
        public ICommand ShowShortcutsCommand { get; }
        public ICommand ShowTermsCommand { get; }
        public ICommand ShowPrivacyCommand { get; }
        public ICommand OpenRecentFileCommand { get; }
        public ICommand CancelExportCommand { get; }
        public ICommand ExitCommand { get; }
        public ICommand ImportJsonCommand { get; }
        public ICommand BatchExportCommand { get; }
        public ICommand OpenAISettingsCommand { get; }
        public ICommand AIGenerateProjectCommand { get; }
        public ICommand LaunchSnipasteCommand { get; }

        #endregion

        #region Constructor

        public MainWindowViewModel(VoiceVoxClient voiceVoxClient, int speakerId,
                                    FFmpegWrapper? ffmpegWrapper, Config config)
        {
            _voiceVoxClient = voiceVoxClient;
            _defaultSpeakerId = speakerId;
            _ffmpegWrapper = ffmpegWrapper;
            _config = config;
            _audioCache = new AudioCache();
            _defaultSubtitleStyle = TextStyle.PRESET_STYLES[0];
            _logger = new AppLogger();

            _project = new Project();
            _project.InitializeDefaultScenes();

            // Commands
            NewProjectCommand = new RelayCommand(NewProject);
            OpenProjectCommand = new RelayCommand(OpenProject);
            SaveProjectCommand = new RelayCommand(SaveProject);
            SaveProjectAsCommand = new RelayCommand(SaveProjectAs);
            ImportPptxCommand = new AsyncRelayCommand(ImportPptxAsync);
            AddSceneCommand = new RelayCommand(AddScene);
            RemoveSceneCommand = new RelayCommand(RemoveScene);
            MoveSceneUpCommand = new RelayCommand(MoveSceneUp);
            MoveSceneDownCommand = new RelayCommand(MoveSceneDown);
            SelectMediaCommand = new RelayCommand(SelectMedia);
            ClearMediaCommand = new RelayCommand(ClearMedia);
            OpenStyleDialogCommand = new RelayCommand(OpenStyleDialog);
            PreviewAudioCommand = new AsyncRelayCommand(PreviewCurrentScene);
            PreviewSceneCommand = new AsyncRelayCommand(PreviewCurrentSceneVideo);
            StopPreviewCommand = new RelayCommand(() => StopAudioRequested?.Invoke());
            ExportVideoCommand = new AsyncRelayCommand(ExportVideo);
            AddOverlayCommand = new RelayCommand(AddOverlay);
            RemoveOverlayCommand = new RelayCommand(RemoveOverlay);
            AddCoverTemplateCommand = new RelayCommand(AddCoverTemplate);
            MoveOverlayUpCommand = new RelayCommand(MoveOverlayUp);
            MoveOverlayDownCommand = new RelayCommand(MoveOverlayDown);
            SetOverlayPositionCommand = new RelayCommand(p => SetOverlayPosition(p as string));
            SelectWatermarkCommand = new RelayCommand(SelectWatermark);
            ClearWatermarkCommand = new RelayCommand(ClearWatermark);
            SaveTemplateCommand = new RelayCommand(SaveTemplate);
            LoadTemplateCommand = new RelayCommand(LoadTemplate);
            OpenOutputFolderCommand = new RelayCommand(OpenOutputFolder);
            BgmSettingsCommand = new RelayCommand(OpenBgmSettings);
            CopyNarrationToSubtitleCommand = new RelayCommand(CopyNarrationToSubtitle);
            ApplyTransitionToAllCommand = new RelayCommand(ApplyTransitionToAll);
            ShowTutorialCommand = new RelayCommand(ShowHelp);
            ShowFaqCommand = new RelayCommand(ShowHelp);
            ShowHelpCommand = new RelayCommand(ShowHelp);
            ShowLicenseManagerCommand = new RelayCommand(ShowLicenseManager);
            ShowLicenseInfoCommand = new RelayCommand(ShowLicenseInfo);
            ShowAboutCommand = new RelayCommand(ShowAbout);
            ShowShortcutsCommand = new RelayCommand(ShowHelp);
            ShowTermsCommand = new RelayCommand(ShowTerms);
            ShowPrivacyCommand = new RelayCommand(ShowPrivacy);
            OpenRecentFileCommand = new RelayCommand(p => { if (p is string path) OpenRecentFile(path); });
            CancelExportCommand = new RelayCommand(CancelExport, () => _isExporting);
            ExitCommand = new RelayCommand(() => ExitRequested?.Invoke());
            ImportJsonCommand = new RelayCommand(ImportJson);
            BatchExportCommand = new RelayCommand(OpenBatchExport);
            OpenAISettingsCommand = new RelayCommand(OpenAISettings);
            AIGenerateProjectCommand = new RelayCommand(OpenAIGenerateProject);
            LaunchSnipasteCommand = new RelayCommand(LaunchSnipaste);

            // Auto-save every 5 minutes
            _autoSaveTimer = new Timer(_ => AutoSave(), null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));

            UpdateStatusText();
            LoadLicense();
            RefreshSceneList();

            // Force initial scene selection after _isLoadingScene is cleared.
            // RefreshSceneList sets SelectedSceneIndex=0 while _isLoadingScene=true,
            // so OnSceneSelected() is skipped. Reset and re-set to trigger it.
            if (SceneItems.Count > 0)
            {
                _selectedSceneIndex = -1;
                SelectedSceneIndex = 0;
            }
        }

        /// <summary>Raised when window should close.</summary>
        public event Action? ExitRequested;

        public IAppLogger Logger => _logger;

        public void SetDialogService(IDialogService dialogService)
        {
            _dialogService = dialogService;
        }

        public async Task InitializeAsync()
        {
            await LoadSpeakers();
            LoadSceneSpeakers();
            _logger.Log(LocalizationService.GetString("VM.Initialized"));

            // Background update check (non-blocking)
            _ = CheckForUpdateAsync();
        }

        private async Task CheckForUpdateAsync()
        {
            try
            {
                var update = await Services.UpdateChecker.CheckForUpdateAsync();
                if (update != null)
                {
                    var msg = LocalizationService.GetString("VM.Update.Available", update.Value.Tag);
                    if (_dialogService?.ShowYesNo(msg,
                        LocalizationService.GetString("VM.Update.Title")) == true)
                    {
                        try
                        {
                            System.Diagnostics.Process.Start(
                                new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = update.Value.Url,
                                    UseShellExecute = true
                                });
                        }
                        catch { /* Ignore browser launch failure */ }
                    }
                }
            }
            catch { /* Update check is best-effort */ }
        }

        #endregion

        #region Status

        private async void UpdateStatusText()
        {
            string vvStatus;
            try
            {
                var version = await _voiceVoxClient.CheckConnectionAsync();
                vvStatus = version != null ? LocalizationService.GetString("VM.VoiceVox.Connected", version) : LocalizationService.GetString("VM.VoiceVox.Guide");
            }
            catch
            {
                vvStatus = LocalizationService.GetString("VM.VoiceVox.Guide");
            }

            var ffStatus = _ffmpegWrapper?.CheckAvailable() == true
                ? LocalizationService.GetString("VM.FFmpeg.Detected")
                : LocalizationService.GetString("VM.FFmpeg.NotDetected");
            StatusText = $"{vvStatus} • {ffStatus}";
        }

        #endregion

        #region Scene List Management

        public void RefreshSceneList()
        {
            _isLoadingScene = true;
            var selectedIndex = _selectedSceneIndex;
            SceneItems.Clear();

            for (int i = 0; i < _project.Scenes.Count; i++)
            {
                SceneItems.Add(new SceneListItem(_project.Scenes[i], i));
            }

            if (selectedIndex >= 0 && selectedIndex < SceneItems.Count)
                SelectedSceneIndex = selectedIndex;
            else if (SceneItems.Count > 0)
                SelectedSceneIndex = SceneItems.Count - 1;  // 一番下を選択
            else
                SelectedSceneIndex = -1;

            _isLoadingScene = false;
        }

        private void OnSceneSelected()
        {
            if (_isLoadingScene) return;
            if (_selectedSceneIndex < 0 || _selectedSceneIndex >= SceneItems.Count) return;

            _isLoadingScene = true;
            var item = SceneItems[_selectedSceneIndex];
            _currentScene = item.Scene;
            OnPropertyChanged(nameof(CurrentScene));
            OnPropertyChanged(nameof(CurrentSceneItem));

            if (_currentScene.HasMedia)
            {
                MediaName = Path.GetFileName(_currentScene.MediaPath) ?? string.Empty;
                ThumbnailUpdateRequested?.Invoke(_currentScene.MediaPath);
            }
            else
            {
                MediaName = LocalizationService.GetString("Common.Unselected");
                ThumbnailUpdateRequested?.Invoke(null);
            }

            NarrationText = _currentScene.NarrationText ?? string.Empty;
            OnPropertyChanged(nameof(NarrationPlaceholderVisible));

            SelectSceneSpeaker(_currentScene.SpeakerId);
            KeepOriginalAudio = _currentScene.KeepOriginalAudio;

            SubtitleText = _currentScene.SubtitleText ?? string.Empty;
            OnPropertyChanged(nameof(SubtitlePlaceholderVisible));

            UpdateStylePreview();

            if (_currentScene.DurationMode == DurationMode.Fixed)
            {
                IsFixedDuration = true;
                IsAutoDuration = false;
            }
            else
            {
                IsAutoDuration = true;
                IsFixedDuration = false;
            }
            DurationSeconds = _currentScene.FixedSeconds.ToString("F1", CultureInfo.InvariantCulture);

            SelectTransition(_currentScene.TransitionType);
            TransitionDuration = _currentScene.TransitionDuration.ToString("F1", CultureInfo.InvariantCulture);

            RefreshOverlayList();
            if (_currentScene.TextOverlays.Count > 0)
                SelectedOverlayIndex = 0;
            else
                SelectedOverlayIndex = -1;

            // Speech speed
            SelectedSpeechSpeedIndex = Array.IndexOf(SpeechSpeedValues, _currentScene.SpeechSpeed);
            if (_selectedSpeechSpeedIndex < 0) SelectedSpeechSpeedIndex = 1;

            _isLoadingScene = false;
        }

        private void AddScene()
        {
            _isDirty = true;
            _project.AddScene();
            RefreshSceneList();
            SelectedSceneIndex = _project.Scenes.Count - 1;
            _logger.Log(LocalizationService.GetString("VM.Scene.Added", _project.Scenes.Count));
            ScenesChanged?.Invoke();
        }

        private void RemoveScene()
        {
            _isDirty = true;
            if (_project.Scenes.Count <= 1)
            {
                _dialogService?.ShowWarning(LocalizationService.GetString("VM.Scene.MinRequired"), LocalizationService.GetString("VM.Scene.MinRequired.Title"));
                return;
            }

            var idx = _selectedSceneIndex;
            if (idx < 0) return;

            _project.RemoveScene(idx);
            RefreshSceneList();
            _logger.Log(LocalizationService.GetString("VM.Scene.Removed", idx + 1));
            ScenesChanged?.Invoke();
        }

        private void MoveSceneUp() => MoveScene(-1);
        private void MoveSceneDown() => MoveScene(1);

        private void MoveScene(int direction)
        {
            var idx = _selectedSceneIndex;
            if (idx < 0) return;

            int newIdx = idx + direction;
            if (newIdx < 0 || newIdx >= _project.Scenes.Count) return;

            _project.MoveScene(idx, newIdx);
            RefreshSceneList();
            SelectedSceneIndex = newIdx;
            ScenesChanged?.Invoke();
        }

        #endregion

        #region Scene Editing

        private void OnNarrationChanged()
        {
            OnPropertyChanged(nameof(NarrationPlaceholderVisible));

            if (_isLoadingScene || _currentScene == null) return;
            _isDirty = true;
            _currentScene.NarrationText = _narrationText;

            var idx = _selectedSceneIndex;
            if (idx >= 0 && idx < SceneItems.Count)
            {
                SceneItems[idx].UpdateLabel(idx);
                SceneItems[idx].RefreshProgress();
            }
        }

        private void OnSubtitleChanged()
        {
            OnPropertyChanged(nameof(SubtitlePlaceholderVisible));

            if (_isLoadingScene || _currentScene == null) return;
            _isDirty = true;
            _currentScene.SubtitleText = _subtitleText;

            var idx = _selectedSceneIndex;
            if (idx >= 0 && idx < SceneItems.Count)
                SceneItems[idx].RefreshProgress();
        }

        private void SelectMedia()
        {
            if (_currentScene == null || _dialogService == null) return;

            var path = _dialogService.ShowOpenFileDialog(
                LocalizationService.GetString("VM.Media.Select"),
                LocalizationService.GetString("VM.Media.Filter"));

            if (path == null) return;

            var ext = Path.GetExtension(path).ToLowerInvariant();
            var imageExts = new HashSet<string> { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };
            var videoExts = new HashSet<string> { ".mp4", ".avi", ".mov", ".wmv", ".mkv" };

            _currentScene.MediaPath = path;
            _currentScene.MediaType = imageExts.Contains(ext) ? MediaType.Image
                                    : videoExts.Contains(ext) ? MediaType.Video
                                    : MediaType.None;

            MediaName = Path.GetFileName(path);
            _isDirty = true;

            if (_currentScene.MediaType == MediaType.Image)
                ThumbnailUpdateRequested?.Invoke(_currentScene.MediaPath);
            else
                ThumbnailUpdateRequested?.Invoke(null);

            _logger.Log(LocalizationService.GetString("VM.Media.Set", Path.GetFileName(path)));

            var idx = _selectedSceneIndex;
            if (idx >= 0 && idx < SceneItems.Count)
                SceneItems[idx].RefreshProgress();
        }

        private void ClearMedia()
        {
            if (_currentScene == null) return;
            _currentScene.MediaPath = null;
            _currentScene.MediaType = MediaType.None;
            MediaName = LocalizationService.GetString("Common.Unselected");
            _isDirty = true;
            ThumbnailUpdateRequested?.Invoke(null);
            _logger.Log(LocalizationService.GetString("VM.Media.Cleared"));

            var idx = _selectedSceneIndex;
            if (idx >= 0 && idx < SceneItems.Count)
                SceneItems[idx].RefreshProgress();
        }

        private void OnDurationModeChanged()
        {
            if (_isLoadingScene || _currentScene == null) return;
            _currentScene.DurationMode = _isAutoDuration ? DurationMode.Auto : DurationMode.Fixed;
            _isDirty = true;
        }

        private void OnDurationSecondsChanged()
        {
            if (_isLoadingScene || _currentScene == null) return;
            if (double.TryParse(_durationSeconds, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var seconds))
            {
                _currentScene.FixedSeconds = Math.Clamp(seconds, 0.1, 60.0);
                _isDirty = true;
            }
        }

        private void OnTransitionChanged()
        {
            if (_isLoadingScene || _currentScene == null) return;
            if (_selectedTransitionIndex >= 0)
            {
                var types = TransitionNames.DisplayNames.Keys.ToArray();
                if (_selectedTransitionIndex < types.Length)
                    _currentScene.TransitionType = types[_selectedTransitionIndex];
                _isDirty = true;
            }
        }

        private void OnTransitionDurationChanged()
        {
            if (_isLoadingScene || _currentScene == null) return;
            if (double.TryParse(_transitionDuration, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var dur))
            {
                _currentScene.TransitionDuration = Math.Clamp(dur, 0.2, 2.0);
                _isDirty = true;
            }
        }

        private void OnSceneSpeakerChanged()
        {
            if (_isLoadingScene || _currentScene == null) return;
            if (_selectedSceneSpeakerIndex >= 0 && _selectedSceneSpeakerIndex < SceneSpeakers.Count)
            {
                var si = SceneSpeakers[_selectedSceneSpeakerIndex];
                _currentScene.SpeakerId = si.StyleId == -1 ? null : si.StyleId;
                _isDirty = true;
            }
        }

        #endregion

        #region Speakers

        private async Task LoadSpeakers()
        {
            try
            {
                var speakers = await _voiceVoxClient.GetSpeakersAsync();
                _speakerStyles.Clear();
                ExportSpeakers.Clear();

                foreach (var speaker in speakers)
                {
                    if (!speaker.TryGetProperty("name", out var nameProp)) continue;
                    var rawSpeakerName = nameProp.GetString() ?? "Unknown";
                    var speakerName = VoiceVox.VoiceVoxClient.GetLocalizedSpeakerName(rawSpeakerName);
                    if (!speaker.TryGetProperty("styles", out var styles)) continue;

                    foreach (var style in styles.EnumerateArray())
                    {
                        if (!style.TryGetProperty("id", out var idProp)) continue;
                        var styleId = idProp.GetInt32();
                        var rawStyleName = style.TryGetProperty("name", out var snProp)
                            ? snProp.GetString() ?? "ノーマル" : "ノーマル";
                        var styleName = VoiceVox.VoiceVoxClient.GetLocalizedStyleName(rawStyleName);
                        var displayName = $"{speakerName} ({styleName})";
                        _speakerStyles[styleId] = displayName;
                        ExportSpeakers.Add(new SpeakerItem { DisplayName = displayName, StyleId = styleId });
                    }
                }

                for (int i = 0; i < ExportSpeakers.Count; i++)
                {
                    if (ExportSpeakers[i].StyleId == _defaultSpeakerId)
                    {
                        SelectedExportSpeakerIndex = i;
                        break;
                    }
                }

                if (_selectedExportSpeakerIndex < 0 && ExportSpeakers.Count > 0)
                    SelectedExportSpeakerIndex = 0;

                _logger.Log(LocalizationService.GetString("VM.Speaker.Loaded", ExportSpeakers.Count));
            }
            catch (Exception ex)
            {
                _logger.LogError(LocalizationService.GetString("VM.Speaker.LoadFailed"), ex);
            }
        }

        private void LoadSceneSpeakers()
        {
            SceneSpeakers.Clear();
            SceneSpeakers.Add(new SpeakerItem
            {
                DisplayName = LocalizationService.GetString("VM.Speaker.Default"),
                StyleId = -1
            });

            foreach (var kvp in _speakerStyles)
                SceneSpeakers.Add(new SpeakerItem { DisplayName = kvp.Value, StyleId = kvp.Key });

            SelectedSceneSpeakerIndex = 0;
        }

        private void SelectSceneSpeaker(int? speakerId)
        {
            if (speakerId == null) { SelectedSceneSpeakerIndex = 0; return; }
            for (int i = 0; i < SceneSpeakers.Count; i++)
            {
                if (SceneSpeakers[i].StyleId == speakerId.Value)
                {
                    SelectedSceneSpeakerIndex = i;
                    return;
                }
            }
            SelectedSceneSpeakerIndex = 0;
        }

        #endregion

        #region Transitions

        public List<string> TransitionDisplayNames { get; } =
            TransitionNames.DisplayNames.Values.ToList();

        private void SelectTransition(TransitionType type)
        {
            var types = TransitionNames.DisplayNames.Keys.ToList();
            var idx = types.IndexOf(type);
            SelectedTransitionIndex = idx >= 0 ? idx : 0;
        }

        #endregion

        #region Style

        private void OpenStyleDialog()
        {
            if (_currentScene == null || _dialogService == null) return;

            var currentStyle = GetStyleForScene(_currentScene);
            var selectedStyle = _dialogService.ShowTextStyleDialog(currentStyle);

            if (selectedStyle != null)
            {
                _currentScene.SubtitleStyleId = selectedStyle.Id;
                _sceneSubtitleStyles[_currentScene.Id] = selectedStyle;
                UpdateStylePreview();
                _logger.Log(LocalizationService.GetString("VM.Style.Changed", selectedStyle.Name));
            }
        }

        public TextStyle GetStyleForScene(Scene scene)
        {
            if (scene.SubtitleStyleId != null &&
                _sceneSubtitleStyles.TryGetValue(scene.Id, out var style))
                return style;

            if (scene.SubtitleStyleId != null)
            {
                var preset = TextStyle.PRESET_STYLES.FirstOrDefault(s => s.Id == scene.SubtitleStyleId);
                if (preset != null) return preset;
            }
            return _defaultSubtitleStyle;
        }

        private void UpdateStylePreview()
        {
            if (_currentScene == null) return;
            var style = GetStyleForScene(_currentScene);
            StylePreviewUpdateRequested?.Invoke(style);
        }

        #endregion

        #region Text Overlays

        private void RefreshOverlayList()
        {
            OverlayItems.Clear();
            if (_currentScene == null) return;

            for (int i = 0; i < _currentScene.TextOverlays.Count; i++)
            {
                OverlayItems.Add(new OverlayListItem(_currentScene.TextOverlays[i], i));
            }

            OnPropertyChanged(nameof(OverlayListVisible));
            OnPropertyChanged(nameof(OverlayEditorVisible));
            RefreshOverlayPreview();
        }

        private void RefreshOverlayPreview()
        {
            OverlayPreviewItems.Clear();
            if (_currentScene == null) return;

            foreach (var overlay in _currentScene.TextOverlays)
            {
                if (overlay.HasText)
                    OverlayPreviewItems.Add(new OverlayPreviewItem(overlay));
            }
        }

        private void AddOverlay()
        {
            if (_currentScene == null) return;

            var overlay = new TextOverlay { Text = LocalizationService.GetString("Overlay.DefaultText") };
            _currentScene.TextOverlays.Add(overlay);
            RefreshOverlayList();
            SelectedOverlayIndex = _currentScene.TextOverlays.Count - 1;
            _logger.Log(LocalizationService.GetString("VM.Overlay.Added"));
        }

        private void RemoveOverlay()
        {
            if (_currentScene == null || _selectedOverlayIndex < 0 ||
                _selectedOverlayIndex >= _currentScene.TextOverlays.Count)
                return;

            _currentScene.TextOverlays.RemoveAt(_selectedOverlayIndex);
            RefreshOverlayList();

            if (_currentScene.TextOverlays.Count > 0)
                SelectedOverlayIndex = Math.Min(_selectedOverlayIndex, _currentScene.TextOverlays.Count - 1);
            else
                SelectedOverlayIndex = -1;

            _logger.Log(LocalizationService.GetString("VM.Overlay.Removed"));
        }

        private void AddCoverTemplate()
        {
            if (_currentScene == null) return;

            // Confirm if overlays already exist
            if (_currentScene.TextOverlays.Count > 0)
            {
                var result = _dialogService?.ShowConfirmation(
                    LocalizationService.GetString("VM.CoverTemplate.ConfirmOverwrite"),
                    LocalizationService.GetString("VM.CoverTemplate.ConfirmTitle"));
                if (result != true) return;
            }

            _currentScene.TextOverlays.Clear();
            _currentScene.TextOverlays.Add(TextOverlay.CreateTitle());
            _currentScene.TextOverlays.Add(TextOverlay.CreateSubheading());

            RefreshOverlayList();
            SelectedOverlayIndex = 0;
            _logger.Log(LocalizationService.GetString("VM.Template.CoverApplied"));
        }

        private void MoveOverlayUp()
        {
            if (_currentScene == null || _selectedOverlayIndex <= 0) return;

            var idx = _selectedOverlayIndex;
            var overlays = _currentScene.TextOverlays;
            (overlays[idx - 1], overlays[idx]) = (overlays[idx], overlays[idx - 1]);
            RefreshOverlayList();
            SelectedOverlayIndex = idx - 1;
        }

        private void MoveOverlayDown()
        {
            if (_currentScene == null || _selectedOverlayIndex < 0 ||
                _selectedOverlayIndex >= _currentScene.TextOverlays.Count - 1)
                return;

            var idx = _selectedOverlayIndex;
            var overlays = _currentScene.TextOverlays;
            (overlays[idx], overlays[idx + 1]) = (overlays[idx + 1], overlays[idx]);
            RefreshOverlayList();
            SelectedOverlayIndex = idx + 1;
        }

        private void SetOverlayPosition(string? position)
        {
            if (_isLoadingOverlay || position == null) return;
            var overlay = GetSelectedOverlay();
            if (overlay == null) return;

            var (x, y) = position switch
            {
                "top-left"      => (15.0, 15.0),
                "top-center"    => (50.0, 15.0),
                "top-right"     => (85.0, 15.0),
                "middle-left"   => (15.0, 50.0),
                "center"        => (50.0, 50.0),
                "middle-right"  => (85.0, 50.0),
                "bottom-left"   => (15.0, 85.0),
                "bottom-center" => (50.0, 85.0),
                "bottom-right"  => (85.0, 85.0),
                _ => (50.0, 50.0)
            };

            _isLoadingOverlay = true;
            overlay.XPercent = x;
            overlay.YPercent = y;
            OverlayXPercent = x.ToString("F1", CultureInfo.InvariantCulture);
            OverlayYPercent = y.ToString("F1", CultureInfo.InvariantCulture);
            _isLoadingOverlay = false;

            if (_selectedOverlayIndex >= 0 && _selectedOverlayIndex < OverlayItems.Count)
                OverlayItems[_selectedOverlayIndex].UpdateLabel(_selectedOverlayIndex);
            RefreshOverlayPreview();
        }

        private void OnOverlaySelected()
        {
            OnPropertyChanged(nameof(OverlayEditorVisible));

            if (_selectedOverlayIndex < 0 || _currentScene == null ||
                _selectedOverlayIndex >= _currentScene.TextOverlays.Count)
                return;

            _isLoadingOverlay = true;
            var overlay = _currentScene.TextOverlays[_selectedOverlayIndex];

            OverlayText = overlay.Text;
            OverlayXPercent = overlay.XPercent.ToString("F1", CultureInfo.InvariantCulture);
            OverlayYPercent = overlay.YPercent.ToString("F1", CultureInfo.InvariantCulture);
            OverlayFontSize = overlay.FontSize.ToString();
            _overlayFontSizeSlider = overlay.FontSize;
            OnPropertyChanged(nameof(OverlayFontSizeSlider));
            _overlayOpacitySlider = overlay.Opacity * 100.0;
            OnPropertyChanged(nameof(OverlayOpacitySlider));
            OnPropertyChanged(nameof(OverlayOpacityDisplay));

            SelectedAlignmentIndex = overlay.Alignment switch
            {
                Models.TextAlignment.Left => 1,
                Models.TextAlignment.Right => 2,
                _ => 0
            };

            // Find matching color
            SelectedOverlayColorIndex = FindColorIndex(overlay.TextColor);

            _isLoadingOverlay = false;
        }

        private int FindColorIndex(int[] color)
        {
            for (int i = 0; i < OverlayColorValues.Length; i++)
            {
                if (OverlayColorValues[i][0] == color[0] &&
                    OverlayColorValues[i][1] == color[1] &&
                    OverlayColorValues[i][2] == color[2])
                    return i;
            }
            return 0; // default to white
        }

        private TextOverlay? GetSelectedOverlay()
        {
            if (_currentScene == null || _selectedOverlayIndex < 0 ||
                _selectedOverlayIndex >= _currentScene.TextOverlays.Count)
                return null;
            return _currentScene.TextOverlays[_selectedOverlayIndex];
        }

        private void OnOverlayTextChanged()
        {
            if (_isLoadingOverlay) return;
            var overlay = GetSelectedOverlay();
            if (overlay == null) return;
            overlay.Text = _overlayText;

            if (_selectedOverlayIndex >= 0 && _selectedOverlayIndex < OverlayItems.Count)
                OverlayItems[_selectedOverlayIndex].UpdateLabel(_selectedOverlayIndex);
            RefreshOverlayPreview();
        }

        private void OnOverlayPositionChanged()
        {
            if (_isLoadingOverlay) return;
            var overlay = GetSelectedOverlay();
            if (overlay == null) return;

            if (double.TryParse(_overlayXPercent, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var x))
                overlay.XPercent = Math.Clamp(x, 0, 100);

            if (double.TryParse(_overlayYPercent, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var y))
                overlay.YPercent = Math.Clamp(y, 0, 100);

            RefreshOverlayPreview();
        }

        private void OnOverlayFontSizeChanged()
        {
            if (_isLoadingOverlay) return;
            var overlay = GetSelectedOverlay();
            if (overlay == null) return;

            if (int.TryParse(_overlayFontSize, out var size))
                overlay.FontSize = Math.Clamp(size, 8, 200);
        }

        private void OnOverlayAlignmentChanged()
        {
            if (_isLoadingOverlay) return;
            var overlay = GetSelectedOverlay();
            if (overlay == null) return;

            overlay.Alignment = _selectedAlignmentIndex switch
            {
                1 => Models.TextAlignment.Left,
                2 => Models.TextAlignment.Right,
                _ => Models.TextAlignment.Center
            };
        }

        private void OnOverlayColorChanged()
        {
            if (_isLoadingOverlay) return;
            var overlay = GetSelectedOverlay();
            if (overlay == null) return;

            if (_selectedOverlayColorIndex >= 0 && _selectedOverlayColorIndex < OverlayColorValues.Length)
                overlay.TextColor = (int[])OverlayColorValues[_selectedOverlayColorIndex].Clone();

            if (_selectedOverlayIndex >= 0 && _selectedOverlayIndex < OverlayItems.Count)
                OverlayItems[_selectedOverlayIndex].UpdateLabel(_selectedOverlayIndex);
            RefreshOverlayPreview();
        }

        private void OnOverlayOpacityChanged()
        {
            if (_isLoadingOverlay) return;
            var overlay = GetSelectedOverlay();
            if (overlay == null) return;

            overlay.Opacity = _overlayOpacitySlider / 100.0;
            RefreshOverlayPreview();
        }

        #endregion

        #region Audio Preview

        private async Task PreviewCurrentScene()
        {
            if (_currentScene == null || string.IsNullOrWhiteSpace(_currentScene.NarrationText))
            {
                _dialogService?.ShowInfo(LocalizationService.GetString("VM.Preview.NoNarration"), LocalizationService.GetString("VM.Preview.Title"));
                return;
            }

            var speakerId = _currentScene.SpeakerId ?? _defaultSpeakerId;
            var text = _currentScene.NarrationText!;

            try
            {
                _logger.Log(LocalizationService.GetString("VM.Preview.Generating"));

                string audioPath;
                if (_audioCache.Exists(text, speakerId))
                {
                    audioPath = _audioCache.GetCachePath(text, speakerId);
                    _logger.Log(LocalizationService.GetString("VM.Preview.CacheLoaded"));
                }
                else
                {
                    var audioData = await _voiceVoxClient.GenerateAudioAsync(text, speakerId);
                    audioPath = _audioCache.Save(text, speakerId, audioData);
                    _logger.Log(LocalizationService.GetString("VM.Preview.Generated"));
                }

                _currentScene.AudioCachePath = audioPath;
                PlayAudioRequested?.Invoke(audioPath, _currentScene.SpeechSpeed);
                _logger.Log(LocalizationService.GetString("VM.Preview.Playing"));
            }
            catch (Exception ex)
            {
                _logger.LogError(LocalizationService.GetString("VM.Preview.Error"), ex);
                _dialogService?.ShowError(LocalizationService.GetString("VM.Preview.Failed", ex.Message), LocalizationService.GetString("Common.Error"));
            }
        }

        #endregion

        #region Scene Preview

        private async Task PreviewCurrentSceneVideo()
        {
            if (_currentScene == null)
            {
                _dialogService?.ShowInfo(LocalizationService.GetString("VM.ScenePreview.NoScene"), LocalizationService.GetString("VM.ScenePreview.Title"));
                return;
            }

            if (_ffmpegWrapper == null || !_ffmpegWrapper.CheckAvailable())
            {
                _dialogService?.ShowError(
                    LocalizationService.GetString("VM.ScenePreview.NoFFmpeg"),
                    LocalizationService.GetString("VM.ScenePreview.Error"));
                return;
            }

            var previewDir = Path.Combine(Path.GetTempPath(), "insightcast_cache", "preview");
            Directory.CreateDirectory(previewDir);

            // Clean up old preview files to prevent disk bloat
            try
            {
                foreach (var old in Directory.GetFiles(previewDir, "preview_*.mp4"))
                    File.Delete(old);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Preview cleanup: {ex.Message}"); }

            var previewPath = Path.Combine(previewDir, $"preview_{Guid.NewGuid():N}.mp4");

            string resolution = _selectedResolutionIndex == 0 ? "1920x1080" : "1080x1920";
            int exportSpeakerId = _defaultSpeakerId;
            if (_selectedExportSpeakerIndex >= 0 && _selectedExportSpeakerIndex < ExportSpeakers.Count)
                exportSpeakerId = ExportSpeakers[_selectedExportSpeakerIndex].StyleId;

            var style = GetStyleForScene(_currentScene);
            var sceneSnapshot = _project.Clone().Scenes
                .ElementAtOrDefault(_selectedSceneIndex) ?? _currentScene;

            var progress = new Progress<string>(msg => _logger.Log(msg));

            _logger.Log(LocalizationService.GetString("VM.ScenePreview.Generating"));
            ExportProgressVisible = true;

            try
            {
                var exportService = new ExportService(_ffmpegWrapper, _voiceVoxClient, _audioCache);
                var success = await Task.Run(() =>
                    exportService.GeneratePreview(sceneSnapshot, previewPath, resolution, 30,
                        exportSpeakerId, style, progress, CancellationToken.None));

                if (success && File.Exists(previewPath))
                {
                    _logger.Log(LocalizationService.GetString("VM.ScenePreview.Complete"));
                    PreviewVideoReady?.Invoke(previewPath);
                }
                else
                {
                    _dialogService?.ShowError(LocalizationService.GetString("VM.ScenePreview.Failed"), LocalizationService.GetString("VM.ScenePreview.Error"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(LocalizationService.GetString("VM.ScenePreview.Error"), ex);
                _dialogService?.ShowError(LocalizationService.GetString("VM.ScenePreview.ErrorDetail", ex.Message), LocalizationService.GetString("Common.Error"));
            }
            finally
            {
                ExportProgressVisible = false;
            }
        }

        #endregion

        #region Watermark/Template

        private void SelectWatermark()
        {
            if (_dialogService == null) return;
            var path = _dialogService.ShowOpenFileDialog(
                LocalizationService.GetString("VM.Watermark.Select"),
                LocalizationService.GetString("VM.Watermark.Filter"));
            if (!string.IsNullOrEmpty(path))
            {
                WatermarkFilePath = path;
                SyncWatermarkToProject();
                OnPropertyChanged(nameof(HasWatermark));
                OnPropertyChanged(nameof(WatermarkFileName));
                _logger.Log(LocalizationService.GetString("VM.Watermark.Set", Path.GetFileName(path)));
            }
        }

        private void ClearWatermark()
        {
            WatermarkFilePath = string.Empty;
            _project.Watermark = new WatermarkSettings();
            OnPropertyChanged(nameof(HasWatermark));
            OnPropertyChanged(nameof(WatermarkFileName));
        }

        private void SyncWatermarkToProject()
        {
            _project.Watermark.Enabled = true;
            _project.Watermark.ImagePath = _watermarkFilePath;
            _project.Watermark.Position = (_selectedWatermarkPosIndex >= 0 &&
                _selectedWatermarkPosIndex < WatermarkPositionValues.Length)
                ? WatermarkPositionValues[_selectedWatermarkPosIndex]
                : "bottom-right";
        }

        private void OpenOutputFolder()
        {
            if (_dialogService == null) return;
            var folder = _project.Output.OutputPath;
            if (!string.IsNullOrEmpty(folder))
            {
                var dir = Path.GetDirectoryName(folder);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    OpenFileRequested?.Invoke(dir);
            }
        }

        private void SaveTemplate()
        {
            var name = LocalizationService.GetString("VM.Template.DefaultName", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            if (HasWatermark) SyncWatermarkToProject();

            var template = TemplateService.CreateFromProject(_project, name);
            TemplateService.SaveTemplate(template);
            _logger.Log(LocalizationService.GetString("VM.Template.Saved", name));
        }

        private void LoadTemplate()
        {
            while (true)
            {
                var templates = TemplateService.LoadAllTemplates();
                if (templates.Count == 0)
                {
                    _dialogService?.ShowInfo(LocalizationService.GetString("VM.Template.None"), LocalizationService.GetString("VM.Template.Title"));
                    return;
                }

                var (action, selectedIndex, newName) = _dialogService?.ShowTemplateDialog(
                    LocalizationService.GetString("VM.Template.Select"), templates) ?? (0, -1, null);

                if (action == 0 || selectedIndex < 0) return; // Cancel

                var template = templates[selectedIndex];

                if (action == 2) // Delete
                {
                    if (_dialogService?.ShowConfirmation(
                        LocalizationService.GetString("Template.DeleteConfirm", template.Name),
                        LocalizationService.GetString("VM.Template.Title")) == true)
                    {
                        TemplateService.DeleteTemplate(template.Name);
                        _logger.Log(LocalizationService.GetString("Template.Deleted", template.Name));
                    }
                    continue; // Show dialog again
                }

                if (action == 3 && !string.IsNullOrWhiteSpace(newName)) // Rename
                {
                    var oldName = template.Name;
                    TemplateService.DeleteTemplate(oldName);
                    template.Name = newName;
                    TemplateService.SaveTemplate(template);
                    _logger.Log(LocalizationService.GetString("Template.Renamed", newName));
                    continue; // Show dialog again
                }

                if (action == 1) // Apply
                {
                    TemplateService.ApplyToProject(template, _project);

                    // Sync project settings back to UI
                    if (_project.Watermark.HasWatermark)
                    {
                        WatermarkFilePath = _project.Watermark.ImagePath!;
                        OnPropertyChanged(nameof(HasWatermark));
                        OnPropertyChanged(nameof(WatermarkFileName));
                    }

                    UpdateBgmStatus();
                    TemplateApplied?.Invoke(); // Notify UI to reload thumbnail settings
                    _logger.Log(LocalizationService.GetString("VM.Template.Applied", template.Name));
                    return;
                }
            }
        }

        #endregion

        #region Export

        private async Task ExportVideo()
        {
            if (_isExporting)
            {
                _dialogService?.ShowWarning(LocalizationService.GetString("VM.Export.InProgress"), LocalizationService.GetString("VM.Export.InProgress.Title"));
                return;
            }

            if (_dialogService == null) return;

            var outputPath = _dialogService.ShowSaveFileDialog(
                LocalizationService.GetString("VM.Export.SaveTitle"),
                LocalizationService.GetString("VM.Export.SaveFilter"),
                ".mp4",
                "InsightCast.mp4");

            if (outputPath == null) return;

            if (_ffmpegWrapper == null || !_ffmpegWrapper.CheckAvailable())
            {
                _dialogService.ShowError(
                    LocalizationService.GetString("VM.Export.NoFFmpeg"),
                    LocalizationService.GetString("VM.Export.Error"));
                return;
            }

            int fps = 30;
            if (int.TryParse(_fpsText, out var parsedFps))
                fps = Math.Clamp(parsedFps, 15, 60);

            string resolution = _selectedResolutionIndex == 0 ? "1920x1080" : "1080x1920";

            int exportSpeakerId = _defaultSpeakerId;
            if (_selectedExportSpeakerIndex >= 0 && _selectedExportSpeakerIndex < ExportSpeakers.Count)
                exportSpeakerId = ExportSpeakers[_selectedExportSpeakerIndex].StyleId;

            IsExporting = true;
            _exportCts = new CancellationTokenSource();
            _exportCts.CancelAfter(TimeSpan.FromHours(4)); // Safety timeout for hung FFmpeg
            ExportProgressValue = 0;
            ExportProgressVisible = true;

            var progress = new Progress<string>(msg =>
            {
                _logger.Log(msg);
                // Parse "[N/M]" progress format to update progress bar
                if (msg.StartsWith("[") && msg.Contains('/'))
                {
                    var bracket = msg.IndexOf(']');
                    if (bracket > 0)
                    {
                        var parts = msg[1..bracket].Split('/');
                        if (parts.Length == 2
                            && int.TryParse(parts[0], out int current)
                            && int.TryParse(parts[1], out int total)
                            && total > 0)
                        {
                            ExportProgressValue = (double)current / total * 100;
                        }
                    }
                }
            });
            var ct = _exportCts.Token;

            _logger.Log(LocalizationService.GetString("VM.Export.Started", outputPath));

            // Sync UI settings to project before snapshot
            if (HasWatermark) SyncWatermarkToProject();

            // Snapshot project data to avoid race conditions with UI thread
            var projectSnapshot = _project.Clone();
            var styleSnapshot = new Dictionary<string, TextStyle>(_sceneSubtitleStyles);
            var defaultStyle = _defaultSubtitleStyle;

            TextStyle GetStyleSnapshot(Scene scene)
            {
                if (scene.SubtitleStyleId != null &&
                    styleSnapshot.TryGetValue(scene.Id, out var style))
                    return style;
                if (scene.SubtitleStyleId != null)
                {
                    var preset = TextStyle.PRESET_STYLES.FirstOrDefault(s => s.Id == scene.SubtitleStyleId);
                    if (preset != null) return preset;
                }
                return defaultStyle;
            }

            try
            {
                var ffmpeg = _ffmpegWrapper!; // null already checked above
                var exportService = new ExportService(ffmpeg, _voiceVoxClient, _audioCache);
                var exportResult = await Task.Run(() =>
                    exportService.ExportFull(projectSnapshot, outputPath, resolution, fps,
                        exportSpeakerId, GetStyleSnapshot, progress, ct), ct);

                if (exportResult.Success)
                {
                    var extras = new List<string>();
                    if (!string.IsNullOrEmpty(exportResult.ThumbnailPath))
                        extras.Add(LocalizationService.GetString("VM.Export.Thumbnail", exportResult.ThumbnailPath));
                    if (!string.IsNullOrEmpty(exportResult.ChapterFilePath))
                        extras.Add(LocalizationService.GetString("VM.Export.Chapter", exportResult.ChapterFilePath));
                    if (!string.IsNullOrEmpty(exportResult.MetadataFilePath))
                        extras.Add(LocalizationService.GetString("VM.Export.Metadata", exportResult.MetadataFilePath));
                    foreach (var extra in extras) _logger.Log(extra);
                    OnExportFinished(true, outputPath);
                }
                else
                {
                    OnExportFinished(false, exportResult.ErrorMessage ?? LocalizationService.GetString("VM.Export.UnknownError"));
                }
            }
            catch (OperationCanceledException)
            {
                OnExportFinished(false, LocalizationService.GetString("VM.Export.Cancelled"));
            }
            catch (Exception ex)
            {
                OnExportFinished(false, LocalizationService.GetString("VM.Export.ErrorDetail", ex.Message));
            }
            finally
            {
                IsExporting = false;
                ExportProgressVisible = false;
                _exportCts?.Dispose();
                _exportCts = null;
            }
        }

        private async void OnExportFinished(bool success, string message)
        {
            if (success)
            {
                _project.Output.OutputPath = message;
                _logger.Log(LocalizationService.GetString("VM.Export.Success", message));

                // D2: Enhanced post-export guidance with output file list
                // Check file existence on background thread to avoid blocking UI
                var dir = System.IO.Path.GetDirectoryName(message) ?? "";
                var baseName = System.IO.Path.GetFileNameWithoutExtension(message);

                var (thumbExists, chapterExists, metaExists) = await Task.Run(() =>
                {
                    var t = System.IO.File.Exists(System.IO.Path.Combine(dir, baseName + "_thumbnail.jpg"));
                    var c = System.IO.File.Exists(System.IO.Path.Combine(dir, baseName + ".chapters.txt"));
                    var m = System.IO.File.Exists(System.IO.Path.Combine(dir, baseName + ".metadata.txt"));
                    return (t, c, m);
                });

                var sb = new System.Text.StringBuilder();
                sb.AppendLine(LocalizationService.GetString("VM.Export.CompleteDetail", message));
                sb.AppendLine();
                sb.AppendLine(LocalizationService.GetString("VM.Export.OutputFiles"));

                if (thumbExists)
                    sb.AppendLine($"  - {LocalizationService.GetString("Export.Thumbnail.Short")}: {baseName}_thumbnail.jpg");
                if (chapterExists)
                    sb.AppendLine($"  - {LocalizationService.GetString("Export.Chapter.Short")}: {baseName}.chapters.txt");
                if (metaExists)
                    sb.AppendLine($"  - {LocalizationService.GetString("Export.Metadata.Short")}: {baseName}.metadata.txt");

                sb.AppendLine();
                sb.AppendLine(LocalizationService.GetString("VM.Export.OpenPrompt"));

                if (_dialogService?.ShowYesNo(sb.ToString(),
                    LocalizationService.GetString("VM.Export.Complete")) == true)
                {
                    OpenFileRequested?.Invoke(message);
                }
            }
            else
            {
                _logger.Log(LocalizationService.GetString("VM.Export.Failed", message));
                _dialogService?.ShowError(LocalizationService.GetString("VM.Export.FailedDetail", message), LocalizationService.GetString("VM.Export.Error"));
            }
        }

        public void CancelExport()
        {
            _exportCts?.Cancel();
        }

        #endregion

        #region Project Operations

        /// <summary>
        /// Loads a project created externally (e.g. from QuickMode).
        /// </summary>
        public void LoadProject(Project project)
        {
            _project = project;
            _sceneSubtitleStyles.Clear();
            WindowTitle = LocalizationService.GetString("VM.ImportProject");
            RefreshSceneList();
            UpdateBgmStatus();
            SyncProjectToUI();
            _logger.Log(LocalizationService.GetString("VM.Project.Loaded", project.Scenes.Count));
        }

        private void SyncProjectToUI()
        {
            if (_project.Watermark?.HasWatermark == true)
            {
                WatermarkFilePath = _project.Watermark.ImagePath!;
                OnPropertyChanged(nameof(HasWatermark));
                OnPropertyChanged(nameof(WatermarkFileName));
            }
        }

        private void NewProject()
        {
            if (_dialogService == null) return;

            if (!_dialogService.ShowConfirmation(
                LocalizationService.GetString("VM.Project.NewConfirm"),
                LocalizationService.GetString("VM.Project.NewTitle")))
                return;

            _project = new Project();
            _project.InitializeDefaultScenes();
            _sceneSubtitleStyles.Clear();
            WatermarkFilePath = string.Empty;
            OnPropertyChanged(nameof(HasWatermark));
            OnPropertyChanged(nameof(WatermarkFileName));
            WindowTitle = LocalizationService.GetString("VM.NewProject");
            RefreshSceneList();
            _logger.Log(LocalizationService.GetString("VM.Project.Created"));
        }

        private void OpenProject()
        {
            if (_dialogService == null) return;

            var path = _dialogService.ShowOpenFileDialog(
                LocalizationService.GetString("VM.Project.OpenTitle"),
                LocalizationService.GetString("VM.Project.JsonFilter"),
                ".json");

            if (path == null) return;

            try
            {
                _project = Project.Load(path);
                _config.AddRecentFile(path);
                _sceneSubtitleStyles.Clear();
                WindowTitle = $"InsightCast - {Path.GetFileNameWithoutExtension(path)}";
                RefreshSceneList();
                UpdateBgmStatus();
                SyncProjectToUI();
                _logger.Log(LocalizationService.GetString("VM.Project.Opened", path));
            }
            catch (Exception ex)
            {
                _dialogService.ShowError(LocalizationService.GetString("VM.Project.LoadFailed", ex.Message), LocalizationService.GetString("VM.Project.LoadError"));
            }
        }

        private void SaveProject()
        {
            if (string.IsNullOrEmpty(_project.ProjectPath))
            {
                SaveProjectAs();
                return;
            }

            try
            {
                _project.Save();
                _isDirty = false;
                _config.AddRecentFile(_project.ProjectPath!);
                _logger.Log(LocalizationService.GetString("VM.Project.Saved", _project.ProjectPath));
            }
            catch (Exception ex)
            {
                _dialogService?.ShowError(LocalizationService.GetString("VM.Project.SaveFailed", ex.Message), LocalizationService.GetString("VM.Project.SaveError"));
            }
        }

        public List<string> RecentFiles => _config.RecentFiles;

        private void OpenRecentFile(string path)
        {
            if (!File.Exists(path))
            {
                _dialogService?.ShowWarning(LocalizationService.GetString("VM.Project.FileNotFound", path), LocalizationService.GetString("VM.Project.RecentFiles"));
                return;
            }

            try
            {
                _project = Project.Load(path);
                _config.AddRecentFile(path);
                _sceneSubtitleStyles.Clear();
                WindowTitle = $"InsightCast - {Path.GetFileNameWithoutExtension(path)}";
                RefreshSceneList();
                UpdateBgmStatus();
                SyncProjectToUI();
                _logger.Log(LocalizationService.GetString("VM.Project.Opened", path));
            }
            catch (Exception ex)
            {
                _dialogService?.ShowError(LocalizationService.GetString("VM.Project.LoadFailed", ex.Message), LocalizationService.GetString("VM.Project.LoadError"));
            }
        }

        private void AutoSave()
        {
            try
            {
                // Serialize on UI thread to avoid concurrent access to _project
                string? json = null;
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    json = JsonSerializer.Serialize(_project);
                });

                if (json == null) return;

                var autoSaveDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "InsightCast", "AutoSave");
                Directory.CreateDirectory(autoSaveDir);
                var autoSavePath = Path.Combine(autoSaveDir, "autosave.json");
                File.WriteAllText(autoSavePath, json);
            }
            catch
            {
                // Auto-save failure should not disrupt the user
            }
        }

        private void SaveProjectAs()
        {
            if (_dialogService == null) return;

            var path = _dialogService.ShowSaveFileDialog(
                LocalizationService.GetString("VM.Project.SaveAsTitle"),
                LocalizationService.GetString("VM.Project.JsonFilter"),
                ".json",
                "InsightCast.json");

            if (path == null) return;

            try
            {
                _project.Save(path);
                _isDirty = false;
                _config.AddRecentFile(path);
                WindowTitle = $"InsightCast - {Path.GetFileNameWithoutExtension(path)}";
                _logger.Log(LocalizationService.GetString("VM.Project.Saved", path));
            }
            catch (Exception ex)
            {
                _dialogService?.ShowError(LocalizationService.GetString("VM.Project.SaveFailed", ex.Message), LocalizationService.GetString("VM.Project.SaveError"));
            }
        }

        private async Task ImportPptxAsync()
        {
            if (_dialogService == null) return;

            if (!License.CanUseFeature(_currentPlan, "pptx_import"))
            {
                _dialogService.ShowInfo(
                    LocalizationService.GetString("VM.Pptx.FeatureLimit"),
                    LocalizationService.GetString("VM.Pptx.PlanRequired"));
                return;
            }

            var path = _dialogService.ShowOpenFileDialog(
                LocalizationService.GetString("VM.Pptx.Select"),
                LocalizationService.GetString("VM.Pptx.Filter"),
                ".pptx");

            if (path == null) return;

            try
            {
                _logger.Log(LocalizationService.GetString("VM.Pptx.Started", path));

                var outputDir = Path.Combine(
                    Path.GetTempPath(),
                    "insightcast_cache",
                    "pptx_slides",
                    $"import_{Guid.NewGuid():N}");

                var importer = new Utils.PptxImporter(
                    (current, total, msg) => _logger.Log(msg));

                var slides = await Task.Run(() => importer.ImportPptx(path, outputDir));

                if (slides.Count == 0)
                {
                    _dialogService.ShowInfo(LocalizationService.GetString("VM.Pptx.NoSlides"), LocalizationService.GetString("VM.Pptx.Result"));
                    return;
                }

                foreach (var slide in slides)
                {
                    var scene = new Scene
                    {
                        NarrationText = slide.Notes,
                        SubtitleText = string.IsNullOrWhiteSpace(slide.SlideText) ? null : slide.SlideText
                    };
                    if (!string.IsNullOrEmpty(slide.ImagePath) && File.Exists(slide.ImagePath))
                    {
                        scene.MediaPath = slide.ImagePath;
                        scene.MediaType = MediaType.Image;
                    }
                    _project.Scenes.Add(scene);
                }

                RefreshSceneList();

                int imageCount = slides.Count(s => !string.IsNullOrEmpty(s.ImagePath) && File.Exists(s.ImagePath));
                _logger.Log(LocalizationService.GetString("VM.Pptx.Imported", slides.Count, imageCount, path));

                if (imageCount == 0)
                {
                    _dialogService.ShowInfo(
                        LocalizationService.GetString("VM.Pptx.NotesOnly", slides.Count),
                        LocalizationService.GetString("VM.Pptx.Result"));
                }
            }
            catch (Exception ex)
            {
                _dialogService.ShowError(LocalizationService.GetString("VM.Pptx.Error", ex.Message), LocalizationService.GetString("VM.Pptx.Failed"));
            }
        }

        #endregion

        #region BGM

        private void OpenBgmSettings()
        {
            if (_dialogService == null) return;

            var result = _dialogService.ShowBgmDialog(_project.Bgm);
            if (result != null)
            {
                _project.Bgm = result;
                UpdateBgmStatus();
                _logger.Log(LocalizationService.GetString("VM.BGM.Updated"));
            }
        }

        private void UpdateBgmStatus()
        {
            if (_project.Bgm.HasBgm)
            {
                BgmStatusText = LocalizationService.GetString("BGM.Set", Path.GetFileName(_project.Bgm.FilePath) ?? "");
                BgmActive = true;
            }
            else
            {
                BgmStatusText = LocalizationService.GetString("BGM.NotSet");
                BgmActive = false;
            }
        }

        #endregion

        #region License

        public void LoadLicense()
        {
            var key = _config.LicenseKey;
            var email = _config.LicenseEmail;
            _licenseInfo = License.ValidateLicenseKey(key, email);
            _currentPlan = _licenseInfo?.IsValid == true ? _licenseInfo.Plan : PlanCode.Free;
            PlanDisplayName = License.GetPlanDisplayName(_currentPlan);
            UpdateFeatureAccess();
        }

        private void UpdateFeatureAccess()
        {
            CanSubtitle = License.CanUseFeature(_currentPlan, "subtitle");
            CanSubtitleStyle = License.CanUseFeature(_currentPlan, "subtitle_style");
            CanTransition = License.CanUseFeature(_currentPlan, "transition");
            CanPptx = License.CanUseFeature(_currentPlan, "pptx_import");

            SubtitlePlaceholder = _canSubtitle
                ? LocalizationService.GetString("Scene.Subtitle.Default")
                : LocalizationService.GetString("VM.Subtitle.ProRequired");
        }

        #endregion

        #region Menu Handlers

        private void ShowHelp()
        {
            try
            {
                var helpWindow = new Views.HelpWindow
                {
                    Owner = System.Windows.Application.Current.MainWindow
                };
                helpWindow.ShowDialog();
            }
            catch
            {
                // Fallback to simple dialog if HelpWindow fails
                _dialogService?.ShowInfo(
                    LocalizationService.GetString("VM.Tutorial"),
                    LocalizationService.GetString("VM.Tutorial.Title"));
            }
        }

        // B9: Copy narration text to subtitle
        private void CopyNarrationToSubtitle()
        {
            if (!string.IsNullOrWhiteSpace(NarrationText))
            {
                SubtitleText = NarrationText;
                StatusText = LocalizationService.GetString("VM.CopiedToSubtitle");
            }
        }

        // B10: Apply current transition settings to all scenes
        private void ApplyTransitionToAll()
        {
            if (_project?.Scenes == null || _currentScene == null) return;

            var type = _currentScene.TransitionType;
            var dur = _currentScene.TransitionDuration;

            foreach (var scene in _project.Scenes)
            {
                scene.TransitionType = type;
                scene.TransitionDuration = dur;
            }
            _isDirty = true;
            StatusText = LocalizationService.GetString("VM.TransitionAppliedAll");
        }

        private void ShowLicenseManager()
        {
            _dialogService?.ShowLicenseDialog(_config);
            LoadLicense();
        }

        private void ShowLicenseInfo()
        {
            var planName = License.GetPlanDisplayName(_currentPlan);
            var expiry = _licenseInfo?.ExpiresAt?.ToString("yyyy-MM-dd") ?? "N/A";
            var status = _licenseInfo?.IsValid == true ? LocalizationService.GetString("VM.License.Active") : LocalizationService.GetString("VM.License.Inactive");

            _dialogService?.ShowInfo(
                LocalizationService.GetString("VM.License.Info", planName, status, expiry),
                LocalizationService.GetString("VM.License.Title"));
        }

        private void ShowAbout()
        {
            var dialog = new Views.AboutDialog();
            dialog.Owner = System.Windows.Application.Current.MainWindow;
            dialog.ShowDialog();
        }

        // ShowShortcuts is now handled by ShowHelp()

        private void ShowTerms()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://harmonic-insight.com/ja/terms",
                    UseShellExecute = true
                });
            }
            catch
            {
                _dialogService?.ShowInfo(
                    LocalizationService.GetString("VM.Terms.Fallback"),
                    LocalizationService.GetString("VM.Terms.Title"));
            }
        }

        private void ShowPrivacy()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://harmonic-insight.com/ja/privacy",
                    UseShellExecute = true
                });
            }
            catch
            {
                _dialogService?.ShowInfo(
                    LocalizationService.GetString("VM.Privacy.Fallback"),
                    LocalizationService.GetString("VM.Privacy.Title"));
            }
        }

        #endregion

        #region Window Lifecycle

        public bool CanClose()
        {
            if (_isExporting)
            {
                if (_dialogService?.ShowConfirmation(
                    LocalizationService.GetString("VM.Exit.Confirm"),
                    LocalizationService.GetString("VM.Exit.Confirm.Title")) == true)
                {
                    _exportCts?.Cancel();
                    return true;
                }
                return false;
            }

            if (_isDirty)
            {
                if (_dialogService?.ShowConfirmation(
                    LocalizationService.GetString("VM.Exit.Unsaved"),
                    LocalizationService.GetString("VM.Exit.Unsaved.Title")) != true)
                {
                    return false;
                }
            }

            // Dispose auto-save timer
            _autoSaveTimer?.Dispose();
            _autoSaveTimer = null;

            // Clean up preview files and PPTX temp directories
            try
            {
                var cacheBase = Path.Combine(Path.GetTempPath(), "insightcast_cache");
                var previewDir = Path.Combine(cacheBase, "preview");
                if (Directory.Exists(previewDir))
                    Directory.Delete(previewDir, true);

                var pptxDir = Path.Combine(cacheBase, "pptx_slides");
                if (Directory.Exists(pptxDir))
                    Directory.Delete(pptxDir, true);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Cache cleanup: {ex.Message}"); }

            return true;
        }

        #endregion

        #region JSON Import & Batch Export

        private void ImportJson()
        {
            if (_dialogService == null) return;

            var batchService = CreateBatchExportService();
            if (batchService == null) return;

            var dialog = new Views.JsonImportDialog(batchService);
            dialog.Owner = System.Windows.Application.Current.MainWindow;

            if (dialog.ShowDialog() == true && dialog.ImportedProject != null)
            {
                _project = dialog.ImportedProject;
                RefreshSceneList();
                if (_project.Scenes.Count > 0)
                    SelectedSceneIndex = 0;

                WindowTitle = LocalizationService.GetString("VM.ImportProject");
                StatusText = LocalizationService.GetString("VM.Project.Loaded", _project.Scenes.Count);
                _isDirty = true;
            }
        }

        private void OpenBatchExport()
        {
            if (_dialogService == null) return;

            var batchService = CreateBatchExportService();
            if (batchService == null) return;

            var dialog = new Views.BatchExportDialog(
                batchService,
                _defaultSpeakerId,
                GetStyleForScene);
            dialog.Owner = System.Windows.Application.Current.MainWindow;
            dialog.ShowDialog();
        }

        private void OpenAISettings()
        {
            var openAIService = new Services.OpenAI.OpenAIService();

            var dialog = new Views.OpenAISettingsDialog(_config, openAIService);
            dialog.Owner = System.Windows.Application.Current.MainWindow;

            if (dialog.ShowDialog() == true)
            {
                StatusText = LocalizationService.GetString("Status.OpenAISettingsUpdated");
            }
        }

        private void OpenAIGenerateProject()
        {
            var openAIService = new Services.OpenAI.OpenAIService();

            // Configure API key if available
            var apiKey = Services.OpenAI.ApiKeyManager.GetApiKey(_config);
            if (!string.IsNullOrEmpty(apiKey))
            {
                _ = openAIService.ConfigureAsync(apiKey);
            }

            var dialog = new Views.AIProjectGenerateDialog(_config, openAIService);
            dialog.Owner = System.Windows.Application.Current.MainWindow;

            if (dialog.ShowDialog() == true && dialog.GeneratedProject != null)
            {
                // Apply generated project
                _project = dialog.GeneratedProject;
                _project.ProjectPath = null;
                RefreshSceneList();

                if (SceneItems.Count > 0)
                {
                    SelectedSceneIndex = 0;
                }

                StatusText = LocalizationService.GetString("Status.AIProjectGenerated");
            }
        }

        /// <summary>
        /// Searches for the Snipaste executable in common locations.
        /// Search order: PATH, application-relative paths, common Windows paths.
        /// </summary>
        private static string? FindSnipaste()
        {
            // --- 1. Search PATH using "where Snipaste" ---
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "where",
                    Arguments = "Snipaste",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(psi);
                if (process != null)
                {
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                    {
                        string firstLine = output.Split(
                            new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                        if (File.Exists(firstLine))
                            return firstLine;
                    }
                }
            }
            catch
            {
                // "where" command not available or failed; continue searching.
            }

            // --- 2. Application-relative and working-directory-relative paths ---
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string cwd = Environment.CurrentDirectory;

            var relativePaths = new List<string>
            {
                // From application base directory (works in published/installed builds)
                Path.Combine(appDir, "snipaste", "Snipaste.exe"),
                Path.Combine(appDir, "Snipaste.exe"),
                // From current working directory (works with "dotnet run")
                Path.Combine(cwd, "snipaste", "Snipaste.exe"),
                Path.Combine(cwd, "Snipaste.exe"),
                // build.ps1 publish output
                Path.Combine(cwd, "publish", "snipaste", "Snipaste.exe"),
            };

            // Walk up from appDir to find project/solution root
            string? dir = appDir;
            for (int i = 0; i < 6 && dir != null; i++)
            {
                dir = Path.GetDirectoryName(dir);
                if (dir != null)
                {
                    relativePaths.Add(Path.Combine(dir, "snipaste", "Snipaste.exe"));
                    relativePaths.Add(Path.Combine(dir, "Snipaste.exe"));
                }
            }

            foreach (string path in relativePaths)
            {
                string fullPath = Path.GetFullPath(path);
                if (File.Exists(fullPath))
                    return fullPath;
            }

            // --- 3. Common Windows installation paths ---
            string[] commonPaths =
            {
                @"C:\Program Files\Snipaste\Snipaste.exe",
                @"C:\Program Files (x86)\Snipaste\Snipaste.exe",
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Snipaste", "Snipaste.exe"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Snipaste", "Snipaste.exe"),
            };

            foreach (string path in commonPaths)
            {
                if (File.Exists(path))
                    return path;
            }

            return null;
        }

        private void LaunchSnipaste()
        {
            var snipastePath = FindSnipaste();
            if (snipastePath == null)
            {
                _dialogService?.ShowError(
                    LocalizationService.GetString("VM.Snipaste.NotFound"),
                    LocalizationService.GetString("VM.Snipaste.NotFound.Title"));
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = snipastePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _dialogService?.ShowError(
                    string.Format(LocalizationService.GetString("VM.Snipaste.LaunchError"), ex.Message),
                    LocalizationService.GetString("VM.Snipaste.LaunchError.Title"));
            }
        }

        private Services.Batch.IBatchExportService? CreateBatchExportService()
        {
            if (_ffmpegWrapper == null)
            {
                _dialogService?.ShowError(
                    LocalizationService.GetString("App.FFmpeg.NotFound"),
                    LocalizationService.GetString("Common.Error"));
                return null;
            }

            var openAIService = new Services.OpenAI.OpenAIService();
            var apiKey = Services.OpenAI.ApiKeyManager.GetApiKey(_config);
            if (!string.IsNullOrEmpty(apiKey))
            {
                _ = openAIService.ConfigureAsync(apiKey);
            }

            return new Services.Batch.BatchExportService(
                _ffmpegWrapper,
                _voiceVoxClient,
                _audioCache,
                openAIService);
        }

        #endregion
    }
}

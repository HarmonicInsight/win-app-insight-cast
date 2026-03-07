using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using InsightCast.Core;
using InsightCast.Infrastructure;
using InsightCast.Models;
using InsightCast.Services;
using InsightCast.Services.OpenAI;
using Microsoft.Win32;

namespace InsightCast.ViewModels
{
    public class MediaItemViewModel
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName => Path.GetFileName(FilePath);
        public ImageSource? Thumbnail { get; set; }
        public bool IsVideo { get; set; }
    }

    public class ColorOption : INotifyPropertyChanged
    {
        private bool _isSelected;

        public int Index { get; set; }
        public string Name { get; set; } = string.Empty;
        public System.Drawing.Color DrawingColor { get; set; }
        public Brush Brush => new SolidColorBrush(Color.FromRgb(DrawingColor.R, DrawingColor.G, DrawingColor.B));

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BorderBrush)));
            }
        }

        public Brush BorderBrush => IsSelected
            ? new SolidColorBrush(Colors.Gold)
            : new SolidColorBrush(Colors.Transparent);

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public class ChatMessage
    {
        public string Text { get; set; } = string.Empty;
        public bool IsUser { get; set; }
        public Brush Background => IsUser
            ? new SolidColorBrush(Color.FromRgb(60, 60, 80))
            : new SolidColorBrush(Color.FromRgb(40, 50, 60));
    }

    public class PlanningSceneItem : INotifyPropertyChanged
    {
        private readonly Scene _scene;
        private int _index;

        public PlanningSceneItem(Scene scene, int index)
        {
            _scene = scene;
            _index = index;
        }

        public Scene Scene => _scene;

        public int Index
        {
            get => _index;
            set { _index = value; OnPropertyChanged(); }
        }

        public string Title
        {
            get => _scene.Title ?? string.Empty;
            set { _scene.Title = value; OnPropertyChanged(); }
        }

        public string Script
        {
            get => _scene.NarrationText ?? string.Empty;
            set
            {
                _scene.NarrationText = value;
                _scene.SubtitleText = value;
                OnPropertyChanged();
            }
        }

        public bool HasImage => _scene.HasMedia;

        public Brush HasImageColor => HasImage
            ? new SolidColorBrush(Colors.LightGreen)
            : new SolidColorBrush(Colors.Gray);

        public string ImagePrompt
        {
            get => _scene.AIGeneration?.ImageDescription ?? string.Empty;
            set
            {
                _scene.AIGeneration ??= new AIGenerationSettings();
                _scene.AIGeneration.ImageDescription = value;
                _scene.AIGeneration.GenerateImage = !string.IsNullOrWhiteSpace(value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasImagePrompt));
            }
        }

        public bool HasImagePrompt => !string.IsNullOrWhiteSpace(_scene.AIGeneration?.ImageDescription);

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class PlanningViewModel : INotifyPropertyChanged
    {
        private readonly Config _config;
        private readonly Project _project;
        private IOpenAIService? _openAIService;

        public Project Project => _project;

        /// <summary>Raised when scenes are added, removed, or modified.</summary>
        public event Action? ScenesChanged;

        private string _chatInput = string.Empty;
        private int _selectedPlanningSceneIndex = -1;
        private CancellationTokenSource? _cts;

        // Quick setup properties
        private int _selectedPurpose = 0;
        private string _customPurposeText = string.Empty;
        private int _selectedDurationIndex = 1; // Default: 30秒
        private string _sceneCount = "3";

        private static readonly int[] DurationValues = { 15, 30, 60, 90, 120, 180 };

        // Thumbnail generator properties
        private readonly ThumbnailService _thumbnailService = new();
        private readonly List<ThumbnailPatternInfo> _thumbnailPatterns;
        private readonly List<StylePresetInfo> _stylePresets;
        private int _selectedThumbnailPatternIndex = 1; // Default: PowerWord
        private int _selectedStyleIndex = 0; // Default: Custom (user colors)
        private string _thumbnailMainText = string.Empty;
        private string _thumbnailSubText = string.Empty;
        private string _thumbnailSubSubText = string.Empty;
        private string? _thumbnailBgPath;
        private int _selectedMainColorIndex = 0;
        private int _selectedSubColorIndex = 1;
        private int _selectedSubSubColorIndex = 2;
        private int _selectedBgColorIndex = 0;
        private int _selectedMainFontIndex = 0;
        private int _selectedSubFontIndex = 0;
        private int _selectedSubSubFontIndex = 0;
        private int _mainFontSize = 0;
        private int _subFontSize = 0;
        private int _subSubFontSize = 0;
        private ImageSource? _thumbnailPreviewImage;
        private string? _lastThumbnailPath;
        private System.Windows.Threading.DispatcherTimer? _previewDebounceTimer;

        // Available fonts for thumbnail
        private static readonly List<string> _availableFonts = new()
        {
            "Yu Gothic UI",
            "Meiryo UI",
            "MS Gothic",
            "HGP創英角ﾎﾟｯﾌﾟ体",
            "HGS創英角ﾎﾟｯﾌﾟ体",
            "HG丸ｺﾞｼｯｸM-PRO",
            "HGP創英角ｺﾞｼｯｸUB",
            "BIZ UDPゴシック",
            "BIZ UDP明朝",
            "游明朝",
            "Arial Black",
            "Impact"
        };

        // Color palette (Excel-style)
        private static readonly List<ColorOption> _colorPalette = CreateColorPalette();

        // JSON editor properties
        private bool _isJsonMode;
        private string _scenesJson = string.Empty;

        public PlanningViewModel(Config config, Project project)
        {
            _config = config;
            _project = project;

            ChatMessages = new ObservableCollection<ChatMessage>();
            PlanningScenes = new ObservableCollection<PlanningSceneItem>();
            MediaItems = new ObservableCollection<MediaItemViewModel>();
            _thumbnailPatterns = ThumbnailService.GetPatterns();
            _stylePresets = ThumbnailService.GetStylePresets();

            // Load thumbnail settings from project
            LoadThumbnailSettingsFromProject();

            // Commands
            SendChatCommand = new RelayCommand(async () => await SendChatAsync());
            GenerateOutlineCommand = new RelayCommand(async () => await GenerateOutlineAsync());
            GenerateAllScriptsCommand = new RelayCommand(async () => await GenerateAllScriptsAsync());
            AddSceneCommand = new RelayCommand(AddScene);
            RemoveSceneCommand = new RelayCommand(RemoveScene);
            MoveSceneUpCommand = new RelayCommand(MoveSceneUp);
            MoveSceneDownCommand = new RelayCommand(MoveSceneDown);
            GenerateProjectCommand = new RelayCommand(async () => await GenerateProjectAsync());

            // Thumbnail commands
            GenerateThumbnailCommand = new RelayCommand(GenerateThumbnail);
            SaveThumbnailCommand = new RelayCommand(SaveThumbnail);
            OpenThumbnailFolderCommand = new RelayCommand(OpenThumbnailFolder);
            SelectThumbnailBgCommand = new RelayCommand(SelectThumbnailBackground);
            ClearThumbnailBgCommand = new RelayCommand(ClearThumbnailBackground);
            ClearThumbnailCommand = new RelayCommand(ClearThumbnail);
            ApplyStylePresetCommand = new RelayCommand(ApplyStylePreset);

            // JSON commands
            ApplyJsonCommand = new RelayCommand(ApplyJsonToScenes);

            // Clear commands
            ClearAllScenesCommand = new RelayCommand(ClearAllScenes);
            ClearSceneCommand = new RelayCommand(ClearScene);

            // CTA endcard command
            AddCtaEndcardCommand = new RelayCommand(AddCtaEndcard);

            // Quick setup command
            ApplyQuickSetupCommand = new RelayCommand(ApplyQuickSetup);

            // JSON mode close command
            CloseJsonModeCommand = new RelayCommand(() => IsJsonMode = false);

            RefreshSceneList();
        }

        #region Properties

        public ObservableCollection<ChatMessage> ChatMessages { get; }
        public ObservableCollection<PlanningSceneItem> PlanningScenes { get; }
        public ObservableCollection<MediaItemViewModel> MediaItems { get; }

        public string ChatInput
        {
            get => _chatInput;
            set { _chatInput = value; OnPropertyChanged(); }
        }

        public int SelectedPlanningSceneIndex
        {
            get => _selectedPlanningSceneIndex;
            set
            {
                _selectedPlanningSceneIndex = value;
                OnPropertyChanged();
            }
        }

        // Quick setup properties
        public int SelectedPurpose
        {
            get => _selectedPurpose;
            set { _selectedPurpose = value; OnPropertyChanged(); }
        }

        public string CustomPurposeText
        {
            get => _customPurposeText;
            set { _customPurposeText = value; OnPropertyChanged(); }
        }

        public int SelectedDurationIndex
        {
            get => _selectedDurationIndex;
            set
            {
                _selectedDurationIndex = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SecondsPerSceneText));
            }
        }

        public string SceneCount
        {
            get => _sceneCount;
            set
            {
                _sceneCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SecondsPerSceneText));
                OnPropertyChanged(nameof(MediaStatusText));
                OnPropertyChanged(nameof(MediaStatusColor));
            }
        }

        public string SecondsPerSceneText
        {
            get
            {
                var duration = GetTargetDuration();
                if (int.TryParse(_sceneCount, out var scenes) && scenes > 0)
                {
                    var secondsPerScene = duration / scenes;
                    var charsPerScene = secondsPerScene * 4;
                    return LocalizationService.GetString("Planning.SecondsPerScene", secondsPerScene, charsPerScene);
                }
                return "";
            }
        }

        public string MediaStatusText
        {
            get
            {
                var sceneCount = GetSceneCount();
                var mediaCount = MediaItems.Count;

                if (mediaCount == 0)
                    return LocalizationService.GetString("Planning.Media.Required", sceneCount);
                if (mediaCount < sceneCount)
                    return LocalizationService.GetString("Planning.Media.Partial", sceneCount, mediaCount, sceneCount - mediaCount);
                if (mediaCount == sceneCount)
                    return LocalizationService.GetString("Planning.Media.Exact", sceneCount);
                return LocalizationService.GetString("Planning.Media.Excess", mediaCount, sceneCount);
            }
        }

        public Brush MediaStatusColor
        {
            get
            {
                var sceneCount = GetSceneCount();
                var mediaCount = MediaItems.Count;

                if (mediaCount >= sceneCount)
                    return new SolidColorBrush(Colors.LightGreen);
                if (mediaCount > 0)
                    return new SolidColorBrush(Colors.Orange);
                return new SolidColorBrush(Colors.Gray);
            }
        }

        // Thumbnail Generator Properties
        public List<ThumbnailPatternInfo> ThumbnailPatterns => _thumbnailPatterns;
        public List<StylePresetInfo> StylePresets => _stylePresets;

        public int SelectedThumbnailPatternIndex
        {
            get => _selectedThumbnailPatternIndex;
            set
            {
                _selectedThumbnailPatternIndex = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedPatternInfo));
                RequestThumbnailPreviewUpdate();
            }
        }

        public ThumbnailPatternInfo? SelectedPatternInfo =>
            _selectedThumbnailPatternIndex >= 0 && _selectedThumbnailPatternIndex < _thumbnailPatterns.Count
                ? _thumbnailPatterns[_selectedThumbnailPatternIndex]
                : null;

        public string ThumbnailMainText
        {
            get => _thumbnailMainText;
            set
            {
                _thumbnailMainText = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ThumbnailCharCountText));
                OnPropertyChanged(nameof(ThumbnailCharCountColor));
                RequestThumbnailPreviewUpdate();
            }
        }

        public string ThumbnailSubText
        {
            get => _thumbnailSubText;
            set
            {
                _thumbnailSubText = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ThumbnailCharCountText));
                OnPropertyChanged(nameof(ThumbnailCharCountColor));
                RequestThumbnailPreviewUpdate();
            }
        }

        public string ThumbnailSubSubText
        {
            get => _thumbnailSubSubText;
            set
            {
                _thumbnailSubSubText = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ThumbnailCharCountText));
                OnPropertyChanged(nameof(ThumbnailCharCountColor));
                RequestThumbnailPreviewUpdate();
            }
        }

        public string ThumbnailCharCountText
        {
            get
            {
                var total = (_thumbnailMainText?.Length ?? 0) + (_thumbnailSubText?.Length ?? 0) + (_thumbnailSubSubText?.Length ?? 0);
                return $"文字数: {total} / 25";
            }
        }

        public Brush ThumbnailCharCountColor
        {
            get
            {
                var total = (_thumbnailMainText?.Length ?? 0) + (_thumbnailSubText?.Length ?? 0) + (_thumbnailSubSubText?.Length ?? 0);
                if (total <= 18) return new SolidColorBrush(Colors.LightGreen);
                if (total <= 25) return new SolidColorBrush(Colors.Orange);
                return new SolidColorBrush(Colors.Red);
            }
        }

        public string ThumbnailBgFileName => string.IsNullOrEmpty(_thumbnailBgPath)
            ? LocalizationService.GetString("Thumbnail.NoBg")
            : Path.GetFileName(_thumbnailBgPath);

        // Excel-style color palette
        public List<ColorOption> ColorPalette => _colorPalette;

        public int SelectedMainColorIndex
        {
            get => _selectedMainColorIndex;
            set { _selectedMainColorIndex = value; SwitchToCustomStyle(); OnPropertyChanged(); OnPropertyChanged(nameof(SelectedMainColorBrush)); RequestThumbnailPreviewUpdate(); }
        }

        public int SelectedSubColorIndex
        {
            get => _selectedSubColorIndex;
            set { _selectedSubColorIndex = value; SwitchToCustomStyle(); OnPropertyChanged(); OnPropertyChanged(nameof(SelectedSubColorBrush)); RequestThumbnailPreviewUpdate(); }
        }

        public int SelectedSubSubColorIndex
        {
            get => _selectedSubSubColorIndex;
            set { _selectedSubSubColorIndex = value; SwitchToCustomStyle(); OnPropertyChanged(); OnPropertyChanged(nameof(SelectedSubSubColorBrush)); RequestThumbnailPreviewUpdate(); }
        }

        public int SelectedBgColorIndex
        {
            get => _selectedBgColorIndex;
            set { _selectedBgColorIndex = value; SwitchToCustomStyle(); OnPropertyChanged(); OnPropertyChanged(nameof(SelectedBgColorBrush)); RequestThumbnailPreviewUpdate(); }
        }

        private void SwitchToCustomStyle()
        {
            if (_selectedStyleIndex != 0)
            {
                _selectedStyleIndex = 0;
                OnPropertyChanged(nameof(SelectedStyleIndex));
            }
        }

        public Brush SelectedMainColorBrush => _selectedMainColorIndex >= 0 && _selectedMainColorIndex < _colorPalette.Count
            ? _colorPalette[_selectedMainColorIndex].Brush : Brushes.Yellow;
        public Brush SelectedSubColorBrush => _selectedSubColorIndex >= 0 && _selectedSubColorIndex < _colorPalette.Count
            ? _colorPalette[_selectedSubColorIndex].Brush : Brushes.White;
        public Brush SelectedSubSubColorBrush => _selectedSubSubColorIndex >= 0 && _selectedSubSubColorIndex < _colorPalette.Count
            ? _colorPalette[_selectedSubSubColorIndex].Brush : Brushes.LightGray;
        public Brush SelectedBgColorBrush => _selectedBgColorIndex >= 0 && _selectedBgColorIndex < _colorPalette.Count
            ? _colorPalette[_selectedBgColorIndex].Brush : Brushes.Black;

        public System.Drawing.Color GetMainTextColor() => _selectedMainColorIndex >= 0 && _selectedMainColorIndex < _colorPalette.Count
            ? _colorPalette[_selectedMainColorIndex].DrawingColor : System.Drawing.Color.Yellow;
        public System.Drawing.Color GetSubTextColor() => _selectedSubColorIndex >= 0 && _selectedSubColorIndex < _colorPalette.Count
            ? _colorPalette[_selectedSubColorIndex].DrawingColor : System.Drawing.Color.White;
        public System.Drawing.Color GetSubSubTextColor() => _selectedSubSubColorIndex >= 0 && _selectedSubSubColorIndex < _colorPalette.Count
            ? _colorPalette[_selectedSubSubColorIndex].DrawingColor : System.Drawing.Color.LightGray;
        public System.Drawing.Color GetBgColor() => _selectedBgColorIndex >= 0 && _selectedBgColorIndex < _colorPalette.Count
            ? _colorPalette[_selectedBgColorIndex].DrawingColor : System.Drawing.Color.FromArgb(30, 30, 30);

        // Font selection
        public List<string> AvailableFonts => _availableFonts;

        public int SelectedMainFontIndex
        {
            get => _selectedMainFontIndex;
            set { _selectedMainFontIndex = value; OnPropertyChanged(); OnPropertyChanged(nameof(SelectedMainFontName)); RequestThumbnailPreviewUpdate(); }
        }

        public int SelectedSubFontIndex
        {
            get => _selectedSubFontIndex;
            set { _selectedSubFontIndex = value; OnPropertyChanged(); OnPropertyChanged(nameof(SelectedSubFontName)); RequestThumbnailPreviewUpdate(); }
        }

        public int SelectedSubSubFontIndex
        {
            get => _selectedSubSubFontIndex;
            set { _selectedSubSubFontIndex = value; OnPropertyChanged(); OnPropertyChanged(nameof(SelectedSubSubFontName)); RequestThumbnailPreviewUpdate(); }
        }

        public string SelectedMainFontName => _selectedMainFontIndex >= 0 && _selectedMainFontIndex < _availableFonts.Count
            ? _availableFonts[_selectedMainFontIndex] : "Yu Gothic UI";
        public string SelectedSubFontName => _selectedSubFontIndex >= 0 && _selectedSubFontIndex < _availableFonts.Count
            ? _availableFonts[_selectedSubFontIndex] : "Yu Gothic UI";
        public string SelectedSubSubFontName => _selectedSubSubFontIndex >= 0 && _selectedSubSubFontIndex < _availableFonts.Count
            ? _availableFonts[_selectedSubSubFontIndex] : "Yu Gothic UI";

        // Font size options (0 = auto)
        private static readonly List<int> _fontSizeOptions = new() { 0, 36, 48, 60, 72, 84, 96, 120, 150, 180 };
        public List<string> FontSizeOptions => _fontSizeOptions.Select(s => s == 0 ? "自動" : s.ToString()).ToList();

        public int MainFontSizeIndex
        {
            get => _fontSizeOptions.IndexOf(_mainFontSize);
            set
            {
                if (value >= 0 && value < _fontSizeOptions.Count)
                {
                    _mainFontSize = _fontSizeOptions[value];
                    OnPropertyChanged();
                    RequestThumbnailPreviewUpdate();
                }
            }
        }

        public int SubFontSizeIndex
        {
            get => _fontSizeOptions.IndexOf(_subFontSize);
            set
            {
                if (value >= 0 && value < _fontSizeOptions.Count)
                {
                    _subFontSize = _fontSizeOptions[value];
                    OnPropertyChanged();
                    RequestThumbnailPreviewUpdate();
                }
            }
        }

        public int SubSubFontSizeIndex
        {
            get => _fontSizeOptions.IndexOf(_subSubFontSize);
            set
            {
                if (value >= 0 && value < _fontSizeOptions.Count)
                {
                    _subSubFontSize = _fontSizeOptions[value];
                    OnPropertyChanged();
                    RequestThumbnailPreviewUpdate();
                }
            }
        }

        public int MainFontSize => _mainFontSize;
        public int SubFontSize => _subFontSize;
        public int SubSubFontSize => _subSubFontSize;

        private static List<ColorOption> CreateColorPalette()
        {
            var colors = new List<ColorOption>();
            int idx = 0;

            // Row 1: Standard colors
            var standardColors = new[] {
                ("黒", System.Drawing.Color.Black),
                ("白", System.Drawing.Color.White),
                ("赤", System.Drawing.Color.Red),
                ("オレンジ", System.Drawing.Color.Orange),
                ("黄色", System.Drawing.Color.Yellow),
                ("緑", System.Drawing.Color.Lime),
                ("水色", System.Drawing.Color.Cyan),
                ("青", System.Drawing.Color.Blue),
                ("紫", System.Drawing.Color.Purple),
                ("ピンク", System.Drawing.Color.HotPink)
            };
            foreach (var (name, color) in standardColors)
                colors.Add(new ColorOption { Index = idx++, Name = name, DrawingColor = color });

            // Row 2: Light variants
            var lightColors = new[] {
                ("ダークグレー", System.Drawing.Color.FromArgb(50, 50, 50)),
                ("ライトグレー", System.Drawing.Color.LightGray),
                ("ライトレッド", System.Drawing.Color.FromArgb(255, 128, 128)),
                ("ライトオレンジ", System.Drawing.Color.FromArgb(255, 200, 128)),
                ("ライトイエロー", System.Drawing.Color.FromArgb(255, 255, 180)),
                ("ライトグリーン", System.Drawing.Color.LightGreen),
                ("ライトシアン", System.Drawing.Color.FromArgb(180, 255, 255)),
                ("ライトブルー", System.Drawing.Color.LightBlue),
                ("ライトパープル", System.Drawing.Color.FromArgb(200, 180, 255)),
                ("ライトピンク", System.Drawing.Color.LightPink)
            };
            foreach (var (name, color) in lightColors)
                colors.Add(new ColorOption { Index = idx++, Name = name, DrawingColor = color });

            // Row 3: Dark/Saturated variants
            var darkColors = new[] {
                ("ダーク", System.Drawing.Color.FromArgb(30, 30, 30)),
                ("グレー", System.Drawing.Color.Gray),
                ("ダークレッド", System.Drawing.Color.DarkRed),
                ("ダークオレンジ", System.Drawing.Color.FromArgb(200, 100, 50)),
                ("ゴールド", System.Drawing.Color.Gold),
                ("ダークグリーン", System.Drawing.Color.DarkGreen),
                ("ティール", System.Drawing.Color.Teal),
                ("ネイビー", System.Drawing.Color.Navy),
                ("インディゴ", System.Drawing.Color.Indigo),
                ("マゼンタ", System.Drawing.Color.Magenta)
            };
            foreach (var (name, color) in darkColors)
                colors.Add(new ColorOption { Index = idx++, Name = name, DrawingColor = color });

            return colors;
        }

        public ImageSource? ThumbnailPreviewImage
        {
            get => _thumbnailPreviewImage;
            set { _thumbnailPreviewImage = value; OnPropertyChanged(); }
        }

        // JSON Editor Properties
        public bool IsJsonMode
        {
            get => _isJsonMode;
            set
            {
                if (_isJsonMode != value)
                {
                    _isJsonMode = value;
                    OnPropertyChanged();
                    if (value)
                    {
                        // Entering JSON mode - serialize scenes to JSON
                        ScenesJson = SerializeScenesToJson();
                    }
                }
            }
        }

        public string ScenesJson
        {
            get => _scenesJson;
            set { _scenesJson = value; OnPropertyChanged(); }
        }

        #endregion

        #region Commands

        public ICommand SendChatCommand { get; }
        public ICommand GenerateOutlineCommand { get; }
        public ICommand GenerateAllScriptsCommand { get; }
        public ICommand AddSceneCommand { get; }
        public ICommand RemoveSceneCommand { get; }
        public ICommand MoveSceneUpCommand { get; }
        public ICommand MoveSceneDownCommand { get; }
        public ICommand GenerateProjectCommand { get; }

        // Thumbnail commands
        public ICommand GenerateThumbnailCommand { get; }
        public ICommand SaveThumbnailCommand { get; }
        public ICommand OpenThumbnailFolderCommand { get; }
        public ICommand SelectThumbnailBgCommand { get; }
        public ICommand ClearThumbnailBgCommand { get; }
        public ICommand ClearThumbnailCommand { get; }
        public ICommand ApplyStylePresetCommand { get; }

        // JSON commands
        public ICommand ApplyJsonCommand { get; }
        public ICommand CloseJsonModeCommand { get; }

        // Clear commands
        public ICommand ClearAllScenesCommand { get; }
        public ICommand ClearSceneCommand { get; }

        // Quick setup command
        public ICommand ApplyQuickSetupCommand { get; }

        // CTA endcard command
        public ICommand AddCtaEndcardCommand { get; }

        #endregion

        #region Methods

        public void AddMediaFiles(string[] files)
        {
            var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };
            var videoExtensions = new[] { ".mp4", ".avi", ".mov", ".wmv", ".mkv", ".webm" };

            foreach (var file in files)
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (!imageExtensions.Contains(ext) && !videoExtensions.Contains(ext))
                    continue;

                if (MediaItems.Any(m => m.FilePath == file))
                    continue;

                var item = new MediaItemViewModel
                {
                    FilePath = file,
                    IsVideo = videoExtensions.Contains(ext),
                    Thumbnail = LoadThumbnail(file, videoExtensions.Contains(ext))
                };
                MediaItems.Add(item);
            }
            OnPropertyChanged(nameof(MediaItems));
            OnPropertyChanged(nameof(MediaStatusText));
            OnPropertyChanged(nameof(MediaStatusColor));
        }

        private ImageSource? LoadThumbnail(string path, bool isVideo)
        {
            try
            {
                if (isVideo) return null;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path);
                bitmap.DecodePixelWidth = 50;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        private int GetTargetDuration()
        {
            if (_selectedDurationIndex >= 0 && _selectedDurationIndex < DurationValues.Length)
                return DurationValues[_selectedDurationIndex];
            return 30;
        }

        private int GetSceneCount()
        {
            if (int.TryParse(_sceneCount, out var count))
                return Math.Clamp(count, 1, 20);
            return 3;
        }

        private string GetPurposeText()
        {
            var purposes = new[]
            {
                "製品・サービスの紹介",
                "使い方・チュートリアル",
                "概念・仕組みの解説",
                "プロモーション・広告"
            };
            var purposeText = _selectedPurpose >= 0 && _selectedPurpose < purposes.Length
                ? purposes[_selectedPurpose]
                : purposes[0];

            if (!string.IsNullOrWhiteSpace(_customPurposeText))
                purposeText += $" - {_customPurposeText}";

            return purposeText;
        }

        private async Task GenerateProjectAsync()
        {
            var duration = GetTargetDuration();
            var purposeText = GetPurposeText();
            var mediaCount = MediaItems.Count;
            var sceneCount = GetSceneCount();
            var secondsPerScene = sceneCount > 0 ? duration / sceneCount : 10;

            await EnsureOpenAIConfigured();

            // Clear existing scenes and create new ones
            while (_project.Scenes.Count > 1)
            {
                _project.RemoveScene(_project.Scenes.Count - 1);
            }
            while (_project.Scenes.Count < sceneCount)
            {
                _project.AddScene();
            }

            _cts = new CancellationTokenSource();
            try
            {
                if (_openAIService != null && _openAIService.IsConfigured)
                {
                    // Generate with AI
                    var mediaInfo = mediaCount > 0
                        ? string.Join(", ", MediaItems.Select((m, i) => $"素材{i + 1}: {m.FileName}"))
                        : "素材なし（プロンプトから生成予定）";

                    var prompt = $@"以下の条件で動画のナレーションスクリプトを{sceneCount}シーン分作成してください。

目的: {purposeText}
素材: {mediaInfo}
各シーンの長さ: 約{secondsPerScene}秒（{secondsPerScene * 4}文字程度）

出力形式（各シーンごとに）:
シーン1:
タイトル: [シーンのタイトル]
スクリプト: [ナレーション文]
画像プロンプト: [DALL-E用の英語プロンプト]

シーン2:
...

必ず{sceneCount}シーン分出力してください。";

                    var request = new TextGenerationRequest
                    {
                        Topic = prompt,
                        Style = "educational",
                        MaxTokens = 2000
                    };

                    var result = await _openAIService.GenerateNarrationAsync(request, _cts.Token);
                    if (result.Success && !string.IsNullOrEmpty(result.Text))
                    {
                        ParseAndApplyGeneratedContent(result.Text, sceneCount, secondsPerScene);
                    }
                    else
                    {
                        ApplyTemplateContent(purposeText, sceneCount, secondsPerScene);
                    }
                }
                else
                {
                    ApplyTemplateContent(purposeText, sceneCount, secondsPerScene);
                }

                // Assign media to scenes
                for (int i = 0; i < Math.Min(MediaItems.Count, _project.Scenes.Count); i++)
                {
                    var scene = _project.Scenes[i];
                    var media = MediaItems[i];
                    scene.MediaPath = media.FilePath;
                    scene.MediaType = media.IsVideo ? MediaType.Video : MediaType.Image;
                }

                RefreshSceneList();
                if (PlanningScenes.Count > 0)
                    SelectedPlanningSceneIndex = 0;

                ScenesChanged?.Invoke();
            }
            catch (OperationCanceledException)
            {
                // Cancelled
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void ParseAndApplyGeneratedContent(string aiResponse, int expectedCount, int secondsPerScene)
        {
            var lines = aiResponse.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var sceneIndex = -1;
            string? currentTitle = null;
            string? currentScript = null;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                if (trimmed.StartsWith("シーン") && trimmed.Contains(":"))
                {
                    // Save previous scene
                    if (sceneIndex >= 0 && sceneIndex < _project.Scenes.Count)
                    {
                        ApplySceneContent(sceneIndex, currentTitle, currentScript);
                    }
                    sceneIndex++;
                    currentTitle = null;
                    currentScript = null;
                }
                else if (trimmed.StartsWith("タイトル:"))
                {
                    currentTitle = trimmed.Substring("タイトル:".Length).Trim();
                }
                else if (trimmed.StartsWith("スクリプト:"))
                {
                    currentScript = trimmed.Substring("スクリプト:".Length).Trim();
                }
            }

            // Apply last scene
            if (sceneIndex >= 0 && sceneIndex < _project.Scenes.Count)
            {
                ApplySceneContent(sceneIndex, currentTitle, currentScript);
            }
        }

        private void ApplySceneContent(int index, string? title, string? script)
        {
            if (index >= _project.Scenes.Count) return;
            var scene = _project.Scenes[index];
            scene.Title = title ?? LocalizationService.GetString("Planning.SceneTitle", index + 1);
            scene.NarrationText = script ?? "";
            scene.SubtitleText = script ?? "";
        }

        private void ApplyTemplateContent(string purpose, int sceneCount, int secondsPerScene)
        {
            var templates = new[]
            {
                ("導入", "こんにちは。今日は〇〇についてご紹介します。"),
                ("概要", "まず、基本的な概要からご説明します。"),
                ("詳細", "それでは、詳しく見ていきましょう。"),
                ("メリット", "この機能のメリットをご紹介します。"),
                ("まとめ", "以上でご紹介を終わります。ご視聴ありがとうございました。")
            };

            for (int i = 0; i < Math.Min(sceneCount, _project.Scenes.Count); i++)
            {
                var templateIdx = Math.Min(i, templates.Length - 1);
                var template = templates[templateIdx];
                var scene = _project.Scenes[i];
                scene.Title = template.Item1;
                scene.NarrationText = template.Item2;
                scene.SubtitleText = template.Item2;
            }
        }


        public void RefreshSceneList()
        {
            PlanningScenes.Clear();
            for (int i = 0; i < _project.Scenes.Count; i++)
            {
                PlanningScenes.Add(new PlanningSceneItem(_project.Scenes[i], i + 1));
            }

            if (PlanningScenes.Count > 0 && _selectedPlanningSceneIndex < 0)
            {
                SelectedPlanningSceneIndex = 0;
            }
        }

        private async Task EnsureOpenAIConfigured()
        {
            if (_openAIService == null)
            {
                _openAIService = new OpenAIService();
            }

            if (!_openAIService.IsConfigured)
            {
                var apiKey = ApiKeyManager.GetApiKey(_config);
                if (!string.IsNullOrEmpty(apiKey))
                {
                    await _openAIService.ConfigureAsync(apiKey);
                }
            }
        }

        private async Task SendChatAsync()
        {
            if (string.IsNullOrWhiteSpace(ChatInput)) return;

            await EnsureOpenAIConfigured();
            if (_openAIService == null || !_openAIService.IsConfigured)
            {
                ChatMessages.Add(new ChatMessage
                {
                    Text = LocalizationService.GetString("AIGenerate.Error.NoApiKey"),
                    IsUser = false
                });
                return;
            }

            var userMessage = ChatInput;
            ChatMessages.Add(new ChatMessage { Text = userMessage, IsUser = true });
            ChatInput = string.Empty;

            _cts = new CancellationTokenSource();
            try
            {
                var request = new TextGenerationRequest
                {
                    Topic = userMessage,
                    Style = "conversational",
                    MaxTokens = 1000
                };

                var result = await _openAIService.GenerateNarrationAsync(request, _cts.Token);
                ChatMessages.Add(new ChatMessage
                {
                    Text = result.Success ? result.Text ?? "No response" : result.ErrorMessage ?? "Error",
                    IsUser = false
                });
            }
            catch (OperationCanceledException)
            {
                // Cancelled
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
            }
        }

        private async Task GenerateOutlineAsync()
        {
            if (string.IsNullOrWhiteSpace(ChatInput)) return;

            await EnsureOpenAIConfigured();
            if (_openAIService == null || !_openAIService.IsConfigured)
            {
                MessageBox.Show(LocalizationService.GetString("AIGenerate.Error.NoApiKey"));
                return;
            }

            var topic = ChatInput;
            ChatMessages.Add(new ChatMessage { Text = $"構成を生成: {topic}", IsUser = true });

            _cts = new CancellationTokenSource();
            try
            {
                // Generate outline with 5 scenes
                var request = new TextGenerationRequest
                {
                    Topic = $@"以下のトピックについて、5つのシーンで構成される動画の構成案を作成してください。
各シーンには「タイトル」と「概要（1-2文）」を含めてください。

トピック: {topic}

出力形式:
シーン1: [タイトル]
概要: [内容]

シーン2: [タイトル]
概要: [内容]
...",
                    Style = "educational",
                    MaxTokens = 1500
                };

                var result = await _openAIService.GenerateNarrationAsync(request, _cts.Token);
                if (result.Success)
                {
                    ChatMessages.Add(new ChatMessage { Text = result.Text ?? "", IsUser = false });
                    // AI narration result is displayed in chat; scene creation from parsed result is planned for a future release
                }
                else
                {
                    ChatMessages.Add(new ChatMessage { Text = result.ErrorMessage ?? "Error", IsUser = false });
                }
            }
            catch (OperationCanceledException)
            {
                // Cancelled
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
            }
        }

        private async Task GenerateAllScriptsAsync()
        {
            await EnsureOpenAIConfigured();
            if (_openAIService == null || !_openAIService.IsConfigured)
            {
                MessageBox.Show(LocalizationService.GetString("AIGenerate.Error.NoApiKey"));
                return;
            }

            _cts = new CancellationTokenSource();
            try
            {
                foreach (var scene in PlanningScenes)
                {
                    if (string.IsNullOrWhiteSpace(scene.Script) && !string.IsNullOrWhiteSpace(scene.Title))
                    {
                        var request = new TextGenerationRequest
                        {
                            Topic = scene.Title,
                            Style = "educational",
                            TargetDurationSeconds = 30,
                            MaxTokens = 500
                        };

                        var result = await _openAIService.GenerateNarrationAsync(request, _cts.Token);
                        if (result.Success)
                        {
                            scene.Script = result.Text ?? string.Empty;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Cancelled
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void AddScene()
        {
            _project.AddScene();
            RefreshSceneList();
            SelectedPlanningSceneIndex = PlanningScenes.Count - 1;
            ScenesChanged?.Invoke();
        }

        private void RemoveScene()
        {
            if (_project.Scenes.Count <= 1) return;
            if (_selectedPlanningSceneIndex < 0) return;

            _project.RemoveScene(_selectedPlanningSceneIndex);
            RefreshSceneList();

            if (_selectedPlanningSceneIndex >= PlanningScenes.Count)
            {
                SelectedPlanningSceneIndex = PlanningScenes.Count - 1;
            }
            ScenesChanged?.Invoke();
        }

        private void MoveSceneUp()
        {
            if (_selectedPlanningSceneIndex <= 0) return;
            _project.MoveScene(_selectedPlanningSceneIndex, _selectedPlanningSceneIndex - 1);
            RefreshSceneList();
            SelectedPlanningSceneIndex--;
            ScenesChanged?.Invoke();
        }

        private void MoveSceneDown()
        {
            if (_selectedPlanningSceneIndex < 0 || _selectedPlanningSceneIndex >= PlanningScenes.Count - 1) return;
            _project.MoveScene(_selectedPlanningSceneIndex, _selectedPlanningSceneIndex + 1);
            RefreshSceneList();
            SelectedPlanningSceneIndex++;
            ScenesChanged?.Invoke();
        }

        private void ClearAllScenes()
        {
            var result = MessageBox.Show(
                LocalizationService.GetString("Planning.ClearAll.Confirm"),
                LocalizationService.GetString("Planning.ClearAll.Confirm.Title"),
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            foreach (var scene in _project.Scenes)
            {
                scene.Title = null;
                scene.NarrationText = null;
                scene.SubtitleText = null;
                scene.MediaPath = null;
            }
            RefreshSceneList();
            ScenesChanged?.Invoke();
        }

        private void ClearScene(object? param)
        {
            if (param is not int index) return;
            var sceneIndex = index - 1; // Index is 1-based in UI
            if (sceneIndex < 0 || sceneIndex >= _project.Scenes.Count) return;

            var scene = _project.Scenes[sceneIndex];
            scene.Title = null;
            scene.NarrationText = null;
            scene.SubtitleText = null;
            scene.MediaPath = null;

            RefreshSceneList();
            ScenesChanged?.Invoke();
        }

        private void AddCtaEndcard()
        {
            var ctaScene = _project.AddScene();
            ctaScene.Title = LocalizationService.GetString("CTA.ThankYou");
            ctaScene.DurationMode = DurationMode.Fixed;
            ctaScene.FixedSeconds = 5.0;
            ctaScene.TextOverlays = TextOverlay.CreateEndcardSet();
            RefreshSceneList();
            SelectedPlanningSceneIndex = PlanningScenes.Count - 1;
            ScenesChanged?.Invoke();
        }

        private void ApplyQuickSetup()
        {
            if (!int.TryParse(_sceneCount, out var targetCount) || targetCount <= 0)
            {
                MessageBox.Show(LocalizationService.GetString("Planning.InvalidSceneCount"), "Insight Training Studio",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                LocalizationService.GetString("Planning.ConfirmApply", targetCount),
                LocalizationService.GetString("Planning.ConfirmApply.Title"),
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            // Clear existing scenes
            _project.Scenes.Clear();

            // Calculate duration per scene
            var totalDuration = GetTargetDuration();
            var durationPerScene = totalDuration / targetCount;

            // Create new scenes
            for (int i = 0; i < targetCount; i++)
            {
                _project.Scenes.Add(new Scene
                {
                    Title = LocalizationService.GetString("Planning.SceneTitle", i + 1),
                    FixedSeconds = durationPerScene,
                    DurationMode = DurationMode.Fixed
                });
            }

            RefreshSceneList();
            ScenesChanged?.Invoke();
        }

        #endregion

        #region Thumbnail Methods

        private void RequestThumbnailPreviewUpdate()
        {
            // Save settings to project for template persistence
            SaveThumbnailSettingsToProject();

            // Debounce: wait 300ms before generating preview
            if (_previewDebounceTimer == null)
            {
                _previewDebounceTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(300)
                };
                _previewDebounceTimer.Tick += (s, e) =>
                {
                    _previewDebounceTimer.Stop();
                    GenerateThumbnailPreview();
                };
            }

            _previewDebounceTimer.Stop();
            _previewDebounceTimer.Start();
        }

        /// <summary>
        /// サムネイル設定をProjectから読み込む（テンプレート適用時に呼び出し）
        /// </summary>
        public void LoadThumbnailSettingsFromProject()
        {
            var settings = _project.ThumbnailGenerator;
            _selectedThumbnailPatternIndex = settings.PatternIndex;
            _thumbnailMainText = settings.MainText;
            _thumbnailSubText = settings.SubText;
            _thumbnailSubSubText = settings.SubSubText;
            _selectedMainFontIndex = settings.MainFontIndex;
            _selectedSubFontIndex = settings.SubFontIndex;
            _selectedSubSubFontIndex = settings.SubSubFontIndex;
            _mainFontSize = settings.MainFontSizeIndex;
            _subFontSize = settings.SubFontSizeIndex;
            _subSubFontSize = settings.SubSubFontSizeIndex;
            _selectedMainColorIndex = settings.MainColorIndex;
            _selectedSubColorIndex = settings.SubColorIndex;
            _selectedSubSubColorIndex = settings.SubSubColorIndex;
            _selectedBgColorIndex = settings.BgColorIndex;
            _thumbnailBgPath = settings.BgImagePath;

            // Notify all properties changed
            OnPropertyChanged(nameof(SelectedThumbnailPatternIndex));
            OnPropertyChanged(nameof(ThumbnailMainText));
            OnPropertyChanged(nameof(ThumbnailSubText));
            OnPropertyChanged(nameof(ThumbnailSubSubText));
            OnPropertyChanged(nameof(SelectedMainFontIndex));
            OnPropertyChanged(nameof(SelectedSubFontIndex));
            OnPropertyChanged(nameof(SelectedSubSubFontIndex));
            OnPropertyChanged(nameof(MainFontSizeIndex));
            OnPropertyChanged(nameof(SubFontSizeIndex));
            OnPropertyChanged(nameof(SubSubFontSizeIndex));
            OnPropertyChanged(nameof(SelectedMainColorIndex));
            OnPropertyChanged(nameof(SelectedSubColorIndex));
            OnPropertyChanged(nameof(SelectedSubSubColorIndex));
            OnPropertyChanged(nameof(SelectedBgColorIndex));
            OnPropertyChanged(nameof(ThumbnailBgFileName));
            OnPropertyChanged(nameof(ThumbnailCharCountText));
            OnPropertyChanged(nameof(ThumbnailCharCountColor));

            // Update preview (don't save back to project - we're loading from it)
            GenerateThumbnailPreview();
        }

        /// <summary>
        /// サムネイル設定をProjectに保存
        /// </summary>
        private void SaveThumbnailSettingsToProject()
        {
            _project.ThumbnailGenerator.PatternIndex = _selectedThumbnailPatternIndex;
            _project.ThumbnailGenerator.MainText = _thumbnailMainText;
            _project.ThumbnailGenerator.SubText = _thumbnailSubText;
            _project.ThumbnailGenerator.SubSubText = _thumbnailSubSubText;
            _project.ThumbnailGenerator.MainFontIndex = _selectedMainFontIndex;
            _project.ThumbnailGenerator.SubFontIndex = _selectedSubFontIndex;
            _project.ThumbnailGenerator.SubSubFontIndex = _selectedSubSubFontIndex;
            _project.ThumbnailGenerator.MainFontSizeIndex = _mainFontSize;
            _project.ThumbnailGenerator.SubFontSizeIndex = _subFontSize;
            _project.ThumbnailGenerator.SubSubFontSizeIndex = _subSubFontSize;
            _project.ThumbnailGenerator.MainColorIndex = _selectedMainColorIndex;
            _project.ThumbnailGenerator.SubColorIndex = _selectedSubColorIndex;
            _project.ThumbnailGenerator.SubSubColorIndex = _selectedSubSubColorIndex;
            _project.ThumbnailGenerator.BgColorIndex = _selectedBgColorIndex;
            _project.ThumbnailGenerator.BgImagePath = _thumbnailBgPath;
        }

        private void GenerateThumbnailPreview()
        {
            // Silent preview update - no error messages
            if (string.IsNullOrWhiteSpace(_thumbnailMainText)) return;

            try
            {
                var pattern = _selectedThumbnailPatternIndex >= 0 && _selectedThumbnailPatternIndex < _thumbnailPatterns.Count
                    ? _thumbnailPatterns[_selectedThumbnailPatternIndex].Pattern
                    : ThumbnailPattern.PowerWord;

                var settings = new ThumbnailSettings
                {
                    Pattern = pattern,
                    MainText = _thumbnailMainText,
                    SubText = _thumbnailSubText,
                    SubSubText = _thumbnailSubSubText,
                    BackgroundImagePath = _thumbnailBgPath,
                    BackgroundColor = GetBgColor(),
                    MainTextColor = GetMainTextColor(),
                    SubTextColor = GetSubTextColor(),
                    SubSubTextColor = GetSubSubTextColor(),
                    StrokeColor = System.Drawing.Color.Black,
                    StrokeWidth = 8,
                    MainFontFamily = SelectedMainFontName,
                    SubFontFamily = SelectedSubFontName,
                    SubSubFontFamily = SelectedSubSubFontName,
                    MainFontSize = _mainFontSize,
                    SubFontSize = _subFontSize,
                    SubSubFontSize = _subSubFontSize
                };

                // Apply style preset if not custom
                if (_selectedStyleIndex > 0 && _selectedStyleIndex < _stylePresets.Count)
                {
                    var style = _stylePresets[_selectedStyleIndex].Style;
                    ThumbnailService.ApplyStylePreset(settings, style);
                }

                // Generate to temp folder
                var outputDir = Path.Combine(Path.GetTempPath(), "InsightCast", "Thumbnails");
                Directory.CreateDirectory(outputDir);
                var outputPath = Path.Combine(outputDir, $"preview_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");

                _lastThumbnailPath = _thumbnailService.GenerateThumbnail(settings, outputPath);
                LoadThumbnailPreview(_lastThumbnailPath);
            }
            catch
            {
                // Silent fail for auto-preview
            }
        }

        private void GenerateThumbnail()
        {
            if (string.IsNullOrWhiteSpace(_thumbnailMainText))
            {
                MessageBox.Show(LocalizationService.GetString("Thumbnail.Error.NoText"),
                    "Insight Training Studio", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var pattern = _selectedThumbnailPatternIndex >= 0 && _selectedThumbnailPatternIndex < _thumbnailPatterns.Count
                    ? _thumbnailPatterns[_selectedThumbnailPatternIndex].Pattern
                    : ThumbnailPattern.PowerWord;

                var settings = new ThumbnailSettings
                {
                    Pattern = pattern,
                    MainText = _thumbnailMainText,
                    SubText = _thumbnailSubText,
                    SubSubText = _thumbnailSubSubText,
                    BackgroundImagePath = _thumbnailBgPath,
                    BackgroundColor = GetBgColor(),
                    MainTextColor = GetMainTextColor(),
                    SubTextColor = GetSubTextColor(),
                    SubSubTextColor = GetSubSubTextColor(),
                    StrokeColor = System.Drawing.Color.Black,
                    StrokeWidth = 8,
                    MainFontFamily = SelectedMainFontName,
                    SubFontFamily = SelectedSubFontName,
                    SubSubFontFamily = SelectedSubSubFontName,
                    MainFontSize = _mainFontSize,
                    SubFontSize = _subFontSize,
                    SubSubFontSize = _subSubFontSize
                };

                // Apply style preset if not custom
                if (_selectedStyleIndex > 0 && _selectedStyleIndex < _stylePresets.Count)
                {
                    var style = _stylePresets[_selectedStyleIndex].Style;
                    ThumbnailService.ApplyStylePreset(settings, style);
                }

                // Generate to temp folder
                var outputDir = Path.Combine(Path.GetTempPath(), "InsightCast", "Thumbnails");
                Directory.CreateDirectory(outputDir);
                var outputPath = Path.Combine(outputDir, $"thumb_{DateTime.Now:yyyyMMdd_HHmmss}.png");

                _lastThumbnailPath = _thumbnailService.GenerateThumbnail(settings, outputPath);

                // Load preview
                LoadThumbnailPreview(_lastThumbnailPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{LocalizationService.GetString("Thumbnail.Error.Generate")}\n{ex.Message}",
                    "Insight Training Studio", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadThumbnailPreview(string path)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                ThumbnailPreviewImage = bitmap;
            }
            catch
            {
                ThumbnailPreviewImage = null;
            }
        }

        private void SaveThumbnail()
        {
            if (string.IsNullOrEmpty(_lastThumbnailPath) || !File.Exists(_lastThumbnailPath))
            {
                MessageBox.Show(LocalizationService.GetString("Thumbnail.Error.NoThumbnail"),
                    "Insight Training Studio", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "PNG Image|*.png|JPEG Image|*.jpg",
                DefaultExt = ".png",
                FileName = $"thumbnail_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    File.Copy(_lastThumbnailPath, dialog.FileName, overwrite: true);
                    MessageBox.Show(LocalizationService.GetString("Thumbnail.Saved"),
                        "Insight Training Studio", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"{LocalizationService.GetString("Thumbnail.Error.Save")}\n{ex.Message}",
                        "Insight Training Studio", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void OpenThumbnailFolder()
        {
            var folder = Path.Combine(Path.GetTempPath(), "InsightCast", "Thumbnails");
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SelectThumbnailBackground()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp|All Files|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                _thumbnailBgPath = dialog.FileName;
                OnPropertyChanged(nameof(ThumbnailBgFileName));
            }
        }

        /// <summary>外部から背景画像パスを設定する（AI 画像生成等）</summary>
        public void SetThumbnailBackground(string imagePath)
        {
            _thumbnailBgPath = imagePath;
            OnPropertyChanged(nameof(ThumbnailBgFileName));
        }

        private void ClearThumbnailBackground()
        {
            _thumbnailBgPath = null;
            OnPropertyChanged(nameof(ThumbnailBgFileName));
        }

        private void ClearThumbnail()
        {
            // Clear all text
            _thumbnailMainText = string.Empty;
            _thumbnailSubText = string.Empty;
            _thumbnailSubSubText = string.Empty;

            // Clear background image
            _thumbnailBgPath = null;

            // Reset colors to defaults (yellow, white, lightgray, black)
            _selectedMainColorIndex = 4; // Yellow
            _selectedSubColorIndex = 1;  // White
            _selectedSubSubColorIndex = 1; // White
            _selectedBgColorIndex = 0;   // Black

            // Reset fonts to default
            _selectedMainFontIndex = 0;
            _selectedSubFontIndex = 0;
            _selectedSubSubFontIndex = 0;

            // Reset font sizes to auto
            _mainFontSize = 0;
            _subFontSize = 0;
            _subSubFontSize = 0;

            // Reset style to custom
            _selectedStyleIndex = 0;

            // Clear preview
            _lastThumbnailPath = null;
            ThumbnailPreviewImage = null;

            // Notify all properties
            OnPropertyChanged(nameof(ThumbnailMainText));
            OnPropertyChanged(nameof(ThumbnailSubText));
            OnPropertyChanged(nameof(ThumbnailSubSubText));
            OnPropertyChanged(nameof(ThumbnailBgFileName));
            OnPropertyChanged(nameof(ThumbnailCharCountText));
            OnPropertyChanged(nameof(ThumbnailCharCountColor));
            OnPropertyChanged(nameof(SelectedMainColorIndex));
            OnPropertyChanged(nameof(SelectedSubColorIndex));
            OnPropertyChanged(nameof(SelectedSubSubColorIndex));
            OnPropertyChanged(nameof(SelectedBgColorIndex));
            OnPropertyChanged(nameof(SelectedMainColorBrush));
            OnPropertyChanged(nameof(SelectedSubColorBrush));
            OnPropertyChanged(nameof(SelectedSubSubColorBrush));
            OnPropertyChanged(nameof(SelectedBgColorBrush));
            OnPropertyChanged(nameof(SelectedMainFontIndex));
            OnPropertyChanged(nameof(SelectedSubFontIndex));
            OnPropertyChanged(nameof(SelectedSubSubFontIndex));
            OnPropertyChanged(nameof(SelectedMainFontName));
            OnPropertyChanged(nameof(SelectedSubFontName));
            OnPropertyChanged(nameof(SelectedSubSubFontName));
            OnPropertyChanged(nameof(MainFontSizeIndex));
            OnPropertyChanged(nameof(SubFontSizeIndex));
            OnPropertyChanged(nameof(SubSubFontSizeIndex));
            OnPropertyChanged(nameof(SelectedStyleIndex));
        }

        private void ApplyStylePreset(object? styleObj)
        {
            if (styleObj is not ThumbnailStyle style) return;

            _selectedStyleIndex = (int)style;
            OnPropertyChanged(nameof(SelectedStyleIndex));

            // Update text colors based on preset (indices match _colorPalette)
            switch (style)
            {
                case ThumbnailStyle.BusinessImpact:
                    SelectedMainColorIndex = 4; // Yellow
                    SelectedSubColorIndex = 1;  // White
                    SelectedSubSubColorIndex = 11; // LightGray
                    break;
                case ThumbnailStyle.TrustManual:
                    SelectedMainColorIndex = 1; // White
                    SelectedSubColorIndex = 16; // LightBlue
                    SelectedSubSubColorIndex = 11; // LightGray
                    break;
                case ThumbnailStyle.ShockReveal:
                    SelectedMainColorIndex = 2; // Red
                    SelectedSubColorIndex = 1;  // White
                    SelectedSubSubColorIndex = 11; // LightGray
                    break;
                case ThumbnailStyle.Elegant:
                    SelectedMainColorIndex = 1; // White
                    SelectedSubColorIndex = 18; // LightPurple
                    SelectedSubSubColorIndex = 11; // LightGray
                    break;
                case ThumbnailStyle.Pop:
                    SelectedMainColorIndex = 9; // HotPink
                    SelectedSubColorIndex = 4;  // Yellow
                    SelectedSubSubColorIndex = 19; // LightPink
                    break;
            }

            // Auto-generate thumbnail with new style
            GenerateThumbnail();
        }

        public int SelectedStyleIndex
        {
            get => _selectedStyleIndex;
            set
            {
                _selectedStyleIndex = value;
                OnPropertyChanged();
            }
        }

        #endregion

        #region JSON Methods

        private class SceneJsonModel
        {
            [JsonPropertyName("title")]
            public string Title { get; set; } = string.Empty;

            [JsonPropertyName("script")]
            public string Script { get; set; } = string.Empty;

            [JsonPropertyName("duration")]
            public double Duration { get; set; } = 5.0;
        }

        private string SerializeScenesToJson()
        {
            var sceneModels = _project.Scenes.Select(s => new SceneJsonModel
            {
                Title = s.Title ?? string.Empty,
                Script = s.NarrationText ?? string.Empty,
                Duration = s.FixedSeconds
            }).ToList();

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            return JsonSerializer.Serialize(sceneModels, options);
        }

        private void ApplyJsonToScenes()
        {
            if (string.IsNullOrWhiteSpace(_scenesJson))
            {
                MessageBox.Show(LocalizationService.GetString("Planning.JsonEditor.Empty"),
                    "Insight Training Studio", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var sceneModels = JsonSerializer.Deserialize<List<SceneJsonModel>>(_scenesJson);
                if (sceneModels == null || sceneModels.Count == 0)
                {
                    MessageBox.Show(LocalizationService.GetString("Planning.JsonEditor.InvalidFormat"),
                        "Insight Training Studio", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Clear existing scenes (keep at least one)
                while (_project.Scenes.Count > 1)
                {
                    _project.RemoveScene(_project.Scenes.Count - 1);
                }

                // Add scenes from JSON
                for (int i = 0; i < sceneModels.Count; i++)
                {
                    var model = sceneModels[i];

                    if (i == 0)
                    {
                        // Update first scene
                        var scene = _project.Scenes[0];
                        scene.Title = model.Title;
                        scene.NarrationText = model.Script;
                        scene.FixedSeconds = model.Duration > 0 ? model.Duration : 5.0;
                    }
                    else
                    {
                        // Add new scenes
                        _project.AddScene();
                        var scene = _project.Scenes[i];
                        scene.Title = model.Title;
                        scene.NarrationText = model.Script;
                        scene.FixedSeconds = model.Duration > 0 ? model.Duration : 5.0;
                    }
                }

                RefreshSceneList();
                if (PlanningScenes.Count > 0)
                    SelectedPlanningSceneIndex = 0;

                ScenesChanged?.Invoke();

                // Exit JSON mode
                IsJsonMode = false;

                MessageBox.Show(
                    string.Format(LocalizationService.GetString("Planning.JsonEditor.Applied"), sceneModels.Count),
                    "Insight Training Studio", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (JsonException ex)
            {
                MessageBox.Show(
                    $"{LocalizationService.GetString("Planning.JsonEditor.ParseError")}\n\n{ex.Message}",
                    "Insight Training Studio", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        #endregion
    }
}

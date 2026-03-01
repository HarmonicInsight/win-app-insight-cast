using System.Windows.Media;
using InsightCast.Infrastructure;
using InsightCast.Models;
using InsightCast.Services;

namespace InsightCast.ViewModels
{
    public class SceneListItem : ViewModelBase
    {
        private string _label = string.Empty;
        private bool _stepMediaDone;
        private bool _stepNarrationDone;
        private bool _stepSubtitleDone;
        private int _completedSteps;
        private Brush _progressBrush = Brushes.Gray;

        public Scene Scene { get; }

        public string Label
        {
            get => _label;
            set => SetProperty(ref _label, value);
        }

        public bool StepMediaDone
        {
            get => _stepMediaDone;
            private set => SetProperty(ref _stepMediaDone, value);
        }

        public bool StepNarrationDone
        {
            get => _stepNarrationDone;
            private set => SetProperty(ref _stepNarrationDone, value);
        }

        public bool StepSubtitleDone
        {
            get => _stepSubtitleDone;
            private set => SetProperty(ref _stepSubtitleDone, value);
        }

        public int CompletedSteps
        {
            get => _completedSteps;
            private set => SetProperty(ref _completedSteps, value);
        }

        public Brush ProgressBrush
        {
            get => _progressBrush;
            private set => SetProperty(ref _progressBrush, value);
        }

        public SceneListItem(Scene scene, int index)
        {
            Scene = scene;
            UpdateLabel(index);
            RefreshProgress();
        }

        public void UpdateLabel(int index)
        {
            var label = LocalizationService.GetString("Scene.Label", index + 1);
            if (!string.IsNullOrEmpty(Scene.NarrationText))
            {
                var preview = Scene.NarrationText.Length > 12
                    ? Scene.NarrationText[..12] + "..."
                    : Scene.NarrationText;
                label += $" - {preview}";
            }
            Label = label;
        }

        public void RefreshProgress()
        {
            StepMediaDone = Scene.HasMedia;
            StepNarrationDone = Scene.HasNarration;
            StepSubtitleDone = Scene.HasSubtitle;

            var count = 0;
            if (StepMediaDone) count++;
            if (StepNarrationDone) count++;
            if (StepSubtitleDone) count++;
            CompletedSteps = count;

            ProgressBrush = count switch
            {
                3 => FindBrush("Success"),
                > 0 => FindBrush("Warning"),
                _ => FindBrush("TextTertiary")
            };
        }

        private static Brush FindBrush(string key)
        {
            if (System.Windows.Application.Current?.TryFindResource(key) is Brush brush)
                return brush;
            return Brushes.Gray;
        }
    }
}

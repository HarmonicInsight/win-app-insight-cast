using System.Collections.ObjectModel;
using System.Windows.Media;
using InsightCast.Infrastructure;

namespace InsightCast.Models
{
    public class WorkingFolderTreeNode : ViewModelBase
    {
        public string Name { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public bool IsFolder { get; set; }
        public WorkingFolderFileType FileType { get; set; }
        public WorkingFolderTreeNode? Parent { get; set; }

        private bool? _isChecked = true;
        private bool _isVisible = true;
        private bool _suppressPropagation;

        public bool? IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value) return;
                _isChecked = value;
                OnPropertyChanged();

                if (_suppressPropagation) return;

                if (IsFolder && value.HasValue)
                {
                    foreach (var child in Children)
                    {
                        child._suppressPropagation = true;
                        child.IsChecked = value.Value;
                        child._suppressPropagation = false;
                        if (child.IsFolder)
                            child.PropagateToChildren(value.Value);
                    }
                }

                Parent?.UpdateFromChildren();
            }
        }

        public bool IsVisible
        {
            get => _isVisible;
            set => SetProperty(ref _isVisible, value);
        }

        public ObservableCollection<WorkingFolderTreeNode> Children { get; set; } = new();

        public string Icon => IsFolder
            ? "\uE8B7"
            : FileType switch
            {
                WorkingFolderFileType.Pdf => "\uEA90",
                WorkingFolderFileType.Word => "\uE8A5",
                WorkingFolderFileType.Excel => "\uE9F9",
                WorkingFolderFileType.PowerPoint => "\uE8A5",
                WorkingFolderFileType.Image => "\uEB9F",
                _ => "\uE8A5"
            };

        public Brush IconBrush => IsFolder
            ? new SolidColorBrush(Color.FromRgb(0xB8, 0x86, 0x0B))
            : FileType switch
            {
                WorkingFolderFileType.Pdf => new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)),
                WorkingFolderFileType.Word => new SolidColorBrush(Color.FromRgb(0x2B, 0x57, 0x9A)),
                WorkingFolderFileType.Excel => new SolidColorBrush(Color.FromRgb(0x21, 0x73, 0x46)),
                WorkingFolderFileType.PowerPoint => new SolidColorBrush(Color.FromRgb(0xD2, 0x4B, 0x27)),
                WorkingFolderFileType.Image => new SolidColorBrush(Color.FromRgb(0x68, 0x68, 0x68)),
                _ => new SolidColorBrush(Color.FromRgb(0x68, 0x68, 0x68))
            };

        private void PropagateToChildren(bool value)
        {
            foreach (var child in Children)
            {
                child._suppressPropagation = true;
                child.IsChecked = value;
                child._suppressPropagation = false;
                if (child.IsFolder)
                    child.PropagateToChildren(value);
            }
        }

        private void UpdateFromChildren()
        {
            if (Children.Count == 0) return;

            var allChecked = true;
            var allUnchecked = true;
            foreach (var c in Children)
            {
                if (c.IsChecked != true) allChecked = false;
                if (c.IsChecked != false) allUnchecked = false;
            }

            _suppressPropagation = true;
            IsChecked = allChecked ? true : allUnchecked ? false : null;
            _suppressPropagation = false;

            Parent?.UpdateFromChildren();
        }
    }
}

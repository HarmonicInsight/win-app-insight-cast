using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using InsightCast.Infrastructure;
using InsightCast.Models;
using InsightCast.Services;

namespace InsightCast.ViewModels
{
    public class WorkingFolderViewModel : ViewModelBase
    {
        private static readonly JsonSerializerOptions s_jsonOptions = new() { PropertyNameCaseInsensitive = true };

        public ObservableCollection<WorkingFolderTreeNode> RootNodes { get; } = new();

        private string? _workingFolderRoot;
        private readonly Dictionary<string, ReferenceMaterial> _parseCache = new();

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        private string _statusText = "";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public bool HasCheckedMaterials => GetCheckedFileNodes().Count > 0;

        public string MaterialCountText
        {
            get
            {
                var count = CountAllFileNodes(RootNodes);
                return count > 0 ? $"{count} files" : "No files";
            }
        }

        public WorkingFolderViewModel()
        {
            RootNodes.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(MaterialCountText));
                OnPropertyChanged(nameof(HasCheckedMaterials));
            };
        }

        public void NotifyCheckedChanged()
        {
            OnPropertyChanged(nameof(HasCheckedMaterials));
            OnPropertyChanged(nameof(MaterialCountText));
        }

        // ── Project lifecycle ──────────────────────────

        public void LoadFromFolder(string folderPath)
        {
            _workingFolderRoot = folderPath;
            _parseCache.Clear();
            RootNodes.Clear();

            if (!Directory.Exists(folderPath))
                Directory.CreateDirectory(folderPath);

            BuildTreeFromDirectory(folderPath, RootNodes, null);
            RestoreCheckedState();

            OnPropertyChanged(nameof(MaterialCountText));
            OnPropertyChanged(nameof(HasCheckedMaterials));
        }

        public void Unload()
        {
            RootNodes.Clear();
            _workingFolderRoot = null;
            _parseCache.Clear();

            OnPropertyChanged(nameof(MaterialCountText));
            OnPropertyChanged(nameof(HasCheckedMaterials));
        }

        // ── Checked state persistence ──────────────────

        public void SaveCheckedState(string projectDir)
        {
            if (_workingFolderRoot == null) return;

            var checkedPaths = new List<string>();
            CollectCheckedPaths(RootNodes, checkedPaths);

            var json = JsonSerializer.Serialize(new CheckedStateData { CheckedPaths = checkedPaths });
            var checkedFile = Path.Combine(projectDir, "working_folder_checked.json");
            File.WriteAllText(checkedFile, json);
        }

        private void RestoreCheckedState()
        {
            if (_workingFolderRoot == null) return;

            var parentDir = Path.GetDirectoryName(_workingFolderRoot);
            if (parentDir == null) return;

            var checkedFile = Path.Combine(parentDir, "working_folder_checked.json");
            if (!File.Exists(checkedFile)) return;

            try
            {
                var json = File.ReadAllText(checkedFile);
                var data = JsonSerializer.Deserialize<CheckedStateData>(json, s_jsonOptions);
                if (data?.CheckedPaths == null) return;

                var checkedSet = new HashSet<string>(data.CheckedPaths, StringComparer.OrdinalIgnoreCase);
                ApplyCheckedState(RootNodes, checkedSet);
            }
            catch
            {
                // Ignore errors - keep all checked
            }
        }

        private static void ApplyCheckedState(ObservableCollection<WorkingFolderTreeNode> nodes, HashSet<string> checkedSet)
        {
            foreach (var node in nodes)
            {
                if (node.IsFolder)
                    ApplyCheckedState(node.Children, checkedSet);
                else
                    node.IsChecked = checkedSet.Contains(node.RelativePath);
            }
        }

        private static void CollectCheckedPaths(ObservableCollection<WorkingFolderTreeNode> nodes, List<string> result)
        {
            foreach (var node in nodes)
            {
                if (node.IsFolder)
                    CollectCheckedPaths(node.Children, result);
                else if (node.IsChecked == true)
                    result.Add(node.RelativePath);
            }
        }

        // ── Tree building ──────────────────────────────

        private void BuildTreeFromDirectory(string dirPath, ObservableCollection<WorkingFolderTreeNode> target, WorkingFolderTreeNode? parent)
        {
            foreach (var subDir in Directory.GetDirectories(dirPath).OrderBy(d => d))
            {
                var dirName = Path.GetFileName(subDir);
                var folderNode = new WorkingFolderTreeNode
                {
                    Name = dirName,
                    RelativePath = GetRelativePath(subDir),
                    FullPath = subDir,
                    IsFolder = true,
                    Parent = parent,
                };
                BuildTreeFromDirectory(subDir, folderNode.Children, folderNode);
                target.Add(folderNode);
            }

            foreach (var filePath in Directory.GetFiles(dirPath).OrderBy(f => f))
            {
                if (!WorkingFolderService.IsSupportedFile(filePath)) continue;

                var fileNode = new WorkingFolderTreeNode
                {
                    Name = Path.GetFileName(filePath),
                    RelativePath = GetRelativePath(filePath),
                    FullPath = filePath,
                    IsFolder = false,
                    FileType = WorkingFolderService.GetFileType(filePath),
                    Parent = parent,
                };
                target.Add(fileNode);
            }
        }

        private string GetRelativePath(string fullPath)
        {
            if (_workingFolderRoot == null) return fullPath;
            return Path.GetRelativePath(_workingFolderRoot, fullPath).Replace('\\', '/');
        }

        // ── File/folder operations ─────────────────────

        public WorkingFolderTreeNode? CreateFolder(WorkingFolderTreeNode? parentNode, string name)
        {
            if (_workingFolderRoot == null) return null;

            var parentDir = parentNode?.FullPath ?? _workingFolderRoot;
            var newDir = Path.Combine(parentDir, name);

            if (Directory.Exists(newDir))
            {
                StatusText = LocalizationService.GetString("WorkFolder.AlreadyExists");
                return null;
            }

            Directory.CreateDirectory(newDir);

            var node = new WorkingFolderTreeNode
            {
                Name = name,
                RelativePath = GetRelativePath(newDir),
                FullPath = newDir,
                IsFolder = true,
                Parent = parentNode,
            };

            var target = parentNode?.Children ?? RootNodes;
            target.Add(node);

            OnPropertyChanged(nameof(MaterialCountText));
            return node;
        }

        public async Task AddFilesAsync(WorkingFolderTreeNode? parentNode, IEnumerable<string> filePaths)
        {
            if (_workingFolderRoot == null)
            {
                EnsureWorkingFolder();
            }
            if (_workingFolderRoot == null) return;

            var parentDir = parentNode?.FullPath ?? _workingFolderRoot;
            var target = parentNode?.Children ?? RootNodes;

            foreach (var srcPath in filePaths)
            {
                if (!WorkingFolderService.IsSupportedFile(srcPath)) continue;

                var fileName = Path.GetFileName(srcPath);
                var destPath = Path.Combine(parentDir, fileName);

                if (File.Exists(destPath))
                {
                    var baseName = Path.GetFileNameWithoutExtension(fileName);
                    var ext = Path.GetExtension(fileName);
                    var counter = 1;
                    while (File.Exists(destPath))
                    {
                        destPath = Path.Combine(parentDir, $"{baseName} ({counter}){ext}");
                        counter++;
                    }
                    fileName = Path.GetFileName(destPath);
                }

                IsLoading = true;
                StatusText = LocalizationService.GetString("WorkFolder.Loading", fileName);
                try
                {
                    await Task.Run(() => File.Copy(srcPath, destPath));

                    var node = new WorkingFolderTreeNode
                    {
                        Name = fileName,
                        RelativePath = GetRelativePath(destPath),
                        FullPath = destPath,
                        IsFolder = false,
                        FileType = WorkingFolderService.GetFileType(destPath),
                        Parent = parentNode,
                    };
                    target.Add(node);
                    StatusText = LocalizationService.GetString("WorkFolder.Added", fileName);
                }
                catch (Exception ex)
                {
                    StatusText = LocalizationService.GetString("Common.ErrorWithMessage", ex.Message);
                }
                finally
                {
                    IsLoading = false;
                }
            }

            OnPropertyChanged(nameof(MaterialCountText));
            OnPropertyChanged(nameof(HasCheckedMaterials));
        }

        public async Task AddFolderAsync(WorkingFolderTreeNode? parentNode, string sourceFolderPath)
        {
            if (_workingFolderRoot == null) return;

            var folderName = Path.GetFileName(sourceFolderPath);
            var parentDir = parentNode?.FullPath ?? _workingFolderRoot;
            var destDir = Path.Combine(parentDir, folderName);

            if (Directory.Exists(destDir))
            {
                var baseName = folderName;
                var counter = 1;
                while (Directory.Exists(destDir))
                {
                    destDir = Path.Combine(parentDir, $"{baseName} ({counter})");
                    counter++;
                }
                folderName = Path.GetFileName(destDir);
            }

            Directory.CreateDirectory(destDir);

            var folderNode = new WorkingFolderTreeNode
            {
                Name = folderName,
                RelativePath = GetRelativePath(destDir),
                FullPath = destDir,
                IsFolder = true,
                Parent = parentNode,
            };

            var target = parentNode?.Children ?? RootNodes;
            target.Add(folderNode);

            foreach (var subDir in Directory.GetDirectories(sourceFolderPath).OrderBy(d => d))
            {
                await AddFolderAsync(folderNode, subDir);
            }

            var files = Directory.GetFiles(sourceFolderPath)
                .Where(WorkingFolderService.IsSupportedFile)
                .OrderBy(f => f);

            if (files.Any())
                await AddFilesAsync(folderNode, files);

            OnPropertyChanged(nameof(MaterialCountText));
            OnPropertyChanged(nameof(HasCheckedMaterials));
        }

        public void DeleteNode(WorkingFolderTreeNode node)
        {
            try
            {
                if (node.IsFolder)
                    Directory.Delete(node.FullPath, true);
                else
                    File.Delete(node.FullPath);

                RemoveFromCache(node);

                var target = node.Parent?.Children ?? RootNodes;
                target.Remove(node);

                StatusText = LocalizationService.GetString("WorkFolder.Removed", node.Name);
                OnPropertyChanged(nameof(MaterialCountText));
                OnPropertyChanged(nameof(HasCheckedMaterials));
            }
            catch (Exception ex)
            {
                StatusText = LocalizationService.GetString("Common.ErrorWithMessage", ex.Message);
            }
        }

        public void RenameNode(WorkingFolderTreeNode node, string newName)
        {
            if (_workingFolderRoot == null) return;

            var parentDir = Path.GetDirectoryName(node.FullPath)!;
            var newPath = Path.Combine(parentDir, newName);

            try
            {
                if (node.IsFolder)
                    Directory.Move(node.FullPath, newPath);
                else
                    File.Move(node.FullPath, newPath);

                if (!node.IsFolder)
                    _parseCache.Remove(node.FullPath);

                node.Name = newName;
                node.FullPath = newPath;
                node.RelativePath = GetRelativePath(newPath);

                if (node.IsFolder)
                    UpdateChildPaths(node);
            }
            catch (Exception ex)
            {
                StatusText = LocalizationService.GetString("Common.ErrorWithMessage", ex.Message);
            }
        }

        // ── Filter ─────────────────────────────────────

        public void ApplyFilter(string filterText)
        {
            if (string.IsNullOrWhiteSpace(filterText))
            {
                SetAllVisible(RootNodes, true);
                return;
            }

            ApplyFilterRecursive(RootNodes, filterText);
        }

        private static bool ApplyFilterRecursive(ObservableCollection<WorkingFolderTreeNode> nodes, string filter)
        {
            bool anyVisible = false;
            foreach (var node in nodes)
            {
                if (node.IsFolder)
                {
                    var childVisible = ApplyFilterRecursive(node.Children, filter);
                    var nameMatch = node.Name.Contains(filter, StringComparison.OrdinalIgnoreCase);
                    node.IsVisible = childVisible || nameMatch;
                }
                else
                {
                    node.IsVisible = node.Name.Contains(filter, StringComparison.OrdinalIgnoreCase);
                }
                if (node.IsVisible) anyVisible = true;
            }
            return anyVisible;
        }

        private static void SetAllVisible(ObservableCollection<WorkingFolderTreeNode> nodes, bool visible)
        {
            foreach (var node in nodes)
            {
                node.IsVisible = visible;
                if (node.IsFolder)
                    SetAllVisible(node.Children, visible);
            }
        }

        // ── AI context ─────────────────────────────────

        public List<WorkingFolderTreeNode> GetCheckedFileNodes()
        {
            var result = new List<WorkingFolderTreeNode>();
            CollectCheckedFileNodes(RootNodes, result);
            return result;
        }

        private static void CollectCheckedFileNodes(ObservableCollection<WorkingFolderTreeNode> nodes, List<WorkingFolderTreeNode> result)
        {
            foreach (var node in nodes)
            {
                if (node.IsFolder)
                    CollectCheckedFileNodes(node.Children, result);
                else if (node.IsChecked == true)
                    result.Add(node);
            }
        }

        public string BuildReferenceContext()
        {
            var checkedNodes = GetCheckedFileNodes();
            if (checkedNodes.Count == 0) return "";

            var materials = new List<ReferenceMaterial>();
            foreach (var node in checkedNodes)
            {
                // Skip image files for text context
                if (node.FileType == WorkingFolderFileType.Image) continue;

                if (!_parseCache.TryGetValue(node.FullPath, out var material))
                {
                    try
                    {
                        material = WorkingFolderService.ParseFile(node.FullPath);
                        _parseCache[node.FullPath] = material;
                    }
                    catch
                    {
                        continue;
                    }
                }
                materials.Add(material);
            }

            return WorkingFolderService.BuildReferenceContext(materials);
        }

        // ── Helpers ────────────────────────────────────

        private void EnsureWorkingFolder()
        {
            if (_workingFolderRoot != null) return;
            var tempDir = Path.Combine(Path.GetTempPath(), "InsightCast", "working_folder");
            Directory.CreateDirectory(tempDir);
            _workingFolderRoot = tempDir;
        }

        private static int CountAllFileNodes(ObservableCollection<WorkingFolderTreeNode> nodes)
        {
            var count = 0;
            foreach (var node in nodes)
            {
                if (node.IsFolder)
                    count += CountAllFileNodes(node.Children);
                else
                    count++;
            }
            return count;
        }

        private void UpdateChildPaths(WorkingFolderTreeNode parentNode)
        {
            foreach (var child in parentNode.Children)
            {
                var newFullPath = Path.Combine(parentNode.FullPath, child.Name);
                _parseCache.Remove(child.FullPath);
                child.FullPath = newFullPath;
                child.RelativePath = GetRelativePath(newFullPath);
                if (child.IsFolder)
                    UpdateChildPaths(child);
            }
        }

        private void RemoveFromCache(WorkingFolderTreeNode node)
        {
            if (node.IsFolder)
            {
                foreach (var child in node.Children)
                    RemoveFromCache(child);
            }
            else
            {
                _parseCache.Remove(node.FullPath);
            }
        }

        private sealed class CheckedStateData
        {
            public List<string> CheckedPaths { get; set; } = new();
        }
    }
}

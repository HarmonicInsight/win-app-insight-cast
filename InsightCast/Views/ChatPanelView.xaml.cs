using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using InsightCast.ViewModels;

namespace InsightCast.Views;

public partial class ChatPanelView : UserControl
{
    public ChatPanelView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ChatPanelViewModel oldVm)
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        if (e.NewValue is ChatPanelViewModel newVm)
            newVm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatPanelViewModel.LastGeneratedThumbnailPath))
            UpdateThumbnailPreview();
    }

    private void UpdateThumbnailPreview()
    {
        if (DataContext is not ChatPanelViewModel vm) return;
        var path = vm.LastGeneratedThumbnailPath;

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            ThumbnailPreview.Source = null;
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new System.Uri(path, System.UriKind.Absolute);
            bitmap.DecodePixelWidth = 640;
            bitmap.EndInit();
            bitmap.Freeze();
            ThumbnailPreview.Source = bitmap;
        }
        catch
        {
            ThumbnailPreview.Source = null;
        }
    }

    /// <summary>
    /// PasswordBox doesn't support binding, so sync manually.
    /// </summary>
    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ChatPanelViewModel vm && sender is PasswordBox pb)
            vm.ApiKeyInput = pb.Password;
    }

    private void OpenAIApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ChatPanelViewModel vm && sender is PasswordBox pb)
            vm.OpenAIApiKeyInput = pb.Password;
    }

    /// <summary>
    /// Ctrl+Enter to execute prompt.
    /// </summary>
    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            if (DataContext is ChatPanelViewModel vm && vm.ExecutePromptCommand.CanExecute(null))
            {
                vm.ExecutePromptCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    /// <summary>
    /// Open HelpWindow to the AI Assistant section.
    /// </summary>
    private void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var helpWindow = new HelpWindow("ai-assistant")
            {
                Owner = Window.GetWindow(this)
            };
            helpWindow.ShowDialog();
        }
        catch
        {
            // Fallback if HelpWindow fails to open
        }
    }

    /// <summary>
    /// Pop out AI assistant into a separate window.
    /// </summary>
    private void PopOutButton_Click(object sender, RoutedEventArgs e)
    {
        PopOutRequested?.Invoke();
    }

    /// <summary>
    /// Close the chat panel.
    /// </summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke();
    }

    /// <summary>
    /// Event raised when the user clicks the pop-out button.
    /// Handled by MainWindow to open AiAssistantWindow.
    /// </summary>
    public event System.Action? PopOutRequested;

    /// <summary>
    /// Event raised when the user clicks the close button.
    /// Handled by MainWindow to close the chat panel.
    /// </summary>
    public event System.Action? CloseRequested;


    // ── Mode Toggle Handlers ──

    private void TextModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ChatPanelViewModel vm)
            vm.IsImageMode = false;
    }

    private void ImageModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ChatPanelViewModel vm)
            vm.IsImageMode = true;
    }

    // ── Context Menu Handlers for Preset Chips ──

    private void PresetDuplicate_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ChatPanelViewModel vm &&
            sender is MenuItem mi &&
            GetPresetFromMenuItem(mi) is PresetPromptVm preset)
        {
            vm.DuplicatePresetCommand.Execute(preset);
        }
    }

    // ── Context Menu Handlers for User Prompt Chips ──

    private void UserPromptEdit_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ChatPanelViewModel vm &&
            GetUserPromptFromMenuItem(sender) is UserPromptVm item)
        {
            vm.EditPromptCommand.Execute(item);
        }
    }

    private void UserPromptDuplicate_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ChatPanelViewModel vm &&
            GetUserPromptFromMenuItem(sender) is UserPromptVm item && item.Source != null)
        {
            // Pre-fill save panel with the user prompt data
            vm.AiInput = item.Source.Prompt;
            vm.SaveAsUserPromptCommand.Execute(null);
        }
    }

    private void UserPromptDelete_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ChatPanelViewModel vm &&
            GetUserPromptFromMenuItem(sender) is UserPromptVm item)
        {
            var title = Application.Current.FindResource("PromptLib.Delete.Confirm") as string ?? "Delete?";
            var warning = Application.Current.FindResource("PromptLib.Delete.Warning") as string ?? "";
            var msg = string.IsNullOrEmpty(warning) ? title : $"{title}\n\n{warning}";
            var result = MessageBox.Show(msg, title, MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                vm.DeletePromptCommand.Execute(item);
            }
        }
    }

    private static PresetPromptVm? GetPresetFromMenuItem(MenuItem mi)
    {
        if (mi.Parent is ContextMenu cm && cm.PlacementTarget is FrameworkElement fe)
            return fe.DataContext as PresetPromptVm;
        return null;
    }

    private static UserPromptVm? GetUserPromptFromMenuItem(object sender)
    {
        if (sender is MenuItem mi && mi.Parent is ContextMenu cm)
            return cm.DataContext as UserPromptVm;
        return null;
    }
}

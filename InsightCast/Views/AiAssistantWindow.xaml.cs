using System;
using System.ComponentModel;
using System.Windows;
using InsightCast.ViewModels;

namespace InsightCast.Views;

public partial class AiAssistantWindow : Window
{
    public AiAssistantWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Share the DataContext from the main ChatPanelView.
    /// </summary>
    public void SetViewModel(ChatPanelViewModel vm)
    {
        InnerChatPanel.DataContext = vm;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (DataContext is ChatPanelViewModel vm)
        {
            vm.IsPoppedOut = false;
        }
    }
}

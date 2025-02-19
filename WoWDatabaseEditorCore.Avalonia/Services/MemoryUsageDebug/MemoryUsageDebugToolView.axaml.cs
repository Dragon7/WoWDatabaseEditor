using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace WoWDatabaseEditorCore.Avalonia.Services.MemoryUsageDebug;

public class MemoryUsageDebugToolView : UserControl
{
    public MemoryUsageDebugToolView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
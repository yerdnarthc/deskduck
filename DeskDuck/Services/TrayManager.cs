using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using DeskDuck.ViewModels;

namespace DeskDuck.Services;

/// <summary>
/// System-tray presence: icon, status tooltip, context menu. The main window
/// owns actual hiding/showing; this class owns everything that lives in the
/// notification area. Dispose removes the icon — otherwise a ghost lingers
/// in the tray until hovered.
/// </summary>
public sealed class TrayManager : IDisposable
{
    private readonly MainWindow _main;
    private readonly MainViewModel _vm;
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _enabledItem;
    private bool _balloonShown;
    private bool _disposed;

    public TrayManager(MainWindow main, MainViewModel vm)
    {
        _main = main;
        _vm = vm;
        _tray = new NotifyIcon
        {
            Icon = ExeIcon(),
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => _main.ShowFromTray();
        _enabledItem = new ToolStripMenuItem("Enabled", null, (_, _) => _vm.Enabled = _enabledItem.Checked)
        {
            CheckOnClick = true,
            Checked = vm.Enabled,
        };
        _tray.ContextMenuStrip = BuildMenu();
        _vm.PropertyChanged += OnVmChanged;
        UpdateTooltip();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Open DeskDuck", null, (_, _) => _main.ShowFromTray()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_enabledItem);
        menu.Items.Add(new ToolStripMenuItem("Test Duck", null, (_, _) => _vm.TestDuckCommand.Execute(null)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Sessions", null, (_, _) => _main.ShowSessions()));
        menu.Items.Add(new ToolStripMenuItem("Log", null, (_, _) => _main.ShowLog()));
        menu.Items.Add(new ToolStripMenuItem("Help", null, (_, _) => _main.ShowHelp()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit DeskDuck", null, (_, _) => _main.RequestRealExit()));
        return menu;
    }

    /// <summary>One-time balloon the first time the window hides to tray.</summary>
    public void NotifyHiddenToTray()
    {
        if (_balloonShown || _disposed)
            return;
        _balloonShown = true;
        _tray.ShowBalloonTip(3000, "DeskDuck keeps running",
            "Right-click the tray icon to bring DeskDuck back or exit.", ToolTipIcon.Info);
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.StatusText) ||
            e.PropertyName == nameof(MainViewModel.Reason))
        {
            UpdateTooltip();
        }
        else if (e.PropertyName == nameof(MainViewModel.Enabled))
        {
            _enabledItem.Checked = _vm.Enabled;
        }
    }

    private void UpdateTooltip()
    {
        // Win32 tooltips cap at 63 chars — truncate, never throw.
        string text = "DeskDuck — " + _vm.StatusText;
        if (!string.IsNullOrWhiteSpace(_vm.Reason))
            text += " — " + _vm.Reason;
        _tray.Text = text.Length <= 63 ? text : text[..63];
    }

    /// <summary>Exe's own icon (app.ico), so tray matches the taskbar.</summary>
    private static Icon ExeIcon()
    {
        try
        {
            string? path = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(path))
            {
                var icon = Icon.ExtractAssociatedIcon(path);
                if (icon is not null)
                    return icon;
            }
        }
        catch
        {
            // Fall through to the fallback.
        }
        return SystemIcons.Application;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _vm.PropertyChanged -= OnVmChanged;
        _tray.ContextMenuStrip?.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
    }
}

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
        _enabledItem = new ToolStripMenuItem("Enabled", null, OnEnabledClicked)
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
        var menu = new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            BackColor = DarkColors.Background,
            ForeColor = DarkColors.Text,
        };
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
        foreach (ToolStripItem item in menu.Items)
            item.ForeColor = DarkColors.Text;
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

    private void OnEnabledClicked(object? sender, EventArgs e)
    {
        if (sender is ToolStripMenuItem item)
            _vm.Enabled = item.Checked;
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

    /// <summary>App palette as WinForms colors (mirrors Styles.xaml tokens).</summary>
    private static class DarkColors
    {
        public static readonly Color Background = Color.FromArgb(0x14, 0x14, 0x13); // #141413
        public static readonly Color Text = Color.FromArgb(0xDF, 0xDC, 0xDC);       // #dfdcdc
        // Hover/pressed: the card surface itself — a lighter gray straight
        // from the palette, matching the in-app cards.
        public static readonly Color Selection = Color.FromArgb(0x44, 0x3F, 0x3B); // #443f3b
        // Cream 18% over background — same role as BorderBrush in-app.
        public static readonly Color Border = Color.FromArgb(0x38, 0x33, 0x2C);
    }

    private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable()) { }

        // Flat fills everywhere: no gradient code path left that could leak
        // the default light-cyan selection rendering.
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item.Selected)
            {
                using var brush = new System.Drawing.SolidBrush(DarkColors.Selection);
                e.Graphics.FillRectangle(brush, new System.Drawing.Rectangle(System.Drawing.Point.Empty, e.Item.Size));
            }
            else
            {
                base.OnRenderMenuItemBackground(e);
            }
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            using var brush = new System.Drawing.SolidBrush(DarkColors.Background);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new System.Drawing.Pen(DarkColors.Border);
            int y = e.Item.Height / 2;
            e.Graphics.DrawLine(pen, e.Item.ContentRectangle.Left, y, e.Item.ContentRectangle.Right, y);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var pen = new System.Drawing.Pen(DarkColors.Border);
            e.Graphics.DrawRectangle(pen, new System.Drawing.Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1));
        }

        // Hand-drawn check (font-independent): the default glyph color is
        // unreadable on a dark check field.
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = e.ImageRectangle;
            using var fill = new System.Drawing.SolidBrush(DarkColors.Selection);
            e.Graphics.FillRectangle(fill, e.ImageRectangle);
            r.Inflate(-4, -4);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var pen = new System.Drawing.Pen(DarkColors.Text, 2f);
            e.Graphics.DrawLines(pen, new System.Drawing.Point[]
            {
                new(r.Left, r.Top + r.Height / 2),
                new(r.Left + r.Width / 2 - 1, r.Bottom - 1),
                new(r.Right, r.Top + 1),
            });
        }
    }

    private sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => DarkColors.Background;
        public override Color MenuBorder => DarkColors.Border;
        public override Color MenuItemBorder => DarkColors.Border;
        public override Color MenuItemSelected => DarkColors.Selection;
        public override Color MenuItemSelectedGradientBegin => DarkColors.Selection;
        public override Color MenuItemSelectedGradientEnd => DarkColors.Selection;
        public override Color MenuItemPressedGradientBegin => DarkColors.Selection;
        public override Color MenuItemPressedGradientMiddle => DarkColors.Selection;
        public override Color MenuItemPressedGradientEnd => DarkColors.Selection;
        public override Color CheckBackground => DarkColors.Selection;
        public override Color CheckSelectedBackground => DarkColors.Selection;
        public override Color CheckPressedBackground => DarkColors.Selection;
        public override Color ImageMarginGradientBegin => DarkColors.Background;
        public override Color ImageMarginGradientMiddle => DarkColors.Background;
        public override Color ImageMarginGradientEnd => DarkColors.Background;
        public override Color SeparatorDark => DarkColors.Border;
        public override Color SeparatorLight => DarkColors.Border;
    }
}

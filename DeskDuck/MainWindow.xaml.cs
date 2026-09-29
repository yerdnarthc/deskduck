using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using DeskDuck.Models;
using DeskDuck.Services;
using DeskDuck.ViewModels;
using DeskDuck.Views;
// WinForms implicit usings collide on this name; WPF wins in this file.
using ComboBox = System.Windows.Controls.ComboBox;

namespace DeskDuck
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // Single-instance modeless tool windows. Lifetime lives here (view
        // concern) rather than behind VM commands — a messaging layer for two
        // toggle windows would be over-engineering for this codebase.
        private DiagnosticsWindow? _sessionsWindow;
        private LogWindow? _logWindow;
        private HelpWindow? _helpWindow;

        // Set only by RequestRealExit: any other close hides to tray instead.
        private bool _allowClose;
        private TrayManager? _tray;

        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        /// <summary>Called by App once startup completes; owns the tray icon.</summary>
        public void AttachTray(MainViewModel viewModel)
        {
            _tray = new TrayManager(this, viewModel);
        }

        public void DetachTray()
        {
            _tray?.Dispose();
            _tray = null;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            WindowChrome.Tint(this);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_allowClose)
            {
                e.Cancel = true;
                HideToTray();
            }
            base.OnClosing(e);
        }

        /// <summary>Close button / Alt+F4: hide everything, stay running.</summary>
        public void HideToTray()
        {
            _sessionsWindow?.Hide();
            _logWindow?.Hide();
            _helpWindow?.Hide();
            Hide();
            _tray?.NotifyHiddenToTray();
        }

        /// <summary>Bring the main window back (tray double-click / menu).</summary>
        public void ShowFromTray()
        {
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Show();
            Activate();
        }

        /// <summary>Real exit path: tray menu, or Windows session ending.</summary>
        public void RequestRealExit()
        {
            _allowClose = true;
            Close();
        }

        public void ShowSessions()
        {
            _sessionsWindow ??= CreateToolWindow(() => new DiagnosticsWindow());
            // Show() is a no-op when already visible, so this both re-shows
            // hidden windows and focuses visible ones.
            _sessionsWindow.Show();
            _sessionsWindow.Activate();
        }

        public void ShowLog()
        {
            _logWindow ??= CreateToolWindow(() => new LogWindow());
            _logWindow.Show();
            _logWindow.Activate();
        }

        public void ShowHelp()
        {
            _helpWindow ??= CreateToolWindow(() => new HelpWindow());
            _helpWindow.Show();
            _helpWindow.Activate();
        }

        private T CreateToolWindow<T>(Func<T> create) where T : Window
        {
            T window = create();
            window.Owner = this;
            window.DataContext ??= DataContext;
            window.Closed += (_, _) => ForgetToolWindow(window);
            return window;
        }

        private void ForgetToolWindow(Window window)
        {
            if (ReferenceEquals(_sessionsWindow, window)) _sessionsWindow = null;
            else if (ReferenceEquals(_logWindow, window)) _logWindow = null;
            else if (ReferenceEquals(_helpWindow, window)) _helpWindow = null;
        }

        private void SessionsButton_Click(object sender, RoutedEventArgs e) => ShowSessions();

        private void LogButton_Click(object sender, RoutedEventArgs e) => ShowLog();

        private void HelpButton_Click(object sender, RoutedEventArgs e) => ShowHelp();

        /// <summary>
        /// Commits the picked app's process name, not its display label.
        /// (Picking "YouTube (chrome)" must set target "chrome" — the label
        /// itself would never match a process.) SetCurrentValue preserves the
        /// Text binding; a plain assignment would silently replace it.
        /// </summary>
        private void TargetBox_DropDownClosed(object? sender, EventArgs e)
        {
            if (TargetBox.SelectedItem is DiscoveredApp app
                && DataContext is MainViewModel viewModel)
            {
                viewModel.TargetProcessName = app.ProcessName;
                TargetBox.SetCurrentValue(ComboBox.TextProperty, app.ProcessName);
            }
        }
    }
}

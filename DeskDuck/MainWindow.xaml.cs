using System.Windows;
using System.Windows.Controls;
using DeskDuck.Models;
using DeskDuck.ViewModels;
using DeskDuck.Views;

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

        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void SessionsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_sessionsWindow is null)
            {
                _sessionsWindow = new DiagnosticsWindow
                {
                    Owner = this,
                    DataContext = DataContext,
                };
                _sessionsWindow.Closed += (_, _) => _sessionsWindow = null;
                _sessionsWindow.Show();
            }
            else
            {
                _sessionsWindow.Activate();
            }
        }

        private void LogButton_Click(object sender, RoutedEventArgs e)
        {
            if (_logWindow is null)
            {
                _logWindow = new LogWindow
                {
                    Owner = this,
                    DataContext = DataContext,
                };
                _logWindow.Closed += (_, _) => _logWindow = null;
                _logWindow.Show();
            }
            else
            {
                _logWindow.Activate();
            }
        }

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

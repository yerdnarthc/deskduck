using System.Windows;
using System.Windows.Controls;
using DeskDuck.Models;
using DeskDuck.ViewModels;

namespace DeskDuck
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
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

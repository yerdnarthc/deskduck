using System.Windows;
using DeskDuck.ViewModels;

namespace DeskDuck
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private MainViewModel? _viewModel;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            _viewModel = new MainViewModel(Dispatcher);
            MainWindow = new MainWindow(_viewModel);
            MainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _viewModel?.Dispose();
            base.OnExit(e);
        }
    }
}

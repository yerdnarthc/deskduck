using System.Windows;
using System.Windows.Media;
using DeskDuck.Services;
using DeskDuck.ViewModels;
// WinForms implicit usings collide on these names; WPF wins in this file.
using Application = System.Windows.Application;
using FontFamily = System.Windows.Media.FontFamily;

namespace DeskDuck
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private MainViewModel? _viewModel;
        private SingleInstance? _singleInstance;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // Must run before anything observable (monitor, tray, engine) exists:
            // a second launch signals the owner and dies here instead.
            _singleInstance = SingleInstance.TryAcquire(() =>
                Dispatcher.Invoke(() => (MainWindow as MainWindow)?.ShowFromTray()));
            if (_singleInstance is null)
            {
                Shutdown();
                return;
            }
            Resources["AppFontFamily"] = ResolveAppFont();
            _viewModel = new MainViewModel(Dispatcher);
            MainWindow = new MainWindow(_viewModel);
            MainWindow.Show();
            ((MainWindow)MainWindow).AttachTray(_viewModel);
            SessionEnding += (_, _) => (MainWindow as MainWindow)?.RequestRealExit();
        }

        /// <summary>
        /// Resolves the bundled Space Mono family. String-URI references
        /// (#Space Mono in any absolute/relative form) provably fail to bind
        /// glyphs at runtime in this app, while folder enumeration binds fine —
        /// so resolve from the enumerated object and fall back to Segoe UI.
        /// (Verified empirically via TryGetGlyphTypeface probes.)
        /// </summary>
        private static FontFamily ResolveAppFont()
        {
            try
            {
                var folder = new Uri("pack://application:,,,/DeskDuck;component/Assets/Fonts/");
                foreach (var family in Fonts.GetFontFamilies(folder))
                {
                    if (family.FamilyNames.Values.Contains("Space Mono"))
                        return family;
                }
            }
            catch
            {
                // Fall through to the system font.
            }
            return new FontFamily("Segoe UI");
        }

        protected override void OnExit(ExitEventArgs e)
        {
            (MainWindow as MainWindow)?.DetachTray();
            _viewModel?.Dispose();
            _singleInstance?.Dispose();
            base.OnExit(e);
        }
    }
}

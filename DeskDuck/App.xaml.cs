using System.Windows;
using System.Windows.Media;
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
            Resources["AppFontFamily"] = ResolveAppFont();
            _viewModel = new MainViewModel(Dispatcher);
            MainWindow = new MainWindow(_viewModel);
            MainWindow.Show();
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
            _viewModel?.Dispose();
            base.OnExit(e);
        }
    }
}

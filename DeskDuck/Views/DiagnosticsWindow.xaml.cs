using System.Windows;

namespace DeskDuck.Views
{
    /// <summary>
    /// Modeless live session monitor. Shares the main ViewModel as its
    /// DataContext, so rows update in place with zero extra plumbing.
    /// </summary>
    public partial class DiagnosticsWindow : Window
    {
        public DiagnosticsWindow()
        {
            InitializeComponent();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            WindowChrome.Tint(this);
        }
    }
}

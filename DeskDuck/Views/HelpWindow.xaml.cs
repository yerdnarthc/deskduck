using System.Windows;

namespace DeskDuck.Views
{
    /// <summary>
    /// Modeless user guide. Static content, no ViewModel needed.
    /// </summary>
    public partial class HelpWindow : Window
    {
        public HelpWindow()
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

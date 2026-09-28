using System.Collections.Specialized;
using System.Windows;
using DeskDuck.ViewModels;

namespace DeskDuck.Views
{
    /// <summary>
    /// Modeless log viewer. Auto-scroll follows new lines unless unchecked;
    /// Clear delegates to the ViewModel. DataContext is the shared main VM.
    /// </summary>
    public partial class LogWindow : Window
    {
        public LogWindow()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                if (DataContext is MainViewModel vm)
                    vm.LogLines.CollectionChanged += OnLogChanged;
            };
            Closed += (_, _) =>
            {
                if (DataContext is MainViewModel vm)
                    vm.LogLines.CollectionChanged -= OnLogChanged;
            };
        }

        private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (AutoScrollBox.IsChecked == true && LogList.Items.Count > 0)
                LogList.ScrollIntoView(LogList.Items[^1]);
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
                vm.ClearLog();
        }
    }
}

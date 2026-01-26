using ScopeDesk.ViewModels;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;

namespace ScopeDesk
{
    public partial class MainWindow : Window
    {
        private bool _isClosing;

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isClosing)
            {
                return;
            }

            _isClosing = true;
            e.Cancel = true;

            try
            {
                if (DataContext is MainViewModel viewModel)
                {
                    await viewModel.ShutdownAsync();
                }
            }
            finally
            {
                Application.Current.Shutdown();
            }
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
    }
}

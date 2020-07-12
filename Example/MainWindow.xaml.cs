using System.Windows;

namespace Example
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void LoadCipherToolPage(object sender, RoutedEventArgs e)
        {
            MainFrame.NavigationService.Navigate(new CipherToolPage());
        }
    }
}

using System.Windows;
using FlowMaster.Desktop.ViewModels;

namespace FlowMaster.Desktop
{
    public partial class MainWindow : Window
    {
        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
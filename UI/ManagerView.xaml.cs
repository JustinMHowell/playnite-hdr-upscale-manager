using System.Windows.Controls;

namespace HdrUpscaleManager.UI
{
    public partial class ManagerView : UserControl
    {
        public ManagerView(ManagerViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}

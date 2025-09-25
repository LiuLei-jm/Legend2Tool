using System.Windows.Controls;

namespace Legend2Tool.WPF.Views
{
    /// &lt;summary&gt;
    /// LogView.xaml 的交互逻辑
    /// &lt;/summary&gt;
    public partial class LogView : UserControl
    {
        public LogView()
        {
            InitializeComponent();
        }

        private void LogTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (e.OriginalSource is TextBox textBox)
            {
                textBox.Dispatcher.BeginInvoke(() =>
                {
                    textBox.ScrollToEnd();
                });
            }
        }
    }
}
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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
                var scrollViewer = GetScrollViewer(textBox);
                var shouldScrollToEnd = scrollViewer == null
                    || scrollViewer.ScrollableHeight - scrollViewer.VerticalOffset < 20;

                textBox.Dispatcher.BeginInvoke(() =>
                {
                    if (shouldScrollToEnd)
                    {
                        textBox.ScrollToEnd();
                    }
                });
            }
        }

        private static ScrollViewer? GetScrollViewer(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is ScrollViewer scrollViewer)
                {
                    return scrollViewer;
                }

                var result = GetScrollViewer(child);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }
    }
}

using CommunityToolkit.Mvvm.Messaging;
using Legend2Tool.WPF.Messages;

namespace Legend2Tool.WPF.State
{
    public class ProgressStore
    {
        private string _progressText = string.Empty;
        public int ProgressPercentage
        {
            get; set;
        }
        public string ProgressText
        {
            get => _progressText;
            set
            {
                if (_progressText != value)
                {
                    _progressText = value;
                    WeakReferenceMessenger.Default.Send(new ProgressChangedMessage());
                }
            }
        }
    }
}

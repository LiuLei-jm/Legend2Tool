using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Legend2Tool.WPF.ViewModels
{
    public partial class PortConfViewModel : ObservableRecipient, IRecipient<ValueChangedMessage<string>>
    {
        [ObservableProperty]
        public string _serverDirectory = string.Empty;

        public PortConfViewModel()
        {
            IsActive = true;
        }

        public void Receive(ValueChangedMessage<string> message)
        {
            ServerDirectory = message.Value;
        }
    }
}

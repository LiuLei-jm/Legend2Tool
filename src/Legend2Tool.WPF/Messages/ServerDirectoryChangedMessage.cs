using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Legend2Tool.WPF.Messages
{
    public class ServerDirectoryChangedMessage : ValueChangedMessage<string>
    {
        public ServerDirectoryChangedMessage(string value) : base(value)
        {
        }
    }
}

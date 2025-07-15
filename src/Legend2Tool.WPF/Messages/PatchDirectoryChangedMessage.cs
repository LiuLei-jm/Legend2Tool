using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Legend2Tool.WPF.Messages
{
    public class PatchDirectoryChangedMessage : ValueChangedMessage<string>
    {
        public PatchDirectoryChangedMessage(string value) : base(value)
        {
        }
    }
}

using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Legend2Tool.WPF.Messages
{
    public class PatchDirectoryChanedMessage : ValueChangedMessage<string>
    {
        public PatchDirectoryChanedMessage(string value) : base(value)
        {
        }
    }
}

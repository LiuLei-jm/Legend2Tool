using Legend2Tool.WPF.Models.Authentication;

namespace Legend2Tool.WPF.Services.Authentication
{
    public interface ICredentialStore
    {
        SavedCredentials? Load();
        void Save(string username, string password);
        void Clear();
    }
}

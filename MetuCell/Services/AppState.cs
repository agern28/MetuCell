using System;

namespace MetuCell.Services
{
    public class AppState
    {
        public string LoggedInPhone { get; private set; }
        public string LoggedInName { get; private set; }

        // Kullanıcı giriş yapmış mı?
        public bool IsLoggedIn => !string.IsNullOrEmpty(LoggedInPhone);

        public event Action OnChange;

        public void Login(string phone, string name)
        {
            LoggedInPhone = phone;
            LoggedInName = name;
            NotifyStateChanged();
        }

        public void Logout()
        {
            LoggedInPhone = null;
            LoggedInName = null;
            NotifyStateChanged();
        }

        private void NotifyStateChanged() => OnChange?.Invoke();
    }
}
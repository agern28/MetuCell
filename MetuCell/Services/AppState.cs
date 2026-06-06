using System;

namespace MetuCell.Services
{
    public class AppState
    {
        public string LoggedInPhone { get; private set; }
        public string LoggedInName { get; private set; }
        public int CustomerId { get; private set; }
        public bool IsBusiness { get; private set; }   // kurumsal musteri mi?
        public bool IsAdmin { get; private set; }       // yonetici (operator) mi?
        public bool IsLoggedIn { get; private set; }

        public event Action OnChange;

        // SIradan musteri girisi: bir telefon hattina ve hesap sahibine baglidir.
        public void LoginUser(string phone, string name, int customerId, bool isBusiness)
        {
            LoggedInPhone = phone;
            LoggedInName = name;
            CustomerId = customerId;
            IsBusiness = isBusiness;
            IsAdmin = false;
            IsLoggedIn = true;
            NotifyStateChanged();
        }

        // Yonetici (operator) girisi hatta bagli degildir o yüzden tum CRM yetkisine sahiptir.
        public void LoginAdmin(string name)
        {
            LoggedInPhone = null;
            LoggedInName = name;
            CustomerId = 0;
            IsBusiness = false;
            IsAdmin = true;
            IsLoggedIn = true;
            NotifyStateChanged();
        }

        public void Logout()
        {
            LoggedInPhone = null;
            LoggedInName = null;
            CustomerId = 0;
            IsBusiness = false;
            IsAdmin = false;
            IsLoggedIn = false;
            NotifyStateChanged();
        }

        private void NotifyStateChanged() => OnChange?.Invoke();
    }
}

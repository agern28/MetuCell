namespace MetuCell.Models
{
    public class BalanceReport
    {
        public int InternetLeft { get; set; }
        public int SmsLeft { get; set; }
        public int MinuteLeft { get; set; }
    }

    // Tek bir aktif paketin kalan + toplam kotalari (Dashboard'da paket-bazli gosterim icin).
    public class PacketUsage
    {
        public int ActivePacketId { get; set; }
        public int PacketId { get; set; }
        public string PlanType { get; set; }
        public bool International { get; set; }
        public System.DateTime DueDate { get; set; }

        public int InternetLeft { get; set; }
        public int SmsLeft { get; set; }
        public int MinuteLeft { get; set; }

        public int InternetSize { get; set; }   // paketin orijinal internet kotasi (yuzde icin)
        public int SmsCount { get; set; }        // paketin orijinal SMS kotasi
        public int MinuteCount { get; set; }     // paketin orijinal dakika kotasi
    }
    public class UserReport
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
    }
    public class SimReport
    {
        public string PukNo { get; set; }
        public string SimType { get; set; }
    }
}
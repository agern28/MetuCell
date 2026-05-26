namespace MetuCell.Models
{
    public class BalanceReport
    {
        public int InternetLeft { get; set; }
        public int SmsLeft { get; set; }
        public int MinuteLeft { get; set; }
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
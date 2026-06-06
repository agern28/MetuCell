using System;
using System.Text.RegularExpressions;

namespace MetuCell.Services
{
    
    /// exp: rejects obviously malformed input at the application
    /// boundary with a clear message, before it can reach the database.
    /// Parameterised queries already prevent SQL injection; these validators
    /// limit value shapes and reduce the surface for second-order issues.
    
    public static class InputValidator
    {
        private static readonly Regex PhoneRegex = new(@"^\d{10,11}$", RegexOptions.Compiled);
        private static readonly Regex EmailRegex = new(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.Compiled);
        private static readonly Regex TrnIdRegex = new(@"^\d{11}$", RegexOptions.Compiled);

        public static void RequireNotEmpty(string value, string field)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException($"{field} is required.");
        }

        public static void RequirePhone(string phone)
        {
            RequireNotEmpty(phone, "Phone number");
            if (!PhoneRegex.IsMatch(phone))
                throw new ArgumentException("Phone number must be 10-11 digits.");
        }

        public static void RequireEmail(string email)
        {
            RequireNotEmpty(email, "Email");
            if (!EmailRegex.IsMatch(email))
                throw new ArgumentException("Email format is invalid.");
        }

        public static void RequireTrnId(string trn)
        {
            RequireNotEmpty(trn, "TRNC ID");
            if (!TrnIdRegex.IsMatch(trn))
                throw new ArgumentException("TRNC ID must be exactly 11 digits.");
        }

        public static void RequirePositive(int n, string field)
        {
            if (n <= 0) throw new ArgumentException($"{field} must be a positive integer.");
        }
    }
}

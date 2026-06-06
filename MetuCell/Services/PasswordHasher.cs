using System;

namespace MetuCell.Services
{
   
    // exp: centralised password hashing using BCrypt (BCrypt.Net-Next).
    // cew passwords are hashed before they reach the database; legacy
    // plaintext seed values are accepted on first login and the caller
    // may opportunistically re-hash them. No schema change is required:
    // a bcrypt hash is 60 ASCII characters and fits in VARCHAR(255).
    
    public static class PasswordHasher
    {
        // work factor = 11 ~ 150 ms per hash on a typical laptop; suitable for an interactive app.
        private const int WorkFactor = 11;

        public static string Hash(string plaintext)
        {
            if (string.IsNullOrEmpty(plaintext))
                throw new ArgumentException("Password cannot be empty.");
            return BCrypt.Net.BCrypt.HashPassword(plaintext, WorkFactor);
        }

        
        // exp: returns true if <paramref name="plaintext"/> matches the stored value.
        // accepts both bcrypt hashes (prefix "$2a$"/"$2b$"/"$2y$") and legacy
        // plaintext values left over from the seed data.
        
        public static bool Verify(string plaintext, string stored)
        {
            if (string.IsNullOrEmpty(stored) || plaintext == null) return false;
            if (IsBcryptHash(stored))
                return BCrypt.Net.BCrypt.Verify(plaintext, stored);
            // Legacy seed fallback (constant-time compare).
            return StringEquals(plaintext, stored);
        }

        public static bool IsBcryptHash(string s)
            => !string.IsNullOrEmpty(s) && s.Length >= 59 && s.StartsWith("$2");

        // constant-time string compare to avoid trivial timing oracles on legacy values.
        private static bool StringEquals(string a, string b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}

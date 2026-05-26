using Npgsql;
using MetuCell.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MetuCell.Services
{
    public class DatabaseService
    {
        private readonly string _connectionString;
        public DatabaseService(string connectionString) => _connectionString = connectionString;

        // ==========================================
        // 1. GİRİŞ VE KULLANICI İŞLEMLERİ
        // ==========================================
        public async Task<UserReport> LoginAsync(string phone, string password)
        {
            await using var conn = new NpgsqlConnection(_connectionString); await conn.OpenAsync();
            var cmd = new NpgsqlCommand(@"SELECT ""First Name"", ""Last Name"" FROM ""User"" WHERE ""Phone Number"" = @p AND ""Password"" = @pw", conn);
            cmd.Parameters.AddWithValue("p", phone); cmd.Parameters.AddWithValue("pw", password);
            using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync() ? new UserReport { FirstName = r.GetString(0), LastName = r.GetString(1) } : null;
        }

        // ==========================================
        // 2. TRANSACTION: DATA ENTRY (VERİ GİRİŞİ)
        // ==========================================

        // Bireysel Müşteri Ekleme
        public async Task AddIndividualCustomerAsync(string address, string email, string trnId, string firstName, string lastName, string gender, DateTime dob)
        {
            await using var conn = new NpgsqlConnection(_connectionString); await conn.OpenAsync();
            using var tx = await conn.BeginTransactionAsync();
            try
            {
                var cmd1 = new NpgsqlCommand(@"INSERT INTO ""Customer"" (""Address"", ""E-mail"") VALUES (@a, @e) RETURNING ""CustomerID""", conn, tx);
                cmd1.Parameters.AddWithValue("a", address); cmd1.Parameters.AddWithValue("e", email);
                int customerId = (int)await cmd1.ExecuteScalarAsync();

                var cmd2 = new NpgsqlCommand(@"INSERT INTO ""Individual Customer"" (""CustomerID"", ""TRNID"", ""First Name"", ""Last Name"", ""Gender"", ""DOB"") VALUES (@id, @trn, @fn, @ln, @g, @dob)", conn, tx);
                cmd2.Parameters.AddWithValue("id", customerId); cmd2.Parameters.AddWithValue("trn", trnId);
                cmd2.Parameters.AddWithValue("fn", firstName); cmd2.Parameters.AddWithValue("ln", lastName);
                cmd2.Parameters.AddWithValue("g", gender); cmd2.Parameters.AddWithValue("dob", dob);
                await cmd2.ExecuteNonQueryAsync();
                await tx.CommitAsync();
            }
            catch { await tx.RollbackAsync(); throw; }
        }

        // Kurumsal Müşteri Ekleme
        public async Task AddBusinessCustomerAsync(string address, string email, string businessNo, string taxNo, string companyName)
        {
            await using var conn = new NpgsqlConnection(_connectionString); await conn.OpenAsync();
            using var tx = await conn.BeginTransactionAsync();
            try
            {
                var cmd1 = new NpgsqlCommand(@"INSERT INTO ""Customer"" (""Address"", ""E-mail"") VALUES (@a, @e) RETURNING ""CustomerID""", conn, tx);
                cmd1.Parameters.AddWithValue("a", address); cmd1.Parameters.AddWithValue("e", email);
                int customerId = (int)await cmd1.ExecuteScalarAsync();

                var cmd2 = new NpgsqlCommand(@"INSERT INTO ""Business Customer"" (""CustomerID"", ""Business Number"", ""Tax Number"", ""Company Name"") VALUES (@id, @bno, @tax, @cname)", conn, tx);
                cmd2.Parameters.AddWithValue("id", customerId); cmd2.Parameters.AddWithValue("bno", businessNo);
                cmd2.Parameters.AddWithValue("tax", taxNo); cmd2.Parameters.AddWithValue("cname", companyName);
                await cmd2.ExecuteNonQueryAsync();
                await tx.CommitAsync();
            }
            catch { await tx.RollbackAsync(); throw; }
        }

        // Yeni Hat Tanımlama (SIM ve User Bağlama)
        public async Task ProvisionMobileLineAsync(string phone, int simId, string trnId, string firstName, string lastName, string gender, DateTime dob)
        {
            await using var conn = new NpgsqlConnection(_connectionString); await conn.OpenAsync();
            using var tx = await conn.BeginTransactionAsync();
            try
            {
                var cmd1 = new NpgsqlCommand(@"INSERT INTO ""Mobile Line"" (""Phone Number"", ""Activation Date"", ""SIM ID"") VALUES (@p, CURRENT_DATE, @sim)", conn, tx);
                cmd1.Parameters.AddWithValue("p", phone); cmd1.Parameters.AddWithValue("sim", simId);
                await cmd1.ExecuteNonQueryAsync();

                var cmd2 = new NpgsqlCommand(@"INSERT INTO ""User"" (""Phone Number"", ""TRNID"", ""First Name"", ""Last Name"", ""Gender"", ""DOB"") VALUES (@p, @trn, @fn, @ln, @g, @dob)", conn, tx);
                cmd2.Parameters.AddWithValue("p", phone); cmd2.Parameters.AddWithValue("trn", trnId);
                cmd2.Parameters.AddWithValue("fn", firstName); cmd2.Parameters.AddWithValue("ln", lastName);
                cmd2.Parameters.AddWithValue("g", gender); cmd2.Parameters.AddWithValue("dob", dob);
                await cmd2.ExecuteNonQueryAsync();
                await tx.CommitAsync();
            }
            catch { await tx.RollbackAsync(); throw; }
        }

        // ==========================================
        // 3. TRANSACTION: DATA UPDATE (KULLANIM DÜŞME & HEDİYE)
        // ==========================================

        // Kullanım Düşme (İnternet, Dakika, SMS harcama)
        public async Task ConsumeServiceAsync(string phone, int mbUsed, int smsUsed, int minUsed)
        {
            await using var conn = new NpgsqlConnection(_connectionString); await conn.OpenAsync();
            var cmd = new NpgsqlCommand(@"UPDATE ""Customers Packet"" SET ""Internet Left"" = GREATEST(0, ""Internet Left"" - @mb), ""Sms Left"" = GREATEST(0, ""Sms Left"" - @sms), ""Minute Left"" = GREATEST(0, ""Minute Left"" - @min) WHERE ""Phone Number"" = @p AND ""isActive"" = TRUE", conn);
            cmd.Parameters.AddWithValue("p", phone); cmd.Parameters.AddWithValue("mb", mbUsed);
            cmd.Parameters.AddWithValue("sms", smsUsed); cmd.Parameters.AddWithValue("min", minUsed);
            await cmd.ExecuteNonQueryAsync();
        }

        // Hediye Paketi Tanımlama
        public async Task GrantGiftPacketAsync(string phone, int packetId)
        {
            await using var conn = new NpgsqlConnection(_connectionString); await conn.OpenAsync();
            using var tx = await conn.BeginTransactionAsync();
            try
            {
                var cmd1 = new NpgsqlCommand(@"UPDATE ""Mobile Line"" SET ""Gift Cooldown Timestamp"" = CURRENT_TIMESTAMP + INTERVAL '7 days' WHERE ""Phone Number"" = @p", conn, tx);
                cmd1.Parameters.AddWithValue("p", phone); await cmd1.ExecuteNonQueryAsync();

                var cmd2 = new NpgsqlCommand(@"INSERT INTO ""Customers Packet"" (""Phone Number"", ""isActive"", ""Internet Left"", ""Sms Left"", ""Minute Left"", ""Due Date"", ""Packet ID"") VALUES (@p, TRUE, 5000, 500, 500, CURRENT_DATE + INTERVAL '7 days', @pid)", conn, tx);
                cmd2.Parameters.AddWithValue("p", phone); cmd2.Parameters.AddWithValue("pid", packetId); await cmd2.ExecuteNonQueryAsync();
                await tx.CommitAsync();
            }
            catch { await tx.RollbackAsync(); throw; }
        }

        public async Task DeleteExpiredPacketsAsync()
        {
            await using var conn = new NpgsqlConnection(_connectionString); await conn.OpenAsync();
            var cmd = new NpgsqlCommand(@"DELETE FROM ""Customers Packet"" WHERE ""Due Date"" < CURRENT_DATE", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        // ==========================================
        // 4. DATA QUERIES (RAPORLAMA SORGULARI A-F)
        // ==========================================

        public async Task<BalanceReport> GetRemainingBalancesAsync(string phone)
        {
            await using var conn = new NpgsqlConnection(_connectionString); await conn.OpenAsync();
            var cmd = new NpgsqlCommand(@"SELECT ""Internet Left"", ""Sms Left"", ""Minute Left"" FROM ""Customers Packet"" WHERE ""Phone Number"" = @p AND ""isActive"" = TRUE", conn);
            cmd.Parameters.AddWithValue("p", phone);
            using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync() ? new BalanceReport { InternetLeft = r.GetInt32(0), SmsLeft = r.GetInt32(1), MinuteLeft = r.GetInt32(2) } : null;
        }

        public async Task<List<UserReport>> GetBusinessUsersAsync(int customerId)
        {
            var list = new List<UserReport>();
            await using var conn = new NpgsqlConnection(_connectionString); await conn.OpenAsync();
            var cmd = new NpgsqlCommand(@"SELECT u.""First Name"", u.""Last Name"" FROM ""User"" u JOIN ""Mobile Line"" ml ON u.""Phone Number"" = ml.""Phone Number"" JOIN ""SIM Card"" s ON ml.""SIM ID"" = s.""SIM ID"" WHERE s.""CustomerID"" = @cid", conn);
            cmd.Parameters.AddWithValue("cid", customerId);
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) list.Add(new UserReport { FirstName = r.GetString(0), LastName = r.GetString(1) });
            return list;
        }

        public async Task<List<string>> GetEligibleLinesForGiftAsync()
        {
            var list = new List<string>();
            await using var conn = new NpgsqlConnection(_connectionString); await conn.OpenAsync();
            var cmd = new NpgsqlCommand(@"SELECT ""Phone Number"" FROM ""Mobile Line"" WHERE ""Gift Cooldown Timestamp"" IS NULL OR ""Gift Cooldown Timestamp"" < CURRENT_TIMESTAMP", conn);
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) list.Add(r.GetString(0));
            return list;
        }

        public async Task<string> GetSimDetailsAsync(string phone)
        {
            await using var conn = new NpgsqlConnection(_connectionString); await conn.OpenAsync();
            var cmd = new NpgsqlCommand(@"SELECT s.""PUK NO"", s.""SIM TYPE"" FROM ""SIM Card"" s JOIN ""Mobile Line"" m ON s.""SIM ID"" = m.""SIM ID"" WHERE m.""Phone Number"" = @p", conn);
            cmd.Parameters.AddWithValue("p", phone);
            using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync() ? $"PUK: {r.GetString(0)} | TİP: {r.GetString(1)}" : "Bulunamadı";
        }

        public async Task<List<string>> GetExpiringLinesAsync()
        {
            var list = new List<string>();
            await using var conn = new NpgsqlConnection(_connectionString); await conn.OpenAsync();
            var cmd = new NpgsqlCommand(@"SELECT ""Phone Number"" FROM ""Customers Packet"" WHERE ""isActive"" = TRUE AND ""Due Date"" BETWEEN CURRENT_DATE AND CURRENT_DATE + INTERVAL '3 days'", conn);
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) list.Add(r.GetString(0));
            return list;
        }

        public async Task<decimal> GetTotalMonthlyFeeAsync(int customerId)
        {
            await using var conn = new NpgsqlConnection(_connectionString); await conn.OpenAsync();
            var cmd = new NpgsqlCommand(@"SELECT COALESCE(SUM(ppp.""Monthly Fee""), 0) FROM ""Customer"" c JOIN ""SIM Card"" s ON c.""CustomerID"" = s.""CustomerID"" JOIN ""Mobile Line"" ml ON s.""SIM ID"" = ml.""SIM ID"" JOIN ""Customers Packet"" cp ON ml.""Phone Number"" = cp.""Phone Number"" JOIN ""Post-Paid Packet"" ppp ON cp.""Packet ID"" = ppp.""Packet ID"" WHERE c.""CustomerID"" = @cid AND cp.""isActive"" = TRUE", conn);
            cmd.Parameters.AddWithValue("cid", customerId);
            return (decimal)await cmd.ExecuteScalarAsync();
        }
    }
}
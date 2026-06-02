using Npgsql;
using MetuCell.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MetuCell.Services
{
    // ==================================================================================
    //  MetuCell - Data Access Layer (Npgsql / PostgreSQL)
    //  NOT: Tum tablo ve kolon isimleri "CNG 352 ... Query Scripts.sql" dosyasindaki
    //  GERCEK semayla birebir uyumludur. PostgreSQL tirnaksiz isimleri kucuk harfe
    //  katladigindan, kolonlari snake_case (kucuk harf) yaziyoruz. Yalnizca "User"
    //  tablosu CREATE'te tirnakli ve buyuk 'U' ile olusturuldugu icin "User" seklinde
    //  tirnak icinde kullanilmak zorundadir.
    // ==================================================================================
    public class DatabaseService
    {
        private readonly string _connectionString;
        public DatabaseService(string connectionString) => _connectionString = connectionString;

        // ==========================================
        // 1. GIRIS VE KULLANICI ISLEMLERI (QUERY)
        // ==========================================
        public async Task<UserReport> LoginAsync(string phone, string password)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var cmd = new NpgsqlCommand(
                @"SELECT first_name, last_name
                  FROM ""User""
                  WHERE phone_number = @p AND password = @pw", conn);
            cmd.Parameters.AddWithValue("p", phone);
            cmd.Parameters.AddWithValue("pw", password);
            await using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync()
                ? new UserReport { FirstName = r.GetString(0), LastName = r.GetString(1) }
                : null;
        }

        // ==========================================
        // 2. TRANSACTION: DATA ENTRY (INSERT)
        // ==========================================

        // --- Bireysel Musteri Ekleme (Customer + Individual_Customer) ---
        public async Task AddIndividualCustomerAsync(string address, string email, string password,
            string trnId, string firstName, string lastName, string gender, DateTime dob)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                // CustomerID SERIAL degil -> bir sonraki ID'yi biz uretiyoruz
                var idCmd = new NpgsqlCommand(
                    "SELECT COALESCE(MAX(customerid), 0) + 1 FROM customer", conn, tx);
                int customerId = Convert.ToInt32(await idCmd.ExecuteScalarAsync());

                var cmd1 = new NpgsqlCommand(
                    @"INSERT INTO customer (customerid, address, email, password)
                      VALUES (@id, @a, @e, @pw)", conn, tx);
                cmd1.Parameters.AddWithValue("id", customerId);
                cmd1.Parameters.AddWithValue("a", address);
                cmd1.Parameters.AddWithValue("e", email);
                cmd1.Parameters.AddWithValue("pw", password);
                await cmd1.ExecuteNonQueryAsync();

                var cmd2 = new NpgsqlCommand(
                    @"INSERT INTO individual_customer (customerid, trnid, first_name, last_name, gender, dob)
                      VALUES (@id, @trn, @fn, @ln, @g, @dob)", conn, tx);
                cmd2.Parameters.AddWithValue("id", customerId);
                cmd2.Parameters.AddWithValue("trn", trnId);
                cmd2.Parameters.AddWithValue("fn", firstName);
                cmd2.Parameters.AddWithValue("ln", lastName);
                cmd2.Parameters.AddWithValue("g", gender);
                cmd2.Parameters.AddWithValue("dob", dob);
                await cmd2.ExecuteNonQueryAsync();

                await tx.CommitAsync();
            }
            catch { await tx.RollbackAsync(); throw; }
        }

        // --- Kurumsal Musteri Ekleme (Customer + Business_Customer) ---
        public async Task AddBusinessCustomerAsync(string address, string email, string password,
            string businessNo, string taxNo, string companyName)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                var idCmd = new NpgsqlCommand(
                    "SELECT COALESCE(MAX(customerid), 0) + 1 FROM customer", conn, tx);
                int customerId = Convert.ToInt32(await idCmd.ExecuteScalarAsync());

                var cmd1 = new NpgsqlCommand(
                    @"INSERT INTO customer (customerid, address, email, password)
                      VALUES (@id, @a, @e, @pw)", conn, tx);
                cmd1.Parameters.AddWithValue("id", customerId);
                cmd1.Parameters.AddWithValue("a", address);
                cmd1.Parameters.AddWithValue("e", email);
                cmd1.Parameters.AddWithValue("pw", password);
                await cmd1.ExecuteNonQueryAsync();

                var cmd2 = new NpgsqlCommand(
                    @"INSERT INTO business_customer (customerid, business_number, tax_number, company_name)
                      VALUES (@id, @bno, @tax, @cname)", conn, tx);
                cmd2.Parameters.AddWithValue("id", customerId);
                cmd2.Parameters.AddWithValue("bno", businessNo);
                cmd2.Parameters.AddWithValue("tax", taxNo);
                cmd2.Parameters.AddWithValue("cname", companyName);
                await cmd2.ExecuteNonQueryAsync();

                await tx.CommitAsync();
            }
            catch { await tx.RollbackAsync(); throw; }
        }

        // --- Yeni Hat Provizyonu (Mobile_Line + "User") ---
        public async Task ProvisionMobileLineAsync(string phone, int simId, string trnId,
            string firstName, string lastName, string gender, DateTime dob, string password)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                // gift_cooldown_timestamp NOT NULL ama DEFAULT CURRENT_TIMESTAMP -> belirtmeye gerek yok
                var cmd1 = new NpgsqlCommand(
                    @"INSERT INTO mobile_line (phone_number, activation_date, sim_id)
                      VALUES (@p, CURRENT_DATE, @sim)", conn, tx);
                cmd1.Parameters.AddWithValue("p", phone);
                cmd1.Parameters.AddWithValue("sim", simId);
                await cmd1.ExecuteNonQueryAsync();

                var cmd2 = new NpgsqlCommand(
                    @"INSERT INTO ""User"" (trnid, phone_number, first_name, last_name, gender, dob, password)
                      VALUES (@trn, @p, @fn, @ln, @g, @dob, @pw)", conn, tx);
                cmd2.Parameters.AddWithValue("trn", trnId);
                cmd2.Parameters.AddWithValue("p", phone);
                cmd2.Parameters.AddWithValue("fn", firstName);
                cmd2.Parameters.AddWithValue("ln", lastName);
                cmd2.Parameters.AddWithValue("g", gender);
                cmd2.Parameters.AddWithValue("dob", dob);
                cmd2.Parameters.AddWithValue("pw", password);
                await cmd2.ExecuteNonQueryAsync();

                await tx.CommitAsync();
            }
            catch { await tx.RollbackAsync(); throw; }
        }

        // ==========================================
        // 3. TRANSACTION: DATA UPDATE / DELETE
        // ==========================================

        // --- Kullanim Dusme (UPDATE) ---
        public async Task ConsumeServiceAsync(string phone, int mbUsed, int smsUsed, int minUsed)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var cmd = new NpgsqlCommand(
                @"UPDATE customers_packet
                  SET internet_left = GREATEST(0, internet_left - @mb),
                      sms_left      = GREATEST(0, sms_left - @sms),
                      minute_left   = GREATEST(0, minute_left - @min)
                  WHERE phone_number = @p AND isactive = TRUE", conn);
            cmd.Parameters.AddWithValue("p", phone);
            cmd.Parameters.AddWithValue("mb", mbUsed);
            cmd.Parameters.AddWithValue("sms", smsUsed);
            cmd.Parameters.AddWithValue("min", minUsed);
            int affected = await cmd.ExecuteNonQueryAsync();
            if (affected == 0)
                throw new Exception("Bu numaraya ait aktif paket bulunamadi.");
        }

        // --- Hediye Paketi Tanimlama (UPDATE cooldown + INSERT customers_packet) ---
        public async Task GrantGiftPacketAsync(string phone, int giftPacketId)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                // 1) Hediye paketinin katalog kotalarini al (FK kontrolu de burada dolayli yapilir)
                var pkCmd = new NpgsqlCommand(
                    @"SELECT internet_size, sms_count, minute_count
                      FROM packets WHERE packet_id = @pid AND plan_type = 'Gift'", conn, tx);
                pkCmd.Parameters.AddWithValue("pid", giftPacketId);
                int net = 0, sms = 0, min = 0;
                await using (var pr = await pkCmd.ExecuteReaderAsync())
                {
                    if (!await pr.ReadAsync())
                        throw new Exception($"Gecerli bir hediye paketi degil (ID: {giftPacketId}).");
                    net = pr.IsDBNull(0) ? 0 : pr.GetInt32(0);
                    sms = pr.IsDBNull(1) ? 0 : pr.GetInt32(1);
                    min = pr.IsDBNull(2) ? 0 : pr.GetInt32(2);
                }

                // 2) Cooldown'u 30 gun ileri al (mukerrer talep engeli)
                var cmd1 = new NpgsqlCommand(
                    @"UPDATE mobile_line
                      SET gift_cooldown_timestamp = CURRENT_TIMESTAMP + INTERVAL '30 days'
                      WHERE phone_number = @p", conn, tx);
                cmd1.Parameters.AddWithValue("p", phone);
                if (await cmd1.ExecuteNonQueryAsync() == 0)
                    throw new Exception("Hat bulunamadi.");

                // 3) Bu hat icin bir sonraki Active_Packet_ID'yi uret (bilesik PK)
                var apCmd = new NpgsqlCommand(
                    @"SELECT COALESCE(MAX(active_packet_id), 0) + 1
                      FROM customers_packet WHERE phone_number = @p", conn, tx);
                apCmd.Parameters.AddWithValue("p", phone);
                int activePacketId = Convert.ToInt32(await apCmd.ExecuteScalarAsync());

                // 4) Hediyeyi aktif paket olarak ekle (7 gun gecerli)
                var cmd2 = new NpgsqlCommand(
                    @"INSERT INTO customers_packet
                        (phone_number, active_packet_id, isactive, internet_left, sms_left, minute_left, due_date, packet_id)
                      VALUES (@p, @apid, TRUE, @net, @sms, @min, CURRENT_DATE + INTERVAL '7 days', @pid)", conn, tx);
                cmd2.Parameters.AddWithValue("p", phone);
                cmd2.Parameters.AddWithValue("apid", activePacketId);
                cmd2.Parameters.AddWithValue("net", net);
                cmd2.Parameters.AddWithValue("sms", sms);
                cmd2.Parameters.AddWithValue("min", min);
                cmd2.Parameters.AddWithValue("pid", giftPacketId);
                await cmd2.ExecuteNonQueryAsync();

                await tx.CommitAsync();
            }
            catch { await tx.RollbackAsync(); throw; }
        }

        // --- Suresi Gecmis Paketleri Sil (DELETE) ---
        public async Task<int> DeleteExpiredPacketsAsync()
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var cmd = new NpgsqlCommand(
                @"DELETE FROM customers_packet WHERE due_date < CURRENT_DATE", conn);
            return await cmd.ExecuteNonQueryAsync();
        }

        // ==========================================
        // 4. DATA QUERIES (RAPORLAMA SORGULARI a-f)
        // ==========================================

        // (a) Belirli hattin gercek zamanli kalan bakiyeleri
        public async Task<BalanceReport> GetRemainingBalancesAsync(string phone)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var cmd = new NpgsqlCommand(
                @"SELECT COALESCE(SUM(internet_left),0), COALESCE(SUM(sms_left),0), COALESCE(SUM(minute_left),0)
                  FROM customers_packet
                  WHERE phone_number = @p AND isactive = TRUE", conn);
            cmd.Parameters.AddWithValue("p", phone);
            await using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync()
                ? new BalanceReport
                {
                    InternetLeft = Convert.ToInt32(r.GetValue(0)),
                    SmsLeft = Convert.ToInt32(r.GetValue(1)),
                    MinuteLeft = Convert.ToInt32(r.GetValue(2))
                }
                : null;
        }

        // (b) Bir kurumsal musterinin sahip oldugu tum hatlarin kullanicilari (JOIN)
        public async Task<List<UserReport>> GetBusinessUsersAsync(int customerId)
        {
            var list = new List<UserReport>();
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var cmd = new NpgsqlCommand(
                @"SELECT u.first_name, u.last_name
                  FROM ""User"" u
                  JOIN mobile_line ml ON u.phone_number = ml.phone_number
                  JOIN sim_card s     ON ml.sim_id      = s.sim_id
                  JOIN business_customer b ON s.customerid = b.customerid
                  WHERE b.customerid = @cid", conn);
            cmd.Parameters.AddWithValue("cid", customerId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new UserReport { FirstName = r.GetString(0), LastName = r.GetString(1) });
            return list;
        }

        // (c) Hediye cekilisine uygun hatlar (cooldown gecmis)
        public async Task<List<string>> GetEligibleLinesForGiftAsync()
        {
            var list = new List<string>();
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var cmd = new NpgsqlCommand(
                @"SELECT phone_number FROM mobile_line
                  WHERE gift_cooldown_timestamp < CURRENT_TIMESTAMP", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) list.Add(r.GetString(0));
            return list;
        }

        // (d) Kilitli hat icin PUK + SIM TYPE (JOIN)
        public async Task<string> GetSimDetailsAsync(string phone)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var cmd = new NpgsqlCommand(
                @"SELECT s.puk_no, s.sim_type
                  FROM sim_card s
                  JOIN mobile_line m ON s.sim_id = m.sim_id
                  WHERE m.phone_number = @p", conn);
            cmd.Parameters.AddWithValue("p", phone);
            await using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync()
                ? $"PUK: {r.GetString(0)}  |  TIP: {r.GetString(1)}"
                : "Kayit bulunamadi.";
        }

        // (e) Son 3 gun icinde faturasi/suresi dolacak aktif hatlar
        public async Task<List<string>> GetExpiringLinesAsync()
        {
            var list = new List<string>();
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var cmd = new NpgsqlCommand(
                @"SELECT DISTINCT phone_number FROM customers_packet
                  WHERE isactive = TRUE
                    AND due_date BETWEEN CURRENT_DATE AND CURRENT_DATE + INTERVAL '3 days'", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) list.Add(r.GetString(0));
            return list;
        }

        // (f) Bir musterinin toplam aylik post-paid faturasi (AGGREGATE + 5'li JOIN)
        public async Task<decimal> GetTotalMonthlyFeeAsync(int customerId)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var cmd = new NpgsqlCommand(
                @"SELECT COALESCE(SUM(ppp.monthly_fee), 0)
                  FROM customer c
                  JOIN sim_card s        ON c.customerid  = s.customerid
                  JOIN mobile_line ml    ON s.sim_id      = ml.sim_id
                  JOIN customers_packet cp ON ml.phone_number = cp.phone_number
                  JOIN post_paid_packet ppp ON cp.packet_id  = ppp.packet_id
                  WHERE c.customerid = @cid AND cp.isactive = TRUE", conn);
            cmd.Parameters.AddWithValue("cid", customerId);
            return Convert.ToDecimal(await cmd.ExecuteScalarAsync());
        }
    }
}
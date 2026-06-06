using Npgsql;
using MetuCell.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MetuCell.Services
{
    
    //Data Access Layer (Npgsql / PostgreSQL)
    
    public class DatabaseService
    {
        private readonly string _connectionString;
        private static readonly Random _rng = new Random();
        public DatabaseService(string connectionString) => _connectionString = connectionString;

        
        //Login 
        
        public async Task<LoginResult> LoginAsync(string phone, string password)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var cmd = new NpgsqlCommand(
                @"SELECT c.customerid, c.password,
                         (bc.customerid IS NOT NULL) AS is_business,
                         COALESCE(bc.company_name, u.first_name || ' ' || u.last_name, 'Customer') AS display_name
                  FROM mobile_line ml
                  JOIN sim_card s ON ml.sim_id    = s.sim_id
                  JOIN customer c ON s.customerid = c.customerid
                  LEFT JOIN business_customer bc ON bc.customerid = c.customerid
                  LEFT JOIN ""User"" u ON u.phone_number = ml.phone_number
                  WHERE ml.phone_number = @p", conn);
            cmd.Parameters.AddWithValue("p", phone);

            int customerId; string storedPw; bool isBusiness; string displayName;
            await using (var r = await cmd.ExecuteReaderAsync())
            {
                if (!await r.ReadAsync()) return null;
                customerId  = r.GetInt32(0);
                storedPw    = r.GetString(1);
                isBusiness  = r.GetBoolean(2);
                displayName = r.GetString(3);
            }

            // Plaintext never appears in any SQL, verification happens in C#.
            if (!PasswordHasher.Verify(password, storedPw)) return null;

            // migrate legacy seed plaintext to a bcrypt hash on first successful login.
            if (!PasswordHasher.IsBcryptHash(storedPw))
            {
                await using var up = new NpgsqlCommand(
                    "UPDATE customer SET password = @new WHERE customerid = @cid", conn);
                up.Parameters.AddWithValue("new", PasswordHasher.Hash(password));
                up.Parameters.AddWithValue("cid", customerId);
                await up.ExecuteNonQueryAsync();
            }

            return new LoginResult { CustomerId = customerId, IsBusiness = isBusiness, DisplayName = displayName };
        }


        //  Helpers (automatic SIM / telephone / sim+user)


        // Generate a unique phone number automatically, Take the largest number and add 1 to it.
        private async Task<string> GenerateNextPhoneAsync(NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            var cmd = new NpgsqlCommand(
                "SELECT COALESCE(MAX(CAST(phone_number AS BIGINT)), 5550000000) + 1 FROM mobile_line", conn, tx);
            return Convert.ToInt64(await cmd.ExecuteScalarAsync()).ToString();
        }

        // Generates a new SIM card (SIM_ID = MAX+1, PUK is random, owner = customerId).
        // Only Network_Range and SIM_TYPE are provided externally (selected from the domain).
        private async Task<int> CreateSimCardAsync(NpgsqlConnection conn, NpgsqlTransaction tx,
            int customerId, string networkRange, string simType)
        {
            var idCmd = new NpgsqlCommand("SELECT COALESCE(MAX(sim_id), 5000) + 1 FROM sim_card", conn, tx);
            int simId = Convert.ToInt32(await idCmd.ExecuteScalarAsync());
            string puk = _rng.Next(10000000, 99999999).ToString();

            var cmd = new NpgsqlCommand(
                @"INSERT INTO sim_card (sim_id, network_range, sim_type, puk_no, customerid)
                  VALUES (@sid, @nr, @st, @puk, @cid)", conn, tx);
            cmd.Parameters.AddWithValue("sid", simId);
            cmd.Parameters.AddWithValue("nr", networkRange);
            cmd.Parameters.AddWithValue("st", simType);
            cmd.Parameters.AddWithValue("puk", puk);
            cmd.Parameters.AddWithValue("cid", customerId);
            await cmd.ExecuteNonQueryAsync();
            return simId;
        }

        // Creates a line (Mobile_Line) and a user .
        private async Task CreateLineAndUserAsync(NpgsqlConnection conn, NpgsqlTransaction tx,
            string phone, int simId, string trnId, string firstName, string lastName,
            string gender, DateTime dob, string password)
        {
            var lineCmd = new NpgsqlCommand(
                @"INSERT INTO mobile_line (phone_number, activation_date, sim_id)
                  VALUES (@p, CURRENT_DATE, @sim)", conn, tx);
            lineCmd.Parameters.AddWithValue("p", phone);
            lineCmd.Parameters.AddWithValue("sim", simId);
            await lineCmd.ExecuteNonQueryAsync();

            var userCmd = new NpgsqlCommand(
                @"INSERT INTO ""User"" (trnid, phone_number, first_name, last_name, gender, dob, password)
                  VALUES (@trn, @p, @fn, @ln, @g, @dob, @pw)", conn, tx);
            userCmd.Parameters.AddWithValue("trn", trnId);
            userCmd.Parameters.AddWithValue("p", phone);
            userCmd.Parameters.AddWithValue("fn", firstName);
            userCmd.Parameters.AddWithValue("ln", lastName);
            userCmd.Parameters.AddWithValue("g", gender);
            userCmd.Parameters.AddWithValue("dob", dob);
            userCmd.Parameters.AddWithValue("pw", PasswordHasher.Hash(password));
            await userCmd.ExecuteNonQueryAsync();
        }

        
        //ADD CUSTOMER (+ automatic first line) - INSERT/TRANSACTION
       

        // Individual customer types -> Customer + Individual_Customer + automatic SIM + automatic line
        // the user of the first line is THE CUSTOMER THEMSELVES (using their own TRNC ID/name)
        // output is automatically generated phone number.
        public async Task<string> AddIndividualCustomerAsync(string address, string email, string password,
            string trnId, string firstName, string lastName, string gender, DateTime dob,
            string networkRange, string simType)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                int customerId = Convert.ToInt32(await new NpgsqlCommand(
                    "SELECT COALESCE(MAX(customerid),0)+1 FROM customer", conn, tx).ExecuteScalarAsync());

                var c1 = new NpgsqlCommand(
                    @"INSERT INTO customer (customerid, address, email, password) VALUES (@id,@a,@e,@pw)", conn, tx);
                c1.Parameters.AddWithValue("id", customerId);
                c1.Parameters.AddWithValue("a", address);
                c1.Parameters.AddWithValue("e", email);
                c1.Parameters.AddWithValue("pw", PasswordHasher.Hash(password));
                await c1.ExecuteNonQueryAsync();

                var c2 = new NpgsqlCommand(
                    @"INSERT INTO individual_customer (customerid, trnid, first_name, last_name, gender, dob)
                      VALUES (@id,@trn,@fn,@ln,@g,@dob)", conn, tx);
                c2.Parameters.AddWithValue("id", customerId);
                c2.Parameters.AddWithValue("trn", trnId);
                c2.Parameters.AddWithValue("fn", firstName);
                c2.Parameters.AddWithValue("ln", lastName);
                c2.Parameters.AddWithValue("g", gender);
                c2.Parameters.AddWithValue("dob", dob);
                await c2.ExecuteNonQueryAsync();

                int simId = await CreateSimCardAsync(conn, tx, customerId, networkRange, simType);
                string phone = await GenerateNextPhoneAsync(conn, tx);
                // User = the customer themselves
                await CreateLineAndUserAsync(conn, tx, phone, simId, trnId, firstName, lastName, gender, dob, password);

                await tx.CommitAsync();
                return phone;
            }
            catch { await tx.RollbackAsync(); throw; }
        }

        // Business customer types -> Customer + Business_Customer + automatic SIM + automatic line;
        // Since a company cannot be a “person,” the user of the first line is the FIRST EMPLOYEE (authorized).
        public async Task<string> AddBusinessCustomerAsync(string address, string email, string password,
            string businessNo, string taxNo, string companyName,
            string networkRange, string simType,
            string empTrnId, string empFirstName, string empLastName, string empGender, DateTime empDob)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                int customerId = Convert.ToInt32(await new NpgsqlCommand(
                    "SELECT COALESCE(MAX(customerid),0)+1 FROM customer", conn, tx).ExecuteScalarAsync());

                var c1 = new NpgsqlCommand(
                    @"INSERT INTO customer (customerid, address, email, password) VALUES (@id,@a,@e,@pw)", conn, tx);
                c1.Parameters.AddWithValue("id", customerId);
                c1.Parameters.AddWithValue("a", address);
                c1.Parameters.AddWithValue("e", email);
                c1.Parameters.AddWithValue("pw", PasswordHasher.Hash(password));
                await c1.ExecuteNonQueryAsync();

                var c2 = new NpgsqlCommand(
                    @"INSERT INTO business_customer (customerid, business_number, tax_number, company_name)
                      VALUES (@id,@bno,@tax,@cname)", conn, tx);
                c2.Parameters.AddWithValue("id", customerId);
                c2.Parameters.AddWithValue("bno", businessNo);
                c2.Parameters.AddWithValue("tax", taxNo);
                c2.Parameters.AddWithValue("cname", companyName);
                await c2.ExecuteNonQueryAsync();

                int simId = await CreateSimCardAsync(conn, tx, customerId, networkRange, simType);
                string phone = await GenerateNextPhoneAsync(conn, tx);
                // User = first employee; User password = account (Customer) password
                await CreateLineAndUserAsync(conn, tx, phone, simId, empTrnId, empFirstName, empLastName, empGender, empDob, password);

                await tx.CommitAsync();
                return phone;
            }
            catch { await tx.RollbackAsync(); throw; }
        }

        
        // adding a new line to generated customer (admin)
        //  Once the SIM is generated, it is determined whether the line is for the “customer themselves” or for “another user.”
       
        public async Task<string> ProvisionLineForCustomerAsync(int customerId, string networkRange, string simType,
            bool forSelf, string trnId, string firstName, string lastName, string gender, DateTime dob)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                // Verify the customer + retrieve their password and type
                string custPassword = null; bool isBusiness = false;
                var info = new NpgsqlCommand(
                    @"SELECT c.password, (bc.customerid IS NOT NULL)
                      FROM customer c LEFT JOIN business_customer bc ON bc.customerid = c.customerid
                      WHERE c.customerid = @cid", conn, tx);
                info.Parameters.AddWithValue("cid", customerId);
                await using (var ir = await info.ExecuteReaderAsync())
                {
                    if (!await ir.ReadAsync())
                        throw new Exception($"Customer not found (ID: {customerId}).");
                    custPassword = ir.GetString(0);
                    isBusiness = ir.GetBoolean(1);
                }

                // If “Self” is selected: retrieve user information from Individual_Customer
                if (forSelf)
                {
                    if (isBusiness)
                        throw new Exception("'Self' cannot be selected for a business customer. Please define an employee.");

                    var ind = new NpgsqlCommand(
                        @"SELECT trnid, first_name, last_name, gender, dob
                          FROM individual_customer WHERE customerid = @cid", conn, tx);
                    ind.Parameters.AddWithValue("cid", customerId);
                    await using var rr = await ind.ExecuteReaderAsync();
                    if (!await rr.ReadAsync())
                        throw new Exception("Individual customer record not found.");
                    trnId = rr.GetString(0);
                    firstName = rr.GetString(1);
                    lastName = rr.GetString(2);
                    gender = rr.GetString(3);
                    dob = rr.GetDateTime(4);
                }

                int simId = await CreateSimCardAsync(conn, tx, customerId, networkRange, simType);
                string phone = await GenerateNextPhoneAsync(conn, tx);

                try
                {
                    await CreateLineAndUserAsync(conn, tx, phone, simId, trnId, firstName, lastName, gender, dob, custPassword);
                }
                catch (PostgresException pe) when (pe.SqlState == "23505") // unique_violation (User PK = TRNID)
                {
                    throw new Exception("A user with this TRNC ID already exists. The same person cannot be the user of a second line; please define a different user.");
                }

                await tx.CommitAsync();
                return phone;
            }
            catch { await tx.RollbackAsync(); throw; }
        }

        // 3. consumeserice/or packet  - Start with the first package; if that's not enough, move on to the next one
        public async Task<string> ConsumeServiceAsync(string phone, int mbUsed, int smsUsed, int minUsed)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                // Retrieve active packages, starting with the one whose expiration date is closest
                var packets = new List<int[]>(); // [apid, net, sms, min]
                var sel = new NpgsqlCommand(
                    @"SELECT active_packet_id, internet_left, sms_left, minute_left
                      FROM customers_packet
                      WHERE phone_number = @p AND isactive = TRUE
                      ORDER BY due_date, active_packet_id", conn, tx);
                sel.Parameters.AddWithValue("p", phone);
                await using (var r = await sel.ExecuteReaderAsync())
                    while (await r.ReadAsync())
                        packets.Add(new[] { r.GetInt32(0),
                                            r.IsDBNull(1)?0:r.GetInt32(1),
                                            r.IsDBNull(2)?0:r.GetInt32(2),
                                            r.IsDBNull(3)?0:r.GetInt32(3) });

                if (packets.Count == 0)
                    throw new Exception("No active package found for this number.");

                // A helper function that iterates through a source one by one across packages (overflow logic)
                int Spend(int need, int idx)
                {
                    for (int i = 0; i < packets.Count && need > 0; i++)
                    {
                        int take = Math.Min(need, packets[i][idx]);
                        packets[i][idx] -= take;   // does not go negative (take <= current)
                        need -= take;
                    }
                    return need; // remaining unmet
                }

                int netLeftover = Spend(mbUsed,  1);
                int smsLeftover = Spend(smsUsed, 2);
                int minLeftover = Spend(minUsed, 3);

                // Update the changed packages
                foreach (var p in packets)
                {
                    var up = new NpgsqlCommand(
                        @"UPDATE customers_packet
                          SET internet_left = @i, sms_left = @s, minute_left = @m
                          WHERE phone_number = @p AND active_packet_id = @apid", conn, tx);
                    up.Parameters.AddWithValue("i", p[1]);
                    up.Parameters.AddWithValue("s", p[2]);
                    up.Parameters.AddWithValue("m", p[3]);
                    up.Parameters.AddWithValue("p", phone);
                    up.Parameters.AddWithValue("apid", p[0]);
                    await up.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();

                var msg = $"Deducted → Internet: {mbUsed - netLeftover} MB, SMS: {smsUsed - smsLeftover}, Minutes: {minUsed - minLeftover}.";
                if (netLeftover > 0 || smsLeftover > 0 || minLeftover > 0)
                    msg += $" (Insufficient balance: {netLeftover} MB / {smsLeftover} SMS / {minLeftover} min could not be covered.)";
                return msg;
            }
            catch { await tx.RollbackAsync(); throw; }
        }


        // 3b. PACKAGE PURCHASE (customer) 
        public async Task<List<PacketCatalogItem>> GetBuyablePacketsAsync()
        {
            var list = new List<PacketCatalogItem>();
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var cmd = new NpgsqlCommand(
                @"SELECT p.packet_id, p.plan_type, p.internet_size, p.sms_count, p.minute_count,
                         p.international_usage, COALESCE(ppp.monthly_fee, prp.fee, 0) AS fee
                  FROM packets p
                  LEFT JOIN post_paid_packet ppp ON p.packet_id = ppp.packet_id
                  LEFT JOIN pre_paid_packet  prp ON p.packet_id = prp.packet_id
                  WHERE p.plan_type IN ('Post-Paid','Pre-Paid')
                  ORDER BY p.plan_type, fee", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new PacketCatalogItem
                {
                    PacketId = r.GetInt32(0),
                    PlanType = r.GetString(1),
                    InternetSize = r.IsDBNull(2)?0:r.GetInt32(2),
                    SmsCount = r.IsDBNull(3)?0:r.GetInt32(3),
                    MinuteCount = r.IsDBNull(4)?0:r.GetInt32(4),
                    International = !r.IsDBNull(5) && r.GetBoolean(5),
                    Fee = r.IsDBNull(6)?0:r.GetDecimal(6)
                });
            return list;
        }

        // A customer “purchases” a package (no payment). They are added to `customers_packet` along with their quotas.
        public async Task PurchasePacketAsync(string phone, int packetId)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                int net = 0, sms = 0, min = 0; string plan = null;
                var pk = new NpgsqlCommand(
                    @"SELECT internet_size, sms_count, minute_count, plan_type
                      FROM packets WHERE packet_id = @pid", conn, tx);
                pk.Parameters.AddWithValue("pid", packetId);
                await using (var r = await pk.ExecuteReaderAsync())
                {
                    if (!await r.ReadAsync()) throw new Exception("Package not found.");
                    net = r.IsDBNull(0)?0:r.GetInt32(0);
                    sms = r.IsDBNull(1)?0:r.GetInt32(1);
                    min = r.IsDBNull(2)?0:r.GetInt32(2);
                    plan = r.GetString(3);
                }
                if (plan == "Gift") throw new Exception("Gift packages cannot be purchased.");

                int apid = Convert.ToInt32(await new NpgsqlCommand(
                    "SELECT COALESCE(MAX(active_packet_id),0)+1 FROM customers_packet WHERE phone_number=@p"
                    , conn, tx) { Parameters = { new("p", phone) } }.ExecuteScalarAsync());

                var ins = new NpgsqlCommand(
                    @"INSERT INTO customers_packet
                        (phone_number, active_packet_id, isactive, internet_left, sms_left, minute_left, due_date, packet_id)
                      VALUES (@p, @apid, TRUE, @net, @sms, @min,
                              CASE WHEN @plan = 'Post-Paid' THEN CURRENT_DATE + INTERVAL '1 month'
                                   ELSE CURRENT_DATE + INTERVAL '30 days' END,
                              @pid)", conn, tx);
                ins.Parameters.AddWithValue("p", phone);
                ins.Parameters.AddWithValue("apid", apid);
                ins.Parameters.AddWithValue("net", net);
                ins.Parameters.AddWithValue("sms", sms);
                ins.Parameters.AddWithValue("min", min);
                ins.Parameters.AddWithValue("plan", plan);
                ins.Parameters.AddWithValue("pid", packetId);
                await ins.ExecuteNonQueryAsync();

                await tx.CommitAsync();
            }
            catch { await tx.RollbackAsync(); throw; }
        }

        //Gift Package
        public async Task GrantGiftPacketAsync(string phone, int giftPacketId)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                var pkCmd = new NpgsqlCommand(
                    @"SELECT internet_size, sms_count, minute_count
                      FROM packets WHERE packet_id = @pid AND plan_type = 'Gift'", conn, tx);
                pkCmd.Parameters.AddWithValue("pid", giftPacketId);
                int net = 0, sms = 0, min = 0;
                await using (var pr = await pkCmd.ExecuteReaderAsync())
                {
                    if (!await pr.ReadAsync())
                        throw new Exception($"Not a valid gift package (ID: {giftPacketId}).");
                    net = pr.IsDBNull(0)?0:pr.GetInt32(0);
                    sms = pr.IsDBNull(1)?0:pr.GetInt32(1);
                    min = pr.IsDBNull(2)?0:pr.GetInt32(2);
                }

                var c1 = new NpgsqlCommand(
                    @"UPDATE mobile_line SET gift_cooldown_timestamp = CURRENT_TIMESTAMP + INTERVAL '30 days'
                      WHERE phone_number = @p", conn, tx);
                c1.Parameters.AddWithValue("p", phone);
                if (await c1.ExecuteNonQueryAsync() == 0) throw new Exception("Line not found.");

                int apid = Convert.ToInt32(await new NpgsqlCommand(
                    "SELECT COALESCE(MAX(active_packet_id),0)+1 FROM customers_packet WHERE phone_number=@p"
                    , conn, tx) { Parameters = { new("p", phone) } }.ExecuteScalarAsync());

                var c2 = new NpgsqlCommand(
                    @"INSERT INTO customers_packet
                        (phone_number, active_packet_id, isactive, internet_left, sms_left, minute_left, due_date, packet_id)
                      VALUES (@p, @apid, TRUE, @net, @sms, @min, CURRENT_DATE + INTERVAL '7 days', @pid)", conn, tx);
                c2.Parameters.AddWithValue("p", phone);
                c2.Parameters.AddWithValue("apid", apid);
                c2.Parameters.AddWithValue("net", net);
                c2.Parameters.AddWithValue("sms", sms);
                c2.Parameters.AddWithValue("min", min);
                c2.Parameters.AddWithValue("pid", giftPacketId);
                await c2.ExecuteNonQueryAsync();

                await tx.CommitAsync();
            }
            catch { await tx.RollbackAsync(); throw; }
        }

        public async Task<int> DeleteExpiredPacketsAsync()
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            return await new NpgsqlCommand(
                @"DELETE FROM customers_packet WHERE due_date < CURRENT_DATE", conn).ExecuteNonQueryAsync();
        }

       
        //ACCOUNT MAINTENANCE (profile update, billing renewal, deletions)
        

        //Update a customer's profile (address / email). Returns affected rows.
        public async Task<int> UpdateCustomerProfileAsync(int customerId, string address, string email)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var cmd = new NpgsqlCommand(
                @"UPDATE customer SET address = @a, email = @e WHERE customerid = @cid", conn);
            cmd.Parameters.AddWithValue("a", address);
            cmd.Parameters.AddWithValue("e", email);
            cmd.Parameters.AddWithValue("cid", customerId);
            return await cmd.ExecuteNonQueryAsync();
        }

        //  Renew every post-paid packet whose billing cycle (due date) has arrived:
        // reset the quotas from the catalog and push the due date one month forward.
        public async Task<int> RenewExpiredPostPaidAsync()
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var cmd = new NpgsqlCommand(
                @"UPDATE customers_packet CP
                  SET sms_left      = P.sms_count,
                      internet_left = P.internet_size,
                      minute_left   = P.minute_count,
                      due_date      = CP.due_date + INTERVAL '1 month'
                  FROM packets P
                  JOIN post_paid_packet PPP ON P.packet_id = PPP.packet_id
                  WHERE CP.packet_id = P.packet_id
                    AND CP.due_date <= CURRENT_DATE
                    AND CP.isactive = TRUE", conn);
            return await cmd.ExecuteNonQueryAsync();
        }

        // Delete a mobile line; ON DELETE CASCADE removes its "User" and Customer's Packet rows.
        public async Task<int> DeleteMobileLineAsync(string phone)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var cmd = new NpgsqlCommand(
                @"DELETE FROM mobile_line WHERE phone_number = @p", conn);
            cmd.Parameters.AddWithValue("p", phone);
            return await cmd.ExecuteNonQueryAsync();
        }

        // Delete a customer; cascade removes SIM cards, mobile lines, users and packets.
        public async Task<int> DeleteCustomerAsync(int customerId)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var cmd = new NpgsqlCommand(
                @"DELETE FROM customer WHERE customerid = @cid", conn);
            cmd.Parameters.AddWithValue("cid", customerId);
            return await cmd.ExecuteNonQueryAsync();
        }

        // 4. REPORTING QUERIES (a–f)
        public async Task<BalanceReport> GetRemainingBalancesAsync(string phone)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var cmd = new NpgsqlCommand(
                @"SELECT COALESCE(SUM(internet_left),0), COALESCE(SUM(sms_left),0), COALESCE(SUM(minute_left),0)
                  FROM customers_packet WHERE phone_number = @p AND isactive = TRUE", conn);
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

        // Freezes EACH active package separately (with remaining and original data allowances).
        // Used for modern-style package-based display on the dashboard.
        public async Task<List<PacketUsage>> GetActivePacketsAsync(string phone)
        {
            var list = new List<PacketUsage>();
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var cmd = new NpgsqlCommand(
                @"SELECT cp.active_packet_id, cp.packet_id, p.plan_type, p.international_usage, cp.due_date,
                         cp.internet_left, cp.sms_left, cp.minute_left,
                         p.internet_size, p.sms_count, p.minute_count
                  FROM customers_packet cp
                  JOIN packets p ON cp.packet_id = p.packet_id
                  WHERE cp.phone_number = @p AND cp.isactive = TRUE
                  ORDER BY cp.due_date, cp.active_packet_id", conn);
            cmd.Parameters.AddWithValue("p", phone);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new PacketUsage
                {
                    ActivePacketId = r.GetInt32(0),
                    PacketId = r.GetInt32(1),
                    PlanType = r.GetString(2),
                    International = !r.IsDBNull(3) && r.GetBoolean(3),
                    DueDate = r.GetDateTime(4),
                    InternetLeft = r.IsDBNull(5) ? 0 : r.GetInt32(5),
                    SmsLeft = r.IsDBNull(6) ? 0 : r.GetInt32(6),
                    MinuteLeft = r.IsDBNull(7) ? 0 : r.GetInt32(7),
                    InternetSize = r.IsDBNull(8) ? 0 : r.GetInt32(8),
                    SmsCount = r.IsDBNull(9) ? 0 : r.GetInt32(9),
                    MinuteCount = r.IsDBNull(10) ? 0 : r.GetInt32(10)
                });
            return list;
        }

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

        public async Task<List<CorporateLine>> GetCorporateLinesAsync(int customerId)
        {
            var list = new List<CorporateLine>();
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var cmd = new NpgsqlCommand(
                @"SELECT u.first_name, u.last_name, ml.phone_number,
                         COALESCE(SUM(cp.internet_left),0), COALESCE(SUM(cp.sms_left),0), COALESCE(SUM(cp.minute_left),0)
                  FROM business_customer b
                  JOIN sim_card s        ON s.customerid    = b.customerid
                  JOIN mobile_line ml    ON ml.sim_id       = s.sim_id
                  LEFT JOIN ""User"" u    ON u.phone_number  = ml.phone_number
                  LEFT JOIN customers_packet cp ON cp.phone_number = ml.phone_number AND cp.isactive = TRUE
                  WHERE b.customerid = @cid
                  GROUP BY u.first_name, u.last_name, ml.phone_number
                  ORDER BY ml.phone_number", conn);
            cmd.Parameters.AddWithValue("cid", customerId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new CorporateLine
                {
                    FirstName = r.IsDBNull(0)?"-":r.GetString(0),
                    LastName = r.IsDBNull(1)?"":r.GetString(1),
                    Phone = r.GetString(2),
                    InternetLeft = Convert.ToInt32(r.GetValue(3)),
                    SmsLeft = Convert.ToInt32(r.GetValue(4)),
                    MinuteLeft = Convert.ToInt32(r.GetValue(5))
                });
            return list;
        }

        // Lines eligible for the giveaway (cooldown period has passed)
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

        // query service fr locked sims PUK + SIM TYPE (JOIN). Returns the structured
        // SimReport (PukNo + SimType) so the UI can mask PUK independently.
        public async Task<SimReport> GetSimDetailsAsync(string phone)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var cmd = new NpgsqlCommand(
                @"SELECT s.puk_no, s.sim_type FROM sim_card s
                  JOIN mobile_line m ON s.sim_id = m.sim_id WHERE m.phone_number = @p", conn);
            cmd.Parameters.AddWithValue("p", phone);
            await using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync()
                ? new SimReport { PukNo = r.GetString(0), SimType = r.GetString(1) }
                : null;
        }

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

    // helper models 
    public class LoginResult
    {
        public int CustomerId { get; set; }
        public bool IsBusiness { get; set; }
        public string DisplayName { get; set; }
    }

    public class CorporateLine
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Phone { get; set; }
        public int InternetLeft { get; set; }
        public int SmsLeft { get; set; }
        public int MinuteLeft { get; set; }
    }

    public class PacketCatalogItem
    {
        public int PacketId { get; set; }
        public string PlanType { get; set; }
        public int InternetSize { get; set; }
        public int SmsCount { get; set; }
        public int MinuteCount { get; set; }
        public bool International { get; set; }
        public decimal Fee { get; set; }
    }
}

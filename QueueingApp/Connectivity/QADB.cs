using Microsoft.Data.SqlClient;
using System.Data;

namespace QueueingApp.Connectivity
{
    public class QADB
    {
        private DatabaseConnectivity _conn;
        public QADB(string connectionString, bool keepAlive)
        {
            _conn = new(connectionString, keepAlive);
        }

        public static Dictionary<string, object?> RowToObject(DataRow row)
        {
            if (row == null)
            {
                return null!;
            }
            Dictionary<string, object?> dict = new();
            foreach (DataColumn col in row.Table.Columns)
            {
                object val = row[col];
                dict[col.ColumnName] = val == DBNull.Value ? null : val;
            }
            return dict;
        }

        public static List<Dictionary<string, object?>> TableToObject(DataTable table)
        {
            List<Dictionary<string, object?>> list = new();
            if (table == null)
            {
                return list;
            }
            foreach (DataRow row in table.Rows)
            {
                list.Add(RowToObject(row));
            }
            return list;
        }

        public int TokenValid(string token)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputString("@Token", token, 50));
            return _conn.ExecuteStoredProcedureAsScalarInt("CheckToken", sqlParams.ToArray());
        }

        public void AddAccessToken(long accountId, string token)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputLong("@AccountId", accountId));
            sqlParams.Add(_conn.GenerateInputString("@Token", token, 50));
            _conn.ExecuteStoredProcedureNoReturn("AddAccessToken", sqlParams.ToArray());
        }

        public void ExtendAccessToken(string token)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputString("@Token", token, 50));
            _conn.ExecuteStoredProcedureNoReturn("ExtendAccessToken", sqlParams.ToArray());
        }

        public void Signup(string email, string name, string password, string salt, int shopId, string? phoneNumber = null)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputString("@Email", email, 255));
            sqlParams.Add(_conn.GenerateInputString("@Name", name, 200));
            sqlParams.Add(_conn.GenerateInputString("@Password", password, 255));
            sqlParams.Add(_conn.GenerateInputString("@Salt", salt, 50));
            sqlParams.Add(_conn.GenerateInputInteger("@ShopId", shopId));
            sqlParams.Add(_conn.GenerateInputString("@PhoneNumber", phoneNumber, 30));
            _conn.ExecuteStoredProcedureNoReturn("CreateEmployee", sqlParams.ToArray());
        }

        /// <summary>New shop + its owner's staff login in one go. Returns UserId and ShopId.</summary>
        public DataRow RegisterShop(string ownerName, string email, string password, string salt, string? phoneNumber,
            string shopName, string? address, string? postcode, decimal? latitude, decimal? longitude)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputString("@OwnerName", ownerName, 200));
            sqlParams.Add(_conn.GenerateInputString("@Email", email, 255));
            sqlParams.Add(_conn.GenerateInputString("@Password", password, 255));
            sqlParams.Add(_conn.GenerateInputString("@Salt", salt, 50));
            sqlParams.Add(_conn.GenerateInputString("@PhoneNumber", phoneNumber, 30));
            sqlParams.Add(_conn.GenerateInputString("@ShopName", shopName, 200));
            sqlParams.Add(_conn.GenerateInputString("@Address", address, 300));
            sqlParams.Add(_conn.GenerateInputString("@Postcode", postcode, 10));
            if (latitude.HasValue && longitude.HasValue)
            {
                sqlParams.Add(_conn.GenerateInputDecimal("@Latitude", latitude.Value));
                sqlParams.Add(_conn.GenerateInputDecimal("@Longitude", longitude.Value));
            }
            return _conn.ExecuteStoredProcedureAsDataRow("RegisterShop", sqlParams.ToArray());
        }

        public void LogError(string source, string message, long accountId)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputString("@SourceLocation", source, 512));
            sqlParams.Add(_conn.GenerateInputString("@ErrorMessage", message, 4000));
            sqlParams.Add(_conn.GenerateInputLong("@AccountId", accountId));
            _conn.ExecuteStoredProcedureNoReturn("LogError", sqlParams.ToArray());
        }

        public DataRow GetAccountByEmail(string email)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputString("@Email", email, 255));
            return _conn.ExecuteStoredProcedureAsDataRow("GetEmployeeByEmail", sqlParams.ToArray());
        }

        public DataRow GetUserById(int id)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputInteger("@Id", id));
            return _conn.ExecuteStoredProcedureAsDataRow("GetUserById", sqlParams.ToArray());
        }

        public void TouchLastLogin(int id)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputInteger("@Id", id));
            _conn.ExecuteStoredProcedureNoReturn("UsersTouchLastLogin", sqlParams.ToArray());
        }

        public DataRow GetShopFromAccount(long accountId)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputLong("@Id", accountId));
            return _conn.ExecuteStoredProcedureAsDataRow("GetShopFromAccount", sqlParams.ToArray());
        }

        public void CreateShop(string name, int adminId, DateTime openingTime,
            DateTime closingTime, int? queueLimit, DateTime? lunchBreak, string? qrCode,
            string? address = null, string? postcode = null, decimal? latitude = null, decimal? longitude = null,
            int? joinRadiusMetres = null)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputString("@Name", name, 200));
            sqlParams.Add(_conn.GenerateInputInteger("@AdminId", adminId));
            sqlParams.Add(_conn.GenerateInputDateTime("@OpeningTime", openingTime));
            sqlParams.Add(_conn.GenerateInputDateTime("@ClosingTime", closingTime));
            sqlParams.Add(_conn.GenerateInputInteger("@QueueLimit", queueLimit ?? 0));
            sqlParams.Add(_conn.GenerateInputDateTime("@LunchBreak", lunchBreak));
            sqlParams.Add(_conn.GenerateInputString("@QrCode", qrCode, 100));
            sqlParams.Add(_conn.GenerateInputString("@Address", address, 300));
            sqlParams.Add(_conn.GenerateInputString("@Postcode", postcode, 10));
            if (latitude.HasValue && longitude.HasValue)
            {
                sqlParams.Add(_conn.GenerateInputDecimal("@Latitude", latitude.Value));
                sqlParams.Add(_conn.GenerateInputDecimal("@Longitude", longitude.Value));
            }
            if (joinRadiusMetres.HasValue)
            {
                sqlParams.Add(_conn.GenerateInputInteger("@JoinRadiusMetres", joinRadiusMetres.Value));
            }
            _conn.ExecuteStoredProcedureNoReturn("CreateShop", sqlParams.ToArray());
        }

        public int EmployeesCountAll()
        {
            return _conn.ExecuteStoredProcedureAsScalarInt("EmployeesCountAll", null);
        }

        public DataRow ShopsGetById(int id)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputInteger("@Id", id));
            return _conn.ExecuteStoredProcedureAsDataRow("ShopsGetById", sqlParams.ToArray());
        }

        public DataTable ShopsGet()
        {
            return _conn.ExecuteStoredProcedureAsDataTable("ShopsGet", null);
        }

        public DataRow ShopsGetByQrCode(string qrCode)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputString("@QrCode", qrCode, 100));
            return _conn.ExecuteStoredProcedureAsDataRow("ShopsGetByQrCode", sqlParams.ToArray());
        }

        public DataRow ShopsUpdateLocation(int shopId, string? address, string? postcode, decimal latitude,
            decimal longitude, int? joinRadiusMetres)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputInteger("@ShopId", shopId));
            sqlParams.Add(_conn.GenerateInputString("@Address", address, 300));
            sqlParams.Add(_conn.GenerateInputString("@Postcode", postcode, 10));
            sqlParams.Add(_conn.GenerateInputDecimal("@Latitude", latitude));
            sqlParams.Add(_conn.GenerateInputDecimal("@Longitude", longitude));
            if (joinRadiusMetres.HasValue)
            {
                sqlParams.Add(_conn.GenerateInputInteger("@JoinRadiusMetres", joinRadiusMetres.Value));
            }
            return _conn.ExecuteStoredProcedureAsDataRow("ShopsUpdateLocation", sqlParams.ToArray());
        }

        public DataRow ShopsRegenerateQrCode(int shopId)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputInteger("@ShopId", shopId));
            return _conn.ExecuteStoredProcedureAsDataRow("ShopsRegenerateQrCode", sqlParams.ToArray());
        }

        public DataRow PendingJoinsCreate(int shopId, string name, string email, string phoneNumber, string otpHash, string otpSalt)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputInteger("@ShopId", shopId));
            sqlParams.Add(_conn.GenerateInputString("@Name", name, 100));
            sqlParams.Add(_conn.GenerateInputString("@Email", email, 255));
            sqlParams.Add(_conn.GenerateInputString("@PhoneNumber", phoneNumber, 20));
            sqlParams.Add(_conn.GenerateInputString("@OtpHash", otpHash, 255));
            sqlParams.Add(_conn.GenerateInputString("@OtpSalt", otpSalt, 255));
            return _conn.ExecuteStoredProcedureAsDataRow("PendingJoinsCreate", sqlParams.ToArray());
        }

        public DataRow PendingJoinsGetById(int id)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputInteger("@Id", id));
            return _conn.ExecuteStoredProcedureAsDataRow("PendingJoinsGetById", sqlParams.ToArray());
        }

        public int PendingJoinsRecordAttempt(int id)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputInteger("@Id", id));
            return _conn.ExecuteStoredProcedureAsScalarInt("PendingJoinsRecordAttempt", sqlParams.ToArray());
        }

        /// <summary>Counts one code guess before it's checked. Returns attempts used, or -1 if none are left.</summary>
        public int PendingJoinsTakeAttempt(int id, int maxAttempts)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputInteger("@Id", id));
            sqlParams.Add(_conn.GenerateInputInteger("@MaxAttempts", maxAttempts));
            return _conn.ExecuteStoredProcedureAsScalarInt("PendingJoinsTakeAttempt", sqlParams.ToArray());
        }

        public DataRow PendingJoinsResend(int id, string otpHash, string otpSalt)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputInteger("@Id", id));
            sqlParams.Add(_conn.GenerateInputString("@OtpHash", otpHash, 255));
            sqlParams.Add(_conn.GenerateInputString("@OtpSalt", otpSalt, 255));
            return _conn.ExecuteStoredProcedureAsDataRow("PendingJoinsResend", sqlParams.ToArray());
        }

        public void PendingJoinsComplete(int id, int queueEntryId)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputInteger("@Id", id));
            sqlParams.Add(_conn.GenerateInputInteger("@QueueEntryId", queueEntryId));
            _conn.ExecuteStoredProcedureNoReturn("PendingJoinsComplete", sqlParams.ToArray());
        }

        public DataTable EmployeesGet(int shopId)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputInteger("@ShopId", shopId));
            return _conn.ExecuteStoredProcedureAsDataTable("EmployeesGet", sqlParams.ToArray());
        }

        /// <summary>waitingCap: the plan's cap on waiting customers (0 = none), enforced inside the join transaction.</summary>
        public DataRow CreateCustomerJoinQueue(string name, string email, string phoneNumber, int shopId, int waitingCap = 0)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputString("@Name", name, 200));
            sqlParams.Add(_conn.GenerateInputString("@Email", email, 255));
            sqlParams.Add(_conn.GenerateInputString("@PhoneNumber", phoneNumber, 30));
            sqlParams.Add(_conn.GenerateInputInteger("@ShopId", shopId));
            sqlParams.Add(_conn.GenerateInputInteger("@WaitingCap", waitingCap));
            return _conn.ExecuteStoredProcedureAsDataRow("CreateCustomerJoinQueue", sqlParams.ToArray());
        }

        public DataTable QueueEntriesGet(int shopId, int status)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputInteger("@ShopId", shopId));
            sqlParams.Add(_conn.GenerateInputInteger("@Status", status));
            return _conn.ExecuteStoredProcedureAsDataTable("QueueEntriesGet", sqlParams.ToArray());
        }

        public DataTable QueueEntriesGetHistory(int shopId)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputInteger("@ShopId", shopId));
            return _conn.ExecuteStoredProcedureAsDataTable("QueueEntriesGetHistory", sqlParams.ToArray());
        }

        public DataRow QueueEntriesGetById(int id)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputInteger("@Id", id));
            return _conn.ExecuteStoredProcedureAsDataRow("QueueEntriesGetById", sqlParams.ToArray());
        }

        /// <summary>dbo.AppSettings.SmsLive: true = send real texts, false = demo mode.</summary>
        /// <summary>One row: SecretKey, WebhookSecret, LimitedMonthly, LimitedYearly, FullMonthly, FullYearly.</summary>
        public DataRow StripeSettingsGet()
        {
            return _conn.ExecuteStoredProcedureAsDataRow("StripeSettingsGet", null);
        }

        public bool AppSettingsGetSmsLive()
        {
            DataRow row = _conn.ExecuteStoredProcedureAsDataRow("AppSettingsGet", null);
            return row != null && row["SmsLive"] != DBNull.Value && Convert.ToBoolean(row["SmsLive"]);
        }

        /// <summary>Gives back one text from this month's count (the text was counted but Twilio didn't send it).</summary>
        public void ShopSmsRelease(int shopId)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputInteger("@ShopId", shopId));
            _conn.ExecuteStoredProcedureNoReturn("ShopSmsRelease", sqlParams.ToArray());
        }

        /// <summary>1 if a text may be sent (and it's counted), 0 if this month's allowance is used up. limit &lt; 0 = unlimited.</summary>
        public int ShopSmsTryConsume(int shopId, int limit)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputInteger("@ShopId", shopId));
            sqlParams.Add(_conn.GenerateInputInteger("@Limit", limit));
            return _conn.ExecuteStoredProcedureAsScalarInt("ShopSmsTryConsume", sqlParams.ToArray());
        }

        public int ShopSmsUsageGet(int shopId)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputInteger("@ShopId", shopId));
            return _conn.ExecuteStoredProcedureAsScalarInt("ShopSmsUsageGet", sqlParams.ToArray());
        }

        public void ShopsSetStripeCustomer(int shopId, string stripeCustomerId)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputInteger("@ShopId", shopId));
            sqlParams.Add(_conn.GenerateInputString("@StripeCustomerId", stripeCustomerId, 64));
            _conn.ExecuteStoredProcedureNoReturn("ShopsSetStripeCustomer", sqlParams.ToArray());
        }

        public DataRow ShopsGetByStripeCustomer(string stripeCustomerId)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputString("@StripeCustomerId", stripeCustomerId, 64));
            return _conn.ExecuteStoredProcedureAsDataRow("ShopsGetByStripeCustomer", sqlParams.ToArray());
        }

        public void ShopsUpdateSubscription(int shopId, string stripeCustomerId, string stripeSubscriptionId, string plan,
            string status, string? interval, DateTime? currentPeriodEnd, bool cancelAtPeriodEnd)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputInteger("@ShopId", shopId));
            sqlParams.Add(_conn.GenerateInputString("@StripeCustomerId", stripeCustomerId, 64));
            sqlParams.Add(_conn.GenerateInputString("@StripeSubscriptionId", stripeSubscriptionId, 64));
            sqlParams.Add(_conn.GenerateInputString("@Plan", plan, 10));
            sqlParams.Add(_conn.GenerateInputString("@SubscriptionStatus", status, 20));
            sqlParams.Add(_conn.GenerateInputString("@BillingInterval", interval, 10));
            sqlParams.Add(_conn.GenerateInputDateTime("@CurrentPeriodEnd", currentPeriodEnd));
            sqlParams.Add(_conn.GenerateInputBool("@CancelAtPeriodEnd", cancelAtPeriodEnd));
            _conn.ExecuteStoredProcedureNoReturn("ShopsUpdateSubscription", sqlParams.ToArray());
        }

        /// <summary>1 the first time a Stripe event id is seen, 0 for repeats.</summary>
        public int BillingEventsTryRecord(string eventId, string type, int? shopId)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputString("@EventId", eventId, 100));
            sqlParams.Add(_conn.GenerateInputString("@Type", type, 100));
            if (shopId.HasValue)
            {
                sqlParams.Add(_conn.GenerateInputInteger("@ShopId", shopId.Value));
            }
            return _conn.ExecuteStoredProcedureAsScalarInt("BillingEventsTryRecord", sqlParams.ToArray());
        }

        public DataTable QueueServiceDurations(int shopId)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputInteger("@ShopId", shopId));
            return _conn.ExecuteStoredProcedureAsDataTable("QueueServiceDurations", sqlParams.ToArray());
        }

        public DataRow ShopsUpdateQueueSettings(int shopId, int queueLimit, string etaMode, int minutesPerCustomer)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputInteger("@ShopId", shopId));
            sqlParams.Add(_conn.GenerateInputInteger("@QueueLimit", queueLimit));
            sqlParams.Add(_conn.GenerateInputString("@EtaMode", etaMode, 10));
            sqlParams.Add(_conn.GenerateInputInteger("@MinutesPerCustomer", minutesPerCustomer));
            return _conn.ExecuteStoredProcedureAsDataRow("ShopsUpdateQueueSettings", sqlParams.ToArray());
        }

        public DataRow ShopsSetBreak(int shopId, int minutes, bool pauseJoins)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputInteger("@ShopId", shopId));
            sqlParams.Add(_conn.GenerateInputInteger("@Minutes", minutes));
            sqlParams.Add(_conn.GenerateInputBool("@PauseJoins", pauseJoins));
            return _conn.ExecuteStoredProcedureAsDataRow("ShopsSetBreak", sqlParams.ToArray());
        }

        public DataRow QueueEntriesMove(int id, int newPosition)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputInteger("@Id", id));
            sqlParams.Add(_conn.GenerateInputInteger("@NewPosition", newPosition));
            return _conn.ExecuteStoredProcedureAsDataRow("QueueEntriesMove", sqlParams.ToArray());
        }

        /// <summary>The first waiting customer, if they haven't had their "you're next" text yet (null otherwise).</summary>
        public DataRow QueueEntriesClaimNextNotify(int shopId)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputInteger("@ShopId", shopId));
            return _conn.ExecuteStoredProcedureAsDataRow("QueueEntriesClaimNextNotify", sqlParams.ToArray());
        }

        public DataRow QueueEntriesRecordPing(int id, string message)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputInteger("@Id", id));
            sqlParams.Add(_conn.GenerateInputString("@Message", message, 300));
            return _conn.ExecuteStoredProcedureAsDataRow("QueueEntriesRecordPing", sqlParams.ToArray());
        }

        public DataRow QueueEntriesUpdateStatus(int id, int status)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputInteger("@Id", id));
            sqlParams.Add(_conn.GenerateInputInteger("@Status", status));
            return _conn.ExecuteStoredProcedureAsDataRow("QueueEntriesUpdateStatus", sqlParams.ToArray());
        }
    }
}

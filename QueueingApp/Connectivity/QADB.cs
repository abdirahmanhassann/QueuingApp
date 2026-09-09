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
            DateTime closingTime, int? queueLimit, DateTime? lunchBreak, string qrCode)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputString("@Name", name, 200));
            sqlParams.Add(_conn.GenerateInputInteger("@AdminId", adminId));
            sqlParams.Add(_conn.GenerateInputDateTime("@OpeningTime", openingTime));
            sqlParams.Add(_conn.GenerateInputDateTime("@ClosingTime", closingTime));
            sqlParams.Add(_conn.GenerateInputInteger("@QueueLimit", queueLimit ?? 0));
            sqlParams.Add(_conn.GenerateInputDateTime("@LunchBreak", lunchBreak));
            sqlParams.Add(_conn.GenerateInputString("@QrCode", qrCode, 100));
            _conn.ExecuteStoredProcedureNoReturn("CreateShop", sqlParams.ToArray());
        }

        public DataTable ShopsGet()
        {
            return _conn.ExecuteStoredProcedureAsDataTable("ShopsGet", null);
        }

        public DataTable EmployeesGet(int shopId)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputInteger("@ShopId", shopId));
            return _conn.ExecuteStoredProcedureAsDataTable("EmployeesGet", sqlParams.ToArray());
        }

        public DataRow CreateCustomerJoinQueue(string name, string email, string phoneNumber, int shopId)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputString("@Name", name, 200));
            sqlParams.Add(_conn.GenerateInputString("@Email", email, 255));
            sqlParams.Add(_conn.GenerateInputString("@PhoneNumber", phoneNumber, 30));
            sqlParams.Add(_conn.GenerateInputInteger("@ShopId", shopId));
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

        public DataRow QueueEntriesUpdateStatus(int id, int status)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputInteger("@Id", id));
            sqlParams.Add(_conn.GenerateInputInteger("@Status", status));
            return _conn.ExecuteStoredProcedureAsDataRow("QueueEntriesUpdateStatus", sqlParams.ToArray());
        }
    }
}

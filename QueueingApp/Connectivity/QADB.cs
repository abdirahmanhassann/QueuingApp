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

        public long TokenValid(string token)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputString("@Token", token, 50));
            return _conn.ExecuteStoredProcedureAsScalarLong("account.CheckToken", sqlParams.ToArray());
        }

        public void AddAccessToken(long accountId, string token)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputLong("@AccountId", accountId));
            sqlParams.Add(_conn.GenerateInputString("@Token", token, 50));
            _conn.ExecuteStoredProcedureNoReturn("account.AddAccessToken", sqlParams.ToArray());
        }

        public void ExtendAccessToken(string token)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputString("@Token", token, 50));
            _conn.ExecuteStoredProcedureNoReturn("account.ExtendAccessToken", sqlParams.ToArray());
        }

        public void Signup(string email, string name, string password, string salt, int shopId)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputString("@Email", email, 20));
            sqlParams.Add(_conn.GenerateInputString("@Name", name, 50));
            sqlParams.Add(_conn.GenerateInputString("@Password", password, 50));
            sqlParams.Add(_conn.GenerateInputString("@Salt", salt, 12));
            sqlParams.Add(_conn.GenerateInputInteger("@ShopId", shopId));
            _conn.ExecuteStoredProcedureNoReturn("CreateEmployee", sqlParams.ToArray());
        }

        public void LogError(string source, string message, long accountId)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputString("@SourceLocation", source, 512));
            sqlParams.Add(_conn.GenerateInputString("@ErrorMessage", message, 4000));
            sqlParams.Add(_conn.GenerateInputLong("@AccountId", accountId));
            _conn.ExecuteStoredProcedureNoReturn("logging.LogError", sqlParams.ToArray());
        }

        public DataRow GetAccountByEmail(string email)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputString("@Email", email, 255));
            return _conn.ExecuteStoredProcedureAsDataRow("GetEmployeeByEmail", sqlParams.ToArray());
        }

        public DataRow GetShopFromAccount(long accountId)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputLong("@Id", accountId));
            return _conn.ExecuteStoredProcedureAsDataRow("GetShopFromAccount", sqlParams.ToArray());
        }
    }
}

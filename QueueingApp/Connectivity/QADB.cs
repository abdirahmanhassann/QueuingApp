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

        public void Signup(string email, string displayName, string password, string salt, string gymName, long countryId, long cityId)
        {
            List<SqlParameter> sqlParams = new();
            sqlParams.Add(_conn.GenerateInputString("@Email", email, 255));
            sqlParams.Add(_conn.GenerateInputString("@DisplayName", displayName, 25));
            sqlParams.Add(_conn.GenerateInputString("@Password", password, 255));
            sqlParams.Add(_conn.GenerateInputString("@Salt", salt, 12));
            sqlParams.Add(_conn.GenerateInputString("@GymName", gymName, 50));
            sqlParams.Add(_conn.GenerateInputLong("@CountryId", countryId));
            sqlParams.Add(_conn.GenerateInputLong("@CityId", cityId));
            _conn.ExecuteStoredProcedureNoReturn("account.CreateNew", sqlParams.ToArray());
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
            return _conn.ExecuteStoredProcedureAsDataRow("account.GetByEmail", sqlParams.ToArray());
        }

    }
}

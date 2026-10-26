using Microsoft.Data.SqlClient;
using QueueingApp.Connectivity;
using QueueingApp.Interfaces;
using System.Data;

namespace QueueingApp.Interfaces
{
    public interface IDatabaseConnectivity
    {
        class DatabaseConnectivity(string connectionString, bool keepAlive = false);
        SqlParameter GenerateInputInteger(string paramName, int val);
        SqlParameter GenerateInputDecimal(string paramName, decimal val);
        SqlParameter GenerateInputString(string paramName, string? val, int length = 2000);
        SqlParameter GenerateInputDateTime(string paramName, DateTime? val);
        SqlParameter GenerateInputLong(string paramName, long val);
        SqlParameter GenerateInputBool(string paramName, bool val);
        SqlParameter GenerateInputCurrency(string paramName, decimal val);
        SqlParameter GenerateInputStringList(string paramName, DataTable vals);
        SqlParameter GenerateInputIntList(string paramName, DataTable vals);
        DataTable ExecuteStoredProcedureAsDataTable(string spName, SqlParameter[]? sqlParams);
        DataRow? ExecuteStoredProcedureAsDataRow(string spName, SqlParameter[]? sqlParams);
        void ExecuteStoredProcedureNoReturn(string spName, SqlParameter[]? sqlParams);
        int ExecuteStoredProcedureAsScalarInt(string spName, SqlParameter[]? sqlParams);
        long ExecuteStoredProcedureAsScalarLong(string spName, SqlParameter[]? sqlParams);
        string? ExecuteStoredProcedureAsScalarString(string spName, SqlParameter[]? sqlParams);
        DataTable ExecuteRawSQLAsDataTable(string sql);
        DataRow ExecuteRawSQLAsDataRow(string sql);
        void ExecuteRawSQLNoReturn(string sql);
        void CloseConnection();
    }
}
public interface IConnection : IDatabaseConnectivity
{
    string Connection();
}


public interface IKeepAlive
{
    bool KeepAlive();
}


public interface IAccountProccesor
{

}
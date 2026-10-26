using Microsoft.Data.SqlClient;
using QueueingApp.Interfaces;
using System.Data;

namespace QueueingApp.Connectivity
{
    public class DatabaseConnectivity : IDatabaseConnectivity
    {

        private SqlConnection _sqlConnection;
        private bool _keepAlive;
        public DatabaseConnectivity(string connectionString, bool keepAlive = false)
        {
            _sqlConnection = new SqlConnection(connectionString);
            if (keepAlive)
            {
                _sqlConnection.Open();
            }
            _keepAlive = keepAlive;
        }

        void IDatabaseConnectivity.CloseConnection()
        {
            _sqlConnection?.Close();
        }


        SqlParameter IDatabaseConnectivity.GenerateInputInteger(string paramName, int val)
        {
            return new SqlParameter(paramName, SqlDbType.Int, 0)
            {
                Value = val
            };
        }

        SqlParameter IDatabaseConnectivity.GenerateInputDecimal(string paramName, decimal val)
        {
            return new SqlParameter(paramName, SqlDbType.Decimal, 0)
            {
                Value = val
            };
        }


        SqlParameter IDatabaseConnectivity.GenerateInputString(string paramName, string? val, int length = 2000)
        {
            return new SqlParameter(paramName, SqlDbType.NVarChar, length)
            {
                Value = (object?)val ?? DBNull.Value
            };
        }

        SqlParameter IDatabaseConnectivity.GenerateInputDateTime(string paramName, DateTime? val)
        {
            // datetime2 matches the table columns; plain datetime can't hold 0001-01-01 and loses precision.
            return new SqlParameter(paramName, SqlDbType.DateTime2)
            {
                Value = (object?)val ?? DBNull.Value
            };
        }

        SqlParameter IDatabaseConnectivity.GenerateInputLong(string paramName, long val)
        {
            return new SqlParameter(paramName, SqlDbType.BigInt, 0)
            {
                Value = val
            };
        }

        SqlParameter IDatabaseConnectivity.GenerateInputBool(string paramName, bool val)
        {
            return new SqlParameter(paramName, SqlDbType.Bit, 0)
            {
                Value = val
            };
        }

        SqlParameter IDatabaseConnectivity.GenerateInputCurrency(string paramName, decimal val)
        {
            return new SqlParameter(paramName, SqlDbType.Money, 0)
            {
                Value = val,
                Precision = 4
            };
        }

        SqlParameter IDatabaseConnectivity.GenerateInputStringList(string paramName, DataTable vals)
        {

            SqlParameter ret = new()
            {
                Value = vals,
                TypeName = "dbo.StringList",
                SqlDbType = SqlDbType.Structured,
                ParameterName = paramName
            };

            return ret;

        }

        SqlParameter IDatabaseConnectivity.GenerateInputIntList(string paramName, DataTable vals)
        {

            SqlParameter ret = new()
            {
                Value = vals,
                TypeName = "dbo.IntList",
                SqlDbType = SqlDbType.Structured,
                ParameterName = paramName
            };

            return ret;

        }

        DataTable IDatabaseConnectivity.ExecuteStoredProcedureAsDataTable(string spName, SqlParameter[]? sqlParams)
        {
            Exception thrownException = null!;
            DataTable table = new DataTable();
            try
            {
                if (!_keepAlive)
                {
                    _sqlConnection.Open();
                }
                SqlCommand sqlCommand = new SqlCommand(spName, _sqlConnection);
                sqlCommand.CommandType = CommandType.StoredProcedure;
                if (sqlParams != null)
                {
                    sqlCommand.Parameters.AddRange(sqlParams);
                }
                SqlDataAdapter adapter = new SqlDataAdapter(sqlCommand);
                adapter.Fill(table);
            }
            catch (Exception ex)
            {
                thrownException = ex;
            }
            finally
            {
                if (!_keepAlive)
                {
                    _sqlConnection.Close();
                }
            }
            if (thrownException != null)
            {
                throw thrownException;
            }
            else
            {
                return table;
            }
        }


        DataRow IDatabaseConnectivity.ExecuteStoredProcedureAsDataRow(string spName, SqlParameter[]? sqlParams)
        {
            Exception thrownException = null!;
            DataTable table = new DataTable();
            try
            {
                if (!_keepAlive)
                {
                    _sqlConnection.Open();
                }
                SqlCommand sqlCommand = new SqlCommand(spName, _sqlConnection);
                sqlCommand.CommandType = CommandType.StoredProcedure;
                if (sqlParams != null)
                {
                    sqlCommand.Parameters.AddRange(sqlParams);
                }
                SqlDataAdapter adapter = new SqlDataAdapter(sqlCommand);
                adapter.Fill(table);
            }
            catch (Exception ex)
            {
                thrownException = ex;
            }
            finally
            {
                if (!_keepAlive)
                {
                    _sqlConnection.Close();
                }
            }
            if (thrownException != null)
            {
                throw thrownException;
            }
            else
            {
                return table.Rows.Count > 0 ? table.Rows[0] : null!;
            }
        }


        void IDatabaseConnectivity.ExecuteStoredProcedureNoReturn(string spName, SqlParameter[]? sqlParams)
        {
            Exception thrownException = null!;
            try
            {
                if (!_keepAlive)
                {
                    _sqlConnection.Open();
                }
                SqlCommand sqlCommand = new SqlCommand(spName, _sqlConnection);
                sqlCommand.CommandType = CommandType.StoredProcedure;
                if (sqlParams != null)
                {
                    sqlCommand.Parameters.AddRange(sqlParams);
                }
                sqlCommand.ExecuteNonQuery();

            }
            catch (Exception ex)
            {
                thrownException = ex;

            }
            finally
            {
                if (!_keepAlive)
                {
                    _sqlConnection.Close();
                }
            }
            if (thrownException != null)
            {
                throw thrownException;
            }

        }

        int IDatabaseConnectivity.ExecuteStoredProcedureAsScalarInt(string spName, SqlParameter[]? sqlParams)
        {
            Exception thrownException = null!;
            int scalarInt = 0;
            try
            {
                if (!_keepAlive)
                {
                    _sqlConnection.Open();
                }
                SqlCommand sqlCommand = new SqlCommand(spName, _sqlConnection);
                sqlCommand.CommandType = CommandType.StoredProcedure;
                if (sqlParams != null)
                {
                    sqlCommand.Parameters.AddRange(sqlParams);
                }
                scalarInt = Convert.ToInt32(sqlCommand.ExecuteScalar());
            }
            catch (Exception ex)
            {
                thrownException = ex;
            }
            finally
            {
                if (!_keepAlive)
                {
                    _sqlConnection.Close();
                }
            }
            if (thrownException != null)
            {
                throw thrownException;
            }
            else
            {
                return scalarInt;
            }


        }

        long IDatabaseConnectivity.ExecuteStoredProcedureAsScalarLong(string spName, SqlParameter[]? sqlParams)
        {
            Exception thrownException = null!;
            long scalarLong = 0;
            try
            {
                if (!_keepAlive)
                {
                    _sqlConnection.Open();
                }
                SqlCommand sqlCommand = new SqlCommand(spName, _sqlConnection);
                sqlCommand.CommandType = CommandType.StoredProcedure;
                if (sqlParams != null)
                {
                    sqlCommand.Parameters.AddRange(sqlParams);
                }
                scalarLong = Convert.ToInt64(sqlCommand.ExecuteScalar());
            }
            catch (Exception ex)
            {
                thrownException = ex;
            }
            finally
            {
                if (!_keepAlive)
                {
                    _sqlConnection.Close();
                }
            }
            if (thrownException != null)
            {
                throw thrownException;
            }
            else
            {
                return scalarLong;
            }

        }

        string? IDatabaseConnectivity.ExecuteStoredProcedureAsScalarString(string spName, SqlParameter[]? sqlParams)
        {
            Exception thrownException = null!;
            string scalarString = "";
            try
            {
                if (!_keepAlive)
                {
                    _sqlConnection.Open();
                }
                SqlCommand sqlCommand = new SqlCommand(spName, _sqlConnection);
                sqlCommand.CommandType = CommandType.StoredProcedure;
                if (sqlParams != null)
                {
                    sqlCommand.Parameters.AddRange(sqlParams);
                }
                scalarString = Convert.ToString(sqlCommand.ExecuteScalar())!;
            }
            catch (Exception ex)
            {
                thrownException = ex;
            }
            finally
            {
                if (!_keepAlive)
                {
                    _sqlConnection.Close();
                }
            }
            if (thrownException != null)
            {
                throw thrownException;
            }
            else
            {
                return scalarString;
            }
        }




        DataTable IDatabaseConnectivity.ExecuteRawSQLAsDataTable(string sql)
        {
            Exception thrownException = null!;
            DataTable table = new DataTable();
            try
            {
                if (!_keepAlive)
                {
                    _sqlConnection.Open();
                }
                SqlCommand sqlCommand = new SqlCommand(sql, _sqlConnection);
                SqlDataAdapter adapter = new SqlDataAdapter(sqlCommand);
                adapter.Fill(table);
            }
            catch (Exception ex)
            {
                thrownException = ex;
            }
            finally
            {
                if (!_keepAlive)
                {
                    _sqlConnection.Close();
                }
            }
            if (thrownException != null)
            {
                throw thrownException;
            }
            else
            {
                return table;
            }
        }


        DataRow IDatabaseConnectivity.ExecuteRawSQLAsDataRow(string sql)
        {
            Exception thrownException = null!;
            DataRow row = null!;
            try
            {
                if (!_keepAlive)
                {
                    _sqlConnection.Open();
                }
                DataTable table = new DataTable();
                SqlCommand sqlCommand = new SqlCommand(sql, _sqlConnection);
                SqlDataAdapter adapter = new SqlDataAdapter(sqlCommand);

                adapter.Fill(table);
                row = table.Rows[0];
            }

            catch (Exception ex)
            {
                thrownException = ex;
            }

            finally
            {
                if (!_keepAlive)
                {
                    _sqlConnection.Close();
                }
            }
            if (thrownException != null)
            {
                throw thrownException;
            }
            else
            {
                return row;
            }
        }


        void IDatabaseConnectivity.ExecuteRawSQLNoReturn(string sql)
        {
            Exception thrownException = null!;
            try
            {
                if (!_keepAlive)
                {
                    _sqlConnection.Open();
                }
                SqlCommand sqlCommand = new SqlCommand(sql, _sqlConnection);

                sqlCommand.ExecuteNonQuery();
            }

            catch (Exception ex)
            {
                thrownException = ex;
            }

            finally
            {
                if (!_keepAlive)
                {
                    _sqlConnection.Close();
                }
            }
            if (thrownException != null)
            {
                throw thrownException;
            }
        }


    }
}
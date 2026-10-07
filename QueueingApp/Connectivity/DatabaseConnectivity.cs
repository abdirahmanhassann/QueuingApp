using Microsoft.Data.SqlClient;
using System.Data;

namespace QueueingApp.Connectivity
{
    public class DatabaseConnectivity
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

        public void CloseConnection()
        {
            _sqlConnection?.Close();
        }


        public SqlParameter GenerateInputInteger(string paramName, int val)
        {
            return new SqlParameter(paramName, SqlDbType.Int, 0)
            {
                Value = val
            };
        }

        public SqlParameter GenerateInputDecimal(string paramName, decimal val)
        {
            return new SqlParameter(paramName, SqlDbType.Decimal, 0)
            {
                Value = val
            };
        }


        public SqlParameter GenerateInputString(string paramName, string? val, int length = 2000)
        {
            return new SqlParameter(paramName, SqlDbType.NVarChar, length)
            {
                Value = (object?)val ?? DBNull.Value
            };
        }

        public SqlParameter GenerateInputDateTime(string paramName, DateTime? val)
        {
            // datetime2 matches the table columns; plain datetime can't hold 0001-01-01 and loses precision.
            return new SqlParameter(paramName, SqlDbType.DateTime2)
            {
                Value = (object?)val ?? DBNull.Value
            };
        }

        public SqlParameter GenerateInputLong(string paramName, long val)
        {
            return new SqlParameter(paramName, SqlDbType.BigInt, 0)
            {
                Value = val
            };
        }

        public SqlParameter GenerateInputBool(string paramName, bool val)
        {
            return new SqlParameter(paramName, SqlDbType.Bit, 0)
            {
                Value = val
            };
        }

        public SqlParameter GenerateInputCurrency(string paramName, decimal val)
        {
            return new SqlParameter(paramName, SqlDbType.Money, 0)
            {
                Value = val,
                Precision = 4
            };
        }

        public SqlParameter GenerateInputStringList(string paramName, DataTable vals)
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

        public SqlParameter GenerateInputIntList(string paramName, DataTable vals)
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

        public DataTable ExecuteStoredProcedureAsDataTable(string spName, SqlParameter[]? sqlParams)
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


        public DataRow ExecuteStoredProcedureAsDataRow(string spName, SqlParameter[]? sqlParams)
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


        public void ExecuteStoredProcedureNoReturn(string spName, SqlParameter[]? sqlParams)
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

        public int ExecuteStoredProcedureAsScalarInt(string spName, SqlParameter[]? sqlParams)
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

        public long ExecuteStoredProcedureAsScalarLong(string spName, SqlParameter[]? sqlParams)
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

        public string? ExecuteStoredProcedureAsScalarString(string spName, SqlParameter[]? sqlParams)
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




        public DataTable ExecuteRawSQLAsDataTable(string sql)
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


        public DataRow ExecuteRawSQLAsDataRow(string sql)
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


        public void ExecuteRawSQLNoReturn(string sql)
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

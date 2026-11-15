using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace eShop.DataStore.SQL.Dapper
{
    public class DataAccess : IDataAccess
    {
        private readonly string connectionString;
        private readonly IDbConnection transactionConnection;
        private readonly IDbTransaction transaction;

        public DataAccess(string connectionString)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
            this.connectionString = connectionString;
        }

        private DataAccess(IDbConnection connection, IDbTransaction transaction)
        {
            transactionConnection = connection;
            this.transaction = transaction;
        }

        public List<T> Query<T, U>(string sql, U parameters)
            => WithConnection((connection, tx) => connection.Query<T>(sql, parameters, tx).ToList());

        public T QuerySingle<T, U>(string sql, U parameters)
            => WithConnection((connection, tx) => connection.QuerySingle<T>(sql, parameters, tx));

        public T QuerySingleOrDefault<T, U>(string sql, U parameters)
            => WithConnection((connection, tx) => connection.QuerySingleOrDefault<T>(sql, parameters, tx));

        public int ExecuteCommand<T>(string sql, T parameters)
            => WithConnection((connection, tx) => connection.Execute(sql, parameters, tx));

        public T InTransaction<T>(Func<IDataAccess, T> operation, IsolationLevel isolationLevel = IsolationLevel.Serializable)
        {
            ArgumentNullException.ThrowIfNull(operation);
            if (transactionConnection != null)
                return operation(this);

            using var connection = new SqlConnection(connectionString);
            connection.Open();
            using var tx = connection.BeginTransaction(isolationLevel);
            // Disposing an uncommitted transaction rolls back every operation on this session.
            var result = operation(new DataAccess(connection, tx));
            tx.Commit();
            return result;
        }

        private T WithConnection<T>(Func<IDbConnection, IDbTransaction, T> operation)
        {
            if (transactionConnection != null)
                return operation(transactionConnection, transaction);

            using var connection = new SqlConnection(connectionString);
            return operation(connection, null);
        }
    }
}

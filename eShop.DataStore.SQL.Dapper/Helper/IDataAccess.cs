using System;
using System.Collections.Generic;
using System.Data;

namespace eShop.DataStore.SQL.Dapper
{
    public interface IDataAccess
    {
        int ExecuteCommand<T>(string sql, T parameters);
        List<T> Query<T, U>(string sql, U parameters);
        T QuerySingle<T, U>(string sql, U parameters);
        T QuerySingleOrDefault<T, U>(string sql, U parameters);
        T InTransaction<T>(Func<IDataAccess, T> operation, IsolationLevel isolationLevel = IsolationLevel.Serializable);
    }
}

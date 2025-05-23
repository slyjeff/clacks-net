using System.Data;
using System.Data.Common;

namespace SlySoft.ClacksNet;

public static class DbConnectionExtensions {
    public static async Task OpenAsync(this IDbConnection connection, CancellationToken cancellationToken = default) {
        if (connection is DbConnection dbConnection) {
            await dbConnection.OpenAsync(cancellationToken);
            return;
        }
        
        connection.Open();
    }

    public static async Task ExecuteNonQueryAsync(this IDbCommand command, CancellationToken cancellationToken = default) {
        if (command is DbCommand dbCommand) {
            await dbCommand.ExecuteNonQueryAsync(cancellationToken);
            return;
        }
        
        command.ExecuteNonQuery();
    }

    public static async Task<IDataReader> ExecuteReaderAsync(this IDbCommand command, CancellationToken cancellationToken = default) {
        if (command is DbCommand dbCommand) {
            return await dbCommand.ExecuteReaderAsync(cancellationToken);
        }
        
        return command.ExecuteReader();
    }
    
    public static async Task<bool> ReadAsync(this IDataReader reader, CancellationToken cancellationToken = default) {
        if (reader is DbDataReader dbDataReader) {
            return await dbDataReader.ReadAsync(cancellationToken);
        }
        
        return reader.Read();
    }
    
    public static bool TableExists(this IDbConnection connection, string tableName) {
        using var command = connection.CreateCommand();
        command.CommandText = connection.GetCheckForTableSql(tableName);
        return Convert.ToInt32(command.ExecuteScalar() ?? 0) > 0;      
    }
    
    private static string GetCheckForTableSql(this IDbConnection connection, string tableName) {
        var typeName = connection.GetType().Name;
        if (typeName.Contains("SqlConnection", StringComparison.OrdinalIgnoreCase) 
            || typeName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("MySql", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("MariaDb", StringComparison.OrdinalIgnoreCase)
           ) {
            return $"SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '{tableName}'";
        }

        if (typeName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)) {
            return $"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '{tableName}'";
        }
        
        if (typeName.Contains("Oracle", StringComparison.OrdinalIgnoreCase)) {
            return $"SELECT COUNT(*) FROM user_tables WHERE table_name = '{tableName}'";
        }
        
        if (typeName.Contains("DB2", StringComparison.OrdinalIgnoreCase)) {
            return $"SELECT COUNT(*) FROM syscat.tables WHERE tabname = '{tableName}'";
        }
        
        throw new NotSupportedException("Unsupported database type");
    }    
}
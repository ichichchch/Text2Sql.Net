using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SqlSugar;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Text2Sql.Net.Domain.Interface;
using Text2Sql.Net.Repositories.Text2Sql.DatabaseConnection;
using DbType = SqlSugar.DbType;

namespace Text2Sql.Net.Domain.Service
{
    /// <summary>
    /// SQL执行服务实现
    /// </summary>
    [ServiceDescription(typeof(ISqlExecutionService), ServiceLifetime.Scoped)]
    public class SqlExecutionService : ISqlExecutionService
    {
        private readonly IDatabaseConnectionConfigRepository _connectionRepository;
        private readonly ILogger<SqlExecutionService> _logger;

        /// <summary>
        /// 构造函数
        /// </summary>
        public SqlExecutionService(
            IDatabaseConnectionConfigRepository connectionRepository,
            ILogger<SqlExecutionService> logger)
        {
            _connectionRepository = connectionRepository;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<(List<Dictionary<string, object>> Result, string ErrorMessage)> ExecuteQueryAsync(string connectionId, string sqlQuery)
        {
            try
            {
                // 获取数据库连接配置
                var connectionConfig = await _connectionRepository.GetByIdAsync(connectionId);
                if (connectionConfig == null)
                {
                    _logger.LogError($"找不到数据库连接配置：{connectionId}");
                    return (null, "找不到数据库连接配置");
                }

                // 创建数据库连接
                var dbType = GetDbType(connectionConfig.DbType);
                var db = new SqlSugarClient(new ConnectionConfig
                {
                    ConnectionString = connectionConfig.ConnectionString,
                    DbType = dbType,
                    IsAutoCloseConnection = true
                });

                // 执行SQL查询
                DataTable dataTable = await db.Ado.GetDataTableAsync(sqlQuery);
                
                // 检查结果是否为空
                if (dataTable == null || dataTable.Rows.Count == 0)
                {
                    return (new List<Dictionary<string, object>>(), null); // 没有错误，但结果为空
                }
                
                // 将DataTable转换为字典列表
                var result = new List<Dictionary<string, object>>();
                foreach (DataRow row in dataTable.Rows)
                {
                    var item = new Dictionary<string, object>();
                    foreach (DataColumn column in dataTable.Columns)
                    {
                        // 处理DBNull值，转换为null
                        object value = row[column] == DBNull.Value ? null : row[column];
                        item[column.ColumnName] = value;
                    }
                    result.Add(item);
                }
                
                return (result, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"执行SQL查询时出错：{ex.Message}");
                return (null, ex.Message);
            }
        }

        /// <param name="dbTypeStr">数据库类型字符串</param>
        /// <returns>对应的SqlSugar数据库类型</returns>
        private DbType GetDbType(string dbTypeStr)
        {
            return dbTypeStr.ToLower() switch
            {
                "sqlserver" => DbType.SqlServer,
                "mysql" => DbType.MySql,
                "postgresql" => DbType.PostgreSQL,
                "sqlite" => DbType.Sqlite,
                "oracle" => DbType.Oracle,
                _ => DbType.SqlServer
            };
        }
    }
}
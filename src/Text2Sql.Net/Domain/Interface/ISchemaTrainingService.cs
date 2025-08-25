using System.Collections.Generic;
using System.Threading.Tasks;
using Text2Sql.Net.Repositories.Text2Sql.DatabaseSchema;

namespace Text2Sql.Net.Domain.Interface
{
    /// <summary>
    /// 数据库Schema训练服务接口
    /// </summary>
    public interface ISchemaTrainingService
    {
        /// <summary>
        /// 训练数据库Schema（全部表）
        /// </summary>
        /// <param name="connectionId">数据库连接ID</param>
        /// <returns>训练结果</returns>
        Task<bool> TrainDatabaseSchemaAsync(string connectionId);
        
        /// <summary>
        /// 训练数据库Schema（选择的表）
        /// </summary>
        /// <param name="connectionId">数据库连接ID</param>
        /// <param name="tableNames">要训练的表名列表</param>
        /// <returns>训练结果</returns>
        Task<bool> TrainDatabaseSchemaAsync(string connectionId, List<string> tableNames);
        
        /// <summary>
        /// 获取数据库表列表
        /// </summary>
        /// <param name="connectionId">数据库连接ID</param>
        /// <returns>表信息列表</returns>
        Task<List<TableInfo>> GetDatabaseTablesAsync(string connectionId);
        
        /// <summary>
        /// 获取数据库Schema
        /// </summary>
        /// <param name="connectionId">数据库连接ID</param>
        /// <returns>Schema信息</returns>
        Task<string> GetDatabaseSchemaAsync(string connectionId);
        
        /// <summary>
        /// 获取已训练的表信息（详细）
        /// </summary>
        /// <param name="connectionId">数据库连接ID</param>
        /// <returns>已训练的表信息列表</returns>
        Task<List<TableInfo>> GetTrainedTablesAsync(string connectionId);
        
        /// <summary>
        /// 获取表的详细信息
        /// </summary>
        /// <param name="connectionId">数据库连接ID</param>
        /// <param name="tableName">表名</param>
        /// <returns>表的详细信息</returns>
        Task<TableInfo> GetTableDetailAsync(string connectionId, string tableName);
    }
} 
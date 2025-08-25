using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Text2Sql.Net.Base;
using Text2Sql.Net.Domain.Model;

namespace Text2Sql.Net.Repositories.Text2Sql.DatabaseConnection
{
    /// <summary>
    /// 数据库连接配置仓储接口
    /// </summary>
    public interface IDatabaseConnectionConfigRepository: IRepository<DatabaseConnectionConfig>
    {
        /// <summary>
        /// 更新连接配置并处理Schema数据一致性
        /// </summary>
        /// <param name="newConfig">新的连接配置</param>
        /// <returns>更新结果和是否需要重新训练Schema</returns>
        Task<(bool Success, bool ShouldRetrainSchema, string Message)> UpdateWithSchemaCleanupAsync(DatabaseConnectionConfig newConfig);
    }
} 
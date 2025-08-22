using Text2Sql.Net.Domain.Service;
using Text2Sql.Net.Repositories.Text2Sql.DatabaseSchema;

namespace Text2Sql.Net.Domain.Interface
{
    /// <summary>
    /// 智能Schema Linking服务接口
    /// </summary>
    public interface IIntelligentSchemaLinkingService
    {
        /// <summary>
        /// 获取与用户查询相关的Schema信息
        /// </summary>
        /// <param name="connectionId">数据库连接ID</param>
        /// <param name="userMessage">用户查询</param>
        /// <param name="relevanceThreshold">相关性阈值</param>
        /// <param name="maxTables">最多返回表数量</param>
        /// <returns>相关Schema信息</returns>
        Task<SchemaLinkingResult> GetRelevantSchemaAsync(string connectionId, string userMessage, double relevanceThreshold = 0.7, int maxTables = 5);

        /// <summary>
        /// 构建Schema图结构
        /// </summary>
        /// <param name="connectionId">数据库连接ID</param>
        /// <returns>Schema图</returns>
        Task<SchemaGraph> BuildSchemaGraphAsync(string connectionId);
    }
}

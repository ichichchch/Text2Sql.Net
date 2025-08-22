using System.Collections.Generic;
using System.Threading.Tasks;
using Text2Sql.Net.Base;

namespace Text2Sql.Net.Repositories.Text2Sql.SchemaEmbedding
{
    /// <summary>
    /// Schema向量嵌入仓储接口
    /// </summary>
    public interface ISchemaEmbeddingRepository : IRepository<SchemaEmbedding>
    {
        /// <summary>
        /// 根据连接ID获取所有嵌入
        /// </summary>
        /// <param name="connectionId">连接ID</param>
        /// <returns>嵌入列表</returns>
        Task<List<SchemaEmbedding>> GetByConnectionIdAsync(string connectionId);

        /// <summary>
        /// 根据连接ID和表名获取嵌入
        /// </summary>
        /// <param name="connectionId">连接ID</param>
        /// <param name="tableName">表名</param>
        /// <returns>嵌入列表</returns>
        Task<List<SchemaEmbedding>> GetByTableAsync(string connectionId, string tableName);

        /// <summary>
        /// 删除指定连接ID的所有嵌入
        /// </summary>
        /// <param name="connectionId">连接ID</param>
        /// <returns>是否成功</returns>
        Task<bool> DeleteByConnectionIdAsync(string connectionId);
        
        /// <summary>
        /// 删除指定连接ID和表名的嵌入
        /// </summary>
        /// <param name="connectionId">连接ID</param>
        /// <param name="tableName">表名</param>
        /// <returns>是否成功</returns>
        Task<bool> DeleteByTableNameAsync(string connectionId, string tableName);
    }
} 
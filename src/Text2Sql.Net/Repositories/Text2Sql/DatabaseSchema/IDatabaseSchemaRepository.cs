using System.Threading.Tasks;
using Text2Sql.Net.Base;

namespace Text2Sql.Net.Repositories.Text2Sql.DatabaseSchema
{
    /// <summary>
    /// 数据库Schema仓储接口
    /// </summary>
    public interface IDatabaseSchemaRepository : IRepository<DatabaseSchema>
    {
        /// <summary>
        /// 根据连接ID获取Schema
        /// </summary>
        /// <param name="connectionId">连接ID</param>
        /// <returns>Schema对象</returns>
        Task<DatabaseSchema> GetByConnectionIdAsync(string connectionId);
    }
} 
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;
using Text2Sql.Net.Base;

namespace Text2Sql.Net.Repositories.Text2Sql.DatabaseSchema
{
    /// <summary>
    /// 数据库Schema仓储实现
    /// </summary>
    [ServiceDescription(typeof(IDatabaseSchemaRepository), ServiceLifetime.Scoped)]
    public class DatabaseSchemaRepository : Repository<DatabaseSchema>, IDatabaseSchemaRepository
    {
        /// <inheritdoc/>
        public async Task<DatabaseSchema> GetByConnectionIdAsync(string connectionId)
        {
            return await GetSingleAsync(x => x.ConnectionId == connectionId);
        }
    }
} 
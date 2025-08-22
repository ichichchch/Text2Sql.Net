using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Threading.Tasks;
using Text2Sql.Net.Base;

namespace Text2Sql.Net.Repositories.Text2Sql.SchemaEmbedding
{
    /// <summary>
    /// Schema向量嵌入仓储实现
    /// </summary>
    [ServiceDescription(typeof(ISchemaEmbeddingRepository), ServiceLifetime.Scoped)]
    public class SchemaEmbeddingRepository : Repository<SchemaEmbedding>, ISchemaEmbeddingRepository
    {
        /// <inheritdoc/>
        public async Task<List<SchemaEmbedding>> GetByConnectionIdAsync(string connectionId)
        {
            return await GetListAsync(x => x.ConnectionId == connectionId);
        }

        /// <inheritdoc/>
        public async Task<List<SchemaEmbedding>> GetByTableAsync(string connectionId, string tableName)
        {
            return await GetListAsync(x => x.ConnectionId == connectionId && x.TableName == tableName);
        }

        /// <inheritdoc/>
        public async Task<bool> DeleteByConnectionIdAsync(string connectionId)
        {
            return await DeleteAsync(x => x.ConnectionId == connectionId);
        }
        
        /// <inheritdoc/>
        public async Task<bool> DeleteByTableNameAsync(string connectionId, string tableName)
        {
            return await DeleteAsync(x => x.ConnectionId == connectionId && x.TableName == tableName);
        }
    }
} 
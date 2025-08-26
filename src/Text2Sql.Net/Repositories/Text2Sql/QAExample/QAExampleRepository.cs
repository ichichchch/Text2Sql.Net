using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Text2Sql.Net.Base;

namespace Text2Sql.Net.Repositories.Text2Sql.QAExample
{
    /// <summary>
    /// 问答示例仓储实现
    /// </summary>
    [ServiceDescription(typeof(IQAExampleRepository), ServiceLifetime.Scoped)]
    public class QAExampleRepository : Repository<QAExample>, IQAExampleRepository
    {
        /// <inheritdoc/>
        public async Task<List<QAExample>> GetEnabledByConnectionIdAsync(string connectionId)
        {
            return await GetListAsync(x => x.ConnectionId == connectionId && x.IsEnabled);
        }

        /// <inheritdoc/>
        public async Task<List<QAExample>> GetByConnectionIdAsync(string connectionId)
        {
            return await GetListAsync(x => x.ConnectionId == connectionId);
        }

        /// <inheritdoc/>
        public async Task<List<QAExample>> GetByCategoryAsync(string connectionId, string category)
        {
            return await GetListAsync(x => x.ConnectionId == connectionId && x.Category == category && x.IsEnabled);
        }

        /// <inheritdoc/>
        public async Task<List<QAExample>> SearchAsync(string connectionId, string keyword)
        {
            if (string.IsNullOrEmpty(keyword))
            {
                return await GetEnabledByConnectionIdAsync(connectionId);
            }

            return await GetListAsync(x => x.ConnectionId == connectionId && x.IsEnabled &&
                (x.Question.Contains(keyword) || x.SqlQuery.Contains(keyword) || 
                 (!string.IsNullOrEmpty(x.Description) && x.Description.Contains(keyword))));
        }

        /// <inheritdoc/>
        public async Task<bool> UpdateUsageStatisticsAsync(string exampleId)
        {
            var example = await GetByIdAsync(exampleId);
            if (example == null)
            {
                return false;
            }

            example.UsageCount++;
            example.LastUsedTime = DateTime.Now;
            example.UpdateTime = DateTime.Now;

            return await UpdateAsync(example);
        }

        /// <inheritdoc/>
        public async Task<bool> DeleteByConnectionIdAsync(string connectionId)
        {
            return await DeleteAsync(x => x.ConnectionId == connectionId);
        }

        /// <inheritdoc/>
        public async Task<bool> BatchUpdateEnabledAsync(List<string> exampleIds, bool isEnabled)
        {
            if (exampleIds == null || exampleIds.Count == 0)
            {
                return false;
            }

            var examples = await GetListAsync(x => exampleIds.Contains(x.Id));
            if (examples == null || examples.Count == 0)
            {
                return false;
            }

            foreach (var example in examples)
            {
                example.IsEnabled = isEnabled;
                example.UpdateTime = DateTime.Now;
            }

            return await Context.Updateable(examples).ExecuteCommandAsync() > 0;
        }
    }
}

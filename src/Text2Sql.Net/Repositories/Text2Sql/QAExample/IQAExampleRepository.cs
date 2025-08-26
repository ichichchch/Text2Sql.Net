using System.Collections.Generic;
using System.Threading.Tasks;
using Text2Sql.Net.Base;

namespace Text2Sql.Net.Repositories.Text2Sql.QAExample
{
    /// <summary>
    /// 问答示例仓储接口
    /// </summary>
    public interface IQAExampleRepository : IRepository<QAExample>
    {
        /// <summary>
        /// 根据连接ID获取所有启用的示例
        /// </summary>
        /// <param name="connectionId">连接ID</param>
        /// <returns>示例列表</returns>
        Task<List<QAExample>> GetEnabledByConnectionIdAsync(string connectionId);

        /// <summary>
        /// 根据连接ID获取所有示例
        /// </summary>
        /// <param name="connectionId">连接ID</param>
        /// <returns>示例列表</returns>
        Task<List<QAExample>> GetByConnectionIdAsync(string connectionId);

        /// <summary>
        /// 根据分类获取示例
        /// </summary>
        /// <param name="connectionId">连接ID</param>
        /// <param name="category">分类</param>
        /// <returns>示例列表</returns>
        Task<List<QAExample>> GetByCategoryAsync(string connectionId, string category);

        /// <summary>
        /// 搜索示例（根据问题或SQL内容）
        /// </summary>
        /// <param name="connectionId">连接ID</param>
        /// <param name="keyword">关键词</param>
        /// <returns>示例列表</returns>
        Task<List<QAExample>> SearchAsync(string connectionId, string keyword);

        /// <summary>
        /// 更新示例使用统计
        /// </summary>
        /// <param name="exampleId">示例ID</param>
        /// <returns>是否成功</returns>
        Task<bool> UpdateUsageStatisticsAsync(string exampleId);

        /// <summary>
        /// 删除指定连接的所有示例
        /// </summary>
        /// <param name="connectionId">连接ID</param>
        /// <returns>是否成功</returns>
        Task<bool> DeleteByConnectionIdAsync(string connectionId);

        /// <summary>
        /// 批量启用/禁用示例
        /// </summary>
        /// <param name="exampleIds">示例ID列表</param>
        /// <param name="isEnabled">是否启用</param>
        /// <returns>是否成功</returns>
        Task<bool> BatchUpdateEnabledAsync(List<string> exampleIds, bool isEnabled);
    }
}

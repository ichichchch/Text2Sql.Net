using System.Collections.Generic;
using System.Threading.Tasks;
using Text2Sql.Net.Repositories.Text2Sql.QAExample;

namespace Text2Sql.Net.Domain.Interface
{
    /// <summary>
    /// 问答示例服务接口
    /// </summary>
    public interface IQAExampleService
    {
        /// <summary>
        /// 获取相关的问答示例（基于语义搜索）
        /// </summary>
        /// <param name="connectionId">数据库连接ID</param>
        /// <param name="userQuestion">用户问题</param>
        /// <param name="limit">返回数量限制</param>
        /// <param name="minRelevanceScore">最小相关性分数</param>
        /// <returns>相关的示例列表</returns>
        Task<List<QAExample>> GetRelevantExamplesAsync(string connectionId, string userQuestion, int limit = 3, double minRelevanceScore = 0.7);

        /// <summary>
        /// 添加问答示例并生成向量嵌入
        /// </summary>
        /// <param name="example">问答示例</param>
        /// <returns>是否成功</returns>
        Task<bool> AddExampleAsync(QAExample example);

        /// <summary>
        /// 从修正中创建问答示例
        /// </summary>
        /// <param name="connectionId">数据库连接ID</param>
        /// <param name="userQuestion">用户问题</param>
        /// <param name="correctSql">正确的SQL</param>
        /// <param name="incorrectSql">错误的SQL（可选）</param>
        /// <param name="description">示例描述</param>
        /// <returns>是否成功</returns>
        Task<bool> CreateFromCorrectionAsync(string connectionId, string userQuestion, string correctSql, string incorrectSql = null, string description = null);

        /// <summary>
        /// 更新问答示例
        /// </summary>
        /// <param name="example">问答示例</param>
        /// <returns>是否成功</returns>
        Task<bool> UpdateExampleAsync(QAExample example);

        /// <summary>
        /// 删除问答示例
        /// </summary>
        /// <param name="exampleId">示例ID</param>
        /// <returns>是否成功</returns>
        Task<bool> DeleteExampleAsync(string exampleId);

        /// <summary>
        /// 获取指定连接的所有示例
        /// </summary>
        /// <param name="connectionId">数据库连接ID</param>
        /// <returns>示例列表</returns>
        Task<List<QAExample>> GetExamplesByConnectionIdAsync(string connectionId);

        /// <summary>
        /// 搜索示例
        /// </summary>
        /// <param name="connectionId">数据库连接ID</param>
        /// <param name="keyword">搜索关键词</param>
        /// <returns>示例列表</returns>
        Task<List<QAExample>> SearchExamplesAsync(string connectionId, string keyword);

        /// <summary>
        /// 批量启用/禁用示例
        /// </summary>
        /// <param name="exampleIds">示例ID列表</param>
        /// <param name="isEnabled">是否启用</param>
        /// <returns>是否成功</returns>
        Task<bool> BatchUpdateEnabledAsync(List<string> exampleIds, bool isEnabled);

        /// <summary>
        /// 为所有现有示例重新生成向量嵌入
        /// </summary>
        /// <param name="connectionId">数据库连接ID</param>
        /// <returns>是否成功</returns>
        Task<bool> RegenerateEmbeddingsAsync(string connectionId);

        /// <summary>
        /// 格式化示例为Few-shot提示词格式
        /// </summary>
        /// <param name="examples">示例列表</param>
        /// <returns>格式化的提示词字符串</returns>
        string FormatExamplesForPrompt(List<QAExample> examples);
    }
}

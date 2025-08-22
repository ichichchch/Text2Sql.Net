using Text2Sql.Net.Domain.Service;

namespace Text2Sql.Net.Domain.Interface
{
    /// <summary>
    /// 高级Prompt工程服务接口
    /// </summary>
    public interface IAdvancedPromptService
    {
        /// <summary>
        /// 创建渐进式复杂度的Few-shot Prompt
        /// </summary>
        /// <param name="userMessage">用户查询</param>
        /// <param name="schemaInfo">Schema信息</param>
        /// <param name="dbType">数据库类型</param>
        /// <param name="userProfile">用户画像（可选）</param>
        /// <returns>优化后的Prompt</returns>
        Task<string> CreateProgressivePromptAsync(string userMessage, string schemaInfo, string dbType, UserProfile userProfile = null);
    }
}


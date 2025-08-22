using Text2Sql.Net.Domain.Service;

namespace Text2Sql.Net.Domain.Interface
{
    /// <summary>
    /// 对话状态管理器接口
    /// </summary>
    public interface IConversationStateManager
    {
        /// <summary>
        /// 更新对话上下文
        /// </summary>
        /// <param name="connectionId">数据库连接ID</param>
        /// <param name="userMessage">用户消息</param>
        /// <param name="assistantMessage">助手回复</param>
        /// <param name="sql">生成的SQL</param>
        /// <param name="result">执行结果</param>
        Task UpdateContextAsync(string connectionId, string userMessage, string assistantMessage, string sql, List<Dictionary<string, object>> result);

        /// <summary>
        /// 解析代词和省略引用
        /// </summary>
        /// <param name="connectionId">数据库连接ID</param>
        /// <param name="userMessage">用户消息</param>
        /// <returns>解析后的消息</returns>
        Task<string> ResolveCoreferencesAsync(string connectionId, string userMessage);

        /// <summary>
        /// 分析后续查询类型
        /// </summary>
        /// <param name="connectionId">数据库连接ID</param>
        /// <param name="userMessage">用户消息</param>
        /// <returns>查询类型</returns>
        Task<FollowupQueryType> AnalyzeFollowupQueryAsync(string connectionId, string userMessage);

        /// <summary>
        /// 处理增量查询
        /// </summary>
        /// <param name="connectionId">数据库连接ID</param>
        /// <param name="userMessage">用户消息</param>
        /// <param name="queryType">查询类型</param>
        /// <returns>处理后的查询消息</returns>
        Task<string> ProcessIncrementalQueryAsync(string connectionId, string userMessage, FollowupQueryType queryType);

        /// <summary>
        /// 获取对话上下文
        /// </summary>
        /// <param name="connectionId">数据库连接ID</param>
        /// <returns>对话上下文</returns>
        Task<ConversationContext> GetContextAsync(string connectionId);

        /// <summary>
        /// 清理对话上下文
        /// </summary>
        /// <param name="connectionId">数据库连接ID</param>
        Task ClearContextAsync(string connectionId);
    }
}


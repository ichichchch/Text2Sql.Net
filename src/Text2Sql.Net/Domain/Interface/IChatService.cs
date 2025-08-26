using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Text2Sql.Net.Repositories.Text2Sql.ChatHistory;

namespace Text2Sql.Net.Domain.Interface
{
    /// <summary>
    /// 聊天服务接口
    /// </summary>
    public interface IChatService
    {
        /// <summary>
        /// 获取指定数据库连接的聊天历史
        /// </summary>
        /// <param name="connectionId">数据库连接ID</param>
        /// <returns>聊天历史列表</returns>
        Task<List<ChatMessage>> GetChatHistoryAsync(string connectionId);

        /// <summary>
        /// 保存聊天消息
        /// </summary>
        /// <param name="message">聊天消息</param>
        /// <returns>保存结果</returns>
        Task<bool> SaveChatMessageAsync(ChatMessage message);

        /// <summary>
        /// 生成并执行SQL
        /// </summary>
        /// <param name="connectionId">数据库连接ID</param>
        /// <param name="userMessage">用户消息</param>
        /// <returns>AI响应（包含生成的SQL和执行结果）</returns>
        Task<ChatMessage> GenerateAndExecuteSqlAsync(string connectionId, string userMessage);

        /// <summary>
        /// 优化SQL并执行
        /// </summary>
        /// <param name="connectionId">数据库连接ID</param>
        /// <param name="userMessage">用户消息</param>
        /// <param name="originalSql">原始SQL</param>
        /// <param name="errorMessage">错误信息</param>
        /// <returns>AI响应（包含优化后的SQL和执行结果）</returns>
        Task<ChatMessage> OptimizeSqlAndExecuteAsync(string connectionId, string userMessage, string originalSql, string errorMessage);

        /// <summary>
        /// 清空指定数据库连接的聊天历史
        /// </summary>
        /// <param name="connectionId">数据库连接ID</param>
        /// <returns>清空结果</returns>
        Task<bool> ClearChatHistoryAsync(string connectionId);

        /// <summary>
        /// 从修正创建问答示例
        /// </summary>
        /// <param name="connectionId">数据库连接ID</param>
        /// <param name="userQuestion">用户问题</param>
        /// <param name="correctSql">正确的SQL</param>
        /// <param name="incorrectSql">错误的SQL</param>
        /// <param name="description">描述</param>
        /// <returns>是否成功</returns>
        Task<bool> CreateExampleFromCorrectionAsync(string connectionId, string userQuestion, string correctSql, string incorrectSql = null, string description = null);
    }
} 
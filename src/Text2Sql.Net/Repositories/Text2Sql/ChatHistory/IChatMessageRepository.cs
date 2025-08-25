using System.Collections.Generic;
using System.Threading.Tasks;
using Text2Sql.Net.Base;

namespace Text2Sql.Net.Repositories.Text2Sql.ChatHistory
{
    /// <summary>
    /// 聊天消息仓储接口
    /// </summary>
    public interface IChatMessageRepository : IRepository<ChatMessage>
    {
        /// <summary>
        /// 获取指定连接的聊天历史
        /// </summary>
        /// <param name="connectionId">数据库连接ID</param>
        /// <returns>聊天消息列表</returns>
        Task<List<ChatMessage>> GetByConnectionIdAsync(string connectionId);

        /// <summary>
        /// 添加聊天消息
        /// </summary>
        /// <param name="chatMessage">聊天消息</param>
        /// <returns>是否成功</returns>
        Task<bool> InsertAsync(ChatMessage chatMessage);

        /// <summary>
        /// 删除指定连接的所有聊天记录
        /// </summary>
        /// <param name="connectionId">数据库连接ID</param>
        /// <returns>是否成功</returns>
        Task<bool> DeleteByConnectionIdAsync(string connectionId);
    }
} 
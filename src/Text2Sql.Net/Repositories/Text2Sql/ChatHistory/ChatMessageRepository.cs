using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SqlSugar;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Text2Sql.Net.Base;

namespace Text2Sql.Net.Repositories.Text2Sql.ChatHistory
{
    /// <summary>
    /// 聊天消息仓储实现
    /// </summary>
    [ServiceDescription(typeof(IChatMessageRepository), ServiceLifetime.Scoped)]
    public class ChatMessageRepository : Repository<ChatMessage>,IChatMessageRepository
    {
        /// <inheritdoc/>
        public async Task<List<ChatMessage>> GetByConnectionIdAsync(string connectionId)
        {
            try
            {
                return await GetDB().Queryable<ChatMessage>()
                    .Where(m => m.ConnectionId == connectionId)
                    .OrderBy(m => m.CreateTime)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine( $"获取聊天历史时出错：{ex.Message}");
                return new List<ChatMessage>();
            }
        }

        /// <inheritdoc/>
        public async Task<bool> InsertAsync(ChatMessage chatMessage)
        {
            try
            {
                return await GetDB().Insertable(chatMessage).ExecuteCommandAsync() > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine( $"添加聊天消息时出错：{ex.Message}");
                return false;
            }
        }
    }
} 
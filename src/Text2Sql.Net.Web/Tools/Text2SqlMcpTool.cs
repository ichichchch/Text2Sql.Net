using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using Text2Sql.Net.Domain.Interface;
using Text2Sql.Net.Repositories.Text2Sql.DatabaseConnection;
using Text2Sql.Net.Repositories.Text2Sql.ChatHistory;
using Text2Sql.Net.Repositories.Text2Sql.DatabaseSchema;

namespace Text2Sql.Net.Web.Tools
{
    /// <summary>
    /// Text2Sql MCP工具 - 提供完整的Text2Sql操作功能
    /// </summary>
    [McpServerToolType]
    public sealed class Text2SqlMcpTool
    {
        private readonly IChatService _chatService;
        private readonly ISqlExecutionService _sqlExecutionService;
        private readonly IDatabaseConnectionConfigRepository _connectionRepository;
        private readonly IChatMessageRepository _chatMessageRepository;
        private readonly IDatabaseSchemaRepository _schemaRepository;
        private readonly ILogger<Text2SqlMcpTool> _logger;

        public Text2SqlMcpTool(
            IChatService chatService,
            ISqlExecutionService sqlExecutionService,
            IDatabaseConnectionConfigRepository connectionRepository,
            IChatMessageRepository chatMessageRepository,
            IDatabaseSchemaRepository schemaRepository,
            ILogger<Text2SqlMcpTool> logger)
        {
            _chatService = chatService;
            _sqlExecutionService = sqlExecutionService;
            _connectionRepository = connectionRepository;
            _chatMessageRepository = chatMessageRepository;
            _schemaRepository = schemaRepository;
            _logger = logger;
        }

        /// <summary>
        /// 获取所有数据库连接配置
        /// </summary>
        /// <param name="thisServer">MCP服务器实例</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>数据库连接列表</returns>
        [McpServerTool(Name = "get_database_connections"), Description("获取所有数据库连接配置信息")]
        public async Task<string> GetDatabaseConnections(
            IMcpServer thisServer,
            CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("获取数据库连接配置列表");

                var connections = await _connectionRepository.GetListAsync();
                
                var result = new StringBuilder();
                result.AppendLine("# 📊 数据库连接配置");
                result.AppendLine($"**📈 总连接数**: {connections.Count}");
                result.AppendLine();

                if (connections.Any())
                {
                    result.AppendLine("## 🔗 连接列表");
                    foreach (var conn in connections)
                    {
                        result.AppendLine($"### 📄 {conn.Name}");
                        result.AppendLine($"**🆔 ID**: `{conn.Id}`");
                        result.AppendLine($"**🗄️ 数据库类型**: {conn.DbType}");
                        result.AppendLine($"**🖥️ 服务器**: {conn.Server ?? "未设置"}");
                        result.AppendLine($"**🔢 端口**: {conn.Port?.ToString() ?? "默认"}");
                        result.AppendLine($"**🗃️ 数据库名**: {conn.Database ?? "未设置"}");
                        result.AppendLine($"**👤 用户名**: {conn.Username ?? "未设置"}");
                        result.AppendLine($"**📝 描述**: {conn.Description ?? "无"}");
                        result.AppendLine($"**🕐 创建时间**: {conn.CreateTime:yyyy-MM-dd HH:mm:ss}");
                        result.AppendLine($"**✏️ 更新时间**: {conn.UpdateTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "从未更新"}");
                        result.AppendLine();
                    }
                }
                else
                {
                    result.AppendLine("## 😔 暂无数据库连接配置");
                    result.AppendLine("请先添加数据库连接配置后再使用Text2Sql功能。");
                }

                return result.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取数据库连接配置时发生错误");
                return $"❌ 获取数据库连接配置失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 获取指定数据库的表结构信息
        /// </summary>
        /// <param name="thisServer">MCP服务器实例</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>表结构信息</returns>
        [McpServerTool(Name = "get_database_schema"), Description("获取当前数据库的表结构信息")]
        public async Task<string> GetDatabaseSchema(
            IMcpServer thisServer,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var connectionId = Text2SqlMcpContextHelper.GetConnectionId(thisServer);
                _logger.LogInformation($"获取数据库 {connectionId} 的表结构信息");

                var connection = await _connectionRepository.GetByIdAsync(connectionId);
                if (connection == null)
                {
                    return $"❌ 未找到数据库连接配置 {connectionId}";
                }

                var schema = await _schemaRepository.GetByConnectionIdAsync(connectionId);
                
                var result = new StringBuilder();
                result.AppendLine("# 🗄️ 数据库表结构");
                result.AppendLine($"**📁 数据库**: {connection.Name} ({connection.DbType})");
                result.AppendLine($"**🆔 连接ID**: {connectionId}");
                result.AppendLine();

                if (schema != null && !string.IsNullOrEmpty(schema.SchemaContent))
                {
                    result.AppendLine("## 📋 Schema信息");
                    result.AppendLine($"**🕐 创建时间**: {schema.CreateTime:yyyy-MM-dd HH:mm:ss}");
                    result.AppendLine($"**✏️ 更新时间**: {schema.UpdateTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "从未更新"}");
                    result.AppendLine();
                    
                    try
                    {
                        // 尝试解析JSON并显示表结构
                        var tables = JsonSerializer.Deserialize<List<TableInfo>>(schema.SchemaContent);
                        if (tables?.Any() == true)
                        {
                            result.AppendLine($"**📊 表总数**: {tables.Count}");
                            result.AppendLine();

                            foreach (var table in tables.Take(10)) // 限制显示前10个表
                            {
                                result.AppendLine($"### 📋 表: {table.TableName}");
                                if (!string.IsNullOrEmpty(table.Description))
                                {
                                    result.AppendLine($"**📝 描述**: {table.Description}");
                                }
                                
                                if (table.Columns?.Any() == true)
                                {
                                    result.AppendLine("**🏷️ 字段列表**:");
                                    foreach (var column in table.Columns.Take(5)) // 限制显示前5个字段
                                    {
                                        var attributes = new List<string>();
                                        if (column.IsPrimaryKey) attributes.Add("主键");
                                        if (!column.IsNullable) attributes.Add("非空");
                                        
                                        var attrText = attributes.Any() ? $" ({string.Join(", ", attributes)})" : "";
                                        result.AppendLine($"- {column.ColumnName}: {column.DataType}{attrText}");
                                        if (!string.IsNullOrEmpty(column.Description))
                                        {
                                            result.AppendLine($"  💬 {column.Description}");
                                        }
                                    }
                                    
                                    if (table.Columns.Count > 5)
                                    {
                                        result.AppendLine($"  ... 还有 {table.Columns.Count - 5} 个字段");
                                    }
                                }
                                result.AppendLine();
                            }
                            
                            if (tables.Count > 10)
                            {
                                result.AppendLine($"... 还有 {tables.Count - 10} 个表未显示");
                            }
                        }
                        else
                        {
                            result.AppendLine("⚠️ Schema内容为空或无法解析");
                        }
                    }
                    catch (JsonException)
                    {
                        result.AppendLine("⚠️ Schema内容格式无效，无法解析JSON");
                        result.AppendLine("**原始内容预览**:");
                        result.AppendLine($"```\n{schema.SchemaContent.Substring(0, Math.Min(500, schema.SchemaContent.Length))}...\n```");
                    }
                }
                else
                {
                    result.AppendLine("## 😔 暂无表结构信息");
                    result.AppendLine("请确保数据库连接正常并已同步表结构。");
                }

                return result.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取数据库表结构时发生错误");
                return $"❌ 获取数据库表结构失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 生成SQL查询
        /// </summary>
        /// <param name="thisServer">MCP服务器实例</param>
        /// <param name="userQuery">用户查询需求</param>
        /// <param name="executeQuery">是否执行查询</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>生成的SQL和执行结果</returns>
        [McpServerTool(Name = "generate_sql"), Description("根据自然语言生成SQL查询语句")]
        public async Task<string> GenerateSql(
            IMcpServer thisServer,
            [Description("用户查询需求（自然语言）")] string userQuery,
            [Description("是否执行生成的SQL查询")] bool executeQuery = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var connectionId = Text2SqlMcpContextHelper.GetConnectionId(thisServer);
                _logger.LogInformation($"为数据库 {connectionId} 生成SQL: {userQuery}");

                var connection = await _connectionRepository.GetByIdAsync(connectionId);
                if (connection == null)
                {
                    return $"❌ 未找到数据库连接配置 {connectionId}";
                }

                var result = new StringBuilder();
                result.AppendLine("# 🤖 Text2SQL 生成结果");
                result.AppendLine($"**📁 数据库**: {connection.Name} ({connection.DbType})");
                result.AppendLine($"**🔍 查询需求**: {userQuery}");
                result.AppendLine($"**▶️ 执行查询**: {(executeQuery ? "是" : "否")}");
                result.AppendLine();

                // 使用ChatService生成SQL
                var chatResponse = await _chatService.GenerateAndExecuteSqlAsync(connectionId, userQuery);
                
                if (chatResponse != null)
                {
                    result.AppendLine("## 📝 生成的SQL");
                    result.AppendLine("```sql");
                    result.AppendLine(chatResponse.SqlQuery ?? "未生成SQL");
                    result.AppendLine("```");
                    result.AppendLine();

                    if (executeQuery && !string.IsNullOrEmpty(chatResponse.SqlQuery))
                    {
                        result.AppendLine("## 📊 执行结果");
                        if (chatResponse.QueryResult?.Any() == true)
                        {
                            result.AppendLine($"✅ **查询成功** - 返回 {chatResponse.QueryResult.Count} 条记录");
                            result.AppendLine();
                            
                            // 显示前几条记录
                            var displayCount = Math.Min(5, chatResponse.QueryResult.Count);
                            for (int i = 0; i < displayCount; i++)
                            {
                                var record = chatResponse.QueryResult[i];
                                result.AppendLine($"**记录 {i + 1}**:");
                                foreach (var kvp in record)
                                {
                                    result.AppendLine($"- {kvp.Key}: {kvp.Value}");
                                }
                                result.AppendLine();
                            }

                            if (chatResponse.QueryResult.Count > displayCount)
                            {
                                result.AppendLine($"... 还有 {chatResponse.QueryResult.Count - displayCount} 条记录");
                            }
                        }
                        else if (string.IsNullOrEmpty(chatResponse.ExecutionError))
                        {
                            result.AppendLine("✅ 查询执行成功，但没有返回数据");
                        }
                        else
                        {
                            result.AppendLine($"❌ 查询执行失败: {chatResponse.ExecutionError}");
                        }
                    }

                    if (!string.IsNullOrEmpty(chatResponse.Message) && chatResponse.Message != chatResponse.SqlQuery)
                    {
                        result.AppendLine("## 💬 AI 解释");
                        result.AppendLine(chatResponse.Message);
                    }
                }
                else
                {
                    result.AppendLine("❌ 无法生成SQL查询");
                }

                return result.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "生成SQL时发生错误");
                return $"❌ 生成SQL失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 执行SQL查询
        /// </summary>
        /// <param name="thisServer">MCP服务器实例</param>
        /// <param name="sqlQuery">SQL查询语句</param>
        /// <param name="maxRows">最大返回行数</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>查询执行结果</returns>
        [McpServerTool(Name = "execute_sql"), Description("执行SQL查询语句")]
        public async Task<string> ExecuteSql(
            IMcpServer thisServer,
            [Description("要执行的SQL查询语句")] string sqlQuery,
            [Description("最大返回行数，默认100")] int maxRows = 100,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var connectionId = Text2SqlMcpContextHelper.GetConnectionId(thisServer);
                _logger.LogInformation($"执行SQL查询，数据库: {connectionId}");

                var connection = await _connectionRepository.GetByIdAsync(connectionId);
                if (connection == null)
                {
                    return $"❌ 未找到数据库连接配置 {connectionId}";
                }

                var result = new StringBuilder();
                result.AppendLine("# 📊 SQL执行结果");
                result.AppendLine($"**📁 数据库**: {connection.Name} ({connection.DbType})");
                result.AppendLine($"**📝 SQL语句**:");
                result.AppendLine("```sql");
                result.AppendLine(sqlQuery);
                result.AppendLine("```");
                result.AppendLine();

                // 执行SQL查询
                var (queryResult, errorMessage) = await _sqlExecutionService.ExecuteQueryAsync(connectionId, sqlQuery);

                if (!string.IsNullOrEmpty(errorMessage))
                {
                    result.AppendLine($"❌ **执行失败**: {errorMessage}");
                }
                else if (queryResult?.Any() == true)
                {
                    var displayCount = Math.Min(maxRows, queryResult.Count);
                    result.AppendLine($"✅ **执行成功** - 返回 {queryResult.Count} 条记录（显示前 {displayCount} 条）");
                    result.AppendLine();

                    // 显示查询结果
                    for (int i = 0; i < displayCount; i++)
                    {
                        var record = queryResult[i];
                        result.AppendLine($"**记录 {i + 1}**:");
                        foreach (var kvp in record)
                        {
                            result.AppendLine($"- **{kvp.Key}**: {kvp.Value ?? "NULL"}");
                        }
                        result.AppendLine();
                    }

                    if (queryResult.Count > displayCount)
                    {
                        result.AppendLine($"... 还有 {queryResult.Count - displayCount} 条记录未显示");
                    }
                }
                else
                {
                    result.AppendLine("✅ SQL执行成功，但没有返回数据");
                }

                return result.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行SQL查询时发生错误");
                return $"❌ 执行SQL查询失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 获取聊天历史
        /// </summary>
        /// <param name="thisServer">MCP服务器实例</param>
        /// <param name="limit">返回记录数限制</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>聊天历史</returns>
        [McpServerTool(Name = "get_chat_history"), Description("获取当前数据库连接的聊天历史")]
        public async Task<string> GetChatHistory(
            IMcpServer thisServer,
            [Description("返回记录数限制，默认20")] int limit = 20,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var connectionId = Text2SqlMcpContextHelper.GetConnectionId(thisServer);
                _logger.LogInformation($"获取数据库 {connectionId} 的聊天历史，限制: {limit}");

                var connection = await _connectionRepository.GetByIdAsync(connectionId);
                if (connection == null)
                {
                    return $"❌ 未找到数据库连接配置 {connectionId}";
                }

                var chatHistory = await _chatService.GetChatHistoryAsync(connectionId);
                var limitedHistory = chatHistory.OrderByDescending(h => h.CreateTime).Take(limit).ToList();

                var result = new StringBuilder();
                result.AppendLine("# 💬 聊天历史");
                result.AppendLine($"**📁 数据库**: {connection.Name} ({connection.DbType})");
                result.AppendLine($"**📊 总记录数**: {chatHistory.Count}（显示最近 {limitedHistory.Count} 条）");
                result.AppendLine();

                if (limitedHistory.Any())
                {
                    foreach (var message in limitedHistory.OrderBy(h => h.CreateTime))
                    {
                        var icon = message.IsUser ? "👤" : "🤖";
                        var roleText = message.IsUser ? "用户" : "AI助手";
                        
                        result.AppendLine($"## {icon} {roleText} - {message.CreateTime:yyyy-MM-dd HH:mm:ss}");
                        
                        if (message.IsUser)
                        {
                            result.AppendLine($"**🔍 查询**: {message.Message}");
                        }
                        else
                        {
                            if (!string.IsNullOrEmpty(message.SqlQuery))
                            {
                                result.AppendLine("**📝 生成的SQL**:");
                                result.AppendLine("```sql");
                                result.AppendLine(message.SqlQuery);
                                result.AppendLine("```");
                            }
                            
                            if (!string.IsNullOrEmpty(message.Message))
                            {
                                result.AppendLine($"**💬 回复**: {message.Message}");
                            }
                            
                            if (!string.IsNullOrEmpty(message.ExecutionError))
                            {
                                result.AppendLine($"**❌ 错误**: {message.ExecutionError}");
                            }
                        }
                        
                        result.AppendLine();
                    }
                }
                else
                {
                    result.AppendLine("## 😔 暂无聊天记录");
                    result.AppendLine("开始使用Text2SQL功能后，这里将显示对话历史。");
                }

                return result.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取聊天历史时发生错误");
                return $"❌ 获取聊天历史失败: {ex.Message}";
            }
        }

        #region 私有辅助方法

        /// <summary>
        /// 格式化字节数为可读字符串
        /// </summary>
        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        #endregion

        #region 数据结构

        /// <summary>
        /// 表信息
        /// </summary>
        private class TableInfo
        {
            public string TableName { get; set; } = "";
            public string Description { get; set; } = "";
            public List<ColumnInfo> Columns { get; set; } = new List<ColumnInfo>();
        }

        /// <summary>
        /// 列信息
        /// </summary>
        private class ColumnInfo
        {
            public string ColumnName { get; set; } = "";
            public string DataType { get; set; } = "";
            public bool IsNullable { get; set; }
            public bool IsPrimaryKey { get; set; }
            public string Description { get; set; } = "";
        }

        #endregion
    }
}
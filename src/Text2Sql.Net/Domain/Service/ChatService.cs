using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Plugins.Core;
using Microsoft.SemanticKernel.Text;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Text2Sql.Net.Domain.Interface;
using Text2Sql.Net.Repositories.Text2Sql.ChatHistory;
using Text2Sql.Net.Repositories.Text2Sql.DatabaseSchema;
using Text2Sql.Net.Repositories.Text2Sql.SchemaEmbedding;

namespace Text2Sql.Net.Domain.Service
{
    /// <summary>
    /// 聊天服务实现
    /// </summary>
    [ServiceDescription(typeof(IChatService), ServiceLifetime.Scoped)]
    public class ChatService : IChatService
    {
        private readonly IChatMessageRepository _chatRepository;
        private readonly IDatabaseSchemaRepository _schemaRepository;
        private readonly ISqlExecutionService _sqlExecutionService;
        private readonly ISemanticService _semanticService;
        private readonly Kernel _kernel;
        private readonly ILogger<ChatService> _logger;

        /// <summary>
        /// 构造函数
        /// </summary>
        public ChatService(
            IChatMessageRepository chatRepository,
            IDatabaseSchemaRepository schemaRepository,
            ISqlExecutionService sqlExecutionService,
            ISemanticService semanticService,
            Kernel kernel,
            ILogger<ChatService> logger)
        {
            _chatRepository = chatRepository;
            _schemaRepository = schemaRepository;
            _sqlExecutionService = sqlExecutionService;
            _semanticService = semanticService;
            _kernel = kernel;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<List<ChatMessage>> GetChatHistoryAsync(string connectionId)
        {
            if (string.IsNullOrEmpty(connectionId))
            {
                return new List<ChatMessage>();
            }

            return await _chatRepository.GetByConnectionIdAsync(connectionId);
        }

        /// <inheritdoc/>
        public async Task<bool> SaveChatMessageAsync(ChatMessage message)
        {
            if (message == null || string.IsNullOrEmpty(message.ConnectionId))
            {
                return false;
            }

            return await _chatRepository.InsertAsync(message);
        }

        /// <inheritdoc/>
        public async Task<ChatMessage> GenerateAndExecuteSqlAsync(string connectionId, string userMessage)
        {
            try
            {
                // 1. 构建语义查询，获取相关的表结构信息
                var schemaInfo = await GetRelevantSchemaInfoAsync(connectionId, userMessage);
                if (string.IsNullOrEmpty(schemaInfo))
                {
                    return CreateErrorResponse(connectionId, "无法找到相关的数据库表结构信息");
                }

                // 2. 使用语义核心生成SQL
                string sqlQuery = await GenerateSqlQueryAsync(userMessage, schemaInfo);
                if (string.IsNullOrEmpty(sqlQuery))
                {
                    return CreateErrorResponse(connectionId, "无法生成SQL查询语句");
                }

                // 3. 执行SQL查询
                var (result, errorMessage) = await _sqlExecutionService.ExecuteQueryAsync(connectionId, sqlQuery);

                // 4. 创建并保存响应消息
                var responseMessage = new ChatMessage
                {
                    Id = Guid.NewGuid().ToString(),
                    ConnectionId = connectionId,
                    Message = string.IsNullOrEmpty(errorMessage)
                        ? $"根据您的问题，我生成并执行了以下SQL查询：\n\n{sqlQuery}\n\n查询结果包含 {result?.Count ?? 0} 条记录。"
                        : $"我生成了以下SQL查询，但执行时出现错误：\n\n{sqlQuery}\n\n错误信息：{errorMessage}",
                    IsUser = false,
                    SqlQuery = sqlQuery,
                    ExecutionError = errorMessage,
                    QueryResult = result,
                    CreateTime = DateTime.Now
                };

                await _chatRepository.InsertAsync(responseMessage);
                return responseMessage;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"生成并执行SQL时出错：{ex.Message}");
                return CreateErrorResponse(connectionId, $"处理请求时出错：{ex.Message}");
            }
        }

        /// <inheritdoc/>
        public async Task<ChatMessage> OptimizeSqlAndExecuteAsync(string connectionId, string userMessage, string originalSql, string errorMessage)
        {
            try
            {
                // 1. 获取数据库Schema信息
                var schema = await _schemaRepository.GetByConnectionIdAsync(connectionId);
                if (schema == null)
                {
                    return CreateErrorResponse(connectionId, "无法找到数据库Schema信息");
                }

                // 2. 使用语义核心优化SQL
                string optimizedSql = await OptimizeSqlQueryAsync(userMessage, originalSql, errorMessage, schema.SchemaContent);
                if (string.IsNullOrEmpty(optimizedSql))
                {
                    return CreateErrorResponse(connectionId, "无法优化SQL查询语句");
                }

                // 3. 执行优化后的SQL查询
                var (result, newErrorMessage) = await _sqlExecutionService.ExecuteQueryAsync(connectionId, optimizedSql);

                // 4. 创建并保存响应消息
                var responseMessage = new ChatMessage
                {
                    Id = Guid.NewGuid().ToString(),
                    ConnectionId = connectionId,
                    Message = string.IsNullOrEmpty(newErrorMessage)
                        ? $"原始SQL执行失败，我已优化SQL查询：\n\n{optimizedSql}\n\n查询结果包含 {result?.Count ?? 0} 条记录。"
                        : $"我尝试优化SQL查询，但仍然存在错误：\n\n{optimizedSql}\n\n错误信息：{newErrorMessage}",
                    IsUser = false,
                    SqlQuery = optimizedSql,
                    ExecutionError = newErrorMessage,
                    QueryResult = result,
                    CreateTime = DateTime.Now
                };

                await _chatRepository.InsertAsync(responseMessage);
                return responseMessage;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"优化并执行SQL时出错：{ex.Message}");
                return CreateErrorResponse(connectionId, $"处理请求时出错：{ex.Message}");
            }
        }

        /// <summary>
        /// 获取与用户问题相关的数据库Schema信息
        /// </summary>
        /// <param name="connectionId">数据库连接ID</param>
        /// <param name="userMessage">用户问题</param>
        /// <returns>相关Schema信息</returns>
        private async Task<string> GetRelevantSchemaInfoAsync(string connectionId, string userMessage)
        {
            try
            {
                // 获取完整的Schema信息
                var schema = await _schemaRepository.GetByConnectionIdAsync(connectionId);
                if (schema == null)
                {
                    return string.Empty;
                }

                // 使用向量存储进行语义搜索
                SemanticTextMemory memory = await _semanticService.GetTextMemory();
                
                // 使用await foreach处理异步枚举
                var searchResults = new List<MemoryQueryResult>();
                await foreach (var result in memory.SearchAsync(connectionId, userMessage, limit: 5, minRelevanceScore: 0.6))
                {
                    searchResults.Add(result);
                }

                if (searchResults.Count == 0)
                {
                    _logger.LogWarning($"未找到与问题相关的表结构信息：{userMessage}");
                    return schema.SchemaContent; // 返回完整Schema
                }

                // 解析搜索结果，提取相关的表信息
                var relevantTables = new List<TableInfo>();
                foreach (var result in searchResults)
                {
                    try
                    {
                        var embedding = JsonConvert.DeserializeObject<SchemaEmbedding>(result.Metadata.Text);
                        if (embedding != null && embedding.EmbeddingType == EmbeddingType.Table)
                        {
                            // 解析表结构
                            List<TableInfo> allTables = JsonConvert.DeserializeObject<List<TableInfo>>(schema.SchemaContent);
                            TableInfo tableInfo = allTables.FirstOrDefault(t => t.TableName == embedding.TableName);
                            if (tableInfo != null && !relevantTables.Any(t => t.TableName == tableInfo.TableName))
                            {
                                relevantTables.Add(tableInfo);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"解析Schema嵌入信息时出错：{ex.Message}");
                    }
                }

                // 如果没有找到相关表，返回完整Schema
                if (relevantTables.Count == 0)
                {
                    return schema.SchemaContent;
                }

                // 返回相关表的JSON
                return JsonConvert.SerializeObject(relevantTables, Formatting.Indented);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"获取相关Schema信息时出错：{ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// 根据用户问题和Schema信息生成SQL查询
        /// </summary>
        /// <param name="userMessage">用户问题</param>
        /// <param name="schemaInfo">Schema信息</param>
        /// <returns>生成的SQL查询</returns>
        private async Task<string> GenerateSqlQueryAsync(string userMessage, string schemaInfo)
        {
            try
            {
                // 构建提示词
                string prompt = $@"您是一个SQL专家，需要将用户的自然语言问题转换为SQL查询。
请基于以下数据库表结构，生成一个有效的SQL查询来回答用户的问题。

数据库表结构：
{schemaInfo}

用户问题：{userMessage}

请生成一个可执行的SQL查询语句，不需要解释。只返回SQL语句本身，不要包含任何其他格式标记，如```sql```。请确保SQL语法正确，适用于通用的SQL数据库。";

                // 使用语义核心生成SQL
                var result = await _kernel.InvokePromptAsync(prompt);
                
                // 提取生成的SQL
                string sql = result?.ToString()?.Trim();
                
                // 简单清理，确保只返回SQL
                sql = CleanSqlResult(sql);
                
                return sql;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"生成SQL查询时出错：{ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// 优化SQL查询
        /// </summary>
        /// <param name="userMessage">用户问题</param>
        /// <param name="originalSql">原始SQL</param>
        /// <param name="errorMessage">错误信息</param>
        /// <param name="schemaInfo">Schema信息</param>
        /// <returns>优化后的SQL</returns>
        private async Task<string> OptimizeSqlQueryAsync(string userMessage, string originalSql, string errorMessage, string schemaInfo)
        {
            try
            {
                // 构建提示词
                string prompt = $@"您是一个SQL优化专家，需要修复和优化一个有错误的SQL查询。

数据库表结构：
{schemaInfo}

用户问题：{userMessage}

原始SQL查询：
{originalSql}

执行错误信息：
{errorMessage}

请根据错误信息和数据库表结构修复并优化SQL查询。只返回优化后的SQL语句，不要包含任何其他格式标记，如```sql```。";

                // 使用语义核心优化SQL
                var result = await _kernel.InvokePromptAsync(prompt);
                
                // 提取优化后的SQL
                string sql = result?.ToString()?.Trim();
                
                // 简单清理，确保只返回SQL
                sql = CleanSqlResult(sql);
                
                return sql;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"优化SQL查询时出错：{ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// 清理SQL结果，移除可能的标记
        /// </summary>
        /// <param name="sql">原始SQL</param>
        /// <returns>清理后的SQL</returns>
        private string CleanSqlResult(string sql)
        {
            if (string.IsNullOrEmpty(sql))
            {
                return string.Empty;
            }

            // 移除可能的代码块标记
            sql = sql.Replace("```sql", "").Replace("```", "");
            
            // 移除多余的空行
            sql = string.Join("\n", sql.Split('\n').Where(line => !string.IsNullOrWhiteSpace(line)));
            
            return sql.Trim();
        }

        /// <summary>
        /// 创建错误响应消息
        /// </summary>
        /// <param name="connectionId">数据库连接ID</param>
        /// <param name="errorMessage">错误信息</param>
        /// <returns>错误响应消息</returns>
        private ChatMessage CreateErrorResponse(string connectionId, string errorMessage)
        {
            return new ChatMessage
            {
                Id = Guid.NewGuid().ToString(),
                ConnectionId = connectionId,
                Message = $"处理您的请求时出现错误：{errorMessage}",
                IsUser = false,
                ExecutionError = errorMessage,
                CreateTime = DateTime.Now
            };
        }
    }
} 
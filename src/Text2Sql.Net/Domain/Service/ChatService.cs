using Azure.AI.OpenAI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
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
using Text2Sql.Net.Repositories.Text2Sql.DatabaseConnection;
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
        private readonly IDatabaseConnectionConfigRepository _connectionRepository;
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
            IDatabaseConnectionConfigRepository connectionRepository,
            IChatMessageRepository chatRepository,
            IDatabaseSchemaRepository schemaRepository,
            ISqlExecutionService sqlExecutionService,
            ISemanticService semanticService,
            Kernel kernel,
            ILogger<ChatService> logger)
        {
            _connectionRepository = connectionRepository;
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

                var con =await _connectionRepository.GetByIdAsync(connectionId);

                // 2. 使用语义核心生成SQL
                string sqlQuery = await GenerateSqlQueryAsync(con.DbType,userMessage, schemaInfo);
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

                // 解析所有表结构信息
                List<TableInfo> allTables = JsonConvert.DeserializeObject<List<TableInfo>>(schema.SchemaContent);
                
                // 使用向量存储进行语义搜索，采用动态阈值策略
                SemanticTextMemory memory = await _semanticService.GetTextMemory();
                
                // 初始化相关性阈值和结果数量限制
                double relevanceThreshold = 0.7; // 开始使用较高阈值
                int minTablesRequired = 1; // 最少需要返回的表数量
                int maxTables = 5; // 最多返回表数量
                
                // 使用await foreach处理异步枚举
                var searchResults = new List<MemoryQueryResult>();
                var relevantTables = new List<TableInfo>();
                
                // 动态阈值搜索策略
                while (relevanceThreshold >= 0.4 && relevantTables.Count < minTablesRequired)
                {
                    searchResults.Clear();
                    _logger.LogInformation($"使用相关性阈值 {relevanceThreshold:F2} 进行搜索");
                    
                    await foreach (var result in memory.SearchAsync(connectionId, userMessage, limit: maxTables, minRelevanceScore: relevanceThreshold))
                    {
                        searchResults.Add(result);
                    }
                    
                    // 解析搜索结果，提取相关的表信息
                    relevantTables.Clear();
                    foreach (var result in searchResults)
                    {
                        try
                        {
                            var embedding = JsonConvert.DeserializeObject<SchemaEmbedding>(result.Metadata.Text);
                            if (embedding != null && embedding.EmbeddingType == EmbeddingType.Table)
                            {
                                // 解析表结构
                                TableInfo tableInfo = allTables.FirstOrDefault(t => t.TableName == embedding.TableName);
                                if (tableInfo != null && !relevantTables.Any(t => t.TableName == tableInfo.TableName))
                                {
                                    _logger.LogInformation($"找到相关表: {tableInfo.TableName}，相关性分数: {result.Relevance:F2}");
                                    relevantTables.Add(tableInfo);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"解析Schema嵌入信息时出错：{ex.Message}");
                        }
                    }
                    
                    // 如果没有找到足够的表，降低阈值继续尝试
                    if (relevantTables.Count < minTablesRequired)
                    {
                        relevanceThreshold -= 0.1; // 每次降低0.1
                    }
                }

                // 如果仍然没有找到相关表，返回完整Schema
                if (relevantTables.Count == 0)
                {
                    _logger.LogWarning($"未找到与问题相关的表结构信息，返回完整Schema：{userMessage}");
                    return schema.SchemaContent;
                }
                
                // 应用表关联推断，添加关联表（将在下一步实现）
                relevantTables = InferRelatedTables(relevantTables, allTables);

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
        /// 推断并添加与已找到表相关联的表
        /// </summary>
        /// <param name="sourceTables">已找到的相关表</param>
        /// <param name="allTables">所有表列表</param>
        /// <returns>包含关联表的扩展列表</returns>
        private List<TableInfo> InferRelatedTables(List<TableInfo> sourceTables, List<TableInfo> allTables)
        {
            var extendedTables = new List<TableInfo>(sourceTables);
            var tableNames = new HashSet<string>(sourceTables.Select(t => t.TableName));
            var addedTables = new HashSet<string>();
            
            _logger.LogInformation($"开始推断相关表关联，源表数量：{sourceTables.Count}");
            
            // 定义最大关联表数（防止表过多）
            int maxRelatedTables = 10;
            
            // 第一步：从外键关系向外扩展
            foreach (var table in sourceTables)
            {
                foreach (var fk in table.ForeignKeys)
                {
                    if (!tableNames.Contains(fk.ReferencedTableName) && !addedTables.Contains(fk.ReferencedTableName))
                    {
                        // 查找引用的表
                        var referencedTable = allTables.FirstOrDefault(t => t.TableName == fk.ReferencedTableName);
                        if (referencedTable != null)
                        {
                            _logger.LogInformation($"添加被引用表：{referencedTable.TableName}，通过外键 {fk.ForeignKeyName}");
                            extendedTables.Add(referencedTable);
                            addedTables.Add(referencedTable.TableName);
                            
                            if (extendedTables.Count >= sourceTables.Count + maxRelatedTables)
                                break;
                        }
                    }
                }
                
                if (extendedTables.Count >= sourceTables.Count + maxRelatedTables)
                    break;
            }
            
            // 第二步：查找引用源表的外表
            if (extendedTables.Count < sourceTables.Count + maxRelatedTables)
            {
                foreach (var sourceTable in sourceTables)
                {
                    // 查找所有引用了这个表的表
                    foreach (var table in allTables)
                    {
                        if (tableNames.Contains(table.TableName) || addedTables.Contains(table.TableName))
                            continue;
                        
                        // 检查该表的外键是否引用了源表
                        bool isReferencing = table.ForeignKeys.Any(fk => fk.ReferencedTableName == sourceTable.TableName);
                        
                        if (isReferencing)
                        {
                            _logger.LogInformation($"添加引用表：{table.TableName}，引用了 {sourceTable.TableName}");
                            extendedTables.Add(table);
                            addedTables.Add(table.TableName);
                            
                            if (extendedTables.Count >= sourceTables.Count + maxRelatedTables)
                                break;
                        }
                    }
                    
                    if (extendedTables.Count >= sourceTables.Count + maxRelatedTables)
                        break;
                }
            }
            
            // 第三步：估计一对多和多对多关系 - 查找中间表
            // 中间表通常有两个外键，分别指向不同的表
            if (extendedTables.Count < sourceTables.Count + maxRelatedTables)
            {
                var currentTableNames = new HashSet<string>(extendedTables.Select(t => t.TableName));
                
                foreach (var table in allTables)
                {
                    if (currentTableNames.Contains(table.TableName))
                        continue;
                    
                    // 检查该表是否连接了两个或更多已知表（可能是中间表）
                    var referencedTables = table.ForeignKeys
                        .Select(fk => fk.ReferencedTableName)
                        .Where(t => currentTableNames.Contains(t))
                        .Distinct()
                        .ToList();
                    
                    if (referencedTables.Count >= 2)
                    {
                        _logger.LogInformation($"添加中间表：{table.TableName}，连接了 {string.Join(", ", referencedTables)}");
                        extendedTables.Add(table);
                        addedTables.Add(table.TableName);
                        
                        if (extendedTables.Count >= sourceTables.Count + maxRelatedTables)
                            break;
                    }
                }
            }
            
            _logger.LogInformation($"表关联推断完成，扩展后表数量：{extendedTables.Count}");
            return extendedTables;
        }

        /// <summary>
        /// 根据用户问题和Schema信息生成SQL查询
        /// </summary>
        /// <param name="userMessage">用户问题</param>
        /// <param name="schemaInfo">Schema信息</param>
        /// <returns>生成的SQL查询</returns>
        private async Task<string> GenerateSqlQueryAsync(string dbType,string userMessage, string schemaInfo)
        {
            try
            {
                OpenAIPromptExecutionSettings settings = new()
                {
                    Temperature = 0.1
                };
                KernelFunction generateSqlFun = _kernel.Plugins.GetFunction("text2sql", "generate_sql_query");
                var args = new KernelArguments(settings)
                {
                    ["$dbType"] = dbType,
                    ["schemaInfo"] = schemaInfo,
                    ["userMessage"] = userMessage

                };
                // 使用语义核心生成SQL
                var result = await _kernel.InvokeAsync(generateSqlFun, args);
                
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
                OpenAIPromptExecutionSettings settings = new()
                {
                    Temperature = 0.1
                };
                KernelFunction generateSqlFun = _kernel.Plugins.GetFunction("text2sql", "optimize_sql_query");
                var args = new KernelArguments(settings)
                {
                    ["schemaInfo"] = schemaInfo,
                    ["userMessage"] = userMessage,
                    ["originalSql"] = originalSql,
                    ["errorMessage"] = errorMessage

                };
                // 使用语义核心生成SQL
                var result = await _kernel.InvokeAsync(generateSqlFun, args);


                
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
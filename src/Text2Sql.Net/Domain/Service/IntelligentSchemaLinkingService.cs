using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel;
using Newtonsoft.Json;
using System.Text;
using Text2Sql.Net.Domain.Interface;
using Text2Sql.Net.Repositories.Text2Sql.DatabaseSchema;
using Text2Sql.Net.Repositories.Text2Sql.SchemaEmbedding;

namespace Text2Sql.Net.Domain.Service
{
    /// <summary>
    /// 智能Schema Linking服务
    /// 实现基于语义相似度的智能表结构匹配
    /// </summary>
    [ServiceDescription(typeof(IIntelligentSchemaLinkingService), ServiceLifetime.Scoped)]
    public class IntelligentSchemaLinkingService : IIntelligentSchemaLinkingService
    {
        private readonly ISemanticService _semanticService;
        private readonly IDatabaseSchemaRepository _schemaRepository;
        private readonly ILogger<IntelligentSchemaLinkingService> _logger;

        public IntelligentSchemaLinkingService(
            ISemanticService semanticService,
            IDatabaseSchemaRepository schemaRepository,
            ILogger<IntelligentSchemaLinkingService> logger)
        {
            _semanticService = semanticService;
            _schemaRepository = schemaRepository;
            _logger = logger;
        }

        /// <summary>
        /// 智能Schema Linking - 基于语义相似度匹配
        /// </summary>
        /// <param name="connectionId">数据库连接ID</param>
        /// <param name="userMessage">用户查询</param>
        /// <param name="relevanceThreshold">相关性阈值</param>
        /// <param name="maxTables">最多返回表数量</param>
        /// <returns>相关Schema信息</returns>
        public async Task<SchemaLinkingResult> GetRelevantSchemaAsync(
            string connectionId, 
            string userMessage, 
            double relevanceThreshold = 0.7, 
            int maxTables = 5)
        {
            try
            {
                // 获取完整Schema信息
                var schema = await _schemaRepository.GetByConnectionIdAsync(connectionId);
                if (schema == null)
                {
                    return new SchemaLinkingResult { Success = false, ErrorMessage = "Schema not found" };
                }

                List<TableInfo> allTables = JsonConvert.DeserializeObject<List<TableInfo>>(schema.SchemaContent);
                SemanticTextMemory memory = await _semanticService.GetTextMemory();

                // 动态阈值搜索策略
                var relevantTables = await PerformDynamicThresholdSearch(
                    memory, connectionId, userMessage, allTables, relevanceThreshold, maxTables);

                if (relevantTables.Count == 0)
                {
                    _logger.LogWarning($"未找到与问题相关的表结构信息：{userMessage}");
                    // 返回完整Schema作为后备
                    return new SchemaLinkingResult
                    {
                        Success = true,
                        RelevantTables = allTables,
                        SchemaJson = schema.SchemaContent,
                        MatchingDetails = new List<SchemaMatchDetail>(),
                        UsedFallback = true
                    };
                }

                // 应用表关联推断
                var extendedTables = await InferRelatedTablesAsync(relevantTables, allTables);

                foreach (var item in extendedTables)
                {
                    item.Columns.RemoveAll(a => a.IsEnable == false);                    
                }

                // 生成匹配详情
                var matchingDetails = await GenerateMatchingDetailsAsync(userMessage, extendedTables, memory, connectionId);

                return new SchemaLinkingResult
                {
                    Success = true,
                    RelevantTables = extendedTables,
                    SchemaJson = JsonConvert.SerializeObject(extendedTables, Formatting.Indented),
                    MatchingDetails = matchingDetails,
                    UsedFallback = false
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"智能Schema Linking时出错：{ex.Message}");
                return new SchemaLinkingResult 
                { 
                    Success = false, 
                    ErrorMessage = ex.Message 
                };
            }
        }

        /// <summary>
        /// 动态阈值搜索策略
        /// </summary>
        private async Task<List<TableInfo>> PerformDynamicThresholdSearch(
            SemanticTextMemory memory, 
            string connectionId, 
            string userMessage, 
            List<TableInfo> allTables,
            double initialThreshold, 
            int maxTables)
        {
            var relevantTables = new List<TableInfo>();
            double currentThreshold = initialThreshold;
            int minTablesRequired = 1;

            while (currentThreshold >= 0.4 && relevantTables.Count < minTablesRequired)
            {
                var searchResults = new List<MemoryQueryResult>();
                _logger.LogInformation($"使用相关性阈值 {currentThreshold:F2} 进行搜索");

                await foreach (var result in memory.SearchAsync(connectionId, userMessage, 
                    limit: maxTables, minRelevanceScore: currentThreshold))
                {
                    searchResults.Add(result);
                }

                // 解析搜索结果
                relevantTables.Clear();
                foreach (var result in searchResults)
                {
                    try
                    {
                        var embedding = JsonConvert.DeserializeObject<SchemaEmbedding>(result.Metadata.Text);
                        if (embedding != null && embedding.EmbeddingType == EmbeddingType.Table)
                        {
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

                if (relevantTables.Count < minTablesRequired)
                {
                    currentThreshold -= 0.1; // 每次降低0.1
                }
            }

            return relevantTables;
        }

        /// <summary>
        /// 推断并添加相关联的表
        /// </summary>
        private async Task<List<TableInfo>> InferRelatedTablesAsync(List<TableInfo> sourceTables, List<TableInfo> allTables)
        {
            var extendedTables = new List<TableInfo>(sourceTables);
            var tableNames = new HashSet<string>(sourceTables.Select(t => t.TableName));
            var addedTables = new HashSet<string>();

            _logger.LogInformation($"开始推断相关表关联，源表数量：{sourceTables.Count}");

            int maxRelatedTables = 10;

            // 第一步：从外键关系向外扩展
            foreach (var table in sourceTables)
            {
                foreach (var fk in table.ForeignKeys)
                {
                    if (!tableNames.Contains(fk.ReferencedTableName) && !addedTables.Contains(fk.ReferencedTableName))
                    {
                        var referencedTable = allTables.FirstOrDefault(t => t.TableName == fk.ReferencedTableName);
                        if (referencedTable != null)
                        {
                            _logger.LogInformation($"添加被引用表：{referencedTable.TableName}");
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
                    foreach (var table in allTables)
                    {
                        if (tableNames.Contains(table.TableName) || addedTables.Contains(table.TableName))
                            continue;

                        bool isReferencing = table.ForeignKeys.Any(fk => fk.ReferencedTableName == sourceTable.TableName);

                        if (isReferencing)
                        {
                            _logger.LogInformation($"添加引用表：{table.TableName}");
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

            // 第三步：查找中间表（连接表）
            if (extendedTables.Count < sourceTables.Count + maxRelatedTables)
            {
                var currentTableNames = new HashSet<string>(extendedTables.Select(t => t.TableName));

                foreach (var table in allTables)
                {
                    if (currentTableNames.Contains(table.TableName))
                        continue;

                    var referencedTables = table.ForeignKeys
                        .Select(fk => fk.ReferencedTableName)
                        .Where(t => currentTableNames.Contains(t))
                        .Distinct()
                        .ToList();

                    if (referencedTables.Count >= 2)
                    {
                        _logger.LogInformation($"添加中间表：{table.TableName}");
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
        /// 生成匹配详情
        /// </summary>
        private async Task<List<SchemaMatchDetail>> GenerateMatchingDetailsAsync(
            string userMessage, 
            List<TableInfo> tables, 
            SemanticTextMemory memory, 
            string connectionId)
        {
            var matchingDetails = new List<SchemaMatchDetail>();

            foreach (var table in tables)
            {
                try
                {
                    // 计算表级别的相关性
                    var tableEmbedding = new SchemaEmbedding
                    {
                        ConnectionId = connectionId,
                        TableName = table.TableName,
                        Description = $"表名: {table.TableName}, 描述: {table.Description ?? "无描述"}",
                        EmbeddingType = EmbeddingType.Table
                    };

                    await foreach (var result in memory.SearchAsync(connectionId, userMessage, limit: 1))
                    {
                        var embedding = JsonConvert.DeserializeObject<SchemaEmbedding>(result.Metadata.Text);
                        if (embedding?.TableName == table.TableName)
                        {
                            matchingDetails.Add(new SchemaMatchDetail
                            {
                                TableName = table.TableName,
                                MatchType = "Direct Semantic Match",
                                RelevanceScore = result.Relevance,
                                MatchedText = userMessage,
                                MatchReason = $"表 {table.TableName} 与查询语义相似度: {result.Relevance:F2}"
                            });
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"生成表 {table.TableName} 匹配详情时出错");
                }
            }

            return matchingDetails;
        }

        /// <summary>
        /// 基于图神经网络的Schema结构分析
        /// </summary>
        public async Task<SchemaGraph> BuildSchemaGraphAsync(string connectionId)
        {
            try
            {
                var schema = await _schemaRepository.GetByConnectionIdAsync(connectionId);
                if (schema == null)
                    return null;

                List<TableInfo> tables = JsonConvert.DeserializeObject<List<TableInfo>>(schema.SchemaContent);
                var graph = new SchemaGraph();

                // 构建表节点
                foreach (var table in tables)
                {
                    graph.AddTableNode(table.TableName, ExtractTableFeatures(table));

                    // 构建列节点
                    foreach (var column in table.Columns)
                    {
                        graph.AddColumnNode($"{table.TableName}.{column.ColumnName}", 
                            ExtractColumnFeatures(column), table.TableName);
                    }
                }

                // 添加外键关系边
                foreach (var table in tables)
                {
                    foreach (var fk in table.ForeignKeys)
                    {
                        graph.AddForeignKeyEdge(
                            $"{table.TableName}.{fk.ColumnName}",
                            $"{fk.ReferencedTableName}.{fk.ReferencedColumnName}",
                            fk.ForeignKeyName);
                    }
                }

                return graph;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"构建Schema图时出错：{ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 提取表特征
        /// </summary>
        private Dictionary<string, object> ExtractTableFeatures(TableInfo table)
        {
            return new Dictionary<string, object>
            {
                {"name", table.TableName},
                {"description", table.Description ?? ""},
                {"column_count", table.Columns.Count},
                {"foreign_key_count", table.ForeignKeys.Count},
                {"has_primary_key", table.Columns.Any(c => c.IsPrimaryKey)},
                {"table_type", InferTableType(table)}
            };
        }

        /// <summary>
        /// 提取列特征
        /// </summary>
        private Dictionary<string, object> ExtractColumnFeatures(ColumnInfo column)
        {
            return new Dictionary<string, object>
            {
                {"name", column.ColumnName},
                {"data_type", column.DataType},
                {"is_primary_key", column.IsPrimaryKey},
                {"is_nullable", column.IsNullable},
                {"description", column.Description ?? ""},
                {"semantic_type", InferSemanticType(column)}
            };
        }

        /// <summary>
        /// 推断表类型
        /// </summary>
        private string InferTableType(TableInfo table)
        {
            var tableName = table.TableName.ToLower();
            
            if (tableName.Contains("log") || tableName.Contains("audit"))
                return "log_table";
            if (tableName.Contains("config") || tableName.Contains("setting"))
                return "config_table";
            if (table.ForeignKeys.Count >= 2 && table.Columns.Count <= 5)
                return "junction_table";
            if (table.Columns.Count > 20)
                return "fact_table";
            
            return "dimension_table";
        }

        /// <summary>
        /// 推断语义类型
        /// </summary>
        private string InferSemanticType(ColumnInfo column)
        {
            var columnName = column.ColumnName.ToLower();
            var dataType = column.DataType.ToLower();

            if (columnName.Contains("id") && column.IsPrimaryKey)
                return "primary_key";
            if (columnName.Contains("id") && !column.IsPrimaryKey)
                return "foreign_key_candidate";
            if (columnName.Contains("name") || columnName.Contains("title"))
                return "name_field";
            if (columnName.Contains("date") || columnName.Contains("time") || dataType.Contains("date"))
                return "temporal_field";
            if (columnName.Contains("amount") || columnName.Contains("price") || columnName.Contains("cost"))
                return "monetary_field";
            if (columnName.Contains("count") || columnName.Contains("number") || columnName.Contains("qty"))
                return "numeric_field";

            return "general_field";
        }
    }

    /// <summary>
    /// Schema Linking结果
    /// </summary>
    public class SchemaLinkingResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public List<TableInfo> RelevantTables { get; set; } = new List<TableInfo>();
        public string SchemaJson { get; set; }
        public List<SchemaMatchDetail> MatchingDetails { get; set; } = new List<SchemaMatchDetail>();
        public bool UsedFallback { get; set; }
    }

    /// <summary>
    /// Schema匹配详情
    /// </summary>
    public class SchemaMatchDetail
    {
        public string TableName { get; set; }
        public string MatchType { get; set; }
        public double RelevanceScore { get; set; }
        public string MatchedText { get; set; }
        public string MatchReason { get; set; }
    }

    /// <summary>
    /// Schema图结构
    /// </summary>
    public class SchemaGraph
    {
        public List<GraphNode> Nodes { get; set; } = new List<GraphNode>();
        public List<GraphEdge> Edges { get; set; } = new List<GraphEdge>();

        public void AddTableNode(string tableName, Dictionary<string, object> features)
        {
            Nodes.Add(new GraphNode
            {
                Id = tableName,
                Type = "table",
                Features = features
            });
        }

        public void AddColumnNode(string columnId, Dictionary<string, object> features, string parentTable)
        {
            Nodes.Add(new GraphNode
            {
                Id = columnId,
                Type = "column",
                Features = features
            });

            // 添加表-列关系边
            Edges.Add(new GraphEdge
            {
                Source = parentTable,
                Target = columnId,
                Type = "contains",
                Properties = new Dictionary<string, object>()
            });
        }

        public void AddForeignKeyEdge(string sourceColumn, string targetColumn, string constraintName)
        {
            Edges.Add(new GraphEdge
            {
                Source = sourceColumn,
                Target = targetColumn,
                Type = "foreign_key",
                Properties = new Dictionary<string, object>
                {
                    {"constraint_name", constraintName}
                }
            });
        }
    }

    /// <summary>
    /// 图节点
    /// </summary>
    public class GraphNode
    {
        public string Id { get; set; }
        public string Type { get; set; }
        public Dictionary<string, object> Features { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// 图边
    /// </summary>
    public class GraphEdge
    {
        public string Source { get; set; }
        public string Target { get; set; }
        public string Type { get; set; }
        public Dictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();
    }
}

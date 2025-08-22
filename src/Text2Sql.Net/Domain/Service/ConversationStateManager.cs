using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using Text2Sql.Net.Domain.Interface;
using Text2Sql.Net.Repositories.Text2Sql.ChatHistory;
using Text2Sql.Net.Repositories.Text2Sql.DatabaseSchema;

namespace Text2Sql.Net.Domain.Service
{
    /// <summary>
    /// 对话状态管理器
    /// 处理多轮对话和上下文理解
    /// </summary>
    [ServiceDescription(typeof(IConversationStateManager), ServiceLifetime.Scoped)]
    public class ConversationStateManager : IConversationStateManager
    {
        private readonly IChatMessageRepository _chatRepository;
        private readonly ILogger<ConversationStateManager> _logger;
        private readonly Dictionary<string, ConversationContext> _activeContexts;

        public ConversationStateManager(
            IChatMessageRepository chatRepository,
            ILogger<ConversationStateManager> logger)
        {
            _chatRepository = chatRepository;
            _logger = logger;
            _activeContexts = new Dictionary<string, ConversationContext>();
        }

        /// <summary>
        /// 更新对话上下文
        /// </summary>
        /// <param name="connectionId">数据库连接ID</param>
        /// <param name="userMessage">用户消息</param>
        /// <param name="assistantMessage">助手回复</param>
        /// <param name="sql">生成的SQL</param>
        /// <param name="result">执行结果</param>
        public async Task UpdateContextAsync(
            string connectionId,
            string userMessage,
            string assistantMessage,
            string sql,
            List<Dictionary<string, object>> result)
        {
            try
            {
                if (!_activeContexts.ContainsKey(connectionId))
                {
                    _activeContexts[connectionId] = new ConversationContext { ConnectionId = connectionId };
                }

                var context = _activeContexts[connectionId];
                
                // 创建对话轮次
                var turn = new ConversationTurn
                {
                    UserMessage = userMessage,
                    AssistantMessage = assistantMessage,
                    GeneratedSql = sql,
                    ResultSummary = SummarizeResult(result),
                    ExtractedEntities = ExtractEntities(userMessage),
                    Timestamp = DateTime.Now
                };

                context.ConversationHistory.Add(turn);
                
                // 更新引用实体
                context.ReferencedEntities.UnionWith(turn.ExtractedEntities);
                
                // 更新活跃过滤条件
                UpdateActiveFilters(context, userMessage, sql);
                
                // 保持最近的上下文（限制历史长度）
                if (context.ConversationHistory.Count > 10)
                {
                    context.ConversationHistory.RemoveRange(0, context.ConversationHistory.Count - 10);
                }

                _logger.LogInformation($"更新对话上下文，连接ID: {connectionId}，当前轮次: {context.ConversationHistory.Count}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"更新对话上下文时出错：{ex.Message}");
            }
        }

        /// <summary>
        /// 解析代词和省略引用
        /// </summary>
        /// <param name="connectionId">数据库连接ID</param>
        /// <param name="userMessage">用户消息</param>
        /// <returns>解析后的消息</returns>
        public async Task<string> ResolveCoreferencesAsync(string connectionId, string userMessage)
        {
            try
            {
                if (!_activeContexts.ContainsKey(connectionId))
                {
                    return userMessage; // 没有上下文，直接返回原消息
                }

                var context = _activeContexts[connectionId];
                var resolvedMessage = userMessage;

                // 处理代词引用
                resolvedMessage = ResolvePronouns(resolvedMessage, context);

                // 处理省略的表名或过滤条件
                if (IsIncompleteQuery(resolvedMessage))
                {
                    resolvedMessage = AddImplicitContext(resolvedMessage, context);
                }

                // 处理相对时间引用
                resolvedMessage = ResolveRelativeTimeReferences(resolvedMessage, context);

                // 处理上下文依赖的查询
                resolvedMessage = ResolveContextualQueries(resolvedMessage, context);

                if (resolvedMessage != userMessage)
                {
                    _logger.LogInformation($"解析前: {userMessage}");
                    _logger.LogInformation($"解析后: {resolvedMessage}");
                }

                return resolvedMessage;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"解析代词引用时出错：{ex.Message}");
                return userMessage;
            }
        }

        /// <summary>
        /// 分析后续查询类型
        /// </summary>
        /// <param name="connectionId">数据库连接ID</param>
        /// <param name="userMessage">用户消息</param>
        /// <returns>查询类型</returns>
        public async Task<FollowupQueryType> AnalyzeFollowupQueryAsync(string connectionId, string userMessage)
        {
            try
            {
                if (!_activeContexts.ContainsKey(connectionId))
                {
                    return FollowupQueryType.NewQuery;
                }

                var context = _activeContexts[connectionId];
                var message = userMessage.ToLower();

                // 过滤精化
                if (ContainsFilterWords(message))
                {
                    return FollowupQueryType.FilterRefinement;
                }

                // 聚合变更
                if (ContainsAggregationWords(message))
                {
                    return FollowupQueryType.AggregationChange;
                }

                // 列扩展
                if (ContainsColumnExpansionWords(message))
                {
                    return FollowupQueryType.ColumnExpansion;
                }

                // 排序变更
                if (ContainsSortingWords(message))
                {
                    return FollowupQueryType.SortingChange;
                }

                // 代词引用查询
                if (ContainsPronouns(message))
                {
                    return FollowupQueryType.PronounReference;
                }

                // 比较查询
                if (ContainsComparisonWords(message))
                {
                    return FollowupQueryType.Comparison;
                }

                return FollowupQueryType.NewQuery;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"分析后续查询类型时出错：{ex.Message}");
                return FollowupQueryType.NewQuery;
            }
        }

        /// <summary>
        /// 处理增量查询
        /// </summary>
        /// <param name="connectionId">数据库连接ID</param>
        /// <param name="userMessage">用户消息</param>
        /// <param name="queryType">查询类型</param>
        /// <returns>处理后的查询消息</returns>
        public async Task<string> ProcessIncrementalQueryAsync(
            string connectionId, 
            string userMessage, 
            FollowupQueryType queryType)
        {
            try
            {
                if (!_activeContexts.ContainsKey(connectionId))
                {
                    return userMessage;
                }

                var context = _activeContexts[connectionId];
                var lastTurn = context.ConversationHistory.LastOrDefault();
                
                if (lastTurn == null)
                {
                    return userMessage;
                }

                switch (queryType)
                {
                    case FollowupQueryType.FilterRefinement:
                        return ProcessFilterRefinement(userMessage, lastTurn, context);
                    
                    case FollowupQueryType.AggregationChange:
                        return ProcessAggregationChange(userMessage, lastTurn, context);
                    
                    case FollowupQueryType.ColumnExpansion:
                        return ProcessColumnExpansion(userMessage, lastTurn, context);
                    
                    case FollowupQueryType.SortingChange:
                        return ProcessSortingChange(userMessage, lastTurn, context);
                    
                    case FollowupQueryType.PronounReference:
                        return ResolveCoreferencesAsync(connectionId, userMessage).Result;
                    
                    case FollowupQueryType.Comparison:
                        return ProcessComparison(userMessage, lastTurn, context);
                    
                    default:
                        return userMessage;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"处理增量查询时出错：{ex.Message}");
                return userMessage;
            }
        }

        /// <summary>
        /// 获取对话上下文
        /// </summary>
        /// <param name="connectionId">数据库连接ID</param>
        /// <returns>对话上下文</returns>
        public async Task<ConversationContext> GetContextAsync(string connectionId)
        {
            if (_activeContexts.ContainsKey(connectionId))
            {
                return _activeContexts[connectionId];
            }

            // 从数据库加载历史对话
            var history = await _chatRepository.GetByConnectionIdAsync(connectionId);
            var context = new ConversationContext { ConnectionId = connectionId };

            foreach (var message in history.TakeLast(10)) // 只加载最近10轮对话
            {
                var turn = new ConversationTurn
                {
                    UserMessage = message.Message,
                    AssistantMessage = "", // 这里需要根据实际情况调整
                    GeneratedSql = message.SqlQuery,
                    ExtractedEntities = ExtractEntities(message.Message),
                    Timestamp = message.CreateTime
                };
                context.ConversationHistory.Add(turn);
            }

            _activeContexts[connectionId] = context;
            return context;
        }

        /// <summary>
        /// 清理对话上下文
        /// </summary>
        /// <param name="connectionId">数据库连接ID</param>
        public async Task ClearContextAsync(string connectionId)
        {
            if (_activeContexts.ContainsKey(connectionId))
            {
                _activeContexts.Remove(connectionId);
                _logger.LogInformation($"清理对话上下文，连接ID: {connectionId}");
            }
        }

        #region 私有方法

        /// <summary>
        /// 总结查询结果
        /// </summary>
        private string SummarizeResult(List<Dictionary<string, object>> result)
        {
            if (result == null || result.Count == 0)
                return "无结果";

            return $"{result.Count}条记录，包含{result[0].Keys.Count}个字段";
        }

        /// <summary>
        /// 提取实体
        /// </summary>
        private List<string> ExtractEntities(string message)
        {
            var entities = new List<string>();
            
            // 使用正则表达式提取可能的实体（数字、引号内容、特殊词汇等）
            var patterns = new[]
            {
                @"\d+",           // 数字
                @"'([^']*)'",     // 单引号内容
                @"""([^""]*)""",  // 双引号内容
                @"\b[A-Z][a-z]+\b" // 首字母大写的词
            };

            foreach (var pattern in patterns)
            {
                var matches = Regex.Matches(message, pattern);
                foreach (Match match in matches)
                {
                    var value = match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        entities.Add(value);
                    }
                }
            }

            return entities.Distinct().ToList();
        }

        /// <summary>
        /// 更新活跃过滤条件
        /// </summary>
        private void UpdateActiveFilters(ConversationContext context, string userMessage, string sql)
        {
            // 从SQL中提取WHERE条件
            var wherePattern = @"WHERE\s+(.+?)(?:\s+GROUP\s+BY|\s+ORDER\s+BY|\s+HAVING|$)";
            var match = Regex.Match(sql, wherePattern, RegexOptions.IgnoreCase);
            
            if (match.Success)
            {
                var whereClause = match.Groups[1].Value.Trim();
                context.ActiveFilters["last_where"] = whereClause;
            }

            // 从用户消息中提取时间范围
            var timePattern = @"(最近|最后|过去|前)\s*(\d+)\s*(天|月|年|小时|分钟)";
            var timeMatch = Regex.Match(userMessage, timePattern);
            
            if (timeMatch.Success)
            {
                context.ActiveFilters["time_range"] = timeMatch.Value;
            }
        }

        /// <summary>
        /// 解析代词
        /// </summary>
        private string ResolvePronouns(string message, ConversationContext context)
        {
            var pronouns = new[] { "它", "这个", "那个", "他们", "它们", "this", "that", "they", "them", "it" };
            var resolvedMessage = message;

            foreach (var pronoun in pronouns)
            {
                if (resolvedMessage.Contains(pronoun))
                {
                    var recentEntity = FindRecentEntity(context);
                    if (!string.IsNullOrEmpty(recentEntity))
                    {
                        resolvedMessage = resolvedMessage.Replace(pronoun, recentEntity);
                    }
                }
            }

            return resolvedMessage;
        }

        /// <summary>
        /// 查找最近的实体
        /// </summary>
        private string FindRecentEntity(ConversationContext context)
        {
            var recentTurns = context.ConversationHistory.TakeLast(3);
            
            foreach (var turn in recentTurns.Reverse())
            {
                if (turn.ExtractedEntities.Any())
                {
                    return turn.ExtractedEntities.First();
                }
            }

            return null;
        }

        /// <summary>
        /// 判断是否为不完整查询
        /// </summary>
        private bool IsIncompleteQuery(string message)
        {
            var incompleteIndicators = new[] { "也", "还", "再", "and", "also", "too" };
            return incompleteIndicators.Any(indicator => message.Contains(indicator));
        }

        /// <summary>
        /// 添加隐式上下文
        /// </summary>
        private string AddImplicitContext(string message, ConversationContext context)
        {
            var enhancedMessage = message;
            var lastTurn = context.ConversationHistory.LastOrDefault();
            
            if (lastTurn != null)
            {
                // 如果当前查询没有明确的表引用，添加上一次查询的表上下文
                if (!ContainsTableReference(message) && ContainsTableReference(lastTurn.UserMessage))
                {
                    var tableContext = ExtractTableContext(lastTurn.UserMessage);
                    enhancedMessage = $"{tableContext}表中，{message}";
                }

                // 继承活跃的过滤条件
                if (context.ActiveFilters.ContainsKey("time_range"))
                {
                    enhancedMessage = $"{context.ActiveFilters["time_range"]}，{enhancedMessage}";
                }
            }

            return enhancedMessage;
        }

        /// <summary>
        /// 解析相对时间引用
        /// </summary>
        private string ResolveRelativeTimeReferences(string message, ConversationContext context)
        {
            var timePattern = @"(同期|同比|环比|上次|之前)";
            
            if (Regex.IsMatch(message, timePattern))
            {
                var lastTurn = context.ConversationHistory.LastOrDefault();
                if (lastTurn != null && context.ActiveFilters.ContainsKey("time_range"))
                {
                    message = message.Replace("同期", context.ActiveFilters["time_range"]);
                    message = message.Replace("上次", context.ActiveFilters["time_range"]);
                }
            }

            return message;
        }

        /// <summary>
        /// 解析上下文相关查询
        /// </summary>
        private string ResolveContextualQueries(string message, ConversationContext context)
        {
            // 处理"其中"、"这些"等上下文引用
            if (message.Contains("其中") || message.Contains("这些") || message.Contains("那些"))
            {
                var lastTurn = context.ConversationHistory.LastOrDefault();
                if (lastTurn != null)
                {
                    message = $"在上一次查询结果基础上，{message}";
                }
            }

            return message;
        }

        /// <summary>
        /// 检查是否包含过滤词汇
        /// </summary>
        private bool ContainsFilterWords(string message)
        {
            var filterWords = new[] { "筛选", "过滤", "条件", "只要", "除了", "不包括", "where", "filter", "条件是" };
            return filterWords.Any(word => message.Contains(word));
        }

        /// <summary>
        /// 检查是否包含聚合词汇
        /// </summary>
        private bool ContainsAggregationWords(string message)
        {
            var aggWords = new[] { "统计", "计算", "求和", "平均", "最大", "最小", "count", "sum", "avg", "max", "min" };
            return aggWords.Any(word => message.Contains(word));
        }

        /// <summary>
        /// 检查是否包含列扩展词汇
        /// </summary>
        private bool ContainsColumnExpansionWords(string message)
        {
            var columnWords = new[] { "加上", "还要", "也显示", "包括", "以及", "and", "include", "show" };
            return columnWords.Any(word => message.Contains(word));
        }

        /// <summary>
        /// 检查是否包含排序词汇
        /// </summary>
        private bool ContainsSortingWords(string message)
        {
            var sortWords = new[] { "排序", "排列", "按", "升序", "降序", "order", "sort", "asc", "desc" };
            return sortWords.Any(word => message.Contains(word));
        }

        /// <summary>
        /// 检查是否包含代词
        /// </summary>
        private bool ContainsPronouns(string message)
        {
            var pronouns = new[] { "它", "这个", "那个", "他们", "它们", "this", "that", "they", "them", "it" };
            return pronouns.Any(pronoun => message.Contains(pronoun));
        }

        /// <summary>
        /// 检查是否包含比较词汇
        /// </summary>
        private bool ContainsComparisonWords(string message)
        {
            var compWords = new[] { "比较", "对比", "差异", "相同", "不同", "compare", "difference", "versus" };
            return compWords.Any(word => message.Contains(word));
        }

        /// <summary>
        /// 检查是否包含表引用
        /// </summary>
        private bool ContainsTableReference(string message)
        {
            var tableWords = new[] { "表", "table", "用户", "订单", "商品", "客户" };
            return tableWords.Any(word => message.Contains(word));
        }

        /// <summary>
        /// 提取表上下文
        /// </summary>
        private string ExtractTableContext(string message)
        {
            var words = message.Split(' ', '，', '。');
            foreach (var word in words)
            {
                if (word.Contains("表") || word.Contains("用户") || word.Contains("订单"))
                {
                    return word;
                }
            }
            return "";
        }

        /// <summary>
        /// 处理过滤精化
        /// </summary>
        private string ProcessFilterRefinement(string userMessage, ConversationTurn lastTurn, ConversationContext context)
        {
            return $"在上一次查询基础上，{userMessage}。上次查询：{lastTurn.UserMessage}";
        }

        /// <summary>
        /// 处理聚合变更
        /// </summary>
        private string ProcessAggregationChange(string userMessage, ConversationTurn lastTurn, ConversationContext context)
        {
            return $"基于相同的数据集，{userMessage}。参考上次查询的表和条件：{lastTurn.UserMessage}";
        }

        /// <summary>
        /// 处理列扩展
        /// </summary>
        private string ProcessColumnExpansion(string userMessage, ConversationTurn lastTurn, ConversationContext context)
        {
            return $"在上次查询的基础上，{userMessage}。上次查询：{lastTurn.UserMessage}";
        }

        /// <summary>
        /// 处理排序变更
        /// </summary>
        private string ProcessSortingChange(string userMessage, ConversationTurn lastTurn, ConversationContext context)
        {
            return $"保持上次查询的内容和条件，{userMessage}。上次查询：{lastTurn.UserMessage}";
        }

        /// <summary>
        /// 处理比较查询
        /// </summary>
        private string ProcessComparison(string userMessage, ConversationTurn lastTurn, ConversationContext context)
        {
            return $"将当前查询与上次结果进行比较：{userMessage}。上次查询：{lastTurn.UserMessage}";
        }

        #endregion
    }

    /// <summary>
    /// 对话上下文
    /// </summary>
    public class ConversationContext
    {
        public string ConnectionId { get; set; }
        public List<ConversationTurn> ConversationHistory { get; set; } = new List<ConversationTurn>();
        public HashSet<string> ReferencedEntities { get; set; } = new HashSet<string>();
        public Dictionary<string, string> ActiveFilters { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// 对话轮次
    /// </summary>
    public class ConversationTurn
    {
        public string UserMessage { get; set; }
        public string AssistantMessage { get; set; }
        public string GeneratedSql { get; set; }
        public string ResultSummary { get; set; }
        public List<string> ExtractedEntities { get; set; } = new List<string>();
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// 后续查询类型
    /// </summary>
    public enum FollowupQueryType
    {
        NewQuery,           // 新查询
        FilterRefinement,   // 过滤精化
        AggregationChange,  // 聚合变更
        ColumnExpansion,    // 列扩展
        SortingChange,      // 排序变更
        PronounReference,   // 代词引用
        Comparison          // 比较查询
    }
}


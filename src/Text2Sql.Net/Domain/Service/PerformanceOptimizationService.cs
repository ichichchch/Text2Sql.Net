using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Text2Sql.Net.Domain.Interface;

namespace Text2Sql.Net.Domain.Service
{
    /// <summary>
    /// 性能优化服务
    /// 包括缓存策略、并行处理、查询复杂度分析等
    /// </summary>
    [ServiceDescription(typeof(IPerformanceOptimizationService), ServiceLifetime.Singleton)]
    public class PerformanceOptimizationService : IPerformanceOptimizationService
    {
        private readonly ConcurrentDictionary<string, object> _cache;
        private readonly ILogger<PerformanceOptimizationService> _logger;
        private readonly ConcurrentDictionary<string, QueryMetrics> _queryMetrics;
        private readonly SemaphoreSlim _parallelProcessingSemaphore;

        public PerformanceOptimizationService(ILogger<PerformanceOptimizationService> logger)
        {
            _cache = new ConcurrentDictionary<string, object>();
            _logger = logger;
            _queryMetrics = new ConcurrentDictionary<string, QueryMetrics>();
            _parallelProcessingSemaphore = new SemaphoreSlim(Environment.ProcessorCount * 2, Environment.ProcessorCount * 2);
        }

        /// <summary>
        /// 智能缓存查询结果
        /// </summary>
        /// <param name="cacheKey">缓存键</param>
        /// <param name="queryFunc">查询函数</param>
        /// <param name="cachePolicy">缓存策略</param>
        /// <returns>查询结果</returns>
        public async Task<T> GetCachedResultAsync<T>(
            string cacheKey, 
            Func<Task<T>> queryFunc, 
            CachePolicy cachePolicy = null)
        {
            try
            {
                // 尝试从缓存获取
                if (_cache.TryGetValue(cacheKey, out object? cachedObj) && cachedObj is CacheItem<T> cachedItem)
                {
                    if (cachedItem.ExpirationTime > DateTime.UtcNow)
                    {
                        _logger.LogDebug($"缓存命中：{cacheKey}");
                        UpdateCacheHitMetrics(cacheKey);
                        return cachedItem.Value;
                    }
                    else
                    {
                        // 过期的缓存项，移除
                        _cache.TryRemove(cacheKey, out _);
                    }
                }

                // 缓存未命中，执行查询
                _logger.LogDebug($"缓存未命中，执行查询：{cacheKey}");
                var result = await queryFunc();

                // 根据缓存策略决定是否缓存
                var policy = cachePolicy ?? DetermineCachePolicy(cacheKey, result);
                
                if (policy.ShouldCache)
                {
                    var cacheItem = new CacheItem<T>
                    {
                        Value = result,
                        ExpirationTime = DateTime.UtcNow.AddSeconds(policy.TtlSeconds),
                        IsHighPriority = policy.Priority == CacheItemPriority.High
                    };

                    _cache.TryAdd(cacheKey, cacheItem);
                    _logger.LogDebug($"结果已缓存：{cacheKey}，TTL: {policy.TtlSeconds}秒");
                }

                UpdateCacheMissMetrics(cacheKey);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"缓存操作失败：{cacheKey}");
                return await queryFunc(); // 直接执行查询作为后备
            }
        }

        /// <summary>
        /// 分析查询复杂度
        /// </summary>
        /// <param name="userMessage">用户查询</param>
        /// <param name="schemaInfo">Schema信息</param>
        /// <returns>复杂度分析结果</returns>
        public async Task<ComplexityAnalysis> AnalyzeQueryComplexityAsync(string userMessage, string schemaInfo)
        {
            try
            {
                var analysis = new ComplexityAnalysis();
                var message = userMessage.ToLower();

                // 基础复杂度指标
                analysis.BasicComplexity = CalculateBasicComplexity(message);

                // 表数量分析
                analysis.TableCount = EstimateTableCount(message, schemaInfo);
                
                // JOIN复杂度
                analysis.JoinComplexity = EstimateJoinComplexity(message, analysis.TableCount);

                // 聚合复杂度
                analysis.AggregationComplexity = EstimateAggregationComplexity(message);

                // 子查询复杂度
                analysis.SubqueryComplexity = EstimateSubqueryComplexity(message);

                // 时间复杂度
                analysis.TemporalComplexity = EstimateTemporalComplexity(message);

                // 计算总体复杂度分数
                analysis.OverallComplexityScore = CalculateOverallComplexity(analysis);

                // 确定处理策略
                analysis.ProcessingStrategy = DetermineProcessingStrategy(analysis);

                _logger.LogInformation($"查询复杂度分析完成：{analysis.OverallComplexityScore:F2}，策略：{analysis.ProcessingStrategy}");

                return analysis;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"分析查询复杂度时出错：{ex.Message}");
                return new ComplexityAnalysis { OverallComplexityScore = 5.0, ProcessingStrategy = ProcessingStrategy.Standard };
            }
        }

        /// <summary>
        /// 并行处理多个查询任务
        /// </summary>
        /// <param name="tasks">任务列表</param>
        /// <param name="maxParallelism">最大并行度</param>
        /// <returns>处理结果</returns>
        public async Task<List<T>> ProcessInParallelAsync<T>(
            IEnumerable<Func<Task<T>>> tasks, 
            int maxParallelism = 0)
        {
            var taskList = tasks.ToList();
            if (taskList.Count == 0) return new List<T>();

            var parallelism = maxParallelism > 0 ? 
                Math.Min(maxParallelism, Environment.ProcessorCount) : 
                Environment.ProcessorCount;

            _logger.LogInformation($"开始并行处理 {taskList.Count} 个任务，并行度：{parallelism}");

            var results = new List<T>();
            var semaphore = new SemaphoreSlim(parallelism, parallelism);

            var allTasks = taskList.Select(async task =>
            {
                await semaphore.WaitAsync();
                try
                {
                    return await task();
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var completedTasks = await Task.WhenAll(allTasks);
            results.AddRange(completedTasks);

            _logger.LogInformation($"并行处理完成，成功处理 {results.Count} 个任务");
            return results;
        }

        /// <summary>
        /// 获取查询性能指标
        /// </summary>
        /// <param name="queryId">查询ID</param>
        /// <returns>性能指标</returns>
        public async Task<QueryMetrics> GetQueryMetricsAsync(string queryId)
        {
            return _queryMetrics.GetValueOrDefault(queryId, new QueryMetrics());
        }

        /// <summary>
        /// 更新查询性能指标
        /// </summary>
        /// <param name="queryId">查询ID</param>
        /// <param name="executionTime">执行时间</param>
        /// <param name="resultSize">结果大小</param>
        /// <param name="cacheHit">是否命中缓存</param>
        public async Task UpdateQueryMetricsAsync(
            string queryId, 
            TimeSpan executionTime, 
            int resultSize, 
            bool cacheHit)
        {
            try
            {
                _queryMetrics.AddOrUpdate(queryId, 
                    new QueryMetrics
                    {
                        QueryId = queryId,
                        TotalExecutions = 1,
                        AverageExecutionTime = executionTime,
                        AverageResultSize = resultSize,
                        CacheHitRate = cacheHit ? 1.0 : 0.0,
                        LastExecutionTime = DateTime.Now
                    },
                    (key, existing) =>
                    {
                        existing.TotalExecutions++;
                        existing.AverageExecutionTime = TimeSpan.FromMilliseconds(
                            (existing.AverageExecutionTime.TotalMilliseconds * (existing.TotalExecutions - 1) + 
                             executionTime.TotalMilliseconds) / existing.TotalExecutions);
                        existing.AverageResultSize = (int)Math.Round(
                            (existing.AverageResultSize * (existing.TotalExecutions - 1.0) + resultSize) / existing.TotalExecutions);
                        existing.CacheHitRate = (existing.CacheHitRate * (existing.TotalExecutions - 1) + (cacheHit ? 1.0 : 0.0)) / existing.TotalExecutions;
                        existing.LastExecutionTime = DateTime.Now;
                        return existing;
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"更新查询指标时出错：{queryId}");
            }
        }

        /// <summary>
        /// 清理过期缓存
        /// </summary>
        public async Task CleanupExpiredCacheAsync()
        {
            try
            {
                // IMemoryCache 会自动处理过期项，这里主要清理性能指标
                var expiredMetrics = _queryMetrics
                    .Where(kvp => DateTime.Now - kvp.Value.LastExecutionTime > TimeSpan.FromHours(24))
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in expiredMetrics)
                {
                    _queryMetrics.TryRemove(key, out _);
                }

                if (expiredMetrics.Count > 0)
                {
                    _logger.LogInformation($"清理了 {expiredMetrics.Count} 个过期的查询指标");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清理过期缓存时出错");
            }
        }

        #region 私有方法

        /// <summary>
        /// 生成缓存键
        /// </summary>
        public string GenerateCacheKey(string prefix, params object[] parameters)
        {
            var keyBuilder = new StringBuilder(prefix);
            
            foreach (var param in parameters)
            {
                keyBuilder.Append("_");
                if (param != null)
                {
                    var paramStr = param.ToString();
                    if (paramStr.Length > 50) // 对于长参数，使用哈希
                    {
                        using var sha256 = SHA256.Create();
                        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(paramStr));
                        keyBuilder.Append(Convert.ToBase64String(hash)[..8]); // 取前8位
                    }
                    else
                    {
                        keyBuilder.Append(paramStr.Replace(" ", "_"));
                    }
                }
            }

            return keyBuilder.ToString();
        }

        /// <summary>
        /// 确定缓存策略
        /// </summary>
        private CachePolicy DetermineCachePolicy<T>(string cacheKey, T result)
        {
            var policy = new CachePolicy();

            // 基于缓存键分析
            if (cacheKey.Contains("schema"))
            {
                // Schema信息缓存时间较长
                policy.TtlSeconds = 3600; // 1小时
                policy.Priority = CacheItemPriority.High;
                policy.ShouldCache = true;
            }
            else if (cacheKey.Contains("config"))
            {
                // 配置信息长期缓存
                policy.TtlSeconds = 7200; // 2小时
                policy.Priority = CacheItemPriority.High;
                policy.ShouldCache = true;
            }
            else if (cacheKey.Contains("query"))
            {
                // 查询结果短期缓存
                policy.TtlSeconds = 300; // 5分钟
                policy.Priority = CacheItemPriority.Normal;
                policy.ShouldCache = true;
            }
            else
            {
                // 默认缓存策略
                policy.TtlSeconds = 600; // 10分钟
                policy.Priority = CacheItemPriority.Low;
                policy.ShouldCache = true;
            }

            // 基于结果大小调整
            var size = CalculateObjectSize(result);
            if (size > 1024 * 1024) // 大于1MB的结果降低缓存优先级
            {
                policy.Priority = CacheItemPriority.Low;
                policy.TtlSeconds = Math.Min(policy.TtlSeconds, 300);
            }

            return policy;
        }

        /// <summary>
        /// 计算对象大小（简化实现）
        /// </summary>
        private long CalculateObjectSize<T>(T obj)
        {
            if (obj == null) return 0;

            try
            {
                var json = JsonSerializer.Serialize(obj);
                return Encoding.UTF8.GetByteCount(json);
            }
            catch
            {
                return 1024; // 默认大小
            }
        }

        /// <summary>
        /// 计算基础复杂度
        /// </summary>
        private double CalculateBasicComplexity(string message)
        {
            var complexity = 1.0;

            // 基于查询长度
            complexity += message.Length / 100.0;

            // 基于关键词
            var keywords = new Dictionary<string, double>
            {
                {"统计", 0.5}, {"count", 0.5},
                {"分组", 1.0}, {"group", 1.0},
                {"排序", 0.3}, {"order", 0.3},
                {"关联", 1.5}, {"join", 1.5},
                {"子查询", 2.0}, {"subquery", 2.0},
                {"窗口函数", 2.5}, {"window", 2.5}
            };

            foreach (var keyword in keywords)
            {
                if (message.Contains(keyword.Key))
                {
                    complexity += keyword.Value;
                }
            }

            return complexity;
        }

        /// <summary>
        /// 估计表数量
        /// </summary>
        private int EstimateTableCount(string message, string schemaInfo)
        {
            var tableKeywords = new[] { "表", "table", "用户", "订单", "商品", "客户", "员工", "部门" };
            var count = 0;

            foreach (var keyword in tableKeywords)
            {
                if (message.Contains(keyword))
                {
                    count++;
                }
            }

            return Math.Max(1, count);
        }

        /// <summary>
        /// 估计JOIN复杂度
        /// </summary>
        private double EstimateJoinComplexity(string message, int tableCount)
        {
            if (tableCount <= 1) return 0;

            var joinKeywords = new[] { "关联", "连接", "join", "和", "与" };
            var joinCount = joinKeywords.Count(keyword => message.Contains(keyword));

            return tableCount * 0.5 + joinCount * 0.3;
        }

        /// <summary>
        /// 估计聚合复杂度
        /// </summary>
        private double EstimateAggregationComplexity(string message)
        {
            var aggKeywords = new Dictionary<string, double>
            {
                {"统计", 0.5}, {"count", 0.5},
                {"求和", 0.7}, {"sum", 0.7},
                {"平均", 0.8}, {"avg", 0.8},
                {"最大", 0.6}, {"max", 0.6},
                {"最小", 0.6}, {"min", 0.6},
                {"分组", 1.0}, {"group", 1.0}
            };

            return aggKeywords
                .Where(kvp => message.Contains(kvp.Key))
                .Sum(kvp => kvp.Value);
        }

        /// <summary>
        /// 估计子查询复杂度
        /// </summary>
        private double EstimateSubqueryComplexity(string message)
        {
            var subqueryKeywords = new[] { "其中", "在...中", "满足", "包含", "存在" };
            var count = subqueryKeywords.Count(keyword => message.Contains(keyword));
            return count * 1.5;
        }

        /// <summary>
        /// 估计时间复杂度
        /// </summary>
        private double EstimateTemporalComplexity(string message)
        {
            var timeKeywords = new[] { "时间", "日期", "最近", "过去", "之前", "之后", "期间" };
            var count = timeKeywords.Count(keyword => message.Contains(keyword));
            return count * 0.5;
        }

        /// <summary>
        /// 计算总体复杂度
        /// </summary>
        private double CalculateOverallComplexity(ComplexityAnalysis analysis)
        {
            return analysis.BasicComplexity +
                   analysis.JoinComplexity +
                   analysis.AggregationComplexity +
                   analysis.SubqueryComplexity +
                   analysis.TemporalComplexity;
        }

        /// <summary>
        /// 确定处理策略
        /// </summary>
        private ProcessingStrategy DetermineProcessingStrategy(ComplexityAnalysis analysis)
        {
            if (analysis.OverallComplexityScore >= 8.0)
                return ProcessingStrategy.Decompose;
            else if (analysis.OverallComplexityScore >= 5.0)
                return ProcessingStrategy.Optimize;
            else
                return ProcessingStrategy.Standard;
        }

        /// <summary>
        /// 更新缓存命中指标
        /// </summary>
        private void UpdateCacheHitMetrics(string cacheKey)
        {
            // 实现缓存命中统计逻辑
        }

        /// <summary>
        /// 更新缓存未命中指标
        /// </summary>
        private void UpdateCacheMissMetrics(string cacheKey)
        {
            // 实现缓存未命中统计逻辑
        }

        #endregion
    }

    /// <summary>
    /// 缓存优先级
    /// </summary>
    public enum CacheItemPriority
    {
        Low,
        Normal,
        High
    }

    /// <summary>
    /// 缓存策略
    /// </summary>
    public class CachePolicy
    {
        public bool ShouldCache { get; set; } = true;
        public int TtlSeconds { get; set; } = 600;
        public CacheItemPriority Priority { get; set; } = CacheItemPriority.Normal;
    }

    /// <summary>
    /// 缓存项
    /// </summary>
    public class CacheItem<T>
    {
        public T Value { get; set; } = default!;
        public DateTime ExpirationTime { get; set; }
        public bool IsHighPriority { get; set; }
    }

    /// <summary>
    /// 复杂度分析结果
    /// </summary>
    public class ComplexityAnalysis
    {
        public double BasicComplexity { get; set; }
        public int TableCount { get; set; }
        public double JoinComplexity { get; set; }
        public double AggregationComplexity { get; set; }
        public double SubqueryComplexity { get; set; }
        public double TemporalComplexity { get; set; }
        public double OverallComplexityScore { get; set; }
        public ProcessingStrategy ProcessingStrategy { get; set; }
    }

    /// <summary>
    /// 处理策略
    /// </summary>
    public enum ProcessingStrategy
    {
        Standard,   // 标准处理
        Optimize,   // 优化处理
        Decompose   // 分解处理
    }

    /// <summary>
    /// 查询性能指标
    /// </summary>
    public class QueryMetrics
    {
        public string QueryId { get; set; }
        public int TotalExecutions { get; set; }
        public TimeSpan AverageExecutionTime { get; set; }
        public int AverageResultSize { get; set; }
        public double CacheHitRate { get; set; }
        public DateTime LastExecutionTime { get; set; }
    }
}


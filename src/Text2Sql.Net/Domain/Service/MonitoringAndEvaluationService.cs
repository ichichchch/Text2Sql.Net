using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Text2Sql.Net.Domain.Interface;

namespace Text2Sql.Net.Domain.Service
{
    /// <summary>
    /// 监控与评估服务
    /// 提供系统性能监控、查询质量评估和用户行为分析
    /// </summary>
    [ServiceDescription(typeof(IMonitoringAndEvaluationService), ServiceLifetime.Singleton)]
    public class MonitoringAndEvaluationService : IMonitoringAndEvaluationService
    {
        private readonly ILogger<MonitoringAndEvaluationService> _logger;
        private readonly ConcurrentDictionary<string, SystemMetrics> _systemMetrics;
        private readonly ConcurrentDictionary<string, QueryEvaluationResult> _queryEvaluations;
        private readonly ConcurrentDictionary<string, UserBehaviorMetrics> _userBehaviorMetrics;
        private readonly Timer _metricsCollectionTimer;

        public MonitoringAndEvaluationService(ILogger<MonitoringAndEvaluationService> logger)
        {
            _logger = logger;
            _systemMetrics = new ConcurrentDictionary<string, SystemMetrics>();
            _queryEvaluations = new ConcurrentDictionary<string, QueryEvaluationResult>();
            _userBehaviorMetrics = new ConcurrentDictionary<string, UserBehaviorMetrics>();
            
            // 每分钟收集一次系统指标
            _metricsCollectionTimer = new Timer(CollectSystemMetrics, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
        }

        /// <summary>
        /// 评估查询质量
        /// </summary>
        /// <param name="evaluationRequest">评估请求</param>
        /// <returns>评估结果</returns>
        public async Task<QueryEvaluationResult> EvaluateQueryQualityAsync(QueryEvaluationRequest evaluationRequest)
        {
            try
            {
                var evaluation = new QueryEvaluationResult
                {
                    QueryId = evaluationRequest.QueryId,
                    EvaluationTime = DateTime.Now,
                    Metrics = new QueryQualityMetrics()
                };

                // 1. 语法准确性评估
                evaluation.Metrics.SyntaxAccuracy = EvaluateSyntaxAccuracy(evaluationRequest);

                // 2. 语义准确性评估
                evaluation.Metrics.SemanticAccuracy = await EvaluateSemanticAccuracyAsync(evaluationRequest);

                // 3. 执行性能评估
                evaluation.Metrics.ExecutionPerformance = EvaluateExecutionPerformance(evaluationRequest);

                // 4. 结果质量评估
                evaluation.Metrics.ResultQuality = EvaluateResultQuality(evaluationRequest);

                // 5. 用户满意度评估
                evaluation.Metrics.UserSatisfaction = EvaluateUserSatisfaction(evaluationRequest);

                // 6. 计算综合评分
                evaluation.OverallScore = CalculateOverallScore(evaluation.Metrics);

                // 7. 生成改进建议
                evaluation.ImprovementSuggestions = GenerateImprovementSuggestions(evaluation.Metrics);

                // 8. 缓存评估结果
                _queryEvaluations[evaluationRequest.QueryId] = evaluation;

                _logger.LogInformation($"查询质量评估完成，ID: {evaluationRequest.QueryId}，综合评分: {evaluation.OverallScore:F2}");

                return evaluation;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"评估查询质量时出错：{ex.Message}");
                return new QueryEvaluationResult
                {
                    QueryId = evaluationRequest.QueryId,
                    EvaluationTime = DateTime.Now,
                    OverallScore = 0,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// 记录查询执行指标
        /// </summary>
        /// <param name="queryMetric">查询指标</param>
        public async Task RecordQueryMetricAsync(QueryExecutionMetric queryMetric)
        {
            try
            {
                var metricKey = $"{DateTime.Now:yyyy-MM-dd-HH}"; // 按小时聚合
                
                _systemMetrics.AddOrUpdate(metricKey, 
                    new SystemMetrics
                    {
                        Timestamp = DateTime.Now,
                        TotalQueries = 1,
                        AverageResponseTime = queryMetric.ExecutionTime,
                        SuccessRate = queryMetric.IsSuccessful ? 1.0 : 0.0,
                        ErrorRate = queryMetric.IsSuccessful ? 0.0 : 1.0,
                        CacheHitRate = queryMetric.CacheHit ? 1.0 : 0.0
                    },
                    (key, existing) =>
                    {
                        existing.TotalQueries++;
                        existing.AverageResponseTime = TimeSpan.FromMilliseconds(
                            (existing.AverageResponseTime.TotalMilliseconds * (existing.TotalQueries - 1) + 
                             queryMetric.ExecutionTime.TotalMilliseconds) / existing.TotalQueries);
                        existing.SuccessRate = (existing.SuccessRate * (existing.TotalQueries - 1) + (queryMetric.IsSuccessful ? 1.0 : 0.0)) / existing.TotalQueries;
                        existing.ErrorRate = (existing.ErrorRate * (existing.TotalQueries - 1) + (queryMetric.IsSuccessful ? 0.0 : 1.0)) / existing.TotalQueries;
                        existing.CacheHitRate = (existing.CacheHitRate * (existing.TotalQueries - 1) + (queryMetric.CacheHit ? 1.0 : 0.0)) / existing.TotalQueries;
                        return existing;
                    });

                _logger.LogDebug($"记录查询指标：{JsonSerializer.Serialize(queryMetric)}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"记录查询指标时出错：{ex.Message}");
            }
        }

        /// <summary>
        /// 记录用户行为
        /// </summary>
        /// <param name="userBehavior">用户行为</param>
        public async Task RecordUserBehaviorAsync(UserBehavior userBehavior)
        {
            try
            {
                var userId = userBehavior.UserId ?? "anonymous";
                
                _userBehaviorMetrics.AddOrUpdate(userId,
                    new UserBehaviorMetrics
                    {
                        UserId = userId,
                        TotalQueries = 1,
                        AverageQueryComplexity = userBehavior.QueryComplexity,
                        PreferredQueryTypes = new Dictionary<string, int> { { userBehavior.QueryType, 1 } },
                        LastActivityTime = DateTime.Now,
                        SessionCount = 1
                    },
                    (key, existing) =>
                    {
                        existing.TotalQueries++;
                        existing.AverageQueryComplexity = (existing.AverageQueryComplexity * (existing.TotalQueries - 1) + userBehavior.QueryComplexity) / existing.TotalQueries;
                        
                        if (existing.PreferredQueryTypes.ContainsKey(userBehavior.QueryType))
                            existing.PreferredQueryTypes[userBehavior.QueryType]++;
                        else
                            existing.PreferredQueryTypes[userBehavior.QueryType] = 1;
                        
                        existing.LastActivityTime = DateTime.Now;
                        
                        // 如果距离上次活动超过30分钟，算作新会话
                        if (DateTime.Now - existing.LastActivityTime > TimeSpan.FromMinutes(30))
                            existing.SessionCount++;
                        
                        return existing;
                    });

                _logger.LogDebug($"记录用户行为：用户ID {userId}，查询类型 {userBehavior.QueryType}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"记录用户行为时出错：{ex.Message}");
            }
        }

        /// <summary>
        /// 获取系统性能报告
        /// </summary>
        /// <param name="timeRange">时间范围</param>
        /// <returns>性能报告</returns>
        public async Task<SystemPerformanceReport> GetSystemPerformanceReportAsync(TimeRange timeRange)
        {
            try
            {
                var now = DateTime.Now;
                var startTime = timeRange switch
                {
                    TimeRange.Last24Hours => now.AddHours(-24),
                    TimeRange.LastWeek => now.AddDays(-7),
                    TimeRange.LastMonth => now.AddMonths(-1),
                    _ => now.AddHours(-1)
                };

                var relevantMetrics = _systemMetrics.Values
                    .Where(m => m.Timestamp >= startTime)
                    .ToList();

                if (!relevantMetrics.Any())
                {
                    return new SystemPerformanceReport
                    {
                        TimeRange = timeRange,
                        ReportGenerated = now,
                        TotalQueries = 0,
                        Message = "指定时间范围内没有数据"
                    };
                }

                var report = new SystemPerformanceReport
                {
                    TimeRange = timeRange,
                    ReportGenerated = now,
                    TotalQueries = relevantMetrics.Sum(m => m.TotalQueries),
                    AverageResponseTime = TimeSpan.FromMilliseconds(
                        relevantMetrics.Average(m => m.AverageResponseTime.TotalMilliseconds)),
                    SuccessRate = relevantMetrics.Average(m => m.SuccessRate),
                    ErrorRate = relevantMetrics.Average(m => m.ErrorRate),
                    CacheHitRate = relevantMetrics.Average(m => m.CacheHitRate),
                    PeakQueriesPerHour = relevantMetrics.Max(m => m.TotalQueries),
                    SystemHealth = CalculateSystemHealth(relevantMetrics)
                };

                // 生成性能趋势
                report.PerformanceTrend = AnalyzePerformanceTrend(relevantMetrics);

                // 生成告警
                report.Alerts = GenerateSystemAlerts(report);

                return report;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"生成系统性能报告时出错：{ex.Message}");
                return new SystemPerformanceReport
                {
                    TimeRange = timeRange,
                    ReportGenerated = DateTime.Now,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// 获取用户行为分析报告
        /// </summary>
        /// <param name="timeRange">时间范围</param>
        /// <returns>用户行为报告</returns>
        public async Task<UserBehaviorReport> GetUserBehaviorReportAsync(TimeRange timeRange)
        {
            try
            {
                var now = DateTime.Now;
                var startTime = timeRange switch
                {
                    TimeRange.Last24Hours => now.AddHours(-24),
                    TimeRange.LastWeek => now.AddDays(-7),
                    TimeRange.LastMonth => now.AddMonths(-1),
                    _ => now.AddHours(-1)
                };

                var activeUsers = _userBehaviorMetrics.Values
                    .Where(u => u.LastActivityTime >= startTime)
                    .ToList();

                var report = new UserBehaviorReport
                {
                    TimeRange = timeRange,
                    ReportGenerated = now,
                    TotalActiveUsers = activeUsers.Count,
                    TotalQueries = activeUsers.Sum(u => u.TotalQueries),
                    AverageQueriesPerUser = activeUsers.Any() ? activeUsers.Average(u => u.TotalQueries) : 0,
                    AverageQueryComplexity = activeUsers.Any() ? activeUsers.Average(u => u.AverageQueryComplexity) : 0,
                    MostPopularQueryTypes = GetMostPopularQueryTypes(activeUsers),
                    UserEngagementLevel = CalculateUserEngagementLevel(activeUsers)
                };

                return report;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"生成用户行为报告时出错：{ex.Message}");
                return new UserBehaviorReport
                {
                    TimeRange = timeRange,
                    ReportGenerated = DateTime.Now,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// 清理过期数据
        /// </summary>
        public async Task CleanupExpiredDataAsync()
        {
            try
            {
                var cutoffTime = DateTime.Now.AddDays(-30); // 保留30天数据

                // 清理系统指标
                var expiredMetricKeys = _systemMetrics
                    .Where(kvp => kvp.Value.Timestamp < cutoffTime)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in expiredMetricKeys)
                {
                    _systemMetrics.TryRemove(key, out _);
                }

                // 清理查询评估
                var expiredEvaluationKeys = _queryEvaluations
                    .Where(kvp => kvp.Value.EvaluationTime < cutoffTime)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in expiredEvaluationKeys)
                {
                    _queryEvaluations.TryRemove(key, out _);
                }

                // 清理不活跃用户数据
                var inactiveUserKeys = _userBehaviorMetrics
                    .Where(kvp => kvp.Value.LastActivityTime < cutoffTime)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in inactiveUserKeys)
                {
                    _userBehaviorMetrics.TryRemove(key, out _);
                }

                if (expiredMetricKeys.Count > 0 || expiredEvaluationKeys.Count > 0 || inactiveUserKeys.Count > 0)
                {
                    _logger.LogInformation($"清理过期数据：指标 {expiredMetricKeys.Count} 项，评估 {expiredEvaluationKeys.Count} 项，用户 {inactiveUserKeys.Count} 项");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"清理过期数据时出错：{ex.Message}");
            }
        }

        #region 私有方法

        /// <summary>
        /// 评估语法准确性
        /// </summary>
        private double EvaluateSyntaxAccuracy(QueryEvaluationRequest request)
        {
            if (string.IsNullOrEmpty(request.GeneratedSql))
                return 0.0;

            double score = 1.0;

            // 检查基本语法元素
            if (!request.GeneratedSql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                score -= 0.3;

            // 检查是否有明显的语法错误
            if (!string.IsNullOrEmpty(request.ExecutionError))
            {
                if (request.ExecutionError.ToLower().Contains("syntax"))
                    score -= 0.5;
                else if (request.ExecutionError.ToLower().Contains("column") && request.ExecutionError.ToLower().Contains("not found"))
                    score -= 0.3;
                else if (request.ExecutionError.ToLower().Contains("table") && request.ExecutionError.ToLower().Contains("not found"))
                    score -= 0.4;
            }

            return Math.Max(0.0, score);
        }

        /// <summary>
        /// 评估语义准确性
        /// </summary>
        private async Task<double> EvaluateSemanticAccuracyAsync(QueryEvaluationRequest request)
        {
            double score = 1.0;

            // 基于执行结果评估
            if (request.QueryResult == null || request.QueryResult.Count == 0)
            {
                // 如果查询没有结果，需要判断是否合理
                if (request.UserMessage.ToLower().Contains("查询") || request.UserMessage.ToLower().Contains("查找"))
                {
                    score -= 0.2; // 可能合理，轻微扣分
                }
            }

            // 基于结果数量合理性评估
            if (request.QueryResult != null)
            {
                var resultCount = request.QueryResult.Count;
                if (request.UserMessage.ToLower().Contains("all") || request.UserMessage.ToLower().Contains("所有"))
                {
                    // 期望较多结果
                    if (resultCount < 5) score -= 0.1;
                }
                else if (request.UserMessage.ToLower().Contains("top") || request.UserMessage.ToLower().Contains("前"))
                {
                    // 期望有限结果
                    if (resultCount > 100) score -= 0.1;
                }
            }

            return Math.Max(0.0, score);
        }

        /// <summary>
        /// 评估执行性能
        /// </summary>
        private double EvaluateExecutionPerformance(QueryEvaluationRequest request)
        {
            if (request.ExecutionTime == TimeSpan.Zero)
                return 0.5; // 无执行时间信息

            var milliseconds = request.ExecutionTime.TotalMilliseconds;

            if (milliseconds < 100) return 1.0;      // 优秀
            if (milliseconds < 500) return 0.9;      // 良好
            if (milliseconds < 1000) return 0.8;     // 一般
            if (milliseconds < 3000) return 0.6;     // 较差
            return 0.3; // 很差
        }

        /// <summary>
        /// 评估结果质量
        /// </summary>
        private double EvaluateResultQuality(QueryEvaluationRequest request)
        {
            if (!string.IsNullOrEmpty(request.ExecutionError))
                return 0.0;

            if (request.QueryResult == null)
                return 0.3;

            double score = 1.0;

            // 基于结果的完整性和一致性评估
            if (request.QueryResult.Count > 0)
            {
                var firstRow = request.QueryResult[0];
                var columnCount = firstRow.Keys.Count;

                // 检查数据完整性
                foreach (var row in request.QueryResult)
                {
                    if (row.Keys.Count != columnCount)
                    {
                        score -= 0.1;
                        break;
                    }
                }

                // 检查是否有过多的NULL值
                var totalCells = request.QueryResult.Count * columnCount;
                var nullCells = request.QueryResult.Sum(row => row.Values.Count(v => v == null || v == DBNull.Value));
                var nullRatio = (double)nullCells / totalCells;

                if (nullRatio > 0.5) score -= 0.2;
                else if (nullRatio > 0.3) score -= 0.1;
            }

            return Math.Max(0.0, score);
        }

        /// <summary>
        /// 评估用户满意度
        /// </summary>
        private double EvaluateUserSatisfaction(QueryEvaluationRequest request)
        {
            // 基于用户反馈评估（如果有的话）
            if (request.UserFeedback.HasValue)
                return Math.Max(0.0, Math.Min(1.0, request.UserFeedback.Value / 5.0));

            // 基于查询成功率推断
            if (!string.IsNullOrEmpty(request.ExecutionError))
                return 0.2;

            if (request.QueryResult?.Count > 0)
                return 0.8;

            return 0.5; // 中性评分
        }

        /// <summary>
        /// 计算综合评分
        /// </summary>
        private double CalculateOverallScore(QueryQualityMetrics metrics)
        {
            // 加权平均
            return 0.25 * metrics.SyntaxAccuracy +
                   0.30 * metrics.SemanticAccuracy +
                   0.20 * metrics.ExecutionPerformance +
                   0.15 * metrics.ResultQuality +
                   0.10 * metrics.UserSatisfaction;
        }

        /// <summary>
        /// 生成改进建议
        /// </summary>
        private List<string> GenerateImprovementSuggestions(QueryQualityMetrics metrics)
        {
            var suggestions = new List<string>();

            if (metrics.SyntaxAccuracy < 0.8)
                suggestions.Add("提升SQL语法准确性，加强基础语法检查");

            if (metrics.SemanticAccuracy < 0.8)
                suggestions.Add("改进语义理解，确保生成的SQL符合用户意图");

            if (metrics.ExecutionPerformance < 0.7)
                suggestions.Add("优化查询性能，考虑添加索引或重构查询逻辑");

            if (metrics.ResultQuality < 0.7)
                suggestions.Add("提升结果质量，检查数据完整性和一致性");

            if (metrics.UserSatisfaction < 0.6)
                suggestions.Add("关注用户体验，收集更多用户反馈");

            return suggestions;
        }

        /// <summary>
        /// 定期收集系统指标
        /// </summary>
        private void CollectSystemMetrics(object state)
        {
            try
            {
                var process = Process.GetCurrentProcess();
                var key = $"system_{DateTime.Now:yyyy-MM-dd-HH-mm}";

                var systemMetric = new SystemMetrics
                {
                    Timestamp = DateTime.Now,
                    MemoryUsage = process.WorkingSet64 / (1024 * 1024), // MB
                    CpuUsage = GetCpuUsage(),
                    ActiveConnections = GetActiveConnections()
                };

                _systemMetrics[key] = systemMetric;

                // 保持最近1000条记录
                if (_systemMetrics.Count > 1000)
                {
                    var oldestKey = _systemMetrics.Keys.OrderBy(k => k).First();
                    _systemMetrics.TryRemove(oldestKey, out _);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "收集系统指标时出错");
            }
        }

        /// <summary>
        /// 获取CPU使用率（简化实现）
        /// </summary>
        private double GetCpuUsage()
        {
            // 这里应该实现真实的CPU使用率获取逻辑
            return 0.0;
        }

        /// <summary>
        /// 获取活跃连接数（简化实现）
        /// </summary>
        private int GetActiveConnections()
        {
            // 这里应该实现真实的连接数获取逻辑
            return 0;
        }

        /// <summary>
        /// 计算系统健康度
        /// </summary>
        private SystemHealth CalculateSystemHealth(List<SystemMetrics> metrics)
        {
            if (!metrics.Any()) return SystemHealth.Unknown;

            var avgSuccessRate = metrics.Average(m => m.SuccessRate);
            var avgResponseTime = metrics.Average(m => m.AverageResponseTime.TotalMilliseconds);

            if (avgSuccessRate >= 0.95 && avgResponseTime < 1000)
                return SystemHealth.Excellent;
            else if (avgSuccessRate >= 0.90 && avgResponseTime < 2000)
                return SystemHealth.Good;
            else if (avgSuccessRate >= 0.80 && avgResponseTime < 5000)
                return SystemHealth.Fair;
            else
                return SystemHealth.Poor;
        }

        /// <summary>
        /// 分析性能趋势
        /// </summary>
        private PerformanceTrend AnalyzePerformanceTrend(List<SystemMetrics> metrics)
        {
            if (metrics.Count < 2) return PerformanceTrend.Stable;

            var sortedMetrics = metrics.OrderBy(m => m.Timestamp).ToList();
            var firstHalf = sortedMetrics.Take(sortedMetrics.Count / 2);
            var secondHalf = sortedMetrics.Skip(sortedMetrics.Count / 2);

            var firstHalfAvgResponse = firstHalf.Average(m => m.AverageResponseTime.TotalMilliseconds);
            var secondHalfAvgResponse = secondHalf.Average(m => m.AverageResponseTime.TotalMilliseconds);

            var improvement = (firstHalfAvgResponse - secondHalfAvgResponse) / firstHalfAvgResponse;

            if (improvement > 0.1) return PerformanceTrend.Improving;
            else if (improvement < -0.1) return PerformanceTrend.Degrading;
            else return PerformanceTrend.Stable;
        }

        /// <summary>
        /// 生成系统告警
        /// </summary>
        private List<SystemAlert> GenerateSystemAlerts(SystemPerformanceReport report)
        {
            var alerts = new List<SystemAlert>();

            if (report.ErrorRate > 0.1)
            {
                alerts.Add(new SystemAlert
                {
                    Level = AlertLevel.High,
                    Message = $"错误率过高：{report.ErrorRate:P2}",
                    Timestamp = DateTime.Now
                });
            }

            if (report.AverageResponseTime.TotalMilliseconds > 3000)
            {
                alerts.Add(new SystemAlert
                {
                    Level = AlertLevel.Medium,
                    Message = $"平均响应时间过长：{report.AverageResponseTime.TotalMilliseconds:F0}ms",
                    Timestamp = DateTime.Now
                });
            }

            if (report.CacheHitRate < 0.5)
            {
                alerts.Add(new SystemAlert
                {
                    Level = AlertLevel.Low,
                    Message = $"缓存命中率偏低：{report.CacheHitRate:P2}",
                    Timestamp = DateTime.Now
                });
            }

            return alerts;
        }

        /// <summary>
        /// 获取最受欢迎的查询类型
        /// </summary>
        private Dictionary<string, int> GetMostPopularQueryTypes(List<UserBehaviorMetrics> users)
        {
            var queryTypeCounts = new Dictionary<string, int>();

            foreach (var user in users)
            {
                foreach (var queryType in user.PreferredQueryTypes)
                {
                    if (queryTypeCounts.ContainsKey(queryType.Key))
                        queryTypeCounts[queryType.Key] += queryType.Value;
                    else
                        queryTypeCounts[queryType.Key] = queryType.Value;
                }
            }

            return queryTypeCounts.OrderByDescending(kvp => kvp.Value)
                .Take(10)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        /// <summary>
        /// 计算用户参与度
        /// </summary>
        private UserEngagementLevel CalculateUserEngagementLevel(List<UserBehaviorMetrics> users)
        {
            if (!users.Any()) return UserEngagementLevel.Low;

            var avgQueriesPerUser = users.Average(u => u.TotalQueries);
            var avgSessionsPerUser = users.Average(u => u.SessionCount);

            if (avgQueriesPerUser >= 10 && avgSessionsPerUser >= 3)
                return UserEngagementLevel.High;
            else if (avgQueriesPerUser >= 5 && avgSessionsPerUser >= 2)
                return UserEngagementLevel.Medium;
            else
                return UserEngagementLevel.Low;
        }

        #endregion

        public void Dispose()
        {
            _metricsCollectionTimer?.Dispose();
        }
    }

    #region 数据模型

    /// <summary>
    /// 查询评估请求
    /// </summary>
    public class QueryEvaluationRequest
    {
        public string QueryId { get; set; }
        public string UserMessage { get; set; }
        public string GeneratedSql { get; set; }
        public List<Dictionary<string, object>> QueryResult { get; set; }
        public string ExecutionError { get; set; }
        public TimeSpan ExecutionTime { get; set; }
        public double? UserFeedback { get; set; } // 1-5分
    }

    /// <summary>
    /// 查询评估结果
    /// </summary>
    public class QueryEvaluationResult
    {
        public string QueryId { get; set; }
        public DateTime EvaluationTime { get; set; }
        public QueryQualityMetrics Metrics { get; set; }
        public double OverallScore { get; set; }
        public List<string> ImprovementSuggestions { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// 查询质量指标
    /// </summary>
    public class QueryQualityMetrics
    {
        public double SyntaxAccuracy { get; set; }      // 语法准确性
        public double SemanticAccuracy { get; set; }    // 语义准确性
        public double ExecutionPerformance { get; set; } // 执行性能
        public double ResultQuality { get; set; }       // 结果质量
        public double UserSatisfaction { get; set; }    // 用户满意度
    }

    /// <summary>
    /// 查询执行指标
    /// </summary>
    public class QueryExecutionMetric
    {
        public string QueryId { get; set; }
        public TimeSpan ExecutionTime { get; set; }
        public bool IsSuccessful { get; set; }
        public bool CacheHit { get; set; }
        public int ResultSize { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// 用户行为
    /// </summary>
    public class UserBehavior
    {
        public string UserId { get; set; }
        public string QueryType { get; set; }
        public double QueryComplexity { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// 系统指标
    /// </summary>
    public class SystemMetrics
    {
        public DateTime Timestamp { get; set; }
        public int TotalQueries { get; set; }
        public TimeSpan AverageResponseTime { get; set; }
        public double SuccessRate { get; set; }
        public double ErrorRate { get; set; }
        public double CacheHitRate { get; set; }
        public long MemoryUsage { get; set; } // MB
        public double CpuUsage { get; set; }
        public int ActiveConnections { get; set; }
    }

    /// <summary>
    /// 用户行为指标
    /// </summary>
    public class UserBehaviorMetrics
    {
        public string UserId { get; set; }
        public int TotalQueries { get; set; }
        public double AverageQueryComplexity { get; set; }
        public Dictionary<string, int> PreferredQueryTypes { get; set; }
        public DateTime LastActivityTime { get; set; }
        public int SessionCount { get; set; }
    }

    /// <summary>
    /// 系统性能报告
    /// </summary>
    public class SystemPerformanceReport
    {
        public TimeRange TimeRange { get; set; }
        public DateTime ReportGenerated { get; set; }
        public int TotalQueries { get; set; }
        public TimeSpan AverageResponseTime { get; set; }
        public double SuccessRate { get; set; }
        public double ErrorRate { get; set; }
        public double CacheHitRate { get; set; }
        public int PeakQueriesPerHour { get; set; }
        public SystemHealth SystemHealth { get; set; }
        public PerformanceTrend PerformanceTrend { get; set; }
        public List<SystemAlert> Alerts { get; set; }
        public string ErrorMessage { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// 用户行为报告
    /// </summary>
    public class UserBehaviorReport
    {
        public TimeRange TimeRange { get; set; }
        public DateTime ReportGenerated { get; set; }
        public int TotalActiveUsers { get; set; }
        public int TotalQueries { get; set; }
        public double AverageQueriesPerUser { get; set; }
        public double AverageQueryComplexity { get; set; }
        public Dictionary<string, int> MostPopularQueryTypes { get; set; }
        public UserEngagementLevel UserEngagementLevel { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// 系统告警
    /// </summary>
    public class SystemAlert
    {
        public AlertLevel Level { get; set; }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// 时间范围
    /// </summary>
    public enum TimeRange
    {
        LastHour,
        Last24Hours,
        LastWeek,
        LastMonth
    }

    /// <summary>
    /// 系统健康状态
    /// </summary>
    public enum SystemHealth
    {
        Excellent,
        Good,
        Fair,
        Poor,
        Unknown
    }

    /// <summary>
    /// 性能趋势
    /// </summary>
    public enum PerformanceTrend
    {
        Improving,
        Stable,
        Degrading
    }

    /// <summary>
    /// 用户参与度级别
    /// </summary>
    public enum UserEngagementLevel
    {
        High,
        Medium,
        Low
    }

    /// <summary>
    /// 告警级别
    /// </summary>
    public enum AlertLevel
    {
        Low,
        Medium,
        High,
        Critical
    }

    #endregion
}


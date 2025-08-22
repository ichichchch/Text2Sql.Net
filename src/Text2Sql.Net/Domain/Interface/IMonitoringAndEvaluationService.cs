using Text2Sql.Net.Domain.Service;

namespace Text2Sql.Net.Domain.Interface
{
    /// <summary>
    /// 监控与评估服务接口
    /// </summary>
    public interface IMonitoringAndEvaluationService : IDisposable
    {
        /// <summary>
        /// 评估查询质量
        /// </summary>
        /// <param name="evaluationRequest">评估请求</param>
        /// <returns>评估结果</returns>
        Task<QueryEvaluationResult> EvaluateQueryQualityAsync(QueryEvaluationRequest evaluationRequest);

        /// <summary>
        /// 记录查询执行指标
        /// </summary>
        /// <param name="queryMetric">查询指标</param>
        Task RecordQueryMetricAsync(QueryExecutionMetric queryMetric);

        /// <summary>
        /// 记录用户行为
        /// </summary>
        /// <param name="userBehavior">用户行为</param>
        Task RecordUserBehaviorAsync(UserBehavior userBehavior);

        /// <summary>
        /// 获取系统性能报告
        /// </summary>
        /// <param name="timeRange">时间范围</param>
        /// <returns>性能报告</returns>
        Task<SystemPerformanceReport> GetSystemPerformanceReportAsync(TimeRange timeRange);

        /// <summary>
        /// 获取用户行为分析报告
        /// </summary>
        /// <param name="timeRange">时间范围</param>
        /// <returns>用户行为报告</returns>
        Task<UserBehaviorReport> GetUserBehaviorReportAsync(TimeRange timeRange);

        /// <summary>
        /// 清理过期数据
        /// </summary>
        Task CleanupExpiredDataAsync();
    }
}


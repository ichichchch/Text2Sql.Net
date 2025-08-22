using Text2Sql.Net.Domain.Service;

namespace Text2Sql.Net.Domain.Interface
{
    /// <summary>
    /// 性能优化服务接口
    /// </summary>
    public interface IPerformanceOptimizationService
    {
        /// <summary>
        /// 智能缓存查询结果
        /// </summary>
        /// <param name="cacheKey">缓存键</param>
        /// <param name="queryFunc">查询函数</param>
        /// <param name="cachePolicy">缓存策略</param>
        /// <returns>查询结果</returns>
        Task<T> GetCachedResultAsync<T>(string cacheKey, Func<Task<T>> queryFunc, CachePolicy cachePolicy = null);

        /// <summary>
        /// 分析查询复杂度
        /// </summary>
        /// <param name="userMessage">用户查询</param>
        /// <param name="schemaInfo">Schema信息</param>
        /// <returns>复杂度分析结果</returns>
        Task<ComplexityAnalysis> AnalyzeQueryComplexityAsync(string userMessage, string schemaInfo);

        /// <summary>
        /// 并行处理多个查询任务
        /// </summary>
        /// <param name="tasks">任务列表</param>
        /// <param name="maxParallelism">最大并行度</param>
        /// <returns>处理结果</returns>
        Task<List<T>> ProcessInParallelAsync<T>(IEnumerable<Func<Task<T>>> tasks, int maxParallelism = 0);

        /// <summary>
        /// 获取查询性能指标
        /// </summary>
        /// <param name="queryId">查询ID</param>
        /// <returns>性能指标</returns>
        Task<QueryMetrics> GetQueryMetricsAsync(string queryId);

        /// <summary>
        /// 更新查询性能指标
        /// </summary>
        /// <param name="queryId">查询ID</param>
        /// <param name="executionTime">执行时间</param>
        /// <param name="resultSize">结果大小</param>
        /// <param name="cacheHit">是否命中缓存</param>
        Task UpdateQueryMetricsAsync(string queryId, TimeSpan executionTime, int resultSize, bool cacheHit);

        /// <summary>
        /// 清理过期缓存
        /// </summary>
        Task CleanupExpiredCacheAsync();

        /// <summary>
        /// 生成缓存键
        /// </summary>
        /// <param name="prefix">前缀</param>
        /// <param name="parameters">参数</param>
        /// <returns>缓存键</returns>
        string GenerateCacheKey(string prefix, params object[] parameters);
    }
}


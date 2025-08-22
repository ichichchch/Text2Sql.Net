using Text2Sql.Net.Domain.Service;

namespace Text2Sql.Net.Domain.Interface
{
    /// <summary>
    /// 执行反馈优化器接口
    /// </summary>
    public interface IExecutionFeedbackOptimizer
    {
        /// <summary>
        /// 基于执行反馈进行迭代优化
        /// </summary>
        /// <param name="connectionId">数据库连接ID</param>
        /// <param name="userMessage">用户查询</param>
        /// <param name="schemaInfo">Schema信息</param>
        /// <param name="initialSql">初始SQL</param>
        /// <param name="maxIterations">最大迭代次数</param>
        /// <returns>优化结果</returns>
        Task<OptimizationResult> OptimizeWithFeedbackAsync(string connectionId, string userMessage, string schemaInfo, string initialSql, int maxIterations = 3);
    }
}


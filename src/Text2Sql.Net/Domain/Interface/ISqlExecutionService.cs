using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Text2Sql.Net.Domain.Interface
{
    /// <summary>
    /// SQL执行服务接口
    /// </summary>
    public interface ISqlExecutionService
    {
        /// <summary>
        /// 执行SQL查询
        /// </summary>
        /// <param name="connectionId">数据库连接ID</param>
        /// <param name="sqlQuery">SQL查询语句</param>
        /// <returns>查询结果和可能的错误信息</returns>
        Task<(List<Dictionary<string, object>> Result, string ErrorMessage)> ExecuteQueryAsync(string connectionId, string sqlQuery);
    }
} 
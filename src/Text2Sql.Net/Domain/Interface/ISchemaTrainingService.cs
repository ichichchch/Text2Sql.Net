using System.Threading.Tasks;

namespace Text2Sql.Net.Domain.Interface
{
    /// <summary>
    /// 数据库Schema训练服务接口
    /// </summary>
    public interface ISchemaTrainingService
    {
        /// <summary>
        /// 训练数据库Schema
        /// </summary>
        /// <param name="connectionId">数据库连接ID</param>
        /// <returns>训练结果</returns>
        Task<bool> TrainDatabaseSchemaAsync(string connectionId);
        
        /// <summary>
        /// 获取数据库Schema
        /// </summary>
        /// <param name="connectionId">数据库连接ID</param>
        /// <returns>Schema信息</returns>
        Task<string> GetDatabaseSchemaAsync(string connectionId);
    }
} 
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Text2Sql.Net.Base;
using Text2Sql.Net.Domain.Model;
using Text2Sql.Net.Repositories.Text2Sql.DatabaseSchema;
using Text2Sql.Net.Repositories.Text2Sql.SchemaEmbedding;

namespace Text2Sql.Net.Repositories.Text2Sql.DatabaseConnection
{
    /// <summary>
    /// 数据库连接配置仓储实现类
    /// </summary>
    [ServiceDescription(typeof(IDatabaseConnectionConfigRepository), ServiceLifetime.Scoped)]
    public class DatabaseConnectionConfigRepository : Repository<DatabaseConnectionConfig>, IDatabaseConnectionConfigRepository
    {
        private readonly IDatabaseSchemaRepository _schemaRepository;
        private readonly ISchemaEmbeddingRepository _embeddingRepository;

        public DatabaseConnectionConfigRepository(IDatabaseSchemaRepository schemaRepository, ISchemaEmbeddingRepository embeddingRepository)
        {
            _schemaRepository = schemaRepository;
            _embeddingRepository = embeddingRepository;
        }

        /// <summary>
        /// 更新连接配置并处理Schema数据一致性
        /// </summary>
        /// <param name="newConfig">新的连接配置</param>
        /// <returns>更新结果和是否需要重新训练Schema</returns>
        public async Task<(bool Success, bool ShouldRetrainSchema, string Message)> UpdateWithSchemaCleanupAsync(DatabaseConnectionConfig newConfig)
        {
            try
            {
                // 获取现有配置
                var existingConfig = await GetByIdAsync(newConfig.Id);
                if (existingConfig == null)
                {
                    // 新增配置，直接插入
                    var insertResult = await InsertOrUpdateAsync(newConfig);
                    return (insertResult, false, insertResult ? "配置添加成功" : "配置添加失败");
                }

                // 检查关键字段是否发生变化
                bool connectionChanged = HasCriticalFieldsChanged(existingConfig, newConfig);

                // 更新配置
                var updateResult = await InsertOrUpdateAsync(newConfig);
                if (!updateResult)
                {
                    return (false, false, "配置更新失败");
                }

                // 如果连接信息发生变化，清理相关Schema数据
                if (connectionChanged)
                {
                    await CleanupSchemaDataAsync(newConfig.Id);
                    return (true, true, "配置更新成功，检测到数据库连接信息变更，已清理相关Schema数据，建议重新训练Schema");
                }

                return (true, false, "配置更新成功");
            }
            catch (Exception ex)
            {
                return (false, false, $"配置更新失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 检查关键字段是否发生变化
        /// </summary>
        /// <param name="existing">现有配置</param>
        /// <param name="newConfig">新配置</param>
        /// <returns>是否有关键字段变化</returns>
        private bool HasCriticalFieldsChanged(DatabaseConnectionConfig existing, DatabaseConnectionConfig newConfig)
        {
            return existing.DbType != newConfig.DbType ||
                   existing.Server != newConfig.Server ||
                   existing.Port != newConfig.Port ||
                   existing.Database != newConfig.Database ||
                   existing.Username != newConfig.Username ||
                   existing.Password != newConfig.Password ||
                   existing.ConnectionString != newConfig.ConnectionString;
        }

        /// <summary>
        /// 清理指定连接ID的Schema相关数据
        /// </summary>
        /// <param name="connectionId">连接ID</param>
        /// <returns></returns>
        private async Task CleanupSchemaDataAsync(string connectionId)
        {
            try
            {
                // 删除Schema嵌入数据
                await _embeddingRepository.DeleteByConnectionIdAsync(connectionId);

                // 删除Schema信息
                var existingSchema = await _schemaRepository.GetByConnectionIdAsync(connectionId);
                if (existingSchema != null)
                {
                    await _schemaRepository.DeleteAsync(existingSchema);
                }
            }
            catch (Exception ex)
            {
                // 记录日志但不抛出异常，避免影响主流程
                Console.WriteLine($"清理Schema数据时出错: {ex.Message}");
            }
        }
    }
} 
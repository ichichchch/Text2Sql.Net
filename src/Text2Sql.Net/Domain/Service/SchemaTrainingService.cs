using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Memory;
using Newtonsoft.Json;
using SqlSugar;
using System.Data;
using System.Linq;
using System.Text;
using Text2Sql.Net.Domain.Interface;
using Text2Sql.Net.Repositories.Text2Sql.DatabaseConnection;
using Text2Sql.Net.Repositories.Text2Sql.DatabaseSchema;
using Text2Sql.Net.Repositories.Text2Sql.SchemaEmbedding;
using DbType = SqlSugar.DbType;

namespace Text2Sql.Net.Domain.Service
{
    /// <summary>
    /// Schema训练服务实现
    /// </summary>
    /// <remarks>
    /// 构造函数
    /// </remarks>
    public class SchemaTrainingService(
        IDatabaseConnectionConfigRepository connectionRepository,
        IDatabaseSchemaRepository schemaRepository,
        ISchemaEmbeddingRepository embeddingRepository,
        Kernel kernel,
        IMemoryStore memoryStore,
        ILogger<SchemaTrainingService> logger,
        ISemanticService semanticService) : ISchemaTrainingService
    {
        private readonly IDatabaseConnectionConfigRepository _connectionRepository = connectionRepository;
        private readonly IDatabaseSchemaRepository _schemaRepository = schemaRepository;
        private readonly ISchemaEmbeddingRepository _embeddingRepository = embeddingRepository;
        private readonly Kernel _kernel = kernel;
        private readonly IMemoryStore _memoryStore = memoryStore;
        private readonly ILogger<SchemaTrainingService> _logger = logger;
        private readonly ISemanticService _semanticService = semanticService;

        /// <inheritdoc/>
        public async Task<bool> TrainDatabaseSchemaAsync(string connectionId)
        {
            try
            {
                // 获取数据库连接配置
                var connectionConfig = await _connectionRepository.GetByIdAsync(connectionId);
                if (connectionConfig == null)
                {
                    _logger.LogError($"找不到数据库连接配置：{connectionId}");
                    return false;
                }

                // 获取数据库Schema信息
                string schemaJson = await GetDatabaseSchemaAsync(connectionId);
                if (string.IsNullOrEmpty(schemaJson))
                {
                    return false;
                }

                // 反序列化Schema信息
                List<TableInfo> tables = JsonConvert.DeserializeObject<List<TableInfo>>(schemaJson);
                // 替换所有 Any() 用于判断集合是否有元素的地方为 Count > 0

                // 1. TrainDatabaseSchemaAsync(string connectionId)
                if (tables?.Count > 0 != true)
                {
                    _logger.LogWarning($"没有找到表信息：{connectionId}");
                    return false;
                }

                // 清理旧的嵌入数据
                await _embeddingRepository.DeleteByConnectionIdAsync(connectionId);

                // 创建内存集合
                string collectionName = $"schema_{connectionId}";

                // 为每个表创建向量嵌入（包含所有列信息）
                foreach (var table in tables)
                {
                    try
                    {
                        // 构建包含所有列信息的表描述文本
                        StringBuilder tableDescription = new();
                        tableDescription.AppendLine($"表名: {table.TableName}");
                        tableDescription.AppendLine($"描述: {table.Description ?? "无描述"}");

                        // 添加外键关系信息
                        if (table.ForeignKeys != null && table.ForeignKeys.Count > 0)
                        {
                            tableDescription.AppendLine("外键关系:");
                            foreach (var fk in table.ForeignKeys)
                            {
                                tableDescription.AppendLine($"  - {fk.RelationshipDescription}");
                            }
                        }

                        tableDescription.AppendLine("列信息:");

                        foreach (var column in table.Columns)
                        {
                            tableDescription.AppendLine($"  - 列名: {column.ColumnName}, 类型: {column.DataType}, 主键: {(column.IsPrimaryKey ? "是" : "否")}, 可空: {(column.IsNullable ? "是" : "否")}, 描述: {column.Description ?? "无描述"}");
                        }

                        // 保存表的向量嵌入（包含所有列信息）
                        var tableEmbedding = new SchemaEmbedding
                        {
                            ConnectionId = connectionId,
                            TableName = table.TableName,
                            Description = tableDescription.ToString(),
                            EmbeddingType = EmbeddingType.Table,
                            Vector = string.Empty // 临时占位，向量数据将在后续生成
                        };

                        // 生成表的向量
                        string tableId = $"{connectionId}_{table.TableName}";
                        SemanticTextMemory textMemory = await _semanticService.GetTextMemory();

                        // 添加到向量存储
                        await textMemory.SaveInformationAsync(connectionId, id: tableId, text: JsonConvert.SerializeObject(tableEmbedding), cancellationToken: default);

                        // 同时保存到数据库表中
                        await _embeddingRepository.InsertAsync(tableEmbedding);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, message: $"训练表{table.TableName}时出错：{ex.Message}");
                    }
                }

                // 保存Schema信息到数据库
                var existingSchema = await _schemaRepository.GetByConnectionIdAsync(connectionId);
                if (existingSchema != null)
                {
                    existingSchema.SchemaContent = schemaJson;
                    existingSchema.UpdateTime = DateTime.Now;
                    await _schemaRepository.UpdateAsync(existingSchema);
                }
                else
                {
                    var newSchema = new DatabaseSchema
                    {
                        ConnectionId = connectionId,
                        SchemaContent = schemaJson
                    };
                    await _schemaRepository.InsertAsync(newSchema);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"训练数据库Schema时出错：{ex.Message}");
                return false;
            }
        }

        /// <inheritdoc/>
        public async Task<bool> TrainDatabaseSchemaAsync(string connectionId, List<string> tableNames)
        {
            try
            {
                if (tableNames == null || !tableNames.Any())
                {
                    _logger.LogWarning("未指定要训练的表");
                    return false;
                }

                // 参数验证和清理
                var cleanedTableNames = tableNames.Where(name => !string.IsNullOrWhiteSpace(name))
                                                 .Select(name => name.Trim())
                                                 .Distinct(StringComparer.OrdinalIgnoreCase)
                                                 .ToList();

                if (!cleanedTableNames.Any())
                {
                    _logger.LogWarning("没有有效的表名");
                    return false;
                }

                // 获取数据库连接配置
                var connectionConfig = await _connectionRepository.GetByIdAsync(connectionId);
                if (connectionConfig == null)
                {
                    _logger.LogError($"找不到数据库连接配置：{connectionId}");
                    return false;
                }

                // 获取所有表信息
                var allTables = await GetDatabaseTablesAsync(connectionId);
                if (allTables == null || !allTables.Any())
                {
                    _logger.LogWarning($"没有找到表信息：{connectionId}");
                    return false;
                }

                // 筛选指定的表
                var selectedTables = allTables.Where(t => cleanedTableNames.Contains(t.TableName, StringComparer.OrdinalIgnoreCase)).ToList();
                if (!selectedTables.Any())
                {
                    _logger.LogWarning($"未找到指定的表：{string.Join(", ", cleanedTableNames)}");
                    return false;
                }

                //已存在的训练的表
                var trainedTables = await GetTrainedTablesAsync(connectionId);

                //选择的和已存在的并集
                var existTabels = trainedTables.Select(a => a.TableName).Intersect(cleanedTableNames);

                // 去除已存在的表，只训练新增的表
                cleanedTableNames = [.. cleanedTableNames.Except(existTabels)];

                // 需要删除的表（已训练但不在本次选择中）
                var deleteTableNames = trainedTables.Select(a => a.TableName).Except(existTabels).ToList();


                // 清理选定表的旧嵌入数据
                foreach (var tableName in cleanedTableNames.Union(deleteTableNames))
                {
                    await _embeddingRepository.DeleteByTableNameAsync(connectionId, tableName);
                }

                if (cleanedTableNames.Count == 0)
                {
                    _logger.LogWarning($"没有新的需要训练的表：{connectionId}");
                    return false;
                }

                // 仅获取选定表的详细Schema信息（避免扫描全部表）
                var selectedTablesWithDetails = await GetTablesDetailsAsync(connectionId, cleanedTableNames);
                if (selectedTablesWithDetails == null || selectedTablesWithDetails.Count == 0)
                {
                    _logger.LogWarning($"未能获取到选定表的详细Schema信息：{string.Join(", ", cleanedTableNames)}");
                    return false;
                }



                // 为选定的表创建向量嵌入
                foreach (var table in selectedTablesWithDetails)
                {
                    try
                    {
                        // if (existTabels.Contains(table.TableName)) continue;

                        // 构建包含所有列信息的表描述文本
                        StringBuilder tableDescription = new StringBuilder();
                        tableDescription.AppendLine($"表名: {table.TableName}");
                        tableDescription.AppendLine($"描述: {table.Description ?? "无描述"}");

                        // 添加外键关系信息
                        if (table.ForeignKeys != null && table.ForeignKeys.Count > 0)
                        {
                            tableDescription.AppendLine("外键关系:");
                            foreach (var fk in table.ForeignKeys)
                            {
                                tableDescription.AppendLine($"  - {fk.RelationshipDescription}");
                            }
                        }

                        tableDescription.AppendLine("列信息:");

                        foreach (var column in table.Columns)
                        {
                            tableDescription.AppendLine($"  - 列名: {column.ColumnName}, 类型: {column.DataType}, 主键: {(column.IsPrimaryKey ? "是" : "否")}, 可空: {(column.IsNullable ? "是" : "否")}, 描述: {column.Description ?? "无描述"}");
                        }

                        // 保存表的向量嵌入
                        var tableEmbedding = new SchemaEmbedding
                        {
                            ConnectionId = connectionId,
                            TableName = table.TableName,
                            Description = tableDescription.ToString(),
                            EmbeddingType = EmbeddingType.Table,
                            Vector = string.Empty // 临时占位，向量数据将在后续生成
                        };

                        // 生成表的向量
                        string tableId = $"{connectionId}_{table.TableName}";
                        SemanticTextMemory textMemory = await _semanticService.GetTextMemory();

                        // 添加到向量存储
                        await textMemory.SaveInformationAsync(connectionId, id: tableId, text: JsonConvert.SerializeObject(tableEmbedding), cancellationToken: default);

                        // 同时保存到数据库表中
                        await _embeddingRepository.InsertAsync(tableEmbedding);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"训练表{table.TableName}时出错：{ex.Message}");
                    }
                }

                // 同步更新/写入 Schema 表，确保“已训练表”页面有数据来源
                try
                {
                    var existingSchema = await _schemaRepository.GetByConnectionIdAsync(connectionId);
                    if (existingSchema != null && !string.IsNullOrWhiteSpace(existingSchema.SchemaContent))
                    {
                        var currentTables = JsonConvert.DeserializeObject<List<TableInfo>>(existingSchema.SchemaContent) ?? [];

                        // 使用线程安全的方式更新Schema - 避免并发修改
                        lock (existingSchema)
                        {
                            // 移除同名表（忽略大小写），用本次训练的详细 Schema 覆盖
                            var comparer = StringComparer.OrdinalIgnoreCase;
                            var filteredTables = currentTables.Where(t => !cleanedTableNames.Contains(t.TableName, comparer)).ToList();
                            filteredTables.AddRange(selectedTablesWithDetails);

                            existingSchema.SchemaContent = JsonConvert.SerializeObject(filteredTables, Formatting.Indented);
                            existingSchema.UpdateTime = DateTime.Now;
                        }
                        await _schemaRepository.UpdateAsync(existingSchema);
                    }
                    else
                    {
                        var newSchema = new DatabaseSchema
                        {
                            ConnectionId = connectionId,
                            SchemaContent = JsonConvert.SerializeObject(selectedTablesWithDetails, Formatting.Indented)
                        };
                        await _schemaRepository.InsertAsync(newSchema);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "更新部分训练后的 SchemaContent 失败，不影响嵌入写入，但会影响已训练表展示。");
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"训练选定数据库表时出错：{ex.Message}");
                return false;
            }
        }

        /// <inheritdoc/>
        public async Task<bool> UpdateTableAsync(string connectionId, TableInfo tableInfo)
        {
            try
            {
                await _embeddingRepository.DeleteByTableNameAsync(connectionId, tableInfo.TableName);

                #region 更新表信息

                var existingSchema = await _schemaRepository.GetByConnectionIdAsync(connectionId);
                var currentTables = JsonConvert.DeserializeObject<List<TableInfo>>(existingSchema.SchemaContent) ?? [];

                var tb = currentTables.Find(a => a.TableName == tableInfo.TableName);
                int idx = currentTables.FindIndex(t => t.TableName == tableInfo.TableName);
                if (idx >= 0)
                {
                    currentTables[idx] = tableInfo;   // ‑->替换整行记录 
                    existingSchema.SchemaContent = JsonConvert.SerializeObject(currentTables, Formatting.Indented);
                    await _schemaRepository.UpdateAsync(existingSchema);
                }

                #endregion


                // 构建包含所有列信息的表描述文本
                var tableDescription = new StringBuilder();
                tableDescription.AppendLine($"表名: {tableInfo.TableName}");
                tableDescription.AppendLine($"描述: {tableInfo.Description ?? "无描述"}");

                // 添加外键关系信息
                if (tableInfo.ForeignKeys != null && tableInfo.ForeignKeys.Count > 0)
                {
                    tableDescription.AppendLine("外键关系:");
                    foreach (var fk in tableInfo.ForeignKeys)
                    {
                        tableDescription.AppendLine($"  - {fk.RelationshipDescription}");
                    }
                }

                tableDescription.AppendLine("列信息:");

                foreach (var column in tableInfo.Columns.Where(a => a.IsEnable == true).ToList())
                {
                    tableDescription.AppendLine($"  - 列名: {column.ColumnName}, 类型: {column.DataType}, 主键: {(column.IsPrimaryKey ? "是" : "否")}, 可空: {(column.IsNullable ? "是" : "否")}, 描述: {column.Description ?? "无描述"}");
                }

                // 保存表的向量嵌入
                var tableEmbedding = new SchemaEmbedding
                {
                    ConnectionId = connectionId,
                    TableName = tableInfo.TableName,
                    Description = tableDescription.ToString(),
                    EmbeddingType = EmbeddingType.Table,
                    Vector = string.Empty // 临时占位，向量数据将在后续生成
                };

                // 生成表的向量
                string tableId = $"{connectionId}_{tableInfo.TableName}";
                SemanticTextMemory textMemory = await _semanticService.GetTextMemory();

                // 添加到向量存储
                await textMemory.SaveInformationAsync(connectionId, id: tableId, text: JsonConvert.SerializeObject(tableEmbedding), cancellationToken: default);

                // 同时保存到数据库表中
                await _embeddingRepository.InsertAsync(tableEmbedding);

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"更新表信息时出错：{ex.Message}");
                return false;
            }

            return true;

        }

        /// <inheritdoc/>
        public async Task<List<TableInfo>> GetDatabaseTablesAsync(string connectionId)
        {
            try
            {
                // 仅获取表名（和可用的简单描述），避免加载完整 Schema
                var connectionConfig = await _connectionRepository.GetByIdAsync(connectionId);
                if (connectionConfig == null)
                {
                    _logger.LogError($"找不到数据库连接配置：{connectionId}");
                    return new List<TableInfo>();
                }

                // Excel 视为 Sqlite
                var typeForSchema = string.Equals(connectionConfig.DbType, "Excel", StringComparison.OrdinalIgnoreCase)
                    ? "sqlite"
                    : connectionConfig.DbType;
                var dbType = SchemaTrainingService.GetDbType(typeForSchema);

                var db = new SqlSugarClient(new ConnectionConfig
                {
                    ConfigId = connectionId,
                    ConnectionString = connectionConfig.ConnectionString,
                    DbType = dbType
                });

                var result = new List<TableInfo>();

                if (dbType == DbType.Sqlite)
                {
                    // SQLite: 直接从 sqlite_master 读取表名
                    var dt = db.Ado.GetDataTable("SELECT name AS TABLE_NAME FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'");
                    foreach (DataRow row in dt.Rows)
                    {
                        var tableName = Convert.ToString(row["TABLE_NAME"]) ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(tableName)) continue;
                        result.Add(new TableInfo
                        {
                            TableName = tableName,
                            Description = $"表: {tableName}"
                        });
                    }
                }
                else if (dbType == DbType.MySql)
                {
                    // MySQL: 可一次性拿到表注释
                    var dt = db.Ado.GetDataTable("SELECT TABLE_NAME, TABLE_COMMENT FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE'");
                    foreach (DataRow row in dt.Rows)
                    {
                        var tableName = Convert.ToString(row["TABLE_NAME"]) ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(tableName)) continue;
                        var comment = Convert.ToString(row["TABLE_COMMENT"]) ?? string.Empty;
                        result.Add(new TableInfo
                        {
                            TableName = tableName,
                            Description = string.IsNullOrEmpty(comment) ? $"表: {tableName}" : $"表: {tableName}, 备注: {comment}"
                        });
                    }
                }
                else if (dbType == DbType.PostgreSQL)
                {
                    // PostgreSQL: 仅获取表名；注释获取代价较高，列表阶段不取
                    var dt = db.Ado.GetDataTable("SELECT table_schema AS TABLE_SCHEMA, table_name AS TABLE_NAME FROM information_schema.tables WHERE table_type = 'BASE TABLE' AND table_schema NOT IN ('pg_catalog', 'information_schema')");
                    foreach (DataRow row in dt.Rows)
                    {
                        var schema = Convert.ToString(row["TABLE_SCHEMA"]) ?? string.Empty;
                        var tableName = Convert.ToString(row["TABLE_NAME"]) ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(tableName)) continue;
                        result.Add(new TableInfo
                        {
                            TableName = tableName,
                            Description = $"Schema: {schema}, 表: {tableName}"
                        });
                    }
                }
                else // 默认走 INFORMATION_SCHEMA（SQL Server 等）
                {
                    var dt = db.Ado.GetDataTable("SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'");
                    foreach (DataRow row in dt.Rows)
                    {
                        var schema = Convert.ToString(row["TABLE_SCHEMA"]) ?? string.Empty;
                        var tableName = Convert.ToString(row["TABLE_NAME"]) ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(tableName)) continue;
                        result.Add(new TableInfo
                        {
                            TableName = tableName,
                            Description = $"Schema: {schema}, 表: {tableName}"
                        });
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"获取数据库表列表时出错：{ex.Message}");
                return new List<TableInfo>();
            }
        }

        /// <inheritdoc/>
        public async Task<string> GetDatabaseSchemaAsync(string connectionId)
        {
            try
            {
                // 获取数据库连接配置
                var connectionConfig = await _connectionRepository.GetByIdAsync(connectionId);
                if (connectionConfig == null)
                {
                    _logger.LogError($"找不到数据库连接配置：{connectionId}");
                    return string.Empty;
                }

                // 检查是否已存在Schema信息
                var existingSchema = await _schemaRepository.GetByConnectionIdAsync(connectionId);
                if (existingSchema != null)
                {
                    return existingSchema.SchemaContent;
                }

                // Excel 视为 Sqlite
                var typeForSchema = string.Equals(connectionConfig.DbType, "Excel", StringComparison.OrdinalIgnoreCase)
                    ? "sqlite"
                    : connectionConfig.DbType;
                var dbType = SchemaTrainingService.GetDbType(typeForSchema);
                // 根据数据库类型创建连接
                var db = new SqlSugarClient(new ConnectionConfig
                {
                    ConfigId = connectionId,
                    ConnectionString = connectionConfig.ConnectionString,
                    DbType = dbType
                });

                // 获取所有表信息
                var tables = new List<TableInfo>();

                // 根据数据库类型使用不同的方式获取表结构
                if (dbType == DbType.Sqlite)
                {
                    // SQLite特定查询
                    return await GetSqliteSchemaAsync(db, connectionConfig);
                }
                else
                {
                    // 其他数据库使用INFORMATION_SCHEMA
                    DataTable schemasTable = db.Ado.GetDataTable("SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'");

                    foreach (DataRow row in schemasTable.Rows)
                    {
                        string tableName = row["TABLE_NAME"].ToString();
                        string tableSchema = row["TABLE_SCHEMA"].ToString();
                        try
                        {
                            // 尝试获取表备注(针对MSSQL)
                            string tableComment = string.Empty;
                            try
                            {
                                if (dbType == DbType.SqlServer)
                                {
                                    DataTable commentTable = db.Ado.GetDataTable(
                                        $@"SELECT CAST(value AS NVARCHAR(MAX)) AS [Description]
                                    FROM sys.extended_properties 
                                    WHERE major_id = OBJECT_ID('{tableName}') 
                                    AND minor_id = 0 
                                    AND name = 'MS_Description'");

                                    if (commentTable.Rows.Count > 0)
                                    {
                                        tableComment = commentTable.Rows[0]["Description"]?.ToString();
                                    }
                                }
                                else if (dbType == DbType.MySql)
                                {
                                    // 针对MySQL获取表注释
                                    DataTable commentTable = db.Ado.GetDataTable(
                                        $@"SELECT TABLE_COMMENT 
                                    FROM INFORMATION_SCHEMA.TABLES 
                                    WHERE TABLE_SCHEMA = DATABASE() 
                                    AND TABLE_NAME = '{tableName}'");

                                    if (commentTable.Rows.Count > 0)
                                    {
                                        tableComment = commentTable.Rows[0]["TABLE_COMMENT"]?.ToString();
                                    }
                                }
                                else if (dbType == DbType.PostgreSQL)
                                {
                                    // 针对PostgreSQL获取表注释
                                    DataTable commentTable = db.Ado.GetDataTable(
                                        $@"SELECT obj_description('{tableSchema}.{tableName}'::regclass, 'pg_class') as comment");

                                    if (commentTable.Rows.Count > 0)
                                    {
                                        tableComment = commentTable.Rows[0]["comment"]?.ToString();
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning($"获取表备注时出错：{ex.Message}");
                            }

                            // 创建表信息对象
                            var tableInfo = new TableInfo
                            {
                                TableName = tableName,
                                Description = !string.IsNullOrEmpty(tableComment)
                                    ? $"Schema: {tableSchema}, 表: {tableName}, 备注: {tableComment}"
                                    : $"Schema: {tableSchema}, 表: {tableName}"
                            };

                            // 获取表的列信息
                            DataTable columnsTable = db.Ado.GetDataTable($"SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{tableName}'");

                            // 获取主键信息
                            DataTable primaryKeyTable = db.Ado.GetDataTable(
                                $"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE " +
                                $"WHERE TABLE_NAME = '{tableName}' AND CONSTRAINT_NAME LIKE 'PK_%'");

                            var primaryKeys = new List<string>();
                            foreach (DataRow pkRow in primaryKeyTable.Rows)
                            {
                                primaryKeys.Add(pkRow["COLUMN_NAME"].ToString());
                            }

                            // 添加列信息
                            foreach (DataRow colRow in columnsTable.Rows)
                            {
                                string columnName = colRow["COLUMN_NAME"].ToString();
                                string dataType = colRow["DATA_TYPE"].ToString();
                                bool isNullable = colRow["IS_NULLABLE"].ToString().Equals("YES", StringComparison.OrdinalIgnoreCase);

                                // 尝试获取列备注
                                string columnComment = string.Empty;
                                try
                                {
                                    if (dbType == DbType.SqlServer)
                                    {
                                        // 针对SQL Server获取列注释
                                        DataTable commentTable = db.Ado.GetDataTable(
                                            $@"SELECT CAST(value AS NVARCHAR(MAX)) AS [Description]
                                        FROM sys.extended_properties
                                        WHERE major_id = OBJECT_ID('{tableName}')
                                        AND minor_id = (
                                            SELECT column_id 
                                            FROM sys.columns 
                                            WHERE object_id = OBJECT_ID('{tableName}') 
                                            AND name = '{columnName}'
                                        )
                                        AND name = 'MS_Description'");

                                        if (commentTable.Rows.Count > 0)
                                        {
                                            columnComment = commentTable.Rows[0]["Description"]?.ToString();
                                        }
                                    }
                                    else if (dbType == DbType.MySql)
                                    {
                                        // 针对MySQL获取列注释
                                        DataTable commentTable = db.Ado.GetDataTable(
                                            $@"SELECT COLUMN_COMMENT 
                                        FROM INFORMATION_SCHEMA.COLUMNS 
                                        WHERE TABLE_SCHEMA = DATABASE() 
                                        AND TABLE_NAME = '{tableName}' 
                                        AND COLUMN_NAME = '{columnName}'");

                                        if (commentTable.Rows.Count > 0)
                                        {
                                            columnComment = commentTable.Rows[0]["COLUMN_COMMENT"]?.ToString();
                                        }
                                    }
                                    else if (dbType == DbType.PostgreSQL)
                                    {
                                        // 针对PostgreSQL获取列注释
                                        DataTable commentTable = db.Ado.GetDataTable(
                                            $@"SELECT col_description('{tableSchema}.{tableName}'::regclass::oid, 
                                            (SELECT ordinal_position 
                                             FROM information_schema.columns 
                                             WHERE table_schema = '{tableSchema}' 
                                             AND table_name = '{tableName}' 
                                             AND column_name = '{columnName}')) as comment");

                                        if (commentTable.Rows.Count > 0)
                                        {
                                            columnComment = commentTable.Rows[0]["comment"]?.ToString();
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning($"获取列备注时出错：{ex.Message}");
                                }

                                var columnInfo = new ColumnInfo
                                {
                                    ColumnName = columnName,
                                    DataType = dataType,
                                    IsNullable = isNullable,
                                    IsPrimaryKey = primaryKeys.Contains(columnName),
                                    Description = !string.IsNullOrEmpty(columnComment)
                                        ? $"列: {columnName}, 类型: {dataType}, 备注: {columnComment}"
                                        : $"列: {columnName}, 类型: {dataType}"
                                };

                                tableInfo.Columns.Add(columnInfo);
                            }

                            // 获取外键信息
                            try
                            {
                                if (dbType == DbType.SqlServer)
                                {
                                    // SQL Server获取外键信息
                                    string fkQuery = @"
                                        SELECT 
                                            FK.name AS FK_NAME,
                                            COL.name AS COLUMN_NAME,
                                            REFCOL.name AS REFERENCED_COLUMN_NAME,
                                            REFTAB.name AS REFERENCED_TABLE_NAME
                                        FROM 
                                            sys.foreign_keys FK
                                            INNER JOIN sys.foreign_key_columns FKC ON FK.object_id = FKC.constraint_object_id
                                            INNER JOIN sys.columns COL ON FKC.parent_column_id = COL.column_id AND FKC.parent_object_id = COL.object_id
                                            INNER JOIN sys.columns REFCOL ON FKC.referenced_column_id = REFCOL.column_id AND FKC.referenced_object_id = REFCOL.object_id
                                            INNER JOIN sys.tables TAB ON FKC.parent_object_id = TAB.object_id
                                            INNER JOIN sys.tables REFTAB ON FKC.referenced_object_id = REFTAB.object_id
                                        WHERE 
                                            TAB.name = '" + tableName + "'";

                                    DataTable fkTable = db.Ado.GetDataTable(fkQuery);
                                    foreach (DataRow fkRow in fkTable.Rows)
                                    {
                                        var fkInfo = new ForeignKeyInfo
                                        {
                                            ForeignKeyName = fkRow["FK_NAME"].ToString(),
                                            ColumnName = fkRow["COLUMN_NAME"].ToString(),
                                            ReferencedColumnName = fkRow["REFERENCED_COLUMN_NAME"].ToString(),
                                            ReferencedTableName = fkRow["REFERENCED_TABLE_NAME"].ToString(),
                                            RelationshipDescription = $"从表 {tableName} 通过 {fkRow["COLUMN_NAME"]} 关联到主表 {fkRow["REFERENCED_TABLE_NAME"]} 的 {fkRow["REFERENCED_COLUMN_NAME"]}"
                                        };
                                        tableInfo.ForeignKeys.Add(fkInfo);
                                    }
                                }
                                else if (dbType == DbType.MySql)
                                {
                                    // MySQL获取外键信息
                                    string fkQuery = $@"
                                        SELECT
                                            CONSTRAINT_NAME AS FK_NAME,
                                            COLUMN_NAME,
                                            REFERENCED_TABLE_NAME,
                                            REFERENCED_COLUMN_NAME
                                        FROM
                                            INFORMATION_SCHEMA.KEY_COLUMN_USAGE
                                        WHERE
                                            TABLE_NAME = '{tableName}'
                                            AND REFERENCED_TABLE_NAME IS NOT NULL
                                            AND TABLE_SCHEMA = DATABASE()";

                                    DataTable fkTable = db.Ado.GetDataTable(fkQuery);
                                    foreach (DataRow fkRow in fkTable.Rows)
                                    {
                                        var fkInfo = new ForeignKeyInfo
                                        {
                                            ForeignKeyName = fkRow["FK_NAME"].ToString(),
                                            ColumnName = fkRow["COLUMN_NAME"].ToString(),
                                            ReferencedColumnName = fkRow["REFERENCED_COLUMN_NAME"].ToString(),
                                            ReferencedTableName = fkRow["REFERENCED_TABLE_NAME"].ToString(),
                                            RelationshipDescription = $"从表 {tableName} 通过 {fkRow["COLUMN_NAME"]} 关联到主表 {fkRow["REFERENCED_TABLE_NAME"]} 的 {fkRow["REFERENCED_COLUMN_NAME"]}"
                                        };
                                        tableInfo.ForeignKeys.Add(fkInfo);
                                    }
                                }
                                else if (dbType == DbType.PostgreSQL)
                                {
                                    // PostgreSQL获取外键信息
                                    string fkQuery = $@"
                                        SELECT
                                            con.conname AS fk_name,
                                            att.attname AS column_name,
                                            ref_att.attname AS referenced_column_name,
                                            ref_cl.relname AS referenced_table_name
                                        FROM
                                            pg_constraint con
                                            JOIN pg_attribute att ON att.attrelid = con.conrelid AND att.attnum = ANY(con.conkey)
                                            JOIN pg_attribute ref_att ON ref_att.attrelid = con.confrelid AND ref_att.attnum = ANY(con.confkey)
                                            JOIN pg_class cl ON cl.oid = con.conrelid
                                            JOIN pg_class ref_cl ON ref_cl.oid = con.confrelid
                                        WHERE
                                            con.contype = 'f'
                                            AND cl.relname = '{tableName}'";

                                    DataTable fkTable = db.Ado.GetDataTable(fkQuery);
                                    foreach (DataRow fkRow in fkTable.Rows)
                                    {
                                        var fkInfo = new ForeignKeyInfo
                                        {
                                            ForeignKeyName = fkRow["fk_name"].ToString(),
                                            ColumnName = fkRow["column_name"].ToString(),
                                            ReferencedColumnName = fkRow["referenced_column_name"].ToString(),
                                            ReferencedTableName = fkRow["referenced_table_name"].ToString(),
                                            RelationshipDescription = $"从表 {tableName} 通过 {fkRow["column_name"]} 关联到主表 {fkRow["referenced_table_name"]} 的 {fkRow["referenced_column_name"]}"
                                        };
                                        tableInfo.ForeignKeys.Add(fkInfo);
                                    }
                                }
                                // SQLite没有标准的外键信息查询，可以通过pragma获取
                                else if (dbType == DbType.Sqlite)
                                {
                                    // SQLite获取外键信息
                                    string fkQuery = $"PRAGMA foreign_key_list('{SchemaTrainingService.SqliteEscapeIdentifier(tableName)}')";
                                    DataTable fkTable = db.Ado.GetDataTable(fkQuery);

                                    foreach (DataRow fkRow in fkTable.Rows)
                                    {
                                        var fkInfo = new ForeignKeyInfo
                                        {
                                            ForeignKeyName = $"FK_{tableName}_{fkRow["from"]}_{fkRow["table"]}_{fkRow["to"]}",
                                            ColumnName = fkRow["from"].ToString(),
                                            ReferencedTableName = fkRow["table"].ToString(),
                                            ReferencedColumnName = fkRow["to"].ToString(),
                                            RelationshipDescription = $"从表 {tableName} 通过 {fkRow["from"]} 关联到主表 {fkRow["table"]} 的 {fkRow["to"]}"
                                        };
                                        tableInfo.ForeignKeys.Add(fkInfo);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning($"获取表{tableName}的外键信息时出错：{ex.Message}");
                            }

                            tables.Add(tableInfo);
                            _logger.LogInformation($"获取表({tableName})Schema");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"获取表{tableName}Schema时出错：{ex.Message}");
                        }
                    }
                }

                // 序列化表信息为JSON
                string schemaJson = JsonConvert.SerializeObject(tables, Formatting.Indented);
                return schemaJson;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"获取数据库Schema时出错：{ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// 获取SQLite数据库的架构信息
        /// </summary>
        /// <param name="db">数据库连接</param>
        /// <param name="connectionConfig">连接配置</param>
        /// <returns>schema JSON</returns>
        private async Task<string> GetSqliteSchemaAsync(SqlSugarClient db, DatabaseConnectionConfig connectionConfig)
        {
            var tables = new List<TableInfo>();

            // 尝试创建注释表(如果不存在)
            await CreateCommentTablesIfNotExistsAsync(db);

            // 获取所有表信息 - SQLite使用sqlite_master视图
            DataTable tablesMaster = db.Ado.GetDataTable("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT IN ('table_comments', 'column_comments')");

            // 检查是否存在注释表
            bool hasTableComments = false;
            bool hasColumnComments = false;
            try
            {
                // 检查是否存在表注释表
                DataTable checkTableCommentsTable = db.Ado.GetDataTable("SELECT name FROM sqlite_master WHERE type='table' AND name='table_comments'");
                hasTableComments = checkTableCommentsTable.Rows.Count > 0;

                // 检查是否存在列注释表
                DataTable checkColumnCommentsTable = db.Ado.GetDataTable("SELECT name FROM sqlite_master WHERE type='table' AND name='column_comments'");
                hasColumnComments = checkColumnCommentsTable.Rows.Count > 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"检查SQLite注释表时出错：{ex.Message}");
            }

            // 获取表注释字典
            Dictionary<string, string> tableComments = new Dictionary<string, string>();
            if (hasTableComments)
            {
                try
                {
                    DataTable commentsTable = db.Ado.GetDataTable("SELECT table_name, comment FROM table_comments");
                    foreach (DataRow row in commentsTable.Rows)
                    {
                        string tableName = row["table_name"]?.ToString() ?? string.Empty;
                        string comment = row["comment"]?.ToString() ?? string.Empty;
                        if (!string.IsNullOrEmpty(tableName))
                        {
                            tableComments[tableName] = comment;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"获取SQLite表注释时出错：{ex.Message}");
                }
            }

            // 获取列注释字典
            Dictionary<string, Dictionary<string, string>> columnComments = new Dictionary<string, Dictionary<string, string>>();
            if (hasColumnComments)
            {
                try
                {
                    DataTable commentsTable = db.Ado.GetDataTable("SELECT table_name, column_name, comment FROM column_comments");
                    foreach (DataRow row in commentsTable.Rows)
                    {
                        string tableName = row["table_name"]?.ToString() ?? string.Empty;
                        string columnName = row["column_name"]?.ToString() ?? string.Empty;
                        string comment = row["comment"]?.ToString() ?? string.Empty;

                        if (!string.IsNullOrEmpty(tableName) && !string.IsNullOrEmpty(columnName))
                        {
                            if (!columnComments.ContainsKey(tableName))
                            {
                                columnComments[tableName] = new Dictionary<string, string>();
                            }

                            columnComments[tableName][columnName] = comment;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"获取SQLite列注释时出错：{ex.Message}");
                }
            }

            foreach (DataRow row in tablesMaster.Rows)
            {
                string tableName = row["name"].ToString();

                // 跳过系统表和注释表
                if (tableName.StartsWith("sqlite_") || tableName == "table_comments" || tableName == "column_comments")
                {
                    continue;
                }

                // 获取表注释
                string tableComment = string.Empty;
                if (tableComments.ContainsKey(tableName))
                {
                    tableComment = tableComments[tableName];
                }

                // 创建表信息对象
                var tableInfo = new TableInfo
                {
                    TableName = tableName,
                    Description = !string.IsNullOrEmpty(tableComment)
                        ? $"表: {tableName}, 备注: {tableComment}"
                        : $"表: {tableName}"
                };

                // 获取表的列信息 - SQLite使用PRAGMA命令
                DataTable columnsTable = db.Ado.GetDataTable($"PRAGMA table_info('{SchemaTrainingService.SqliteEscapeIdentifier(tableName)}')");

                // 添加列信息
                foreach (DataRow colRow in columnsTable.Rows)
                {
                    string columnName = colRow["name"].ToString();
                    string dataType = colRow["type"].ToString();
                    bool isNullable = colRow["notnull"].ToString() == "0";
                    bool isPrimaryKey = colRow["pk"].ToString() == "1";

                    // 获取列注释
                    string columnComment = string.Empty;
                    if (columnComments.ContainsKey(tableName) && columnComments[tableName].ContainsKey(columnName))
                    {
                        columnComment = columnComments[tableName][columnName];
                    }

                    var columnInfo = new ColumnInfo
                    {
                        ColumnName = columnName,
                        DataType = dataType,
                        IsNullable = isNullable,
                        IsPrimaryKey = isPrimaryKey,
                        Description = !string.IsNullOrEmpty(columnComment)
                            ? $"列: {columnName}, 类型: {dataType}, 备注: {columnComment}"
                            : $"列: {columnName}, 类型: {dataType}"
                    };

                    tableInfo.Columns.Add(columnInfo);
                }

                // 获取外键信息
                try
                {
                    if (SchemaTrainingService.GetDbType(connectionConfig.DbType) == DbType.SqlServer)
                    {
                        // SQL Server获取外键信息
                        string fkQuery = @"
                            SELECT 
                                FK.name AS FK_NAME,
                                COL.name AS COLUMN_NAME,
                                REFCOL.name AS REFERENCED_COLUMN_NAME,
                                REFTAB.name AS REFERENCED_TABLE_NAME
                            FROM 
                                sys.foreign_keys FK
                                INNER JOIN sys.foreign_key_columns FKC ON FK.object_id = FKC.constraint_object_id
                                INNER JOIN sys.columns COL ON FKC.parent_column_id = COL.column_id AND FKC.parent_object_id = COL.object_id
                                INNER JOIN sys.columns REFCOL ON FKC.referenced_column_id = REFCOL.column_id AND FKC.referenced_object_id = REFCOL.object_id
                                INNER JOIN sys.tables TAB ON FKC.parent_object_id = TAB.object_id
                                INNER JOIN sys.tables REFTAB ON FKC.referenced_object_id = REFTAB.object_id
                            WHERE 
                                TAB.name = '" + tableName + "'";

                        DataTable fkTable = db.Ado.GetDataTable(fkQuery);
                        foreach (DataRow fkRow in fkTable.Rows)
                        {
                            var fkInfo = new ForeignKeyInfo
                            {
                                ForeignKeyName = fkRow["FK_NAME"].ToString(),
                                ColumnName = fkRow["COLUMN_NAME"].ToString(),
                                ReferencedColumnName = fkRow["REFERENCED_COLUMN_NAME"].ToString(),
                                ReferencedTableName = fkRow["REFERENCED_TABLE_NAME"].ToString(),
                                RelationshipDescription = $"从表 {tableName} 通过 {fkRow["COLUMN_NAME"]} 关联到主表 {fkRow["REFERENCED_TABLE_NAME"]} 的 {fkRow["REFERENCED_COLUMN_NAME"]}"
                            };
                            tableInfo.ForeignKeys.Add(fkInfo);
                        }
                    }
                    else if (SchemaTrainingService.GetDbType(connectionConfig.DbType) == DbType.MySql)
                    {
                        // MySQL获取外键信息
                        string fkQuery = $@"
                            SELECT
                                CONSTRAINT_NAME AS FK_NAME,
                                COLUMN_NAME,
                                REFERENCED_TABLE_NAME,
                                REFERENCED_COLUMN_NAME
                            FROM
                                INFORMATION_SCHEMA.KEY_COLUMN_USAGE
                            WHERE
                                TABLE_NAME = '{tableName}'
                                AND REFERENCED_TABLE_NAME IS NOT NULL
                                AND TABLE_SCHEMA = DATABASE()";

                        DataTable fkTable = db.Ado.GetDataTable(fkQuery);
                        foreach (DataRow fkRow in fkTable.Rows)
                        {
                            var fkInfo = new ForeignKeyInfo
                            {
                                ForeignKeyName = fkRow["FK_NAME"].ToString(),
                                ColumnName = fkRow["COLUMN_NAME"].ToString(),
                                ReferencedColumnName = fkRow["REFERENCED_COLUMN_NAME"].ToString(),
                                ReferencedTableName = fkRow["REFERENCED_TABLE_NAME"].ToString(),
                                RelationshipDescription = $"从表 {tableName} 通过 {fkRow["COLUMN_NAME"]} 关联到主表 {fkRow["REFERENCED_TABLE_NAME"]} 的 {fkRow["REFERENCED_COLUMN_NAME"]}"
                            };
                            tableInfo.ForeignKeys.Add(fkInfo);
                        }
                    }
                    else if (SchemaTrainingService.GetDbType(connectionConfig.DbType) == DbType.PostgreSQL)
                    {
                        // PostgreSQL获取外键信息
                        string fkQuery = $@"
                            SELECT
                                con.conname AS fk_name,
                                att.attname AS column_name,
                                ref_att.attname AS referenced_column_name,
                                ref_cl.relname AS referenced_table_name
                            FROM
                                pg_constraint con
                                JOIN pg_attribute att ON att.attrelid = con.conrelid AND att.attnum = ANY(con.conkey)
                                JOIN pg_attribute ref_att ON ref_att.attrelid = con.confrelid AND ref_att.attnum = ANY(con.confkey)
                                JOIN pg_class cl ON cl.oid = con.conrelid
                                JOIN pg_class ref_cl ON ref_cl.oid = con.confrelid
                            WHERE
                                con.contype = 'f'
                                AND cl.relname = '{tableName}'";

                        DataTable fkTable = db.Ado.GetDataTable(fkQuery);
                        foreach (DataRow fkRow in fkTable.Rows)
                        {
                            var fkInfo = new ForeignKeyInfo
                            {
                                ForeignKeyName = fkRow["fk_name"].ToString(),
                                ColumnName = fkRow["column_name"].ToString(),
                                ReferencedColumnName = fkRow["referenced_column_name"].ToString(),
                                ReferencedTableName = fkRow["referenced_table_name"].ToString(),
                                RelationshipDescription = $"从表 {tableName} 通过 {fkRow["column_name"]} 关联到主表 {fkRow["referenced_table_name"]} 的 {fkRow["referenced_column_name"]}"
                            };
                            tableInfo.ForeignKeys.Add(fkInfo);
                        }
                    }
                    // SQLite没有标准的外键信息查询，可以通过pragma获取
                    else if (SchemaTrainingService.GetDbType(connectionConfig.DbType) == DbType.Sqlite)
                    {
                        // SQLite获取外键信息
                        string fkQuery = $"PRAGMA foreign_key_list('{SchemaTrainingService.SqliteEscapeIdentifier(tableName)}')";
                        DataTable fkTable = db.Ado.GetDataTable(fkQuery);

                        foreach (DataRow fkRow in fkTable.Rows)
                        {
                            var fkInfo = new ForeignKeyInfo
                            {
                                ForeignKeyName = $"FK_{tableName}_{fkRow["from"]}_{fkRow["table"]}_{fkRow["to"]}",
                                ColumnName = fkRow["from"].ToString(),
                                ReferencedTableName = fkRow["table"].ToString(),
                                ReferencedColumnName = fkRow["to"].ToString(),
                                RelationshipDescription = $"从表 {tableName} 通过 {fkRow["from"]} 关联到主表 {fkRow["table"]} 的 {fkRow["to"]}"
                            };
                            tableInfo.ForeignKeys.Add(fkInfo);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"获取表{tableName}的外键信息时出错：{ex.Message}");
                }

                tables.Add(tableInfo);
            }

            // 序列化表信息为JSON
            string schemaJson = JsonConvert.SerializeObject(tables, Formatting.Indented);
            return schemaJson;
        }

        /// <summary>
        /// SQLite标识符转义处理
        /// </summary>
        /// <param name="identifier">需要转义的标识符</param>
        /// <returns>转义后的标识符</returns>
        private static string SqliteEscapeIdentifier(string identifier)
        {
            if (string.IsNullOrEmpty(identifier))
            {
                return string.Empty;
            }

            // 替换单引号为两个单引号
            return identifier.Replace("'", "''");
        }

        /// <summary>
        /// 为SQLite创建注释表(如果不存在)
        /// </summary>
        /// <param name="db">数据库连接</param>
        /// <returns>成功与否</returns>
        private async Task<bool> CreateCommentTablesIfNotExistsAsync(SqlSugarClient db)
        {
            try
            {
                // 创建表注释表
                await db.Ado.ExecuteCommandAsync(@"
                    CREATE TABLE IF NOT EXISTS table_comments (
                        table_name TEXT PRIMARY KEY,
                        comment TEXT
                    )");

                // 创建列注释表
                await db.Ado.ExecuteCommandAsync(@"
                    CREATE TABLE IF NOT EXISTS column_comments (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        table_name TEXT NOT NULL,
                        column_name TEXT NOT NULL,
                        comment TEXT,
                        UNIQUE(table_name, column_name)
                    )");

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建SQLite注释表时出错");
                return false;
            }
        }

        /// <inheritdoc/>
        public async Task<List<TableInfo>> GetTrainedTablesAsync(string connectionId)
        {
            try
            {
                // 获取已训练的Schema信息
                var existingSchema = await _schemaRepository.GetByConnectionIdAsync(connectionId);
                if (existingSchema == null || string.IsNullOrEmpty(existingSchema.SchemaContent))
                {
                    return [];
                }

                // 反序列化Schema信息
                var tables = JsonConvert.DeserializeObject<List<TableInfo>>(existingSchema.SchemaContent);
                if (tables == null)
                {
                    return [];
                }

                // 获取已训练的嵌入数据
                var embeddings = await _embeddingRepository.GetByConnectionIdAsync(connectionId);
                var trainedTableNames = embeddings.Where(e => e.EmbeddingType == EmbeddingType.Table)
                                                 .Select(e => e.TableName)
                                                 .Distinct()
                                                 .ToList();

                // 只返回已训练的表信息
                return [.. tables.Where(t => trainedTableNames.Contains(t.TableName, StringComparer.OrdinalIgnoreCase))];
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"获取已训练表信息时出错：{ex.Message}");
                return [];
            }
        }

        /// <inheritdoc/>
        public async Task<TableInfo> GetTableDetailAsync(string connectionId, string tableName)
        {
            try
            {
                // 获取完整的Schema信息
                var existingSchema = await _schemaRepository.GetByConnectionIdAsync(connectionId);
                if (existingSchema == null || string.IsNullOrEmpty(existingSchema.SchemaContent))
                {
                    return null;
                }

                // 反序列化Schema信息
                var tables = JsonConvert.DeserializeObject<List<TableInfo>>(existingSchema.SchemaContent);
                if (tables == null)
                {
                    return null;
                }

                // 查找指定的表
                var table = tables.FirstOrDefault(t => string.Equals(t.TableName, tableName, StringComparison.OrdinalIgnoreCase));
                if (table == null)
                {
                    return null;
                }

                // 检查是否已训练
                var embedding = await _embeddingRepository.GetByTableAsync(connectionId, tableName);
                if (embedding == null || !embedding.Any())
                {
                    return null;
                }

                /* var regex = new Regex(@"^\s*-\s*列名:\s*(?<col>[\w\-]+).*?,\s*描述:\s*(?<desc>.*)$", RegexOptions.Multiline | RegexOptions.IgnoreCase);

                 var columnDict = regex.Matches(embedding[0].Description)
                          .Select(m => new
                          {
                              Column = m.Groups["col"].Value.Trim(),
                              Description = m.Groups["desc"].Value.Trim()
                          }).ToList().ToDictionary(x => x.Column);


                 foreach (var item in table.Columns)
                 {
                     item.IsEnable = false;
                     if (columnDict.TryGetValue(item.ColumnName!, out var info))
                     {
                         item.IsEnable = true;
                         item.Description = info.Description;
                     }
                 }*/


                return table;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"获取表详细信息时出错：{ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 获取SqlSugar数据库类型
        /// </summary>
        private static DbType GetDbType(string dbTypeStr)
        {
            return dbTypeStr.ToLower() switch
            {
                "sqlserver" => DbType.SqlServer,
                "mysql" => DbType.MySql,
                "postgresql" => DbType.PostgreSQL,
                "sqlite" => DbType.Sqlite,
                "oracle" => DbType.Oracle,
                _ => DbType.SqlServer
            };
        }

        /// <summary>
        /// 仅为指定表获取详细 Schema（列与外键等），避免全库扫描
        /// </summary>
        private async Task<List<TableInfo>> GetTablesDetailsAsync(string connectionId, List<string> tableNames)
        {
            var results = new List<TableInfo>();
            if (tableNames == null || tableNames.Count == 0) return results;

            var connectionConfig = await _connectionRepository.GetByIdAsync(connectionId);
            if (connectionConfig == null)
            {
                _logger.LogError($"找不到数据库连接配置：{connectionId}");
                return results;
            }

            var typeForSchema = string.Equals(connectionConfig.DbType, "Excel", StringComparison.OrdinalIgnoreCase)
                ? "sqlite"
                : connectionConfig.DbType;
            var dbType = SchemaTrainingService.GetDbType(typeForSchema);

            var db = new SqlSugarClient(new ConnectionConfig
            {
                ConfigId = connectionId,
                ConnectionString = connectionConfig.ConnectionString,
                DbType = dbType
            });

            foreach (var rawName in tableNames.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(rawName)) continue;
                try
                {
                    string tableName = rawName;
                    string schemaName = string.Empty;
                    string tableComment = string.Empty;

                    if (dbType == DbType.Sqlite)
                    {
                        // SQLite: PRAGMA 方式
                        // 可选：从自定义注释表取注释
                        try
                        {
                            var hasTableComments = db.Ado.GetInt("SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name='table_comments'") > 0;
                            if (hasTableComments)
                            {
                                var dt = db.Ado.GetDataTable($"SELECT comment FROM table_comments WHERE table_name='{SchemaTrainingService.SqliteEscapeIdentifier(tableName)}' LIMIT 1");
                                if (dt.Rows.Count > 0)
                                {
                                    tableComment = Convert.ToString(dt.Rows[0]["comment"]) ?? string.Empty;
                                }
                            }
                        }
                        catch { }

                        var tableInfo = new TableInfo
                        {
                            TableName = tableName,
                            Description = string.IsNullOrEmpty(tableComment) ? $"表: {tableName}" : $"表: {tableName}, 备注: {tableComment}"
                        };

                        var columnsTable = db.Ado.GetDataTable($"PRAGMA table_info('{SchemaTrainingService.SqliteEscapeIdentifier(tableName)}')");
                        foreach (DataRow colRow in columnsTable.Rows)
                        {
                            var columnName = Convert.ToString(colRow["name"]) ?? string.Empty;
                            var dataType = Convert.ToString(colRow["type"]) ?? string.Empty;
                            bool isNullable = (Convert.ToString(colRow["notnull"]) ?? "0") == "0";
                            bool isPrimaryKey = (Convert.ToString(colRow["pk"]) ?? "0") == "1";

                            tableInfo.Columns.Add(new ColumnInfo
                            {
                                ColumnName = columnName,
                                DataType = dataType,
                                IsNullable = isNullable,
                                IsPrimaryKey = isPrimaryKey,
                                Description = $"列: {columnName}, 类型: {dataType}"
                            });
                        }

                        // 外键
                        try
                        {
                            var fkTable = db.Ado.GetDataTable($"PRAGMA foreign_key_list('{SchemaTrainingService.SqliteEscapeIdentifier(tableName)}')");
                            foreach (DataRow fkRow in fkTable.Rows)
                            {
                                var col = Convert.ToString(fkRow["from"]) ?? string.Empty;
                                var refTab = Convert.ToString(fkRow["table"]) ?? string.Empty;
                                var refCol = Convert.ToString(fkRow["to"]) ?? string.Empty;
                                tableInfo.ForeignKeys.Add(new ForeignKeyInfo
                                {
                                    ForeignKeyName = $"FK_{tableName}_{col}_{refTab}_{refCol}",
                                    ColumnName = col,
                                    ReferencedTableName = refTab,
                                    ReferencedColumnName = refCol,
                                    RelationshipDescription = $"从表 {tableName} 通过 {col} 关联到主表 {refTab} 的 {refCol}"
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"SQLite 获取表{tableName}外键失败：{ex.Message}");
                        }

                        results.Add(tableInfo);
                        continue;
                    }

                    // 获取 schema 名（适用于 SQL Server / MySQL / PostgreSQL）
                    try
                    {
                        if (dbType == DbType.MySql)
                        {
                            var dt = db.Ado.GetDataTable($"SELECT TABLE_SCHEMA, TABLE_COMMENT FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{tableName}'");
                            if (dt.Rows.Count > 0)
                            {
                                schemaName = Convert.ToString(dt.Rows[0]["TABLE_SCHEMA"]) ?? string.Empty;
                                tableComment = Convert.ToString(dt.Rows[0]["TABLE_COMMENT"]) ?? string.Empty;
                            }
                        }
                        else if (dbType == DbType.PostgreSQL)
                        {
                            var dt = db.Ado.GetDataTable($"SELECT table_schema FROM information_schema.tables WHERE table_type='BASE TABLE' AND table_name='{tableName}' LIMIT 1");
                            if (dt.Rows.Count > 0)
                            {
                                schemaName = Convert.ToString(dt.Rows[0]["table_schema"]) ?? string.Empty;
                                var cdt = db.Ado.GetDataTable($"SELECT obj_description('{schemaName}.{tableName}'::regclass, 'pg_class') as comment");
                                if (cdt.Rows.Count > 0)
                                {
                                    tableComment = Convert.ToString(cdt.Rows[0]["comment"]) ?? string.Empty;
                                }
                            }
                        }
                        else // SQL Server 默认
                        {
                            var dt = db.Ado.GetDataTable($"SELECT TABLE_SCHEMA FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '{tableName}'");
                            if (dt.Rows.Count > 0)
                            {
                                schemaName = Convert.ToString(dt.Rows[0]["TABLE_SCHEMA"]) ?? string.Empty;
                                var cdt = db.Ado.GetDataTable($@"SELECT CAST(value AS NVARCHAR(MAX)) AS [Description] FROM sys.extended_properties WHERE major_id = OBJECT_ID('{tableName}') AND minor_id = 0 AND name = 'MS_Description'");
                                if (cdt.Rows.Count > 0)
                                {
                                    tableComment = Convert.ToString(cdt.Rows[0]["Description"]) ?? string.Empty;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"获取表{tableName}备注/Schema失败：{ex.Message}");
                    }

                    var tableInfoGen = new TableInfo
                    {
                        TableName = tableName,
                        Description = string.IsNullOrEmpty(tableComment)
                            ? (string.IsNullOrEmpty(schemaName) ? $"表: {tableName}" : $"Schema: {schemaName}, 表: {tableName}")
                            : (string.IsNullOrEmpty(schemaName) ? $"表: {tableName}, 备注: {tableComment}" : $"Schema: {schemaName}, 表: {tableName}, 备注: {tableComment}")
                    };

                    // 列
                    DataTable cols;
                    if (dbType == DbType.MySql)
                    {
                        cols = db.Ado.GetDataTable($"SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{tableName}'");
                    }
                    else if (dbType == DbType.PostgreSQL)
                    {
                        if (string.IsNullOrEmpty(schemaName))
                        {
                            var sdt = db.Ado.GetDataTable($"SELECT table_schema FROM information_schema.tables WHERE table_type='BASE TABLE' AND table_name='{tableName}' LIMIT 1");
                            schemaName = sdt.Rows.Count > 0 ? Convert.ToString(sdt.Rows[0]["table_schema"]) ?? string.Empty : string.Empty;
                        }
                        cols = db.Ado.GetDataTable($"SELECT column_name AS COLUMN_NAME, data_type AS DATA_TYPE, is_nullable AS IS_NULLABLE FROM information_schema.columns WHERE table_schema = '{schemaName}' AND table_name = '{tableName}'");
                    }
                    else // SQL Server
                    {
                        cols = db.Ado.GetDataTable($"SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{tableName}'");
                    }

                    // 主键列
                    var primaryKeys = new List<string>();
                    try
                    {
                        if (dbType == DbType.MySql)
                        {
                            var pk = db.Ado.GetDataTable($"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{tableName}' AND CONSTRAINT_NAME='PRIMARY'");
                            foreach (DataRow r in pk.Rows)
                            {
                                var cn = Convert.ToString(r["COLUMN_NAME"]) ?? string.Empty;
                                if (!string.IsNullOrEmpty(cn)) primaryKeys.Add(cn);
                            }
                        }
                        else if (dbType == DbType.PostgreSQL)
                        {
                            if (string.IsNullOrEmpty(schemaName))
                            {
                                var sdt = db.Ado.GetDataTable($"SELECT table_schema FROM information_schema.tables WHERE table_type='BASE TABLE' AND table_name='{tableName}' LIMIT 1");
                                schemaName = sdt.Rows.Count > 0 ? Convert.ToString(sdt.Rows[0]["table_schema"]) ?? string.Empty : string.Empty;
                            }
                            var pk = db.Ado.GetDataTable($@"SELECT a.attname AS COLUMN_NAME FROM pg_index i JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY(i.indkey) JOIN pg_class c ON c.oid = i.indrelid JOIN pg_namespace n ON n.oid = c.relnamespace WHERE i.indisprimary AND c.relname = '{tableName}' AND n.nspname = '{schemaName}'");
                            foreach (DataRow r in pk.Rows)
                            {
                                var cn = Convert.ToString(r["COLUMN_NAME"]) ?? string.Empty;
                                if (!string.IsNullOrEmpty(cn)) primaryKeys.Add(cn);
                            }
                        }
                        else // SQL Server 默认
                        {
                            var pk = db.Ado.GetDataTable($"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE WHERE TABLE_NAME = '{tableName}' AND CONSTRAINT_NAME LIKE 'PK_%'");
                            foreach (DataRow r in pk.Rows)
                            {
                                var cn = Convert.ToString(r["COLUMN_NAME"]) ?? string.Empty;
                                if (!string.IsNullOrEmpty(cn)) primaryKeys.Add(cn);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"获取表{tableName}主键失败：{ex.Message}");
                    }

                    foreach (DataRow colRow in cols.Rows)
                    {
                        var columnName = Convert.ToString(colRow["COLUMN_NAME"]) ?? string.Empty;
                        var dataType = Convert.ToString(colRow["DATA_TYPE"]) ?? string.Empty;
                        var isNullableStr = Convert.ToString(colRow["IS_NULLABLE"]) ?? string.Empty;
                        bool isNullable = isNullableStr.Equals("YES", StringComparison.OrdinalIgnoreCase) || isNullableStr.Equals("1");

                        tableInfoGen.Columns.Add(new ColumnInfo
                        {
                            ColumnName = columnName,
                            DataType = dataType,
                            IsNullable = isNullable,
                            IsPrimaryKey = primaryKeys.Contains(columnName)
                        });
                    }

                    // 外键
                    try
                    {
                        if (dbType == DbType.MySql)
                        {
                            var fkTable = db.Ado.GetDataTable($@"SELECT CONSTRAINT_NAME AS FK_NAME, COLUMN_NAME, REFERENCED_TABLE_NAME, REFERENCED_COLUMN_NAME FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{tableName}' AND REFERENCED_TABLE_NAME IS NOT NULL");
                            foreach (DataRow fkRow in fkTable.Rows)
                            {
                                var fkName = Convert.ToString(fkRow["FK_NAME"]) ?? string.Empty;
                                var col = Convert.ToString(fkRow["COLUMN_NAME"]) ?? string.Empty;
                                var refCol = Convert.ToString(fkRow["REFERENCED_COLUMN_NAME"]) ?? string.Empty;
                                var refTab = Convert.ToString(fkRow["REFERENCED_TABLE_NAME"]) ?? string.Empty;
                                tableInfoGen.ForeignKeys.Add(new ForeignKeyInfo
                                {
                                    ForeignKeyName = fkName,
                                    ColumnName = col,
                                    ReferencedColumnName = refCol,
                                    ReferencedTableName = refTab,
                                    RelationshipDescription = $"从表 {tableName} 通过 {col} 关联到主表 {refTab} 的 {refCol}"
                                });
                            }
                        }
                        else if (dbType == DbType.PostgreSQL)
                        {
                            if (string.IsNullOrEmpty(schemaName))
                            {
                                var sdt = db.Ado.GetDataTable($"SELECT table_schema FROM information_schema.tables WHERE table_type='BASE TABLE' AND table_name='{tableName}' LIMIT 1");
                                schemaName = sdt.Rows.Count > 0 ? Convert.ToString(sdt.Rows[0]["table_schema"]) ?? string.Empty : string.Empty;
                            }
                            var fkTable = db.Ado.GetDataTable($@"SELECT con.conname AS fk_name, att.attname AS column_name, ref_att.attname AS referenced_column_name, ref_cl.relname AS referenced_table_name FROM pg_constraint con JOIN pg_attribute att ON att.attrelid = con.conrelid AND att.attnum = ANY(con.conkey) JOIN pg_attribute ref_att ON ref_att.attrelid = con.confrelid AND ref_att.attnum = ANY(con.confkey) JOIN pg_class cl ON cl.oid = con.conrelid JOIN pg_class ref_cl ON ref_cl.oid = con.confrelid JOIN pg_namespace n ON n.oid = cl.relnamespace WHERE con.contype = 'f' AND cl.relname = '{tableName}' AND n.nspname = '{schemaName}'");
                            foreach (DataRow fkRow in fkTable.Rows)
                            {
                                var fkName = Convert.ToString(fkRow["fk_name"]) ?? string.Empty;
                                var col = Convert.ToString(fkRow["column_name"]) ?? string.Empty;
                                var refCol = Convert.ToString(fkRow["referenced_column_name"]) ?? string.Empty;
                                var refTab = Convert.ToString(fkRow["referenced_table_name"]) ?? string.Empty;
                                tableInfoGen.ForeignKeys.Add(new ForeignKeyInfo
                                {
                                    ForeignKeyName = fkName,
                                    ColumnName = col,
                                    ReferencedColumnName = refCol,
                                    ReferencedTableName = refTab,
                                    RelationshipDescription = $"从表 {tableName} 通过 {col} 关联到主表 {refTab} 的 {refCol}"
                                });
                            }
                        }
                        else // SQL Server
                        {
                            string fkQuery = @"
                                        SELECT 
                                            FK.name AS FK_NAME,
                                            COL.name AS COLUMN_NAME,
                                            REFCOL.name AS REFERENCED_COLUMN_NAME,
                                            REFTAB.name AS REFERENCED_TABLE_NAME
                                        FROM 
                                            sys.foreign_keys FK
                                            INNER JOIN sys.foreign_key_columns FKC ON FK.object_id = FKC.constraint_object_id
                                            INNER JOIN sys.columns COL ON FKC.parent_column_id = COL.column_id AND FKC.parent_object_id = COL.object_id
                                            INNER JOIN sys.columns REFCOL ON FKC.referenced_column_id = REFCOL.column_id AND FKC.referenced_object_id = REFCOL.object_id
                                            INNER JOIN sys.tables TAB ON FKC.parent_object_id = TAB.object_id
                                            INNER JOIN sys.tables REFTAB ON FKC.referenced_object_id = REFTAB.object_id
                                        WHERE 
                                            TAB.name = '" + tableName + "'";
                            DataTable fkTable = db.Ado.GetDataTable(fkQuery);
                            foreach (DataRow fkRow in fkTable.Rows)
                            {
                                var fkName = Convert.ToString(fkRow["FK_NAME"]) ?? string.Empty;
                                var col = Convert.ToString(fkRow["COLUMN_NAME"]) ?? string.Empty;
                                var refCol = Convert.ToString(fkRow["REFERENCED_COLUMN_NAME"]) ?? string.Empty;
                                var refTab = Convert.ToString(fkRow["REFERENCED_TABLE_NAME"]) ?? string.Empty;
                                tableInfoGen.ForeignKeys.Add(new ForeignKeyInfo
                                {
                                    ForeignKeyName = fkName,
                                    ColumnName = col,
                                    ReferencedColumnName = refCol,
                                    ReferencedTableName = refTab,
                                    RelationshipDescription = $"从表 {tableName} 通过 {col} 关联到主表 {refTab} 的 {refCol}"
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"获取表{tableName}外键失败：{ex.Message}");
                    }

                    results.Add(tableInfoGen);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"获取选定表{rawName}详细Schema时出错：{ex.Message}");
                }
            }

            return results;
        }
    }
}
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Memory;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SqlSugar;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Text2Sql.Net.Domain.Interface;
using Text2Sql.Net.Repositories.Text2Sql.DatabaseConnection;
using Text2Sql.Net.Repositories.Text2Sql.DatabaseSchema;
using Text2Sql.Net.Repositories.Text2Sql.SchemaEmbedding;
using DbType = SqlSugar.DbType;
using static Dm.net.buffer.ByteArrayBuffer;

namespace Text2Sql.Net.Domain.Service
{
    /// <summary>
    /// Schema训练服务实现
    /// </summary>
    public class SchemaTrainingService : ISchemaTrainingService
    {
        private readonly IDatabaseConnectionConfigRepository _connectionRepository;
        private readonly IDatabaseSchemaRepository _schemaRepository;
        private readonly ISchemaEmbeddingRepository _embeddingRepository;
        private readonly Kernel _kernel;
        private readonly IMemoryStore _memoryStore;
        private readonly ILogger<SchemaTrainingService> _logger;
        private readonly ISemanticService _semanticService;

        /// <summary>
        /// 构造函数
        /// </summary>
        public SchemaTrainingService(
            IDatabaseConnectionConfigRepository connectionRepository,
            IDatabaseSchemaRepository schemaRepository,
            ISchemaEmbeddingRepository embeddingRepository,
            Kernel kernel,
            IMemoryStore memoryStore,
            ILogger<SchemaTrainingService> logger,
            ISemanticService semanticService)
        {
            _connectionRepository = connectionRepository;
            _schemaRepository = schemaRepository;
            _embeddingRepository = embeddingRepository;
            _kernel = kernel;
            _memoryStore = memoryStore;
            _logger = logger;
            _semanticService = semanticService;
        }

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
                if (tables == null || !tables.Any())
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
                    // 构建包含所有列信息的表描述文本
                    StringBuilder tableDescription = new StringBuilder();
                    tableDescription.AppendLine($"表名: {table.TableName}");
                    tableDescription.AppendLine($"描述: {table.Description ?? "无描述"}");
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
                        EmbeddingType = EmbeddingType.Table
                    };

                    // 生成表的向量
                    string tableId = $"{connectionId}_{table.TableName}";
                    SemanticTextMemory textMemory = await _semanticService.GetTextMemory();
       
                    // 添加到向量存储
                    await textMemory.SaveInformationAsync(connectionId, id: tableId, text: JsonConvert.SerializeObject(tableEmbedding), cancellationToken: default);
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

                var dbType = GetDbType(connectionConfig.DbType);
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
                DataTable columnsTable = db.Ado.GetDataTable($"PRAGMA table_info('{SqliteEscapeIdentifier(tableName)}')");
                
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
                
                tables.Add(tableInfo);
            }

            // 序列化表信息为JSON
            string schemaJson = JsonConvert.SerializeObject(tables, Formatting.Indented);
            return schemaJson;
        }

        /// <summary>
        /// 转义SQLite标识符
        /// </summary>
        /// <param name="identifier">需要转义的标识符</param>
        /// <returns>转义后的标识符</returns>
        private string SqliteEscapeIdentifier(string identifier)
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

        /// <summary>
        /// 获取SqlSugar数据库类型
        /// </summary>
        private DbType GetDbType(string dbTypeStr)
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
    }
} 
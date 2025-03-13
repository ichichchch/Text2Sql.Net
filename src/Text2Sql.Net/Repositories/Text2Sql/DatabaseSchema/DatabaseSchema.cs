using SqlSugar;
using System;
using System.Collections.Generic;

namespace Text2Sql.Net.Repositories.Text2Sql.DatabaseSchema
{
    /// <summary>
    /// 数据库Schema信息
    /// </summary>
    [SugarTable("DatabaseSchemas")]
    public class DatabaseSchema
    {
        /// <summary>
        /// Schema ID
        /// </summary>
        [SugarColumn(IsPrimaryKey = true)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// 数据库连接ID
        /// </summary>
        [SugarColumn(IsNullable = false)]
        public string ConnectionId { get; set; }

        /// <summary>
        /// 表和列信息的JSON字符串
        /// </summary>
        [SugarColumn(ColumnDataType = "text", IsNullable = false)]
        public string SchemaContent { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreateTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime? UpdateTime { get; set; }
    }

    /// <summary>
    /// 数据库表信息
    /// </summary>
    public class TableInfo
    {
        /// <summary>
        /// 表名
        /// </summary>
        public string TableName { get; set; }

        /// <summary>
        /// 表描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 列信息
        /// </summary>
        public List<ColumnInfo> Columns { get; set; } = new List<ColumnInfo>();
    }

    /// <summary>
    /// 数据库列信息
    /// </summary>
    public class ColumnInfo
    {
        /// <summary>
        /// 列名
        /// </summary>
        public string ColumnName { get; set; }

        /// <summary>
        /// 数据类型
        /// </summary>
        public string DataType { get; set; }

        /// <summary>
        /// 是否主键
        /// </summary>
        public bool IsPrimaryKey { get; set; }

        /// <summary>
        /// 是否允许为空
        /// </summary>
        public bool IsNullable { get; set; }

        /// <summary>
        /// 列描述
        /// </summary>
        public string Description { get; set; }
    }
} 
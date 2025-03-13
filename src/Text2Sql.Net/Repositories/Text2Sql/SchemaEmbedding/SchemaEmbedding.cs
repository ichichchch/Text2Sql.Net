using SqlSugar;
using System;

namespace Text2Sql.Net.Repositories.Text2Sql.SchemaEmbedding
{
    /// <summary>
    /// Schema向量嵌入
    /// </summary>
    [SugarTable("SchemaEmbeddings")]
    public class SchemaEmbedding
    {
        /// <summary>
        /// 嵌入ID
        /// </summary>
        [SugarColumn(IsPrimaryKey = true)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// 数据库连接ID
        /// </summary>
        [SugarColumn(IsNullable = false)]
        public string ConnectionId { get; set; }

        /// <summary>
        /// 表名
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public string TableName { get; set; }

        /// <summary>
        /// 列名
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public string ColumnName { get; set; }

        /// <summary>
        /// 描述信息
        /// </summary>
        [SugarColumn(ColumnDataType = "text", IsNullable = false)]
        public string Description { get; set; }

        /// <summary>
        /// 向量数据（JSON格式）
        /// </summary>
        [SugarColumn(ColumnDataType = "text", IsNullable = false)]
        public string Vector { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreateTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime? UpdateTime { get; set; }

        /// <summary>
        /// 向量类型（表/列/关系等）
        /// </summary>
        public string EmbeddingType { get; set; }
    }

    /// <summary>
    /// 嵌入类型枚举
    /// </summary>
    public static class EmbeddingType
    {
        /// <summary>
        /// 表信息嵌入
        /// </summary>
        public const string Table = "Table";

        /// <summary>
        /// 列信息嵌入
        /// </summary>
        public const string Column = "Column";

        /// <summary>
        /// 表关系嵌入
        /// </summary>
        public const string Relation = "Relation";
    }
} 
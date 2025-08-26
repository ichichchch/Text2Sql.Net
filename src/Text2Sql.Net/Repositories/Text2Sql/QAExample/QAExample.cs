using SqlSugar;
using System;
using System.ComponentModel.DataAnnotations;

namespace Text2Sql.Net.Repositories.Text2Sql.QAExample
{
    /// <summary>
    /// 问答示例
    /// </summary>
    [SugarTable("QAExamples")]
    public class QAExample
    {
        /// <summary>
        /// 示例ID
        /// </summary>
        [SugarColumn(IsPrimaryKey = true)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// 数据库连接ID
        /// </summary>
        [SugarColumn(IsNullable = false)]
        public string ConnectionId { get; set; } = string.Empty;

        /// <summary>
        /// 用户问题
        /// </summary>
        [SugarColumn(ColumnDataType = "text", IsNullable = false)]
        [Required(ErrorMessage = "请输入用户问题")]
        public string Question { get; set; } = string.Empty;

        /// <summary>
        /// 对应的SQL查询
        /// </summary>
        [SugarColumn(ColumnDataType = "text", IsNullable = false)]
        [Required(ErrorMessage = "请输入SQL查询")]
        public string SqlQuery { get; set; } = string.Empty;

        /// <summary>
        /// 示例说明/描述
        /// </summary>
        [SugarColumn(ColumnDataType = "text", IsNullable = true)]
        public string? Description { get; set; }

        /// <summary>
        /// 示例分类/标签
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public string? Category { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        [SugarColumn(IsNullable = false)]
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 示例来源：manual(手动创建)、correction(修正生成)
        /// </summary>
        [SugarColumn(IsNullable = false)]
        public string Source { get; set; } = "manual";

        /// <summary>
        /// 如果是修正生成的，记录原始的错误SQL
        /// </summary>
        [SugarColumn(ColumnDataType = "text", IsNullable = true)]
        public string? OriginalIncorrectSql { get; set; }

        /// <summary>
        /// 使用次数（被匹配到的次数）
        /// </summary>
        [SugarColumn(IsNullable = false)]
        public int UsageCount { get; set; } = 0;

        /// <summary>
        /// 最后使用时间
        /// </summary>
        public DateTime? LastUsedTime { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreateTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime? UpdateTime { get; set; }

        /// <summary>
        /// 创建者
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public string? CreatedBy { get; set; }
    }

    /// <summary>
    /// 示例来源类型
    /// </summary>
    public static class ExampleSource
    {
        /// <summary>
        /// 手动创建
        /// </summary>
        public const string Manual = "manual";

        /// <summary>
        /// 修正生成
        /// </summary>
        public const string Correction = "correction";
    }
}

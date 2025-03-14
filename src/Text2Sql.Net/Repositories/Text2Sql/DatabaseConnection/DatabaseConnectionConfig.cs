using SqlSugar;
using System;
using System.ComponentModel.DataAnnotations;

namespace Text2Sql.Net.Repositories.Text2Sql.DatabaseConnection
{
    /// <summary>
    /// 数据库连接配置
    /// </summary>
    [SugarTable("DatabaseConnectionConfigs")]
    public class DatabaseConnectionConfig
    {
        /// <summary>
        /// 配置ID
        /// </summary>
        [SugarColumn(IsPrimaryKey = true)]
        public string Id { get; set; }

        /// <summary>
        /// 数据库连接名称
        /// </summary>
        [Required(ErrorMessage = "请输入名称")]
        public string Name { get; set; }

        /// <summary>
        /// 数据库类型简称 (兼容现有代码)
        /// </summary>
        [Required(ErrorMessage = "请输入数据库类型")]
        public string DbType { get; set; }

        /// <summary>
        /// 数据库服务器地址
        /// </summary>
        public string? Server { get; set; } = "";

        /// <summary>
        /// 端口号
        /// </summary>
        public int? Port { get; set; }

        /// <summary>
        /// 数据库名称
        /// </summary>
        public string? Database { get; set; }

        /// <summary>
        /// 用户名
        /// </summary>
        public string? Username { get; set; }

        /// <summary>
        /// 密码
        /// </summary>
        public string? Password { get; set; }

        /// <summary>
        /// 连接字符串
        /// </summary>
        [Required(ErrorMessage = "请输入连接字符串")]
        public string ConnectionString { get; set; }

        /// <summary>
        /// 描述信息
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreateTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime? UpdateTime { get; set; }
    }
} 
using System;

namespace Text2Sql.Net.Domain.Model
{
    /// <summary>
    /// 数据库类型枚举
    /// </summary>
    public static class DatabaseTypeEnum
    {
        /// <summary>
        /// SQL Server 数据库
        /// </summary>
        public const string SQLServer = "SQLServer";

        /// <summary>
        /// MySQL 数据库
        /// </summary>
        public const string MySQL = "MySQL";

        /// <summary>
        /// PostgreSQL 数据库
        /// </summary>
        public const string PostgreSQL = "PostgreSQL";

        /// <summary>
        /// SQLite 数据库
        /// </summary>
        public const string SQLite = "SQLite";
    }
} 
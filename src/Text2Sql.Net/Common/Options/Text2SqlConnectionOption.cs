namespace Text2Sql.Net.Options
{
    public class Text2SqlConnectionOption
    {
        /// <summary>
        /// sqlite连接字符串
        /// </summary>
        public static string DbType { get; set; } = "Sqlite";


        /// <summary>
        /// 业务数据链接字符串
        /// </summary>
        public static string DBConnection { get; set; } = $"Data Source=text2sql.db";
        /// <summary>
        /// 向量数据连接字符串
        /// </summary>
        public static string VectorConnection { get; set; } = "text2sqlmem.db";
        /// <summary>
        /// 向量数据维度，PG需要设置
        /// </summary>
        public static int VectorSize { get; set; } = 1536;
    }

}

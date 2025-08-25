using System;
using System.Collections.Generic;
using System.Text.Json;
using SqlSugar;

namespace Text2Sql.Net.Repositories.Text2Sql.ChatHistory
{
    /// <summary>
    /// 聊天消息实体
    /// </summary>
    [SugarTable("Chat_Messages")]
    public class ChatMessage
    {
        /// <summary>
        /// 消息ID
        /// </summary>
        [SugarColumn(IsPrimaryKey = true)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// 数据库连接ID
        /// </summary>
        public string ConnectionId { get; set; } = string.Empty;

        /// <summary>
        /// 消息内容
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 是否为用户消息
        /// </summary>
        public bool IsUser { get; set; }

        /// <summary>
        /// SQL查询语句（仅AI响应消息）
        /// </summary>
        [SugarColumn( ColumnDataType = "text", IsNullable = true)]
        public string? SqlQuery { get; set; }

        /// <summary>
        /// 执行错误信息（仅AI响应消息）
        /// </summary>
        [SugarColumn( ColumnDataType = "text", IsNullable = true)]
        public string? ExecutionError { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreateTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 查询结果（JSON格式持久化）
        /// </summary>
        [SugarColumn(ColumnDataType = "text", IsNullable = true)]
        public string? QueryResultJson { get; set; }

        /// <summary>
        /// 查询结果（非持久化，用于运行时处理）
        /// </summary>
        [SugarColumn(IsIgnore = true)]
        public List<Dictionary<string, object>> QueryResult 
        { 
            get
            {
                if (_queryResult == null && !string.IsNullOrEmpty(QueryResultJson))
                {
                    try
                    {
                        _queryResult = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(QueryResultJson);
                    }
                    catch
                    {
                        _queryResult = new List<Dictionary<string, object>>();
                    }
                }
                return _queryResult ?? new List<Dictionary<string, object>>();
            }
            set
            {
                _queryResult = value;
                if (value != null && value.Count > 0)
                {
                    try
                    {
                        QueryResultJson = JsonSerializer.Serialize(value);
                    }
                    catch
                    {
                        QueryResultJson = null;
                    }
                }
                else
                {
                    QueryResultJson = null;
                }
            }
        }

        private List<Dictionary<string, object>>? _queryResult;
    }
} 
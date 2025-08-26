using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using System;

namespace Text2Sql.Net.Web.Tools
{
    /// <summary>
    /// Text2Sql MCP上下文帮助类 - 用于从连接参数中获取数据库连接ID
    /// </summary>
    public static class Text2SqlMcpContextHelper
    {
        private static IHttpContextAccessor _httpContextAccessor;

        /// <summary>
        /// 初始化HTTP上下文访问器（在Program中调用）
        /// </summary>
        /// <param name="httpContextAccessor"></param>
        public static void Initialize(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        /// <summary>
        /// 从MCP服务器上下文中获取数据库连接ID
        /// </summary>
        /// <param name="thisServer">MCP服务器实例</param>
        /// <returns>数据库连接ID，如果未找到则返回默认值</returns>
        public static string GetConnectionId(IMcpServer thisServer)
        {
            try
            {
                if (_httpContextAccessor?.HttpContext == null)
                {
                    return "default"; // 返回默认连接ID
                }

                var httpContext = _httpContextAccessor.HttpContext;
                
                // 从查询参数中获取数据库连接ID
                var connectionId = httpContext.Request.Query["connectionId"].ToString();
                
                if (string.IsNullOrEmpty(connectionId))
                {
                    // 如果没有指定连接ID，尝试从id参数获取（兼容性）
                    connectionId = httpContext.Request.Query["id"].ToString();
                }

                // 如果仍然为空，返回默认值
                if (string.IsNullOrEmpty(connectionId))
                {
                    connectionId = "default";
                }

                return connectionId;
            }
            catch (Exception)
            {
                return "default"; // 发生错误时返回默认连接ID
            }
        }

        /// <summary>
        /// 尝试从MCP服务器上下文中获取数据库连接ID
        /// </summary>
        /// <param name="thisServer">MCP服务器实例</param>
        /// <param name="connectionId">输出的数据库连接ID</param>
        /// <returns>是否成功获取</returns>
        public static bool TryGetConnectionId(IMcpServer thisServer, out string connectionId)
        {
            connectionId = null;
            
            try
            {
                connectionId = GetConnectionId(thisServer);
                return !string.IsNullOrEmpty(connectionId);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 获取连接信息摘要
        /// </summary>
        /// <param name="thisServer">MCP服务器实例</param>
        /// <returns>连接信息</returns>
        public static string GetConnectionInfo(IMcpServer thisServer)
        {
            try
            {
                if (_httpContextAccessor?.HttpContext == null)
                {
                    return "无HTTP上下文";
                }

                var httpContext = _httpContextAccessor.HttpContext;
                var connectionId = GetConnectionId(thisServer);
                var userAgent = httpContext.Request.Headers["User-Agent"].ToString();
                var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString();

                return $"数据库连接ID: {connectionId}, 客户端: {userAgent}, IP: {remoteIp}";
            }
            catch (Exception ex)
            {
                return $"获取连接信息失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 获取用户会话信息
        /// </summary>
        /// <param name="thisServer">MCP服务器实例</param>
        /// <returns>会话信息</returns>
        public static string GetSessionInfo(IMcpServer thisServer)
        {
            try
            {
                if (_httpContextAccessor?.HttpContext == null)
                {
                    return "无会话信息";
                }

                var httpContext = _httpContextAccessor.HttpContext;
                var sessionId = httpContext.Session?.Id ?? "无会话";
                var connectionId = GetConnectionId(thisServer);

                return $"会话ID: {sessionId}, 数据库连接: {connectionId}";
            }
            catch (Exception ex)
            {
                return $"获取会话信息失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 验证数据库连接ID是否有效
        /// </summary>
        /// <param name="connectionId">数据库连接ID</param>
        /// <returns>是否有效</returns>
        public static bool IsValidConnectionId(string connectionId)
        {
            return !string.IsNullOrWhiteSpace(connectionId) && 
                   connectionId.Length >= 1 && 
                   connectionId.Length <= 100; // 合理的长度限制
        }

        /// <summary>
        /// 获取请求的来源信息
        /// </summary>
        /// <param name="thisServer">MCP服务器实例</param>
        /// <returns>来源信息</returns>
        public static string GetRequestSource(IMcpServer thisServer)
        {
            try
            {
                if (_httpContextAccessor?.HttpContext == null)
                {
                    return "未知来源";
                }

                var httpContext = _httpContextAccessor.HttpContext;
                var referer = httpContext.Request.Headers["Referer"].ToString();
                var origin = httpContext.Request.Headers["Origin"].ToString();
                var host = httpContext.Request.Host.ToString();

                return $"Host: {host}, Origin: {origin ?? "无"}, Referer: {referer ?? "无"}";
            }
            catch (Exception ex)
            {
                return $"获取请求来源失败: {ex.Message}";
            }
        }
    }
}

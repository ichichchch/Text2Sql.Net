# Text2Sql.Net MCP工具集

本文档介绍了Text2Sql.Net项目中集成的MCP (Model Context Protocol) 工具集，这些工具允许AI助手直接与Text2Sql系统交互。

## 🚀 快速开始

### 启动服务

1. 启动Text2Sql.Net.Web应用程序
2. MCP服务将在以下端点可用：
   - HTTP传输：`http://localhost:5000/mcp`
   - WebSocket传输：`ws://localhost:5000/mcp/ws`

### 连接方式

MCP客户端可以通过查询参数指定要操作的数据库连接：

```
http://localhost:5000/mcp?connectionId=your-database-connection-id
```

如果未指定`connectionId`，系统将使用默认连接。

## 🔧 可用工具

### 1. get_database_connections
获取所有已配置的数据库连接信息。

**参数**：无

**示例用法**：
```
查询所有数据库连接配置
```

**返回**：包含连接ID、名称、数据库类型、服务器信息等的详细列表。

### 2. get_database_schema
获取当前数据库的表结构信息。

**参数**：无（自动使用当前连接上下文）

**示例用法**：
```
获取当前数据库的所有表结构
```

### 3. generate_sql
根据自然语言生成SQL查询语句。

**参数**：
- `userQuery` (必需): 自然语言查询需求
- `executeQuery` (可选): 是否执行生成的SQL，默认false

**示例用法**：
```
生成查询: "查找所有活跃用户的姓名和邮箱"
生成并执行查询: "统计每个部门的员工数量"
```

### 4. execute_sql
直接执行SQL查询语句。

**参数**：
- `sqlQuery` (必需): 要执行的SQL语句
- `maxRows` (可选): 最大返回行数，默认100

**示例用法**：
```
执行SQL: "SELECT * FROM users LIMIT 10"
```

### 5. get_chat_history
获取当前数据库连接的聊天历史记录。

**参数**：
- `limit` (可选): 返回记录数限制，默认20

**示例用法**：
```
获取最近20条聊天记录
```

### 6. get_system_status
获取Text2Sql系统的运行状态和统计信息。

**参数**：无

**示例用法**：
```
查看系统状态和统计信息
```

**返回**：包括数据库连接统计、聊天记录统计、系统信息和内存使用情况。



## 🛠️ 技术架构

### MCP工具注册

在`Program.cs`中注册MCP服务：

```csharp
// 添加MCP服务支持
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<Text2SqlMcpTool>();

// 添加HTTP上下文访问器
builder.Services.AddHttpContextAccessor();
```

### 上下文管理

`Text2SqlMcpContextHelper`类提供了从MCP连接中提取数据库连接ID等上下文信息的功能：

```csharp
// 获取数据库连接ID
var connectionId = Text2SqlMcpContextHelper.GetConnectionId(thisServer);

// 获取连接信息
var info = Text2SqlMcpContextHelper.GetConnectionInfo(thisServer);
```

## 📊 使用场景

### 1. 数据库探索
```
我想了解这个系统有哪些数据库连接
数据库A有哪些表？
用户表的结构是什么样的？
```

### 2. 自然语言查询
```
查找所有注册时间在最近30天的用户
统计每个城市的订单数量
找出销售额最高的前10个产品
```

### 3. SQL调试和优化
```
这个查询为什么这么慢？帮我优化一下
这个SQL语句报错了，帮我修复
```

### 4. 系统监控
```
系统现在的运行状态如何？
有多少个数据库连接？
最近的聊天记录统计是什么？
```

## 🔒 安全考虑

1. **连接隔离**：每个MCP会话只能访问通过上下文指定的数据库连接
2. **SQL限制**：执行的SQL查询受到应用程序层面的限制和验证
3. **日志记录**：所有MCP工具调用都会被记录到日志中
4. **确认操作**：危险操作（如清空聊天历史）需要明确的确认参数

## 🐛 故障排除

### 常见问题

1. **"未找到数据库连接配置"**
   - 确认MCP连接上下文中的connectionId正确
   - 检查数据库连接是否已在系统中配置

2. **"MCP服务未响应"**
   - 确认应用程序正在运行
   - 检查防火墙设置
   - 验证MCP端点是否可访问

3. **"SQL执行失败"**
   - 检查数据库连接字符串
   - 验证SQL语法
   - 确认数据库权限

### 日志查看

检查应用程序日志以获取详细错误信息：
```
[INFO] Text2SqlMcpTool: 获取数据库 conn-001 的基本信息
[ERROR] Text2SqlMcpTool: 获取数据库表结构时发生错误
```

## 📝 贡献指南

要添加新的MCP工具：

1. 在`Text2SqlMcpTool`类中添加新方法
2. 使用`[McpServerTool]`属性标记方法
3. 添加适当的参数描述
4. 实现错误处理和日志记录
5. 更新此文档

## 📞 支持

如果您在使用MCP工具时遇到问题，请：

1. 查看应用程序日志
2. 检查数据库连接配置
3. 验证MCP客户端设置
4. 联系技术支持团队

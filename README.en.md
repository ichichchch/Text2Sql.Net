[简体中文](./README.md) | English

## A simple .NET implementation of Text2SQL

![demo](https://github.com/AIDotNet/Text2Sql.Net/blob/main/doc/demo.png?raw=true)

This project supports SQL Server, MySQL, PostgreSQL and SQLite. Configuration example:

```json
"Text2SqlOpenAI": {
  "Key": "your-api-key",
  "EndPoint": "https://api.antsk.cn/",
  "ChatModel": "gpt-4o",
  "EmbeddingModel": "text-embedding-ada-002"
},
"Text2SqlConnection": {
  "DbType": "Sqlite", //PostgreSQL
  "DBConnection": "Data Source=text2sql.db",
  "VectorConnection": "text2sqlmem.db",
  "VectorSize": 1536 //Required for PostgreSQL
}
```

## Community
Join our developer community through WeChat (ID: xuzeyu91) or visit [AntSK](https://demo.antsk.cn) for more RAG solutions.


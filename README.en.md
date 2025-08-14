[简体中文](./README.md) | English

## A simple .NET implementation of Text2SQL

![index](https://github.com/AIDotNet/Text2Sql.Net/blob/main/doc/index.png?raw=true)

![db](https://github.com/AIDotNet/Text2Sql.Net/blob/main/doc/db.png?raw=true)

![schecm](https://github.com/AIDotNet/Text2Sql.Net/blob/main/doc/schecm.png?raw=true)

![demo](https://github.com/AIDotNet/Text2Sql.Net/blob/main/doc/demo.png?raw=true)

![demo1](https://github.com/AIDotNet/Text2Sql.Net/blob/main/doc/demo1.png?raw=true)

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
  "VectorSize": 1536 //Required for PostgreSQL, optional for SQLite
}
```

### Core Modules
1. **Database Abstraction Layer**
   - Multi-database support via strategy pattern
   - Standardized operations through IDatabaseProvider interface
   - Dynamic loading of database drivers (SQLite/Postgres/MySql/SqlServer)
   - Auto-generated database-specific SQL dialects

4. **Vector Database Integration**
   - SQLite in-memory vector search
   - PostgreSQL pgvector extension support
   - Unified IVectorRepository interface
   - Cosine similarity/Euclidean distance calculations

## Core Process Flow
```mermaid
flowchart TD
    A[User Natural Language Query] --> B{Database Connection Selected}
    B -->|Not Selected| C[Prompt to Select Database]
    B -->|Selected| D[Save User Message to Chat History]
    
    D --> E[Semantic Search for Relevant Schema]
    E --> F[Vector Database Query]
    F --> G[Relevance Scoring & Table Relationship Inference]
    G --> H[Build Schema Context]
    
    H --> I[Call LLM to Generate SQL]
    I --> J[Use Semantic Kernel Plugins]
    J --> K[SQL Security Check]
    
    K -->|Query Statement| L[Auto Execute SQL]
    K -->|Operation Statement| M[Generate SQL Only<br/>No Auto Execution]
    
    L --> N{Execution Successful}
    N -->|Success| O[Return Query Results]
    N -->|Failed| P[SQL Optimization]
    
    P --> Q[Optimize SQL Using Error Info]
    Q --> R[Re-execute Optimized SQL]
    R --> S[Return Final Results]
    
    M --> T[Prompt Manual Execution]
    O --> U[Save Response to Chat History]
    S --> U
    T --> U
    U --> V[Display Results to User]
    
    style A fill:#e1f5fe
    style V fill:#e8f5e8
    style K fill:#fff3e0
    style P fill:#fce4ec
```

## Schema Training & Vector Search Flow
```mermaid
flowchart TD
    A[Database Connection Config] --> B[Schema Training Service]
    B --> C[Extract Database Table Structure]
    C --> D[Get Table/Column/Foreign Key Info]
    D --> E[Generate Table Description Text]
    
    E --> F[Text Vectorization]
    F --> G[Store to Vector Database]
    
    G --> H{Vector Storage Type}
    H -->|SQLite| I[SQLiteMemoryStore]
    H -->|PostgreSQL| J[PostgresMemoryStore with pgvector]
    
    I --> K[Schema Training Complete]
    J --> K
    
    K --> L[Wait for User Query]
    L --> M[Semantic Search]
    M --> N[Relevance Matching]
    N --> O[Return Relevant Table Structure]
    
    style A fill:#e3f2fd
    style F fill:#f3e5f5
    style G fill:#e8f5e8
    style M fill:#fff3e0
```

## System Architecture
```mermaid
flowchart LR
    subgraph "UI Layer"
        A[Blazor Frontend Pages]
        B[Database Connection Selection]
        C[Chat Input Box]
        D[SQL Result Display]
    end
    
    subgraph "Service Layer"
        E[ChatService<br/>Chat Service]
        F[SchemaTrainingService<br/>Schema Training Service]
        G[SemanticService<br/>Semantic Service]
        H[SqlExecutionService<br/>SQL Execution Service]
    end
    
    subgraph "Data Access Layer"
        I[DatabaseConnectionRepository<br/>Database Connection Repository]
        J[ChatMessageRepository<br/>Chat Message Repository]
        K[DatabaseSchemaRepository<br/>Schema Repository]
        L[SchemaEmbeddingRepository<br/>Vector Embedding Repository]
    end
    
    subgraph "External Services"
        M[OpenAI API<br/>LLM Service]
        N[Vector Database<br/>SQLite/PostgreSQL]
        O[Business Database<br/>Multi-Database Support]
    end
    
    A --> E
    B --> I
    C --> E
    D --> H
    
    E --> F
    E --> G
    E --> H
    E --> J
    
    F --> K
    F --> L
    G --> N
    H --> I
    H --> O
    
    E --> M
    G --> M
    
    style A fill:#e1f5fe
    style E fill:#f3e5f5
    style M fill:#fff3e0
    style N fill:#e8f5e8
```

## Community
Join our developer community through WeChat (ID: xuzeyu91) or visit [AntSK](https://demo.antsk.cn) for more RAG solutions.


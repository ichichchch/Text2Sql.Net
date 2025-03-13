using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Text2Sql.Net.Base;
using Text2Sql.Net.Domain.Model;

namespace Text2Sql.Net.Repositories.Text2Sql.DatabaseConnection
{
    /// <summary>
    /// 数据库连接配置仓储实现类
    /// </summary>
    [ServiceDescription(typeof(IDatabaseConnectionConfigRepository), ServiceLifetime.Scoped)]
    public class DatabaseConnectionConfigRepository : Repository<DatabaseConnectionConfig>, IDatabaseConnectionConfigRepository
    {
       
    }
} 
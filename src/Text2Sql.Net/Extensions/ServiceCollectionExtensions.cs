using System.Reflection;
using Text2Sql.Net;
using Text2Sql.Net.Options;
using Text2Sql.Net.Utils;
using Microsoft.SemanticKernel;
using SqlSugar;
using Text2Sql.Net.Repositories.Text2Sql.DatabaseConnection;
using Text2Sql.Net.Domain.Interface;
using Text2Sql.Net.Domain.Service;
using Text2Sql.Net.Repositories.Text2Sql.DatabaseSchema;
using Text2Sql.Net.Repositories.Text2Sql.SchemaEmbedding;
using Microsoft.SemanticKernel.Memory;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// 容器扩展
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 从程序集中加载类型并添加到容器中
        /// </summary>
        /// <param name="services">容器</param>
        /// <returns></returns>
        public static IServiceCollection AddText2SqlNet(this IServiceCollection services, Kernel _kernel = null)
        {
            Type attributeType = typeof(ServiceDescriptionAttribute);
            //var refAssembyNames = Assembly.GetExecutingAssembly().GetReferencedAssemblies();

            Assembly assembly = Assembly.Load("Text2Sql.Net");

            var types = assembly.GetTypes();

            foreach (var classType in types)
            {
                if (!classType.IsAbstract && classType.IsClass && classType.IsDefined(attributeType, false))
                {
                    ServiceDescriptionAttribute serviceAttribute = (classType.GetCustomAttribute(attributeType) as ServiceDescriptionAttribute);
                    switch (serviceAttribute.Lifetime)
                    {
                        case ServiceLifetime.Scoped:
                            services.AddScoped(serviceAttribute.ServiceType, classType);
                            break;

                        case ServiceLifetime.Singleton:
                            services.AddSingleton(serviceAttribute.ServiceType, classType);
                            break;

                        case ServiceLifetime.Transient:
                            services.AddTransient(serviceAttribute.ServiceType, classType);
                            break;
                    }
                }
            }

            // 注册Schema训练相关服务
            services.AddScoped<IDatabaseSchemaRepository, DatabaseSchemaRepository>();
            services.AddScoped<ISchemaEmbeddingRepository, SchemaEmbeddingRepository>();
            services.AddScoped<ISchemaTrainingService, SchemaTrainingService>();
            
            // 注册内存存储
            services.AddSingleton<IMemoryStore, VolatileMemoryStore>();

            CodeFirst();
            InitSK(services, _kernel);

            return services;
        }

        /// <summary>
        /// 初始化SK
        /// </summary>
        /// <param name="services"></param>
        /// <param name="_kernel">可以提供自定义Kernel</param>
        static void InitSK(IServiceCollection services,Kernel _kernel = null)
        {
            var handler = new OpenAIHttpClientHandler();
            services.AddTransient<Kernel>((serviceProvider) =>
            {
                if (_kernel == null)
                {
                    _kernel = Kernel.CreateBuilder()
                    .AddOpenAIChatCompletion(
                      modelId: Text2SqlOpenAIOption.ChatModel,
                      apiKey: Text2SqlOpenAIOption.Key,
                      httpClient: new HttpClient(handler)
                         )
                    .Build();
                }
                //导入插件
                if (!_kernel.Plugins.Any(p => p.Name == "text2sql"))
                {
                    var pluginPatth = Path.Combine(RepoFiles.SamplePluginsPath(), "text2sql");
                    Console.WriteLine($"pluginPatth:{pluginPatth}");
                    _kernel.ImportPluginFromPromptDirectory(pluginPatth);
                }
                return _kernel;
            });
        }

        /// <summary>
        /// 初始化DB
        /// </summary>
        static void CodeFirst()
        {
            // 获取仓储服务
            var _repository = new DatabaseConnectionConfigRepository();

            // 创建数据库（如果不存在）
            _repository.GetDB().DbMaintenance.CreateDatabase();
            // 在所有程序集中查找具有[SugarTable]特性的类
            var assembly = Assembly.GetExecutingAssembly();
            // 获取该程序集中所有具有SugarTable特性的类型
            var entityTypes = assembly.GetTypes()
                    .Where(type => TypeIsEntity(type));
            // 为每个找到的类型初始化数据库表
            foreach (var type in entityTypes)
            {
                _repository.GetDB().CodeFirst.InitTables(type);
            }
        }

        static bool TypeIsEntity(Type type)
        {
            // 检查类型是否具有SugarTable特性
            return type.GetCustomAttributes(typeof(SugarTable), inherit: false).Length > 0;
        }
    }
}

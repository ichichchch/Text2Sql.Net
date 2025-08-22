using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Newtonsoft.Json;
using System.Text;
using System.Text.RegularExpressions;
using Text2Sql.Net.Domain.Interface;
using Text2Sql.Net.Repositories.Text2Sql.DatabaseSchema;

namespace Text2Sql.Net.Domain.Service
{
    /// <summary>
    /// 执行反馈优化器
    /// 通过执行结果分析来优化SQL查询
    /// </summary>
    [ServiceDescription(typeof(IExecutionFeedbackOptimizer), ServiceLifetime.Scoped)]
    public class ExecutionFeedbackOptimizer : IExecutionFeedbackOptimizer
    {
        private readonly ISqlExecutionService _sqlExecutionService;
        private readonly Kernel _kernel;
        private readonly ILogger<ExecutionFeedbackOptimizer> _logger;

        public ExecutionFeedbackOptimizer(
            ISqlExecutionService sqlExecutionService,
            Kernel kernel,
            ILogger<ExecutionFeedbackOptimizer> logger)
        {
            _sqlExecutionService = sqlExecutionService;
            _kernel = kernel;
            _logger = logger;
        }

        /// <summary>
        /// 基于执行反馈进行迭代优化
        /// </summary>
        /// <param name="connectionId">数据库连接ID</param>
        /// <param name="userMessage">用户查询</param>
        /// <param name="schemaInfo">Schema信息</param>
        /// <param name="initialSql">初始SQL</param>
        /// <param name="maxIterations">最大迭代次数</param>
        /// <returns>优化结果</returns>
        public async Task<OptimizationResult> OptimizeWithFeedbackAsync(
            string connectionId,
            string userMessage, 
            string schemaInfo,
            string initialSql, 
            int maxIterations = 3)
        {
            var result = new OptimizationResult
            {
                OriginalSql = initialSql,
                OptimizationSteps = new List<OptimizationStep>()
            };

            string currentSql = initialSql;
            
            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                _logger.LogInformation($"开始第 {iteration + 1} 轮优化迭代");

                var step = new OptimizationStep
                {
                    Iteration = iteration + 1,
                    InputSql = currentSql
                };

                try
                {
                    // 1. 尝试执行SQL
                    var (executionResult, errorMessage) = await _sqlExecutionService.ExecuteQueryAsync(connectionId, currentSql);
                    
                    step.ExecutionResult = new ExecutionResultInfo
                    {
                        Success = string.IsNullOrEmpty(errorMessage),
                        ErrorMessage = errorMessage,
                        RowCount = executionResult?.Count ?? 0,
                        ExecutionTime = DateTime.Now // 简化处理，实际应该记录真实执行时间
                    };

                    if (string.IsNullOrEmpty(errorMessage))
                    {
                        // 执行成功，验证结果合理性
                        var validationResult = await ValidateResultAsync(executionResult, userMessage, schemaInfo);
                        step.ValidationResult = validationResult;

                        if (validationResult.IsValid)
                        {
                            // 结果合理，优化完成
                            step.OptimizedSql = currentSql;
                            step.OptimizationType = "验证通过";
                            result.OptimizationSteps.Add(step);
                            result.FinalSql = currentSql;
                            result.Success = true;
                            break;
                        }
                        else
                        {
                            // 结果不合理，需要进一步优化
                            var feedback = GenerateResultFeedback(validationResult, userMessage);
                            step.Feedback = feedback;
                            
                            currentSql = await RefineBasedOnResultFeedbackAsync(
                                userMessage, schemaInfo, currentSql, feedback);
                            step.OptimizedSql = currentSql;
                            step.OptimizationType = "结果优化";
                        }
                    }
                    else
                    {
                        // 执行错误，分析并修复
                        var errorAnalysis = AnalyzeExecutionError(errorMessage, currentSql, schemaInfo);
                        step.ErrorAnalysis = errorAnalysis;
                        
                        currentSql = await FixSqlErrorAsync(
                            userMessage, schemaInfo, currentSql, errorAnalysis);
                        step.OptimizedSql = currentSql;
                        step.OptimizationType = "错误修复";
                    }

                    result.OptimizationSteps.Add(step);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"第 {iteration + 1} 轮优化时出错：{ex.Message}");
                    step.ErrorAnalysis = new ErrorAnalysis
                    {
                        ErrorType = "系统错误",
                        ErrorMessage = ex.Message,
                        SuggestedFix = "请检查系统配置或联系管理员"
                    };
                    result.OptimizationSteps.Add(step);
                    break;
                }
            }

            // 如果所有迭代都未能成功，标记为失败
            if (!result.Success && result.OptimizationSteps.Count > 0)
            {
                result.FinalSql = result.OptimizationSteps.Last().OptimizedSql;
                result.Success = false;
            }

            _logger.LogInformation($"优化完成，共进行 {result.OptimizationSteps.Count} 轮迭代");
            return result;
        }

        /// <summary>
        /// 验证查询结果的合理性
        /// </summary>
        /// <param name="result">查询结果</param>
        /// <param name="userMessage">用户查询</param>
        /// <param name="schemaInfo">Schema信息</param>
        /// <returns>验证结果</returns>
        private async Task<ValidationResult> ValidateResultAsync(
            List<Dictionary<string, object>> result, 
            string userMessage, 
            string schemaInfo)
        {
            var validation = new ValidationResult { IsValid = true, Issues = new List<string>() };

            if (result == null)
            {
                validation.IsValid = false;
                validation.Issues.Add("查询结果为null");
                return validation;
            }

            // 1. 检查结果大小合理性
            if (!CheckResultSizeReasonableness(result, userMessage))
            {
                validation.Issues.Add($"结果数量({result.Count})可能不符合预期");
            }

            // 2. 检查数据类型一致性
            if (result.Count > 0 && !CheckDataTypeConsistency(result))
            {
                validation.Issues.Add("数据类型不一致");
            }

            // 3. 检查业务逻辑一致性
            if (!await CheckBusinessLogicConsistencyAsync(result, userMessage, schemaInfo))
            {
                validation.Issues.Add("业务逻辑不一致");
            }

            // 4. 检查空值处理
            if (!CheckNullHandling(result, userMessage))
            {
                validation.Issues.Add("空值处理可能存在问题");
            }

            validation.IsValid = validation.Issues.Count == 0;
            return validation;
        }

        /// <summary>
        /// 检查结果大小合理性
        /// </summary>
        private bool CheckResultSizeReasonableness(List<Dictionary<string, object>> result, string userMessage)
        {
            var message = userMessage.ToLower();
            int resultSize = result.Count;

            if (message.Contains("top") || message.Contains("前") || message.Contains("最多"))
            {
                // 如果查询包含限制词汇，结果不应该太多
                return resultSize <= 100;
            }

            if (message.Contains("所有") || message.Contains("全部") || message.Contains("all"))
            {
                // 如果查询要求所有记录，结果不应该太少（除非数据确实很少）
                return resultSize >= 1;
            }

            // 一般查询结果不应该过多或为空
            return resultSize > 0 && resultSize <= 10000;
        }

        /// <summary>
        /// 检查数据类型一致性
        /// </summary>
        private bool CheckDataTypeConsistency(List<Dictionary<string, object>> result)
        {
            if (result.Count <= 1) return true;

            var firstRow = result[0];
            
            foreach (var row in result.Skip(1))
            {
                foreach (var key in firstRow.Keys)
                {
                    if (row.ContainsKey(key))
                    {
                        var firstType = firstRow[key]?.GetType();
                        var currentType = row[key]?.GetType();
                        
                        if (firstType != null && currentType != null && firstType != currentType)
                        {
                            // 允许数值类型之间的转换
                            if (!IsNumericTypeConversion(firstType, currentType))
                            {
                                return false;
                            }
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 检查业务逻辑一致性
        /// </summary>
        private async Task<bool> CheckBusinessLogicConsistencyAsync(
            List<Dictionary<string, object>> result, 
            string userMessage, 
            string schemaInfo)
        {
            var message = userMessage.ToLower();

            // 检查排序逻辑
            if (message.Contains("最高") || message.Contains("最大") || message.Contains("highest") || message.Contains("max"))
            {
                return CheckDescendingOrder(result);
            }

            if (message.Contains("最低") || message.Contains("最小") || message.Contains("lowest") || message.Contains("min"))
            {
                return CheckAscendingOrder(result);
            }

            // 检查时间范围逻辑
            if (message.Contains("最近") || message.Contains("recent"))
            {
                return CheckRecentTimeOrder(result);
            }

            return true;
        }

        /// <summary>
        /// 检查空值处理
        /// </summary>
        private bool CheckNullHandling(List<Dictionary<string, object>> result, string userMessage)
        {
            if (result.Count == 0) return true;

            // 如果查询明确要求非空值，检查结果中是否包含null
            if (userMessage.ToLower().Contains("非空") || userMessage.ToLower().Contains("not null"))
            {
                foreach (var row in result)
                {
                    if (row.Values.Any(v => v == null || v == DBNull.Value))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 分析执行错误
        /// </summary>
        private ErrorAnalysis AnalyzeExecutionError(string errorMessage, string sql, string schemaInfo)
        {
            var analysis = new ErrorAnalysis
            {
                ErrorMessage = errorMessage,
                ErrorType = ClassifyError(errorMessage),
                SuggestedFix = GenerateFixSuggestion(errorMessage, sql, schemaInfo)
            };

            return analysis;
        }

        /// <summary>
        /// 错误分类
        /// </summary>
        private string ClassifyError(string errorMessage)
        {
            var error = errorMessage.ToLower();

            if (error.Contains("column") && (error.Contains("not found") || error.Contains("doesn't exist")))
                return "列不存在";
            
            if (error.Contains("table") && (error.Contains("not found") || error.Contains("doesn't exist")))
                return "表不存在";
            
            if (error.Contains("syntax") || error.Contains("near"))
                return "语法错误";
            
            if (error.Contains("type") && error.Contains("mismatch"))
                return "类型不匹配";
            
            if (error.Contains("aggregate") || error.Contains("group by"))
                return "聚合错误";
            
            if (error.Contains("join") || error.Contains("foreign key"))
                return "关联错误";

            return "未知错误";
        }

        /// <summary>
        /// 生成修复建议
        /// </summary>
        private string GenerateFixSuggestion(string errorMessage, string sql, string schemaInfo)
        {
            var errorType = ClassifyError(errorMessage);

            return errorType switch
            {
                "列不存在" => "检查列名拼写，确认列是否存在于对应表中",
                "表不存在" => "检查表名拼写，确认表是否存在于数据库中",
                "语法错误" => "检查SQL语法，特别是关键字使用和标点符号",
                "类型不匹配" => "检查数据类型转换，确保比较操作的类型一致",
                "聚合错误" => "确保所有非聚合列都包含在GROUP BY中",
                "关联错误" => "检查JOIN条件，确保关联字段存在且类型匹配",
                _ => "请仔细检查SQL语句的语法和逻辑"
            };
        }

        /// <summary>
        /// 基于错误分析修复SQL
        /// </summary>
        private async Task<string> FixSqlErrorAsync(
            string userMessage, 
            string schemaInfo, 
            string currentSql, 
            ErrorAnalysis errorAnalysis)
        {
            try
            {
                var settings = new OpenAIPromptExecutionSettings { Temperature = 0.1 };
                var function = _kernel.Plugins.GetFunction("text2sql", "optimize_sql_query");
                
                var args = new KernelArguments(settings)
                {
                    ["schemaInfo"] = schemaInfo,
                    ["userMessage"] = userMessage,
                    ["originalSql"] = currentSql,
                    ["errorMessage"] = $"{errorAnalysis.ErrorType}: {errorAnalysis.ErrorMessage}\n建议: {errorAnalysis.SuggestedFix}"
                };

                var result = await _kernel.InvokeAsync(function, args);
                return CleanSqlResult(result?.ToString()?.Trim());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "使用AI修复SQL时出错");
                return currentSql; // 返回原始SQL
            }
        }

        /// <summary>
        /// 基于结果反馈优化SQL
        /// </summary>
        private async Task<string> RefineBasedOnResultFeedbackAsync(
            string userMessage, 
            string schemaInfo, 
            string currentSql, 
            string feedback)
        {
            try
            {
                var settings = new OpenAIPromptExecutionSettings { Temperature = 0.1 };
                var function = _kernel.Plugins.GetFunction("text2sql", "optimize_sql_query");
                
                var args = new KernelArguments(settings)
                {
                    ["schemaInfo"] = schemaInfo,
                    ["userMessage"] = userMessage,
                    ["originalSql"] = currentSql,
                    ["errorMessage"] = $"结果验证问题: {feedback}"
                };

                var result = await _kernel.InvokeAsync(function, args);
                return CleanSqlResult(result?.ToString()?.Trim());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "基于结果反馈优化SQL时出错");
                return currentSql;
            }
        }

        /// <summary>
        /// 生成结果反馈信息
        /// </summary>
        private string GenerateResultFeedback(ValidationResult validationResult, string userMessage)
        {
            var feedback = new StringBuilder();
            feedback.AppendLine("查询结果验证发现以下问题：");
            
            foreach (var issue in validationResult.Issues)
            {
                feedback.AppendLine($"- {issue}");
            }

            feedback.AppendLine($"\n用户原始需求：{userMessage}");
            feedback.AppendLine("请根据验证问题调整SQL查询，使结果更符合用户预期。");

            return feedback.ToString();
        }

        /// <summary>
        /// 检查降序排列
        /// </summary>
        private bool CheckDescendingOrder(List<Dictionary<string, object>> result)
        {
            if (result.Count <= 1) return true;

            // 查找数值列
            var numericColumns = FindNumericColumns(result[0]);
            
            foreach (var column in numericColumns)
            {
                var values = result.Where(r => r.ContainsKey(column) && r[column] != null)
                    .Select(r => Convert.ToDouble(r[column]))
                    .ToList();

                if (values.Count > 1)
                {
                    for (int i = 0; i < values.Count - 1; i++)
                    {
                        if (values[i] < values[i + 1])
                            return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 检查升序排列
        /// </summary>
        private bool CheckAscendingOrder(List<Dictionary<string, object>> result)
        {
            if (result.Count <= 1) return true;

            var numericColumns = FindNumericColumns(result[0]);
            
            foreach (var column in numericColumns)
            {
                var values = result.Where(r => r.ContainsKey(column) && r[column] != null)
                    .Select(r => Convert.ToDouble(r[column]))
                    .ToList();

                if (values.Count > 1)
                {
                    for (int i = 0; i < values.Count - 1; i++)
                    {
                        if (values[i] > values[i + 1])
                            return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 检查时间排序
        /// </summary>
        private bool CheckRecentTimeOrder(List<Dictionary<string, object>> result)
        {
            if (result.Count <= 1) return true;

            var timeColumns = FindTimeColumns(result[0]);
            
            foreach (var column in timeColumns)
            {
                var values = result.Where(r => r.ContainsKey(column) && r[column] != null)
                    .Select(r => Convert.ToDateTime(r[column]))
                    .ToList();

                if (values.Count > 1)
                {
                    for (int i = 0; i < values.Count - 1; i++)
                    {
                        if (values[i] < values[i + 1]) // 应该是降序（最近的在前）
                            return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 查找数值列
        /// </summary>
        private List<string> FindNumericColumns(Dictionary<string, object> row)
        {
            var numericColumns = new List<string>();
            
            foreach (var kvp in row)
            {
                if (kvp.Value != null && IsNumericType(kvp.Value.GetType()))
                {
                    numericColumns.Add(kvp.Key);
                }
            }

            return numericColumns;
        }

        /// <summary>
        /// 查找时间列
        /// </summary>
        private List<string> FindTimeColumns(Dictionary<string, object> row)
        {
            var timeColumns = new List<string>();
            
            foreach (var kvp in row)
            {
                if (kvp.Value != null && (kvp.Value is DateTime || kvp.Value is DateTimeOffset))
                {
                    timeColumns.Add(kvp.Key);
                }
            }

            return timeColumns;
        }

        /// <summary>
        /// 判断是否为数值类型
        /// </summary>
        private bool IsNumericType(Type type)
        {
            return type == typeof(int) || type == typeof(long) || type == typeof(float) || 
                   type == typeof(double) || type == typeof(decimal) || type == typeof(short) ||
                   type == typeof(byte) || type == typeof(uint) || type == typeof(ulong) ||
                   type == typeof(ushort) || type == typeof(sbyte);
        }

        /// <summary>
        /// 判断是否为数值类型转换
        /// </summary>
        private bool IsNumericTypeConversion(Type type1, Type type2)
        {
            return IsNumericType(type1) && IsNumericType(type2);
        }

        /// <summary>
        /// 清理SQL结果
        /// </summary>
        private string CleanSqlResult(string sql)
        {
            if (string.IsNullOrEmpty(sql)) return string.Empty;

            // 移除代码块标记
            sql = sql.Replace("```sql", "").Replace("```", "");
            
            // 移除多余的空行
            sql = string.Join("\n", sql.Split('\n').Where(line => !string.IsNullOrWhiteSpace(line)));
            
            return sql.Trim();
        }
    }

    /// <summary>
    /// 优化结果
    /// </summary>
    public class OptimizationResult
    {
        public string OriginalSql { get; set; }
        public string FinalSql { get; set; }
        public bool Success { get; set; }
        public List<OptimizationStep> OptimizationSteps { get; set; } = new List<OptimizationStep>();
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// 优化步骤
    /// </summary>
    public class OptimizationStep
    {
        public int Iteration { get; set; }
        public string InputSql { get; set; }
        public string OptimizedSql { get; set; }
        public string OptimizationType { get; set; }
        public ExecutionResultInfo ExecutionResult { get; set; }
        public ValidationResult ValidationResult { get; set; }
        public ErrorAnalysis ErrorAnalysis { get; set; }
        public string Feedback { get; set; }
    }

    /// <summary>
    /// 执行结果信息
    /// </summary>
    public class ExecutionResultInfo
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public int RowCount { get; set; }
        public DateTime ExecutionTime { get; set; }
    }

    /// <summary>
    /// 验证结果
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Issues { get; set; } = new List<string>();
    }

    /// <summary>
    /// 错误分析
    /// </summary>
    public class ErrorAnalysis
    {
        public string ErrorType { get; set; }
        public string ErrorMessage { get; set; }
        public string SuggestedFix { get; set; }
    }
}


using AntDesign;
using Microsoft.AspNetCore.Components;
using Text2Sql.Net.Repositories.Text2Sql.DatabaseSchema;

namespace Text2Sql.Net.Web.Pages.DatabaseConnection
{
    public partial class TableDetail
    {
        [Parameter] public string ConnectionId { get; set; } = string.Empty;
        [Parameter] public string TableName { get; set; } = string.Empty;

        private TableInfo? _tableInfo;
        private bool _loading = false;

        protected override async Task OnInitializedAsync()
        {
            await LoadTableData();
        }

        private async Task LoadTableData()
        {
            if (string.IsNullOrEmpty(ConnectionId) || string.IsNullOrEmpty(TableName))
            {
                _ = MessageService.Error("连接ID或表名不能为空");
                NavigateToTableList();
                return;
            }

            _loading = true;
            try
            {
                _tableInfo = await SchemaTrainingService.GetTableDetailAsync(ConnectionId, TableName);
                if (_tableInfo == null)
                {
                    _ = MessageService.Warning($"未找到表 '{TableName}' 的训练信息");
                }
            }
            catch (Exception ex)
            {
                _ = MessageService.Error($"加载表详细信息失败: {ex.Message}");
            }
            finally
            {
                _loading = false;
                StateHasChanged();
            }
        }

        private async Task SubmitAll()
        {
            _loading = true;
           
            try
            {
                if (_tableInfo != null)
                {
                    await SchemaTrainingService.UpdateTableAsync(ConnectionId, _tableInfo);
                    _ = MessageService.Success("表信息更新成功");
                }
            }
            catch (Exception ex)
            {
                _ = MessageService.Error($"更新表信息失败: {ex.Message}");
            }
            finally
            {
                _loading = false;
                StateHasChanged();
            }
        }

        private void NavigateToTableList()
        {
            NavigationManager.NavigateTo($"/database-connection/trained-tables/{ConnectionId}");
        }

        private void NavigateToConnection()
        {
            NavigationManager.NavigateTo($"/database-connection/details/{ConnectionId}");
        }

        private static string GetDataTypeColor(string dataType)
        {
            if (string.IsNullOrEmpty(dataType))
                return "";

            var type = dataType.ToLower();
            return type switch
            {
                var t when t.Contains("int") || t.Contains("number") || t.Contains("decimal") || t.Contains("numeric") => "blue",
                var t when t.Contains("varchar") || t.Contains("text") || t.Contains("char") || t.Contains("string") => "green",
                var t when t.Contains("datetime") || t.Contains("timestamp") || t.Contains("date") || t.Contains("time") => "orange",
                var t when t.Contains("bool") || t.Contains("bit") => "purple",
                var t when t.Contains("float") || t.Contains("real") || t.Contains("double") => "cyan",
                var t when t.Contains("binary") || t.Contains("blob") || t.Contains("image") => "red",
                _ => "default"
            };
        }
    }
}
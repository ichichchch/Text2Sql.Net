
using Microsoft.SemanticKernel.Memory;

namespace Text2Sql.Net.Domain.Interface
{
    public interface ISemanticService
    {
        Task<SemanticTextMemory> GetTextMemory();
    }
}

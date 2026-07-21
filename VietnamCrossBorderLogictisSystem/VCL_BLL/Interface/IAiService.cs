using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface;

public interface IAiService
{
    Task<PutawaySuggestionResponseDto> GetPutawaySuggestionsAsync(PutawaySuggestionRequest request);
    Task<SalesOrderStatusQueryResponse> QuerySalesOrderStatusAsync(SalesOrderStatusQueryRequest request);
}

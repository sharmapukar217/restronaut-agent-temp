using Refit;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace RestronautService.DataAccess.Remote;

public interface IVendureData
{
    [Post("/Order")]
    Task CreateOrder(
        [Body] Dictionary<string, string> body
    // [Header("X-Content-Encoding")] string contentEncodingHeader = "gzip"
    );

    [Post("/prep-sales-report")]
    Task ReportPrepOrder(
        [Body] string body,
        [Header("X-Content-Encoding")] string contentEncodingHeader = "gzip"
    );


    [Post("/instore-sales-report")]
    Task ReportInStoreSales(
        [Body] Dictionary<string, string> body,
        [Header("X-Content-Encoding")] string contentEncodingHeader = "gzip"
    );
}
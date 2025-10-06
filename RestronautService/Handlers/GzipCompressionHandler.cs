using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.IO.Compression;
using System;
using System.Linq;


public class GzipRequestHandler : DelegatingHandler
{
    public GzipRequestHandler() { }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (
            request.Content != null &&
            request.Headers.TryGetValues("X-Content-Encoding", out var values) && values.Contains("gzip")
        )
        {
            var originalData = await request.Content.ReadAsByteArrayAsync();
            var compressedBody = Compress(originalData);

            request.Content = new ByteArrayContent(compressedBody);
            request.Content.Headers.ContentEncoding.Add("gzip");
        }

        return await base.SendAsync(request, cancellationToken);
    }

    private byte[] Compress(byte[] data)
    {
        using var compressedStream = new MemoryStream();
        using (var gzipStream = new GZipStream(compressedStream, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzipStream.Write(data, 0, data.Length);
        }
        return compressedStream.ToArray();
    }
}

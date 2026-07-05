using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace LayoutFix.Core.Services;

public class ModelDownloadService
{
    private readonly HttpClient _httpClient = new HttpClient();

    public bool IsModelDownloaded(string path)
    {
        return File.Exists(path) && new FileInfo(path).Length > 1024 * 1024; // > 1MB
    }

    public async Task DownloadModelAsync(string url, string destinationPath, Action<double> progressCallback)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength;
        using var stream = await response.Content.ReadAsStreamAsync();
        
        var dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        byte[] buffer = new byte[8192];
        long totalRead = 0;
        int bytesRead;
        DateTime lastUpdate = DateTime.Now;

        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) != 0)
        {
            await fileStream.WriteAsync(buffer, 0, bytesRead);
            totalRead += bytesRead;

            if (totalBytes.HasValue)
            {
                var now = DateTime.Now;
                if ((now - lastUpdate).TotalMilliseconds > 100)
                {
                    double progress = (double)totalRead / totalBytes.Value;
                    progressCallback(progress);
                    lastUpdate = now;
                }
            }
        }
        
        progressCallback(1.0);
    }
}

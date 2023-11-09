using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;

namespace NordicDaysDemo
{
    public class LogAnalysisMessage
    {
        public string ContainerId { get; set; }
        public string BlobName { get; set; }
    }

    public class LogAnalysis
    {
        [FunctionName("LogAnalysis")]
        public async Task Run([ServiceBusTrigger("log-analysis", Connection = "ServiceBusConnection")] string myQueueItem, 
            ILogger log)
        {
            var logAnalysisMessage = JsonSerializer.Deserialize<LogAnalysisMessage>(myQueueItem);

            var storageConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");

            var blobServiceClient = new BlobServiceClient(storageConnectionString);
            var containerClient = blobServiceClient.GetBlobContainerClient(logAnalysisMessage.ContainerId);

            log.Log(LogLevel.Information, $"blob name: {logAnalysisMessage.BlobName}");

            var blobClient = containerClient.GetBlobClient(logAnalysisMessage.BlobName);

            // Download the blob to a stream
            BlobDownloadInfo blobDownloadInfo = await blobClient.DownloadAsync();
            var blobStream = blobDownloadInfo.Content;

            // Read the blob content line by line
            using var reader = new StreamReader(blobStream);
            string line;

            var resultBlobClient = containerClient.GetBlobClient("log-analysis-result.txt");

            using (var stream = new MemoryStream())
            using (var writer = new StreamWriter(stream))
            {
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Contains("error", StringComparison.CurrentCultureIgnoreCase))
                    {
                        writer.WriteLine(line);
                    }
                }
                writer.Flush();
                stream.Position = 0;

                await resultBlobClient.UploadAsync(stream, true);

                log.Log(LogLevel.Information, "upload completed");
            }
        }
    }
}

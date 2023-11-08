using System;
using System.IO;
using System.Threading.Tasks;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;

namespace NordicDaysDemo
{
    public class LogAnalysisMessage
    {
        public string ContainerId { get; set; }
        public string BlobName { get; set; }
        public int PartitionKey { get; set; }
    }

    public class LogAnalysis
    {
        [FunctionName("LogAnalysis")]
        public async Task Run([QueueTrigger("log-analysis", Connection = "AzureWebJobsStorage")] LogAnalysisMessage logAnalysisMessage, 
            ILogger log)
        {
            var storageConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");

            var blobServiceClient = new BlobServiceClient(storageConnectionString);
            var containerClient = blobServiceClient.GetBlobContainerClient(logAnalysisMessage.ContainerId);

            var keyVaultName = Environment.GetEnvironmentVariable("KeyVaultName");
            var kvUri = "https://" + keyVaultName + ".vault.azure.net";
            var client = new SecretClient(new Uri(kvUri), new DefaultAzureCredential());
            var dbConnectionString = (await client.GetSecretAsync("database-key")).Value.Value;

            var cosmosClient = new CosmosClient(dbConnectionString, new CosmosClientOptions()
            {
                ApplicationRegion = Regions.NorthEurope,
            });

            var container = cosmosClient.GetContainer("feedback", "reports");


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

            var report = (await container.ReadItemAsync<Report>(id: logAnalysisMessage.ContainerId,
                partitionKey: new PartitionKey(logAnalysisMessage.PartitionKey))).Resource;

            report.Status = ReportStatus.Analyzed;

            await container.ReplaceItemAsync(report, report.id, partitionKey: new PartitionKey(report.CreationDay));
        }
    }
}

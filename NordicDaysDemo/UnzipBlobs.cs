using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Queues;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using System.Globalization;
using Microsoft.Azure.Cosmos;

namespace NordicDaysDemo
{
    public class UnzipMessage
    {
        public string ContainerId { get; set; }
    }

    public class UnzipBlobs
    {
        private string _destinationDirectory = "unzipped";

        [FunctionName("UnzipBlobs")]
        public async Task Run([ServiceBusTrigger("blob-unzip", Connection = "ServiceBusConnection")]string myQueueItem, ILogger log)
        {
            var unzipMessage = JsonSerializer.Deserialize<UnzipMessage>(myQueueItem);
            
            var report = await SaveCosmosDbDocument(unzipMessage.ContainerId);

            var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");

            var blobServiceClient = new BlobServiceClient(connectionString);
            var containerClient = blobServiceClient.GetBlobContainerClient(unzipMessage.ContainerId);

            var queueClient = new QueueClient(connectionString, "log-analysis", new QueueClientOptions(){MessageEncoding = QueueMessageEncoding.Base64});
            queueClient.CreateIfNotExists();

            await foreach (var blobItem in containerClient.GetBlobsAsync())
            {
                if (blobItem.Name.EndsWith(".zip"))
                {
                    var unzippedFiles = await UnzipAndUploadBlob(containerClient, blobItem.Name);

                    foreach (var unzippedFile in unzippedFiles)
                    {
                        await queueClient.SendMessageAsync(JsonSerializer.Serialize(new LogAnalysisMessage 
                            { ContainerId = unzipMessage.ContainerId, BlobName = unzippedFile, PartitionKey = report.CreationDay}));
                    }
                }
            }
        }

        private async Task<List<string>> UnzipAndUploadBlob(BlobContainerClient containerClient, string blobName)
        {
            var blobClient = containerClient.GetBlobClient(blobName);

            BlobDownloadInfo blobDownloadInfo = await blobClient.DownloadAsync();
            using var memoryStream = new MemoryStream();
            await blobDownloadInfo.Content.CopyToAsync(memoryStream);

            // Create a temporary directory to unzip the content
            var tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDirectory);

            // Unzip the content
            using (var zipArchive = new ZipArchive(memoryStream, ZipArchiveMode.Read))
            {
                foreach (var entry in zipArchive.Entries)
                {
                    var destinationPath = Path.Combine(tempDirectory, _destinationDirectory, entry.FullName);
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));

                    await using var entryStream = entry.Open();
                    await using var fileStream = new FileStream(destinationPath, FileMode.Create);
                    await entryStream.CopyToAsync(fileStream);
                }
            }

            var outputFiles = new List<string>();

            // Upload unzipped content to the same container in a new directory
            foreach (var filePath in Directory.GetFiles(Path.Combine(tempDirectory, _destinationDirectory)))
            {
                var destinationBlobName = $"unzipped/{Path.GetFileName(filePath)}";
                var destinationBlobClient = containerClient.GetBlobClient(destinationBlobName);
                await using var fs = File.OpenRead(filePath);
                await destinationBlobClient.UploadAsync(fs, true);

                outputFiles.Add(destinationBlobName);
            }

            // Clean up: Delete the temporary directory
            Directory.Delete(tempDirectory, true);

            return outputFiles;
        }

        private async Task<Report> SaveCosmosDbDocument(string containerId)
        {
            var keyVaultName = Environment.GetEnvironmentVariable("KeyVaultName");
            var kvUri = "https://" + keyVaultName + ".vault.azure.net";
            var client = new SecretClient(new Uri(kvUri), new DefaultAzureCredential());

            var connectionString = (await client.GetSecretAsync("database-key")).Value.Value;

            var cosmosClient = new CosmosClient(connectionString, new CosmosClientOptions()
                {
                    ApplicationRegion = Regions.NorthEurope,
                });

            var container = cosmosClient.GetContainer("feedback", "reports");

            var report = new Report() { id = containerId, Status = ReportStatus.Created, CreationDay = GetCreationDay() };

            return await container.CreateItemAsync(
                item: report,
                partitionKey: new PartitionKey(report.CreationDay)
            );
        }

        private int GetCreationDay()
        {
            var dateTime = DateTime.Now;
            var calendar = CultureInfo.InvariantCulture.Calendar;

            var year = calendar.GetYear(dateTime);
            var month = calendar.GetMonth(dateTime);
            var day = calendar.GetDayOfMonth(dateTime);
            return year * 10000 + month * 100 + day; // YYYYMMDD
        }
    }

    public enum ReportStatus
    {
        Unknown,
        Created,
        Analyzed
    }

    public class Report
    {
        public string id { get; set; }
        public ReportStatus Status { get; set; }
        public int CreationDay { get; set; }
    }
}

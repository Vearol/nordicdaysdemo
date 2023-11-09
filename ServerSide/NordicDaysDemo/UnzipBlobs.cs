using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Azure.Messaging.ServiceBus;

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
            
            var storageConnStr = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            var blobStorageClient = new BlobServiceClient(storageConnStr);
            var containerClient = blobStorageClient.GetBlobContainerClient(unzipMessage.ContainerId);

            var serviceBusConnStr = Environment.GetEnvironmentVariable("ServiceBusConnection");
            var serviceBusClient = new ServiceBusClient(serviceBusConnStr);
            var queue = serviceBusClient.CreateSender("log-analysis");

            await foreach (var blobItem in containerClient.GetBlobsAsync())
            {
                if (blobItem.Name.EndsWith(".zip"))
                {
                    var unzippedFiles = await UnzipAndUploadBlob(containerClient, blobItem.Name);

                    foreach (var unzippedFile in unzippedFiles)
                    {
                        await queue.SendMessageAsync(new ServiceBusMessage(JsonSerializer.Serialize(new LogAnalysisMessage
                            { ContainerId = unzipMessage.ContainerId, BlobName = unzippedFile })));
                    }
                }
                else
                {
                    await queue.SendMessageAsync(new ServiceBusMessage(JsonSerializer.Serialize(new LogAnalysisMessage
                        { ContainerId = unzipMessage.ContainerId, BlobName = blobItem.Name })));
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
                var destinationBlobName = $"{_destinationDirectory}/{Path.GetFileName(filePath)}";
                var destinationBlobClient = containerClient.GetBlobClient(destinationBlobName);
                await using var fs = File.OpenRead(filePath);
                await destinationBlobClient.UploadAsync(fs, true);

                outputFiles.Add(destinationBlobName);
            }

            // Clean up: Delete the temporary directory
            Directory.Delete(tempDirectory, true);

            return outputFiles;
        }
    }
}

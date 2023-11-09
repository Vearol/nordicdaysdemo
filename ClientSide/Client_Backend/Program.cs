using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

var app = builder.Build();

app.UseCors(builder => builder
    .AllowAnyOrigin()
    .AllowAnyMethod()
    .AllowAnyHeader());

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => "Hello World!");

app.MapPost("/upload", async (IFormFile file) => {

    if (file == null || file.Length == 0)
        return "File not selected";

    // upload file to the storage
    var containerId = Guid.NewGuid().ToString();

    var serviceClient = new BlobServiceClient(builder.Configuration["AzureStorageConnectionString"]);
    var containerClient = serviceClient.GetBlobContainerClient(containerId);
    await containerClient.CreateAsync();

    var blobClient = containerClient.GetBlobClient(file.FileName);
    await blobClient.UploadAsync(file.OpenReadStream(), true);

    // send message to the queue
    await using var serviceBusClient = new ServiceBusClient(builder.Configuration["AzureServiceBusConnectionString"]);
    var serviceBusSender = serviceBusClient.CreateSender("blob-unzip");

    var serviceBusMessagePayload = new { ContainerId = containerId };
    var serviceBusMessagePayloadJson = JsonSerializer.Serialize(serviceBusMessagePayload);

    var serviceBusMessage = new ServiceBusMessage(serviceBusMessagePayloadJson)
    {
        ContentType = "application/json"
    };

    await serviceBusSender.SendMessageAsync(serviceBusMessage);
    
    return "File uploaded successfully!";
}).RequireAuthorization();

app.Run();

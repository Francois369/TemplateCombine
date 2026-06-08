using System;
using Azure.Data.Tables;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TemplateCombine.Services;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

// Register Azure clients
var configuration = builder.Configuration;
var blobConnection = configuration["AzureWebJobsStorage"] ?? Environment.GetEnvironmentVariable("AzureWebJobsStorage");
var serviceBusConnection = configuration["ServiceBusConnectionString"] ?? Environment.GetEnvironmentVariable("ServiceBusConnectionString");
var trackingStorageConnection = configuration["TrackingStorageConnectionString"] ?? Environment.GetEnvironmentVariable("TrackingStorageConnectionString");
var trackingTableName = configuration["TrackingTableName"] ?? Environment.GetEnvironmentVariable("TrackingTableName") ?? "DocumentWorkflowTracking";

if (!string.IsNullOrEmpty(blobConnection))
{
    builder.Services.AddSingleton(new BlobServiceClient(blobConnection));
}

if (!string.IsNullOrEmpty(serviceBusConnection))
{
    builder.Services.AddSingleton(new ServiceBusClient(serviceBusConnection));
}

if (!string.IsNullOrWhiteSpace(trackingStorageConnection))
{
    builder.Services.AddSingleton(new TableServiceClient(trackingStorageConnection));
    builder.Services.AddSingleton<IWorkflowTrackingService>(serviceProvider =>
        new TableWorkflowTrackingService(
            serviceProvider.GetRequiredService<TableServiceClient>().GetTableClient(trackingTableName),
            serviceProvider.GetRequiredService<ILogger<TableWorkflowTrackingService>>()));
}
else
{
    builder.Services.AddSingleton<IWorkflowTrackingService, NullWorkflowTrackingService>();
}

builder.Build().Run();

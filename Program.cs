using System;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

// Register Azure clients
var configuration = builder.Configuration;
var blobConnection = configuration["AzureWebJobsStorage"] ?? Environment.GetEnvironmentVariable("AzureWebJobsStorage");
var serviceBusConnection = configuration["ServiceBusConnectionString"] ?? Environment.GetEnvironmentVariable("ServiceBusConnectionString");

if (!string.IsNullOrEmpty(blobConnection))
{
    builder.Services.AddSingleton(new BlobServiceClient(blobConnection));
}

if (!string.IsNullOrEmpty(serviceBusConnection))
{
    builder.Services.AddSingleton(new ServiceBusClient(serviceBusConnection));
}

builder.Build().Run();

using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Xsl;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace TemplateCombine;

public class Function1
{
    private readonly ILogger<Function1> _logger;
    private readonly BlobServiceClient _blobServiceClient;
    private readonly ServiceBusClient _serviceBusClient;

    public Function1(ILogger<Function1> logger, BlobServiceClient blobServiceClient, ServiceBusClient serviceBusClient)
    {
        _logger = logger;
        _blobServiceClient = blobServiceClient;
        _serviceBusClient = serviceBusClient;
    }

    [Function("DocumentProcessor")]
    public async Task Run(
        [ServiceBusTrigger("%ServiceBusQueueName%", Connection = "ServiceBusConnectionString")] ServiceBusReceivedMessage message)
    {
        var request = JsonSerializer.Deserialize<ProcessingRequest>(message.Body);
        if (request is null || string.IsNullOrWhiteSpace(request.BlobName))
        {
            _logger.LogError("Invalid Service Bus message body. Expected blob processing request payload.");
            return;
        }

        var inputContainerName = string.IsNullOrWhiteSpace(request.InputContainer) ? "input" : request.InputContainer;
        var templateName = string.IsNullOrWhiteSpace(request.TemplateName)
            ? (Environment.GetEnvironmentVariable("TemplateBlobName") ?? "default.xslt")
            : request.TemplateName;
        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? message.CorrelationId
            : request.CorrelationId;

        _logger.LogInformation(
            "Service Bus message received for blob {blobName} with correlation {correlationId}",
            request.BlobName,
            correlationId);

        try
        {
            var inputContainer = _blobServiceClient.GetBlobContainerClient(inputContainerName);
            var inputBlob = inputContainer.GetBlobClient(request.BlobName);

            if (!await inputBlob.ExistsAsync())
            {
                _logger.LogError("Input blob '{blobName}' not found in container '{container}'.", request.BlobName, inputContainerName);
                return;
            }

            var templatesContainer = _blobServiceClient.GetBlobContainerClient("templates");
            var templateBlob = templatesContainer.GetBlobClient(templateName);

            if (!await templateBlob.ExistsAsync())
            {
                _logger.LogError("Template blob '{templateName}' not found in 'templates' container.", templateName);
                return;
            }

            await using var inputStream = new MemoryStream();
            await inputBlob.DownloadToAsync(inputStream);
            inputStream.Position = 0;

            using var inputReader = XmlReader.Create(inputStream);

            await using var templateStream = new MemoryStream();
            await templateBlob.DownloadToAsync(templateStream);
            templateStream.Position = 0;

            var xslt = new XslCompiledTransform();
            using (var templateReader = XmlReader.Create(templateStream))
            {
                xslt.Load(templateReader);
            }

            await using var outputStream = new MemoryStream();
            using (var writer = XmlWriter.Create(outputStream, xslt.OutputSettings))
            {
                xslt.Transform(inputReader, writer);
            }

            outputStream.Position = 0;

            var outputContainer = _blobServiceClient.GetBlobContainerClient("output");
            await outputContainer.CreateIfNotExistsAsync();

            var outputBlob = outputContainer.GetBlobClient(request.BlobName);
            await outputBlob.UploadAsync(outputStream, overwrite: true);

            _logger.LogInformation("Transformed blob uploaded to output container: {blobName}", request.BlobName);

            var statusEntity = Environment.GetEnvironmentVariable("ServiceBusStatusQueueName");
            if (!string.IsNullOrWhiteSpace(statusEntity))
            {
                await using var sender = _serviceBusClient.CreateSender(statusEntity);
                var messagePayload = new ProcessingResult(
                    CorrelationId: correlationId,
                    BlobName: request.BlobName,
                    InputContainer: inputContainerName,
                    OutputContainer: "output",
                    TemplateName: templateName,
                    Status: "Completed",
                    Timestamp: DateTimeOffset.UtcNow);

                var messageBody = JsonSerializer.Serialize(messagePayload);
                var statusMessage = new ServiceBusMessage(messageBody)
                {
                    ContentType = "application/json",
                    CorrelationId = correlationId,
                    Subject = "DocumentProcessed"
                };

                await sender.SendMessageAsync(statusMessage);
                _logger.LogInformation("Sent completion message for blob {blobName} to {entity}", request.BlobName, statusEntity);
            }
            else
            {
                _logger.LogInformation("No ServiceBusStatusQueueName configured; skipping completion notification.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing blob {blobName}", request.BlobName);
            throw;
        }
    }

    private sealed record ProcessingRequest(
        string BlobName,
        string? InputContainer,
        string? TemplateName,
        string? CorrelationId);

    private sealed record ProcessingResult(
        string? CorrelationId,
        string BlobName,
        string InputContainer,
        string OutputContainer,
        string TemplateName,
        string Status,
        DateTimeOffset Timestamp);
}



using System.Text.Json;
using System.Xml;
using System.Xml.Xsl;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TemplateCombine.Models;

namespace TemplateCombine;

public sealed class DocumentProcessorFunction
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<DocumentProcessorFunction> _logger;
    private readonly BlobServiceClient _blobServiceClient;
    private readonly ServiceBusClient _serviceBusClient;
    private readonly string _defaultInputContainer;
    private readonly string _defaultTemplateContainer;
    private readonly string _defaultOutputContainer;
    private readonly string _defaultTemplateBlobName;
    private readonly string? _statusQueueName;

    public DocumentProcessorFunction(
        ILogger<DocumentProcessorFunction> logger,
        BlobServiceClient blobServiceClient,
        ServiceBusClient serviceBusClient,
        IConfiguration configuration)
    {
        _logger = logger;
        _blobServiceClient = blobServiceClient;
        _serviceBusClient = serviceBusClient;
        _defaultInputContainer = configuration["InputContainerName"] ?? "input";
        _defaultTemplateContainer = configuration["TemplateContainerName"] ?? "template";
        _defaultOutputContainer = configuration["OutputContainerName"] ?? "output";
        _defaultTemplateBlobName = configuration["TemplateBlobName"] ?? "default.xslt";
        _statusQueueName = configuration["ServiceBusStatusQueueName"];
    }

    [Function("DocumentProcessor")]
    public async Task ProcessAsync(
        [ServiceBusTrigger("%ServiceBusQueueName%", Connection = "ServiceBusConnectionString")]
        ServiceBusReceivedMessage message)
    {
        var request = DeserializeRequest(message);
        if (request is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(request.BlobName))
        {
            _logger.LogError("Invalid processing request. 'blobName' is required.");
            await PublishStatusMessageAsync(
                CreateStatusMessage(request, message, "Failed", errorMessage: "blobName is required"));
            return;
        }

        var inputContainerName = ResolveValue(request.InputContainer, _defaultInputContainer);
        var templateContainerName = ResolveValue(request.TemplateContainer, _defaultTemplateContainer);
        var outputContainerName = ResolveValue(request.OutputContainer, _defaultOutputContainer);
        var templateBlobName = ResolveValue(request.TemplateBlobName, _defaultTemplateBlobName);
        var workflowId = ResolveIdentifier(request.WorkflowId, GetApplicationProperty(message, "workflowId"), message.MessageId);
        var correlationId = ResolveIdentifier(request.CorrelationId, message.CorrelationId, workflowId);

        _logger.LogInformation(
            "Starting document processing. WorkflowId={workflowId}, CorrelationId={correlationId}, Blob={blobName}, InputContainer={inputContainer}, Template={templateBlobName}, TemplateContainer={templateContainer}, OutputContainer={outputContainer}",
            workflowId,
            correlationId,
            request.BlobName,
            inputContainerName,
            templateBlobName,
            templateContainerName,
            outputContainerName);

        try
        {
            var inputBlob = _blobServiceClient
                .GetBlobContainerClient(inputContainerName)
                .GetBlobClient(request.BlobName);

            if (!await inputBlob.ExistsAsync())
            {
                var errorMessage = $"Input blob '{request.BlobName}' was not found in container '{inputContainerName}'.";
                _logger.LogError(errorMessage);
                await PublishStatusMessageAsync(
                    CreateStatusMessage(request, message, "Failed", workflowId, correlationId, inputContainerName, templateContainerName, outputContainerName, templateBlobName, errorMessage));
                return;
            }

            var templateBlob = _blobServiceClient
                .GetBlobContainerClient(templateContainerName)
                .GetBlobClient(templateBlobName);

            if (!await templateBlob.ExistsAsync())
            {
                var errorMessage = $"Template blob '{templateBlobName}' was not found in container '{templateContainerName}'.";
                _logger.LogError(errorMessage);
                await PublishStatusMessageAsync(
                    CreateStatusMessage(request, message, "Failed", workflowId, correlationId, inputContainerName, templateContainerName, outputContainerName, templateBlobName, errorMessage));
                return;
            }

            await using var inputStream = new MemoryStream();
            await inputBlob.DownloadToAsync(inputStream);
            inputStream.Position = 0;

            await using var templateStream = new MemoryStream();
            await templateBlob.DownloadToAsync(templateStream);
            templateStream.Position = 0;

            var xslt = new XslCompiledTransform();
            using (var templateReader = XmlReader.Create(templateStream))
            {
                xslt.Load(templateReader);
            }

            await using var outputStream = new MemoryStream();
            using (var inputReader = XmlReader.Create(inputStream))
            using (var writer = XmlWriter.Create(outputStream, xslt.OutputSettings))
            {
                xslt.Transform(inputReader, writer);
            }

            outputStream.Position = 0;

            var outputContainer = _blobServiceClient.GetBlobContainerClient(outputContainerName);
            await outputContainer.CreateIfNotExistsAsync();

            var outputBlob = outputContainer.GetBlobClient(request.BlobName);
            await outputBlob.UploadAsync(outputStream, overwrite: true);

            var metadata = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(workflowId))
            {
                metadata["workflowId"] = workflowId;
            }

            if (!string.IsNullOrWhiteSpace(correlationId))
            {
                metadata["correlationId"] = correlationId;
            }

            if (metadata.Count > 0)
            {
                await outputBlob.SetMetadataAsync(metadata);
            }

            _logger.LogInformation(
                "Completed document processing. WorkflowId={workflowId}, CorrelationId={correlationId}, OutputBlob={blobName}, OutputContainer={outputContainer}",
                workflowId,
                correlationId,
                request.BlobName,
                outputContainerName);

            await PublishStatusMessageAsync(
                CreateStatusMessage(request, message, "Completed", workflowId, correlationId, inputContainerName, templateContainerName, outputContainerName, templateBlobName));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing blob {blobName}.", request.BlobName);
            await PublishStatusMessageAsync(
                CreateStatusMessage(request, message, "Failed", workflowId, correlationId, inputContainerName, templateContainerName, outputContainerName, templateBlobName, ex.Message));
            throw;
        }
    }

    private DocumentProcessingRequest? DeserializeRequest(ServiceBusReceivedMessage message)
    {
        try
        {
            return message.Body.ToObjectFromJson<DocumentProcessingRequest>(SerializerOptions);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            _logger.LogError(ex, "Invalid Service Bus request payload. Expected JSON matching DocumentProcessingRequest schema.");
            return null;
        }
    }

    private async Task PublishStatusMessageAsync(DocumentProcessingStatus statusMessage)
    {
        if (string.IsNullOrWhiteSpace(_statusQueueName))
        {
            _logger.LogDebug("ServiceBusStatusQueueName not configured. Skipping status message for blob {blobName}.", statusMessage.BlobName);
            return;
        }

        await using var sender = _serviceBusClient.CreateSender(_statusQueueName);
        var busMessage = new ServiceBusMessage(JsonSerializer.Serialize(statusMessage, SerializerOptions))
        {
            ContentType = "application/json",
            Subject = "DocumentProcessingStatus",
            CorrelationId = statusMessage.CorrelationId
        };

        if (!string.IsNullOrWhiteSpace(statusMessage.WorkflowId))
        {
            busMessage.ApplicationProperties["workflowId"] = statusMessage.WorkflowId;
        }

        await sender.SendMessageAsync(busMessage);

        _logger.LogInformation(
            "Published processing status '{status}' for blob {blobName} to {queueName}.",
            statusMessage.Status,
            statusMessage.BlobName,
            _statusQueueName);
    }

    private static DocumentProcessingStatus CreateStatusMessage(
        DocumentProcessingRequest request,
        ServiceBusReceivedMessage sourceMessage,
        string status,
        string? workflowId = null,
        string? correlationId = null,
        string? inputContainer = null,
        string? templateContainer = null,
        string? outputContainer = null,
        string? templateBlobName = null,
        string? errorMessage = null)
    {
        var resolvedWorkflowId = ResolveIdentifier(workflowId, request.WorkflowId, GetApplicationProperty(sourceMessage, "workflowId"), sourceMessage.MessageId);
        var resolvedCorrelationId = ResolveIdentifier(correlationId, request.CorrelationId, sourceMessage.CorrelationId, resolvedWorkflowId);

        return new DocumentProcessingStatus(
            Status: status,
            BlobName: request.BlobName ?? string.Empty,
            InputContainer: inputContainer ?? request.InputContainer ?? "input",
            TemplateBlobName: templateBlobName ?? request.TemplateBlobName ?? string.Empty,
            TemplateContainer: templateContainer ?? request.TemplateContainer ?? "template",
            OutputContainer: outputContainer ?? request.OutputContainer ?? "output",
            WorkflowId: resolvedWorkflowId,
            CorrelationId: resolvedCorrelationId,
            ProcessedAtUtc: DateTimeOffset.UtcNow,
            ErrorMessage: errorMessage);
    }

    private static string ResolveValue(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string? ResolveIdentifier(params string?[] candidates) =>
        candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate))?.Trim();

    private static string? GetApplicationProperty(ServiceBusReceivedMessage message, string key)
    {
        if (!message.ApplicationProperties.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return Convert.ToString(value);
    }
}

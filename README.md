# TemplateCombine

`TemplateCombine` is a .NET 8 Azure Functions isolated worker app that processes XML documents with XSLT using a Service Bus queue trigger.

## Required Azure resources

1. **Azure Storage Account** with blob containers:
   - `input`
   - `template`
   - `output`
2. **Azure Service Bus namespace** with:
   - Processing queue (example: `document-processing`)
   - Optional status queue (example: `document-processing-status`)
3. **Function App** hosting this project.

## Processing flow

1. Upstream system uploads source XML into `input`.
2. Upstream system sends a Service Bus message to `%ServiceBusQueueName%` with blob + template references.
3. `DocumentProcessor` function:
   - Deserializes and validates the request payload.
   - Reads source XML from `input` by default.
   - Reads XSLT from `template` by default.
   - Transforms XML and writes result to `output`.
   - Preserves workflow/correlation IDs in logs, output blob metadata, and optional status message.
4. If `%ServiceBusStatusQueueName%` is configured, the function emits a completion/failure status message.

## Service Bus request contract

Expected JSON payload:

```json
{
  "workflowId": "8c4d0f4d-2f40-4a46-a7f8-3b783e0a4f25",
  "correlationId": "8c4d0f4d-2f40-4a46-a7f8-3b783e0a4f25",
  "blobName": "requests/request-001.xml",
  "inputContainer": "input",
  "templateBlobName": "default.xslt",
  "templateContainer": "template",
  "outputContainer": "output"
}
```

### Request fields

- `blobName` (**required**): input blob path/name.
- `workflowId` (optional): workflow tracking identifier.
- `correlationId` (optional): correlation identifier for logs/status.
- `inputContainer` (optional): defaults to app setting `InputContainerName` or `input`.
- `templateBlobName` (optional): defaults to app setting `TemplateBlobName` or `default.xslt`.
- `templateContainer` (optional): defaults to app setting `TemplateContainerName` or `template`.
- `outputContainer` (optional): defaults to app setting `OutputContainerName` or `output`.

## Status/completion contract

When status queue is configured, the function publishes JSON payloads with:

- `status` (`Completed` or `Failed`)
- `workflowId`
- `correlationId`
- `blobName`
- `inputContainer`
- `templateBlobName`
- `templateContainer`
- `outputContainer`
- `processedAtUtc`
- `errorMessage` (only for failures)

## Configuration

Set these values in `local.settings.json` (local) or App Settings (Azure).

### Required

- `AzureWebJobsStorage`
- `ServiceBusConnectionString`
- `ServiceBusQueueName`

### Optional

- `ServiceBusStatusQueueName`
- `InputContainerName` (default: `input`)
- `TemplateContainerName` (default: `template`)
- `OutputContainerName` (default: `output`)
- `TemplateBlobName` (default: `default.xslt`)

### Example `local.settings.json`

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "ServiceBusConnectionString": "<your-service-bus-connection-string>",
    "ServiceBusQueueName": "document-processing",
    "ServiceBusStatusQueueName": "document-processing-status",
    "InputContainerName": "input",
    "TemplateContainerName": "template",
    "OutputContainerName": "output",
    "TemplateBlobName": "default.xslt"
  }
}
```

## Build

```bash
dotnet build
dotnet test
```

## Main files

- `DocumentProcessorFunction.cs` - Service Bus-triggered XML processing function.
- `Models/DocumentProcessingRequest.cs` - request contract.
- `Models/DocumentProcessingStatus.cs` - completion/failure status contract.
- `Program.cs` - startup and dependency registration.

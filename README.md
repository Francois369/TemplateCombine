# TemplateCombine

`TemplateCombine` is a .NET 8 Azure Functions isolated worker app that processes XML documents with XSLT using a Service Bus queue trigger.

## Required Azure resources

1. **Azure Storage Account** with blob containers:
   - `input`
   - `template`
   - `output`
2. **Azure Table Storage account** with table:
   - `DocumentWorkflowTracking`
3. **Azure Service Bus namespace** with:
   - Processing queue (example: `document-processing`)
   - Optional status queue (example: `document-processing-status`)
4. **Function App** hosting this project.

## Processing flow

1. Upstream system uploads source XML into `input`.
2. Upstream system sends a Service Bus message to `%ServiceBusQueueName%` with blob + template references.
3. `DocumentProcessor` function:
   - Deserializes and validates the request payload.
   - Resolves a workflow ID from the request, Service Bus properties, correlation ID, or message ID when upstream does not provide one.
   - Creates or upserts an Azure Table Storage tracking row in `DocumentWorkflowTracking`.
   - Marks the workflow `Queued`, then `Processing`.
   - Reads source XML from `input` by default.
   - Reads XSLT from `template` by default.
   - Transforms XML and writes result to `output`.
   - Marks the workflow `Completed` on success or `Failed` on validation/processing errors.
   - Persists workflow/correlation IDs in logs, output blob metadata, optional status messages, and Azure Table Storage tracking.
4. If `%ServiceBusStatusQueueName%` is configured, the function emits a completion/failure status message.

## Service Bus request contract

Expected JSON payload:

```json
{
  "workflowId": "8c4d0f4d-2f40-4a46-a7f8-3b783e0a4f25",
  "correlationId": "a11c1f03-7569-4f84-b0d0-a2b18ea15af5",
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

## Azure Table workflow tracking

Each workflow is stored as a row in Azure Table Storage with status values:

- `Queued` - the function accepted the Service Bus message and created or refreshed the tracking row.
- `Processing` - the function is actively loading blobs or transforming the document.
- `Completed` - the output blob was written successfully and processing finished.
- `Failed` - validation, blob lookup, or transformation failed. `ErrorMessage` is populated when available.

Tracked columns include, where available:

- `WorkflowId`
- `CorrelationId`
- `Status`
- `InputContainer` / `InputBlobName`
- `TemplateContainer` / `TemplateBlobName`
- `OutputContainer` / `OutputBlobName`
- `ServiceBusMessageId`
- `CreatedAtUtc`
- `UpdatedAtUtc`
- `CompletedAtUtc`
- `ErrorMessage`

### Workflow ID determination

If upstream does not supply `workflowId`, the function derives one in this order:

1. Request payload `workflowId`
2. Service Bus application property `workflowId`
3. Request payload `correlationId`
4. Service Bus application property `correlationId`
5. Service Bus `CorrelationId`
6. Service Bus `MessageId`
7. Deterministic SHA-256 hash of the Service Bus message body

That keeps tracking stable even when upstream does not yet provide a dedicated workflow ID.

## Configuration

Set these values in `local.settings.json` (local) or App Settings (Azure).

### Required

- `AzureWebJobsStorage`
- `TrackingStorageConnectionString`
- `ServiceBusConnectionString`
- `ServiceBusQueueName`

### Optional

- `TrackingTableName` (default and recommended: `DocumentWorkflowTracking`)
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
    "TrackingStorageConnectionString": "<your-tracking-storage-connection-string>",
    "TrackingTableName": "DocumentWorkflowTracking",
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
- `Models/WorkflowTrackingEntity.cs` - Azure Table Storage entity for workflow tracking.
- `Services/TableWorkflowTrackingService.cs` - Azure Table Storage workflow tracking implementation.
- `Program.cs` - startup and dependency registration.

# TemplateCombine

`TemplateCombine` is a .NET 8 Azure Functions isolated worker app that processes XML documents using a Service Bus-driven workflow.

## Overview

The function is triggered by a Service Bus message, reads an XML document from Blob Storage, applies an XSLT template from a `templates` container, writes the transformed result to an `output` container, and can publish a completion message back to Service Bus.

## Processing Flow

1. A client uploads an XML file to the `input` blob container.
2. The client sends a Service Bus message to the processing queue.
3. The `DocumentProcessor` function receives the message.
4. The function reads the source XML from Blob Storage.
5. The function loads the XSLT template from the `templates` container.
6. The function transforms the XML.
7. The transformed XML is written to the `output` container.
8. An optional completion message is sent to a status queue.

## Service Bus Request Message

The processing queue expects a JSON payload like this:

```json
{
  "blobName": "document1.xml",
  "inputContainer": "input",
  "templateName": "default.xslt",
  "correlationId": "abc-123"
}
```

### Request Fields

- `blobName` - required; name of the source blob to process.
- `inputContainer` - optional; defaults to `input`.
- `templateName` - optional; defaults to `TemplateBlobName` or `default.xslt`.
- `correlationId` - optional; used for end-to-end tracking.

## Completion Message

If `ServiceBusStatusQueueName` is configured, the function sends a completion message containing:

- `correlationId`
- `blobName`
- `inputContainer`
- `outputContainer`
- `templateName`
- `status`
- `timestamp`

## Storage Containers

Create these blob containers in your storage account:

- `input`
- `output`
- `templates`

## Configuration

Configure these settings in `local.settings.json` for local development or in Azure App Settings when deployed.

### Required

- `AzureWebJobsStorage`
- `ServiceBusConnectionString`
- `ServiceBusQueueName`

### Optional

- `ServiceBusStatusQueueName`
- `TemplateBlobName`

## Example `local.settings.json`

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "ServiceBusConnectionString": "<your-service-bus-connection-string>",
    "ServiceBusQueueName": "document-processing",
    "ServiceBusStatusQueueName": "document-processing-status",
    "TemplateBlobName": "default.xslt"
  }
}
```

## Running Locally

1. Ensure Azure Storage and Service Bus are available.
2. Create the `input`, `output`, and `templates` containers.
3. Upload an XSLT file such as `default.xslt` to `templates`.
4. Start the function app.
5. Upload an XML file to `input`.
6. Send a Service Bus message with the processing request JSON.

## Build

```powershell
dotnet build
```

## Notes

- The current implementation uses `XslCompiledTransform`, which supports XSLT 1.0.
- Service Bus is the primary workflow trigger and the preferred place for correlation and tracking.
- Blob Storage is used for document payload storage.

## Main Files

- `Function1.cs` - Service Bus-triggered XML processing function.
- `Program.cs` - application startup and dependency registration.
- `TemplateCombine.csproj` - project dependencies.

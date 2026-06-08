using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using TemplateCombine.Models;

namespace TemplateCombine.Services;

public sealed class TableWorkflowTrackingService : IWorkflowTrackingService
{
    private readonly TableClient _tableClient;
    private readonly ILogger<TableWorkflowTrackingService> _logger;

    public TableWorkflowTrackingService(TableClient tableClient, ILogger<TableWorkflowTrackingService> logger)
    {
        _tableClient = tableClient;
        _logger = logger;
    }

    public async Task TrackAsync(
        WorkflowTrackingContext context,
        WorkflowTrackingStatus status,
        string? errorMessage = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        await _tableClient.CreateIfNotExistsAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var rowKey = WorkflowTrackingEntity.CreateRowKey(context.WorkflowId);
        NullableResponse<WorkflowTrackingEntity> existingEntity;

        try
        {
            existingEntity = await _tableClient.GetEntityIfExistsAsync<WorkflowTrackingEntity>(
                WorkflowTrackingEntity.WorkflowPartitionKey,
                rowKey,
                cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Failed to load workflow tracking entity for workflow {workflowId}.", context.WorkflowId);
            throw;
        }

        WorkflowTrackingEntity entity;

        if (existingEntity.HasValue && existingEntity.Value is not null)
        {
            entity = existingEntity.Value;
        }
        else
        {
            entity = new WorkflowTrackingEntity(context.WorkflowId)
            {
                CreatedAtUtc = now
            };
        }

        entity.WorkflowId = context.WorkflowId;
        entity.CorrelationId = Normalize(context.CorrelationId);
        entity.Status = status.ToString();
        entity.InputContainer = Normalize(context.InputContainer);
        entity.InputBlobName = Normalize(context.InputBlobName);
        entity.TemplateContainer = Normalize(context.TemplateContainer);
        entity.TemplateBlobName = Normalize(context.TemplateBlobName);
        entity.OutputContainer = Normalize(context.OutputContainer);
        entity.OutputBlobName = Normalize(context.OutputBlobName);
        entity.ServiceBusMessageId = Normalize(context.ServiceBusMessageId);
        entity.UpdatedAtUtc = now;
        entity.CompletedAtUtc = status is WorkflowTrackingStatus.Completed or WorkflowTrackingStatus.Failed ? now : null;
        entity.ErrorMessage = status == WorkflowTrackingStatus.Failed ? Normalize(errorMessage) : null;

        if (entity.CreatedAtUtc == default)
        {
            entity.CreatedAtUtc = now;
        }

        try
        {
            await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(
                ex,
                "Failed to upsert workflow tracking entity for workflow {workflowId} with status {status}.",
                context.WorkflowId,
                status);
            throw;
        }
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

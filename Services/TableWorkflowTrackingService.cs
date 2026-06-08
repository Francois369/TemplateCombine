using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using TemplateCombine.Models;

namespace TemplateCombine.Services;

public sealed class TableWorkflowTrackingService : IWorkflowTrackingService
{
    private readonly TableClient _tableClient;
    private readonly ILogger<TableWorkflowTrackingService> _logger;
    private readonly SemaphoreSlim _tableInitializationLock = new(1, 1);
    private bool _tableInitialized;

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

        await EnsureTableExistsAsync(cancellationToken);

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

        if (existingEntity.HasValue)
        {
            entity = existingEntity.Value
                ?? throw new InvalidOperationException($"Workflow tracking entity for '{context.WorkflowId}' was expected but not returned.");
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

    private async Task EnsureTableExistsAsync(CancellationToken cancellationToken)
    {
        if (_tableInitialized)
        {
            return;
        }

        await _tableInitializationLock.WaitAsync(cancellationToken);

        try
        {
            if (_tableInitialized)
            {
                return;
            }

            await _tableClient.CreateIfNotExistsAsync(cancellationToken);
            _tableInitialized = true;
        }
        finally
        {
            _tableInitializationLock.Release();
        }
    }
}

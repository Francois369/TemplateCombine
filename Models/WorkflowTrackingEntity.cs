using System.Text;
using Azure;
using Azure.Data.Tables;

namespace TemplateCombine.Models;

public sealed class WorkflowTrackingEntity : ITableEntity
{
    public const string WorkflowPartitionKey = "workflow";

    public WorkflowTrackingEntity()
    {
    }

    public WorkflowTrackingEntity(string workflowId)
    {
        WorkflowId = workflowId;
        PartitionKey = WorkflowPartitionKey;
        RowKey = CreateRowKey(workflowId);
    }

    public string PartitionKey { get; set; } = WorkflowPartitionKey;

    public string RowKey { get; set; } = string.Empty;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string WorkflowId { get; set; } = string.Empty;

    public string? CorrelationId { get; set; }

    public string? Status { get; set; }

    public string? InputContainer { get; set; }

    public string? InputBlobName { get; set; }

    public string? TemplateContainer { get; set; }

    public string? TemplateBlobName { get; set; }

    public string? OutputContainer { get; set; }

    public string? OutputBlobName { get; set; }

    public string? ServiceBusMessageId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public string? ErrorMessage { get; set; }

    public static string CreateRowKey(string workflowId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);

        var bytes = Encoding.UTF8.GetBytes(workflowId.Trim());
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

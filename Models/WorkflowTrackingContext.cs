namespace TemplateCombine.Models;

public sealed record WorkflowTrackingContext(
    string WorkflowId,
    string? CorrelationId,
    string? InputContainer,
    string? InputBlobName,
    string? TemplateContainer,
    string? TemplateBlobName,
    string? OutputContainer,
    string? OutputBlobName,
    string? ServiceBusMessageId);

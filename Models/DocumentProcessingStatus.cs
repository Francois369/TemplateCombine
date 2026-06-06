namespace TemplateCombine.Models;

public sealed record DocumentProcessingStatus(
    string Status,
    string BlobName,
    string InputContainer,
    string TemplateBlobName,
    string TemplateContainer,
    string OutputContainer,
    string? WorkflowId,
    string? CorrelationId,
    DateTimeOffset ProcessedAtUtc,
    string? ErrorMessage = null);

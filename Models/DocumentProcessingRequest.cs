namespace TemplateCombine.Models;

public sealed record DocumentProcessingRequest
{
    public string? WorkflowId { get; init; }

    public string? CorrelationId { get; init; }

    public string? BlobName { get; init; }

    public string? InputContainer { get; init; }

    public string? TemplateBlobName { get; init; }

    public string? TemplateContainer { get; init; }

    public string? OutputContainer { get; init; }
}

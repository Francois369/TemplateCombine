using TemplateCombine.Models;

namespace TemplateCombine.Services;

public sealed class NullWorkflowTrackingService : IWorkflowTrackingService
{
    public Task TrackAsync(
        WorkflowTrackingContext context,
        WorkflowTrackingStatus status,
        string? errorMessage = null,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

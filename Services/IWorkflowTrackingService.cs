using TemplateCombine.Models;

namespace TemplateCombine.Services;

public interface IWorkflowTrackingService
{
    Task TrackAsync(
        WorkflowTrackingContext context,
        WorkflowTrackingStatus status,
        string? errorMessage = null,
        CancellationToken cancellationToken = default);
}

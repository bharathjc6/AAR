// =============================================================================
// AAR.Application - Interfaces/IAnalysisHubNotifier.cs
// Interface for sending real-time notifications to clients
// =============================================================================

using AAR.Application.DTOs;

namespace AAR.Application.Interfaces;

/// <summary>
/// Interface for sending real-time updates to connected clients via SignalR or other transport
/// </summary>
public interface IAnalysisHubNotifier
{
    /// <summary>
    /// Sends progress update to subscribed clients
    /// </summary>
    Task SendProgressAsync(Guid projectId, JobProgressUpdate progress);

    /// <summary>
    /// Sends partial finding to subscribed clients
    /// </summary>
    Task SendFindingAsync(Guid projectId, PartialFindingUpdate finding);

    /// <summary>
    /// Sends completion notification to subscribed clients
    /// </summary>
    Task SendCompletionAsync(Guid projectId, JobCompletionUpdate completion);

    /// <summary>
    /// Notifies organization of new pending approval
    /// </summary>
    Task SendApprovalRequestAsync(string organizationId, PendingApprovalJob job);
}

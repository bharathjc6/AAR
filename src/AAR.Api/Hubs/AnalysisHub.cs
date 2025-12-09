// =============================================================================
// AAR.Api - Hubs/AnalysisHub.cs
// SignalR hub for real-time analysis progress streaming
// =============================================================================

using AAR.Application.DTOs;
using AAR.Application.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace AAR.Api.Hubs;

/// <summary>
/// SignalR hub for streaming analysis progress and partial results
/// </summary>
public class AnalysisHub : Hub
{
    private readonly ILogger<AnalysisHub> _logger;

    public AnalysisHub(ILogger<AnalysisHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Subscribe to progress updates for a project
    /// </summary>
    public async Task SubscribeToProject(Guid projectId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, GetProjectGroup(projectId));
        _logger.LogInformation(
            "Client {ConnectionId} subscribed to project {ProjectId}",
            Context.ConnectionId, projectId);
    }

    /// <summary>
    /// Unsubscribe from project updates
    /// </summary>
    public async Task UnsubscribeFromProject(Guid projectId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetProjectGroup(projectId));
        _logger.LogDebug(
            "Client {ConnectionId} unsubscribed from project {ProjectId}",
            Context.ConnectionId, projectId);
    }

    /// <summary>
    /// Subscribe to all projects for an organization
    /// </summary>
    public async Task SubscribeToOrganization(string organizationId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, GetOrganizationGroup(organizationId));
        _logger.LogInformation(
            "Client {ConnectionId} subscribed to organization {OrgId}",
            Context.ConnectionId, organizationId);
    }

    /// <summary>
    /// Request current status for a project
    /// </summary>
    public async Task<JobProgressUpdate?> GetCurrentStatus(Guid projectId)
    {
        // This would query the current checkpoint status
        // For now, return null to indicate no active job
        _logger.LogDebug("Status requested for project {ProjectId}", projectId);
        return null;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogDebug("Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogDebug(
            "Client disconnected: {ConnectionId}, error: {Error}",
            Context.ConnectionId, exception?.Message);
        await base.OnDisconnectedAsync(exception);
    }

    internal static string GetProjectGroup(Guid projectId) => $"project:{projectId}";
    internal static string GetOrganizationGroup(string orgId) => $"org:{orgId}";
}

/// <summary>
/// Implementation that sends notifications via SignalR
/// </summary>
public class SignalRAnalysisHubNotifier : IAnalysisHubNotifier
{
    private readonly IHubContext<AnalysisHub> _hubContext;
    private readonly ILogger<SignalRAnalysisHubNotifier> _logger;

    public SignalRAnalysisHubNotifier(
        IHubContext<AnalysisHub> hubContext,
        ILogger<SignalRAnalysisHubNotifier> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task SendProgressAsync(Guid projectId, JobProgressUpdate progress)
    {
        var group = AnalysisHub.GetProjectGroup(projectId);
        await _hubContext.Clients.Group(group).SendAsync("ProgressUpdate", progress);
        
        _logger.LogDebug(
            "Sent progress update for {ProjectId}: {Percent}%",
            projectId, progress.ProgressPercent);
    }

    public async Task SendFindingAsync(Guid projectId, PartialFindingUpdate finding)
    {
        var group = AnalysisHub.GetProjectGroup(projectId);
        // Send as "PartialFinding" to match frontend expectations
        await _hubContext.Clients.Group(group).SendAsync("PartialFinding", new { projectId, finding = finding.Finding });
        
        _logger.LogDebug(
            "Sent finding update for {ProjectId}: {Severity}",
            projectId, finding.Finding.Severity);
    }

    public async Task SendCompletionAsync(Guid projectId, JobCompletionUpdate completion)
    {
        var group = AnalysisHub.GetProjectGroup(projectId);
        
        // Send as "StatusChanged" to match frontend expectations
        var status = completion.IsSuccess ? "completed" : "failed";
        await _hubContext.Clients.Group(group).SendAsync("StatusChanged", new { projectId = projectId.ToString(), status });
        
        // Also send the full completion data for consumers who need it
        await _hubContext.Clients.Group(group).SendAsync("JobCompleted", completion);
        
        _logger.LogInformation(
            "Sent completion for {ProjectId}: success={Success}",
            projectId, completion.IsSuccess);
    }

    public async Task SendApprovalRequestAsync(string organizationId, PendingApprovalJob job)
    {
        var group = AnalysisHub.GetOrganizationGroup(organizationId);
        await _hubContext.Clients.Group(group).SendAsync("ApprovalRequired", job);
        
        _logger.LogInformation(
            "Sent approval request to {OrgId} for project {ProjectId}",
            organizationId, job.ProjectId);
    }
}

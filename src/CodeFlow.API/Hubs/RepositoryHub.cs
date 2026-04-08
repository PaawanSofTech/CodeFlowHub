using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CodeFlow.API.Hubs
{
    /// <summary>
    /// Real-time WebSocket hub for push/pull progress and live repo updates.
    /// Connect: ws://host/hubs/repo?access_token=JWT
    /// </summary>
    [Authorize]
    public class RepositoryHub : Hub
    {
        public async Task JoinRepo(string owner, string name)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(owner, name));
            await Clients.Caller.SendAsync("Joined", new { owner, name });
        }

        public async Task LeaveRepo(string owner, string name)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(owner, name));
        }

        // Called from server-side services to broadcast events
        public static async Task BroadcastPushEvent(IHubContext<RepositoryHub> hub,
            string owner, string name, string commitHash, string branch)
        {
            await hub.Clients.Group(GroupName(owner, name)).SendAsync("PushReceived", new
            {
                owner, name, commitHash, branch, timestamp = System.DateTime.UtcNow
            });
        }

        public static async Task BroadcastPullRequest(IHubContext<RepositoryHub> hub,
            string owner, string name, string prId, string title)
        {
            await hub.Clients.Group(GroupName(owner, name)).SendAsync("PullRequestCreated", new
            {
                owner, name, prId, title, timestamp = System.DateTime.UtcNow
            });
        }

        private static string GroupName(string owner, string name) => $"repo:{owner}/{name}";
    }
}

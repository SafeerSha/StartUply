using Microsoft.AspNetCore.SignalR;

namespace StartUply.Presentation.Hubs
{
    public class ProgressHub : Hub
    {
        public async Task SendProgress(string connectionId, string message, int percentage)
        {
            await Clients.Client(connectionId).SendAsync("ReceiveProgress", message, percentage);
        }
    }
}
using System.Threading.Tasks;

namespace FlowMaster.Domain.Interfaces
{
    public interface INotificationService
    {
        Task SendEmailAsync(string to, string subject, string body);
        Task SendTeamsMessageAsync(string userId, string message);
    }
}

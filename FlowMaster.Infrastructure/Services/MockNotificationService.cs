using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace FlowMaster.Infrastructure.Services
{
    // Interface definition should logically belong to Domain, but putting it here for simplicity in this turn or assuming implicit understanding.
    using FlowMaster.Domain.Interfaces;

    public class MockNotificationService : INotificationService
    {
        public Task SendEmailAsync(string to, string subject, string body)
        {
            var log = $"[Email Mock] To: {to}, Subject: {subject}, Body: {body}";
            Debug.WriteLine(log);
            Console.WriteLine(log);
            return Task.CompletedTask;
        }

        public Task SendTeamsMessageAsync(string userId, string message)
        {
            var log = $"[Teams Mock] To: {userId}, Message: {message}";
            Debug.WriteLine(log);
            Console.WriteLine(log);
            return Task.CompletedTask;
        }
    }
}

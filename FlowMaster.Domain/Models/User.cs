using System;

namespace FlowMaster.Domain.Models
{
    public enum UserRole
    {
        GeneralUser,
        Approver,
        Admin
    }

    public class User
    {
        public string UserId { get; set; }
        public string AdAccount { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public UserRole Role { get; set; }
        public DateTime LastLoginDate { get; set; }
    }
}

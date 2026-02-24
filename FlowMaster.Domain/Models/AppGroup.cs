using System;
using System.Collections.Generic;

namespace FlowMaster.Domain.Models
{
    /// <summary>
    /// 앱 자체 그룹 (AD 그룹과 무관).
    /// 관리자가 앱 내에서 생성/관리하며, 사용자에게 역할(권한)을 부여하는 단위입니다.
    /// </summary>
    public class AppGroup
    {
        public int GroupId { get; set; }
        public string GroupName { get; set; }
        public string Description { get; set; }
        public UserRole Role { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<User> Members { get; set; } = new List<User>();
    }
}

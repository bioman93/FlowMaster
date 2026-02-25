using System;
using System.Collections.Generic;

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
        public DateTime LastLoginDate { get; set; }

        /// <summary>
        /// 소속 그룹명 목록. API 기반 사용자는 서버에서 채워짐.
        /// Emulator/레거시 사용자는 빈 목록이며 Role 직접 설정 방식 사용.
        /// </summary>
        public List<string> Groups { get; set; } = new List<string>();

        // Role 직접 설정용 backing field (Emulator/레거시 호환)
        private UserRole _role = UserRole.GeneralUser;

        /// <summary>
        /// Groups가 있으면 그룹명에서 파생, 없으면 직접 설정된 값 반환.
        /// 기존 코드(u.Role == UserRole.Approver 등)와 완전 호환됩니다.
        /// </summary>
        public UserRole Role
        {
            get
            {
                if (Groups != null && Groups.Count > 0)
                {
                    if (Groups.Contains("Admin"))    return UserRole.Admin;
                    if (Groups.Contains("Approver")) return UserRole.Approver;
                    return UserRole.GeneralUser;
                }
                return _role;
            }
            set => _role = value;
        }

        /// <summary>그룹 목록 표시용. DataGrid "그룹" 컬럼에 바인딩.</summary>
        public string GroupsDisplay => Groups != null && Groups.Count > 0
            ? string.Join(", ", Groups)
            : Role.ToString();
    }
}

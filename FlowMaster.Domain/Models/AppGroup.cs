using System;
using System.Collections.Generic;

namespace FlowMaster.Domain.Models
{
    /// <summary>
    /// 앱 자체 그룹 (AD 그룹과 무관).
    /// GeneralUser/Approver/Admin은 기본 그룹으로 삭제 불가.
    /// 관리자가 임의의 그룹을 추가/수정/삭제할 수 있습니다.
    /// 사용자는 복수의 그룹에 소속될 수 있습니다.
    /// </summary>
    public class AppGroup
    {
        public int GroupId { get; set; }
        public string GroupName { get; set; }
        public string Description { get; set; }
        /// <summary>GeneralUser/Approver/Admin 기본 그룹. true이면 삭제 불가.</summary>
        public bool IsDefault { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<User> Members { get; set; } = new List<User>();

        /// <summary>그룹 목록 표시용 (기본 그룹에 "(기본)" 뱃지 추가).</summary>
        public string DisplayName => IsDefault ? $"{GroupName} (기본)" : GroupName;
    }
}

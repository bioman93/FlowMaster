using System.Collections.Generic;
using System.Threading.Tasks;
using FlowMaster.Domain.Models;

namespace FlowMaster.Domain.Interfaces
{
    /// <summary>
    /// AD 인증 서비스 인터페이스
    /// 개발: Emulator(localhost:3900) 연동
    /// 운영: 실제 AD 연동 구현체로 교체
    /// </summary>
    public interface IAuthService
    {
        /// <summary>Emulator 서버에 연결 가능한지 여부</summary>
        bool IsEmulatorAvailable { get; }

        /// <summary>현재 로그인된 사용자의 JWT 토큰 (없으면 null)</summary>
        string CurrentToken { get; }

        /// <summary>
        /// 사용 가능한 전체 사용자 목록을 로드합니다.
        /// Emulator 미실행 시 Mock 사용자(4명)를 반환합니다.
        /// </summary>
        Task<List<User>> GetUsersAsync();

        /// <summary>
        /// 지정한 계정으로 로그인하여 JWT 토큰을 반환합니다.
        /// Emulator 미실행 시 null을 반환합니다 (무인증 모드).
        /// </summary>
        Task<string> LoginAsync(string adAccount);

        /// <summary>
        /// Emulator의 Current Context 사용자를 반환합니다.
        /// GET /api/context/user — Emulator 대시보드에서 선택된 현재 사용자.
        /// Emulator 미실행 시 null을 반환합니다.
        /// </summary>
        Task<User> GetCurrentContextUserAsync();
    }
}

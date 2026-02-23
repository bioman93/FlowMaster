using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using FlowMaster.Domain.DTOs;
using Newtonsoft.Json;

namespace FlowMaster.Infrastructure.Services
{
    /// <summary>
    /// ApprovalService REST API 클라이언트
    /// 개발: http://localhost:5001/api
    /// 운영: AD 인증 + 프로덕션 URL
    /// </summary>
    public class ApprovalApiClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private bool _isAvailable;

        public bool IsAvailable => _isAvailable;

        /// <summary>
        /// ApprovalSystem API 요청에 사용할 JWT 토큰을 설정합니다.
        /// null 전달 시 Authorization 헤더를 제거합니다 (무인증 모드).
        /// </summary>
        public void SetAuthToken(string token)
        {
            if (string.IsNullOrEmpty(token))
                _httpClient.DefaultRequestHeaders.Authorization = null;
            else
                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        public ApprovalApiClient(string baseUrl = "http://localhost:5002/api")
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5)
            };
        }

        /// <summary>
        /// API 서버 연결 상태 확인
        /// </summary>
        public async Task<bool> CheckConnectionAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/approvals/my-requests?userId=ping&pageSize=1");
                _isAvailable = response.IsSuccessStatusCode || (int)response.StatusCode == 400;
                return _isAvailable;
            }
            catch
            {
                _isAvailable = false;
                return false;
            }
        }

        /// <summary>
        /// 결재 생성
        /// POST /api/approvals
        /// </summary>
        public async Task<ApprovalResponse> CreateApprovalAsync(CreateApprovalRequest request)
        {
            var json = JsonConvert.SerializeObject(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_baseUrl}/approvals", content);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                throw new Exception($"결재 생성 실패 ({(int)response.StatusCode}): {errorBody}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<ApprovalResponse>(responseJson);
        }

        /// <summary>
        /// 결재 조회
        /// GET /api/approvals/{id}
        /// </summary>
        public async Task<ApprovalResponse> GetApprovalAsync(string approvalId)
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/approvals/{approvalId}");

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                throw new Exception($"결재 조회 실패 ({(int)response.StatusCode}): {errorBody}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<ApprovalResponse>(responseJson);
        }

        /// <summary>
        /// 내 결재 대기 목록 (결재자용)
        /// GET /api/approvals/my-approvals?userId=xxx&status=Pending
        /// </summary>
        public async Task<PaginationResponse<ApprovalResponse>> GetMyApprovalsAsync(
            string userId, string status = null, int page = 1, int pageSize = 20)
        {
            var url = $"{_baseUrl}/approvals/my-approvals?userId={Uri.EscapeDataString(userId)}&page={page}&pageSize={pageSize}";
            if (!string.IsNullOrEmpty(status))
                url += $"&status={status}";

            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                throw new Exception($"결재 목록 조회 실패 ({(int)response.StatusCode}): {errorBody}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<PaginationResponse<ApprovalResponse>>(responseJson);
        }

        /// <summary>
        /// 내가 요청한 결재 목록 (요청자용)
        /// GET /api/approvals/my-requests?userId=xxx
        /// </summary>
        public async Task<PaginationResponse<ApprovalResponse>> GetMyRequestsAsync(
            string userId, string status = null, int page = 1, int pageSize = 20)
        {
            var url = $"{_baseUrl}/approvals/my-requests?userId={Uri.EscapeDataString(userId)}&page={page}&pageSize={pageSize}";
            if (!string.IsNullOrEmpty(status))
                url += $"&status={status}";

            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                throw new Exception($"내 요청 목록 조회 실패 ({(int)response.StatusCode}): {errorBody}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<PaginationResponse<ApprovalResponse>>(responseJson);
        }

        /// <summary>
        /// 결재 승인/반려
        /// POST /api/approvals/{id}/decision
        /// </summary>
        public async Task<ApprovalDecisionResponse> MakeDecisionAsync(
            string approvalId, string decision, string approverId, string comment = null)
        {
            var request = new ApprovalDecisionRequest
            {
                Decision = decision,    // "approve" or "reject"
                ApproverId = approverId,
                Comment = comment
            };

            var json = JsonConvert.SerializeObject(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_baseUrl}/approvals/{approvalId}/decision", content);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                throw new Exception($"결재 처리 실패 ({(int)response.StatusCode}): {errorBody}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<ApprovalDecisionResponse>(responseJson);
        }

        /// <summary>
        /// 결재 취소
        /// POST /api/approvals/{id}/cancel?requesterId=xxx
        /// </summary>
        public async Task CancelApprovalAsync(string approvalId, string requesterId)
        {
            var response = await _httpClient.PostAsync(
                $"{_baseUrl}/approvals/{approvalId}/cancel?requesterId={Uri.EscapeDataString(requesterId)}",
                null);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                throw new Exception($"결재 취소 실패 ({(int)response.StatusCode}): {errorBody}");
            }
        }
    }
}

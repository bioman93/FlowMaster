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

        // ═══════════════════════════════════════════════════════════════
        //  FM 문서 API (/api/fm/*)
        // ═══════════════════════════════════════════════════════════════

        private StringContent ToJson(object obj)
            => new StringContent(JsonConvert.SerializeObject(obj), Encoding.UTF8, "application/json");

        /// <summary>.NET Framework 4.7.2는 PatchAsync를 지원하지 않으므로 SendAsync로 우회합니다.</summary>
        private Task<HttpResponseMessage> PatchAsync(string url, HttpContent content)
        {
            var request = new HttpRequestMessage(new HttpMethod("PATCH"), url) { Content = content };
            return _httpClient.SendAsync(request);
        }

        private async Task<T> ReadJson<T>(HttpResponseMessage response, string errorPrefix)
        {
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                throw new Exception($"{errorPrefix} ({(int)response.StatusCode}): {body}");
            }
            var json = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<T>(json);
        }

        private async Task EnsureSuccess(HttpResponseMessage response, string errorPrefix)
        {
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                throw new Exception($"{errorPrefix} ({(int)response.StatusCode}): {body}");
            }
        }

        // ─── 문서 ────────────────────────────────────────────────────

        public async Task<int> FmCreateDocumentAsync(FmDocumentDto doc)
        {
            var response = await _httpClient.PostAsync($"{_baseUrl}/fm/documents", ToJson(doc));
            var result = await ReadJson<FmDocumentDto>(response, "FM 문서 생성 실패");
            return result.DocId;
        }

        public async Task FmUpdateDocumentAsync(FmDocumentDto doc)
        {
            var response = await _httpClient.PutAsync($"{_baseUrl}/fm/documents/{doc.DocId}", ToJson(doc));
            await EnsureSuccess(response, "FM 문서 수정 실패");
        }

        public async Task FmUpdateStatusAsync(int docId, int status)
        {
            var response = await PatchAsync(
                $"{_baseUrl}/fm/documents/{docId}/status",
                ToJson(new FmStatusUpdateRequest { Status = status }));
            await EnsureSuccess(response, "FM 문서 상태 변경 실패");
        }

        public async Task FmDeleteDocumentAsync(int docId)
        {
            var response = await _httpClient.DeleteAsync($"{_baseUrl}/fm/documents/{docId}");
            await EnsureSuccess(response, "FM 문서 삭제 실패");
        }

        public async Task<FmDocumentDto> FmGetDocumentAsync(int docId)
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/fm/documents/{docId}");
            return await ReadJson<FmDocumentDto>(response, "FM 문서 조회 실패");
        }

        public async Task<System.Collections.Generic.List<FmDocumentDto>> FmGetAllDocumentsAsync()
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/fm/documents/all");
            return await ReadJson<System.Collections.Generic.List<FmDocumentDto>>(response, "FM 전체 문서 조회 실패");
        }

        public async Task<System.Collections.Generic.List<FmDocumentDto>> FmGetMyDraftsAsync(string userId)
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/fm/documents/my-drafts?userId={Uri.EscapeDataString(userId)}");
            return await ReadJson<System.Collections.Generic.List<FmDocumentDto>>(response, "FM 내 문서 조회 실패");
        }

        public async Task<System.Collections.Generic.List<FmDocumentDto>> FmGetPendingAsync(string approverId)
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/fm/documents/pending?approverId={Uri.EscapeDataString(approverId)}");
            return await ReadJson<System.Collections.Generic.List<FmDocumentDto>>(response, "FM 결재 대기 조회 실패");
        }

        public async Task FmUpdateApprovalIdAsync(int docId, string approvalId)
        {
            var response = await PatchAsync(
                $"{_baseUrl}/fm/documents/{docId}/approval-id",
                ToJson(approvalId));
            await EnsureSuccess(response, "FM ApprovalId 업데이트 실패");
        }

        public async Task<FmDocumentDto> FmGetDocumentByApprovalIdAsync(string approvalId)
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/fm/documents/by-approval-id/{Uri.EscapeDataString(approvalId)}");
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            return await ReadJson<FmDocumentDto>(response, "FM ApprovalId 조회 실패");
        }

        public async Task<System.Collections.Generic.List<FmDocumentDto>> FmGetUnsyncedDocumentsAsync()
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/fm/documents/unsynced");
            return await ReadJson<System.Collections.Generic.List<FmDocumentDto>>(response, "FM 미동기화 문서 조회 실패");
        }

        public async Task FmUpdateSyncStatusAsync(int docId, int syncStatus, int retryCount, string error)
        {
            var response = await PatchAsync(
                $"{_baseUrl}/fm/documents/{docId}/sync-status",
                ToJson(new FmSyncStatusUpdateRequest { SyncStatus = syncStatus, RetryCount = retryCount, Error = error }));
            await EnsureSuccess(response, "FM 동기화 상태 업데이트 실패");
        }

        public async Task<System.Collections.Generic.List<string>> FmGetVersionSuggestionsAsync(string keyword, string genType, string injType)
        {
            var url = $"{_baseUrl}/fm/documents/version-suggestions?keyword={Uri.EscapeDataString(keyword)}";
            if (!string.IsNullOrEmpty(genType)) url += $"&genType={Uri.EscapeDataString(genType)}";
            if (!string.IsNullOrEmpty(injType)) url += $"&injType={Uri.EscapeDataString(injType)}";
            var response = await _httpClient.GetAsync(url);
            return await ReadJson<System.Collections.Generic.List<string>>(response, "FM 버전 제안 조회 실패");
        }

        // ─── 체크리스트 ─────────────────────────────────────────────

        public async Task FmSaveChecklistAsync(int docId, System.Collections.Generic.List<FmChecklistItemDto> items)
        {
            var response = await _httpClient.PutAsync($"{_baseUrl}/fm/documents/{docId}/checklist", ToJson(items));
            await EnsureSuccess(response, "FM 체크리스트 저장 실패");
        }

        public async Task<System.Collections.Generic.List<FmChecklistItemDto>> FmGetChecklistAsync(int docId)
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/fm/documents/{docId}/checklist");
            return await ReadJson<System.Collections.Generic.List<FmChecklistItemDto>>(response, "FM 체크리스트 조회 실패");
        }

        // ─── 결재선 ─────────────────────────────────────────────────

        public async Task FmAddApprovalLineAsync(int docId, FmApprovalLineDto line)
        {
            line.DocId = docId;
            var response = await _httpClient.PostAsync($"{_baseUrl}/fm/documents/{docId}/approval-lines", ToJson(line));
            await EnsureSuccess(response, "FM 결재선 추가 실패");
        }

        public async Task FmUpdateLineStatusAsync(int lineId, int status, string comment)
        {
            var response = await PatchAsync(
                $"{_baseUrl}/fm/approval-lines/{lineId}/status",
                ToJson(new FmLineStatusUpdateRequest { Status = status, Comment = comment }));
            await EnsureSuccess(response, "FM 결재선 상태 변경 실패");
        }

        // ─── 테스트 결과 ─────────────────────────────────────────────

        public async Task FmAddTestResultAsync(int docId, FmTestResultDto result)
        {
            result.DocId = docId;
            var response = await _httpClient.PostAsync($"{_baseUrl}/fm/documents/{docId}/test-results", ToJson(result));
            await EnsureSuccess(response, "FM 테스트 결과 추가 실패");
        }

        public async Task<System.Collections.Generic.List<FmTestResultDto>> FmGetTestResultsAsync(int docId)
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/fm/documents/{docId}/test-results");
            return await ReadJson<System.Collections.Generic.List<FmTestResultDto>>(response, "FM 테스트 결과 조회 실패");
        }

        // ─── 참여자 ─────────────────────────────────────────────────

        public async Task<System.Collections.Generic.List<FmParticipantDto>> FmGetParticipantsAsync(int docId)
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/fm/documents/{docId}/participants");
            return await ReadJson<System.Collections.Generic.List<FmParticipantDto>>(response, "FM 참여자 조회 실패");
        }

        public async Task FmSaveParticipantsAsync(int docId, System.Collections.Generic.List<FmParticipantDto> list)
        {
            var response = await _httpClient.PutAsync($"{_baseUrl}/fm/documents/{docId}/participants", ToJson(list));
            await EnsureSuccess(response, "FM 참여자 저장 실패");
        }

        public async Task<System.Collections.Generic.List<FmParticipantDto>> FmGetGroupAsync(string groupName)
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/fm/groups/{Uri.EscapeDataString(groupName)}/participants");
            return await ReadJson<System.Collections.Generic.List<FmParticipantDto>>(response, "FM 그룹 조회 실패");
        }

        public async Task FmAddGroupMemberAsync(string groupName, FmParticipantDto user)
        {
            var response = await _httpClient.PostAsync($"{_baseUrl}/fm/groups/{Uri.EscapeDataString(groupName)}/participants", ToJson(user));
            await EnsureSuccess(response, "FM 그룹 참여자 추가 실패");
        }

        public async Task FmRemoveGroupMemberAsync(string groupName, string userId)
        {
            var response = await _httpClient.DeleteAsync($"{_baseUrl}/fm/groups/{Uri.EscapeDataString(groupName)}/participants/{Uri.EscapeDataString(userId)}");
            await EnsureSuccess(response, "FM 그룹 참여자 제거 실패");
        }

        // ═══════════════════════════════════════════════════════════════
        //  앱 사용자 API (/api/fm/app-users)
        // ═══════════════════════════════════════════════════════════════

        public async Task<System.Collections.Generic.List<FmAppUserDto>> FmGetAllAppUsersAsync(bool includeDisabled = false)
        {
            var url = $"{_baseUrl}/fm/app-users?includeDisabled={includeDisabled.ToString().ToLowerInvariant()}";
            var response = await _httpClient.GetAsync(url);
            return await ReadJson<System.Collections.Generic.List<FmAppUserDto>>(response, "앱 사용자 목록 조회 실패");
        }

        public async Task FmUpsertAppUserAsync(FmAppUserDto user)
        {
            var response = await _httpClient.PostAsync($"{_baseUrl}/fm/app-users", ToJson(user));
            await EnsureSuccess(response, "앱 사용자 추가/교체 실패");
        }

        public async Task FmUpdateAppUserAsync(string userId, FmAppUserDto user)
        {
            var response = await _httpClient.PutAsync($"{_baseUrl}/fm/app-users/{Uri.EscapeDataString(userId)}", ToJson(user));
            await EnsureSuccess(response, "앱 사용자 수정 실패");
        }

        public async Task FmDeleteAppUserAsync(string userId)
        {
            var response = await _httpClient.DeleteAsync($"{_baseUrl}/fm/app-users/{Uri.EscapeDataString(userId)}");
            await EnsureSuccess(response, "앱 사용자 삭제 실패");
        }

        // ═══════════════════════════════════════════════════════════════
        //  앱 그룹 API (/api/fm/app-groups)
        // ═══════════════════════════════════════════════════════════════

        public async Task<System.Collections.Generic.List<FmAppGroupDto>> FmGetAllAppGroupsAsync()
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/fm/app-groups");
            return await ReadJson<System.Collections.Generic.List<FmAppGroupDto>>(response, "앱 그룹 목록 조회 실패");
        }

        public async Task<FmAppGroupDto> FmGetAppGroupWithMembersAsync(int groupId)
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/fm/app-groups/{groupId}");
            return await ReadJson<FmAppGroupDto>(response, "앱 그룹 조회 실패");
        }

        public async Task<int> FmAddAppGroupAsync(FmAppGroupDto group)
        {
            var response = await _httpClient.PostAsync($"{_baseUrl}/fm/app-groups", ToJson(group));
            return await ReadJson<int>(response, "앱 그룹 추가 실패");
        }

        public async Task FmUpdateAppGroupAsync(FmAppGroupDto group)
        {
            var response = await _httpClient.PutAsync($"{_baseUrl}/fm/app-groups/{group.GroupId}", ToJson(group));
            await EnsureSuccess(response, "앱 그룹 수정 실패");
        }

        public async Task FmDeleteAppGroupAsync(int groupId)
        {
            var response = await _httpClient.DeleteAsync($"{_baseUrl}/fm/app-groups/{groupId}");
            await EnsureSuccess(response, "앱 그룹 삭제 실패");
        }

        public async Task FmAddAppGroupMemberAsync(int groupId, string userId)
        {
            var req = new FmAddGroupMemberRequest { UserId = userId };
            var response = await _httpClient.PostAsync($"{_baseUrl}/fm/app-groups/{groupId}/members", ToJson(req));
            await EnsureSuccess(response, "앱 그룹 멤버 추가 실패");
        }

        public async Task FmRemoveAppGroupMemberAsync(int groupId, string userId)
        {
            var response = await _httpClient.DeleteAsync($"{_baseUrl}/fm/app-groups/{groupId}/members/{Uri.EscapeDataString(userId)}");
            await EnsureSuccess(response, "앱 그룹 멤버 제거 실패");
        }
    }
}

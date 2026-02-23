# FlowMaster — 기술 인수인계 문서

> **작성일**: 2026-02-11
> **버전**: MVP (최근 커밋: a317c52)
> **목적**: 프로젝트 전체 기술 인수인계 (신규 개발자 온보딩, AI 재구현 참고)

---

## 목차

1. [프로젝트 개요](#1-프로젝트-개요)
2. [기술 스택](#2-기술-스택)
3. [아키텍처](#3-아키텍처)
4. [프로젝트 구조](#4-프로젝트-구조)
5. [도메인 모델](#5-도메인-모델)
6. [데이터베이스 스키마](#6-데이터베이스-스키마)
7. [비즈니스 로직 (Core Layer)](#7-비즈니스-로직-core-layer)
8. [인프라 레이어](#8-인프라-레이어)
9. [프레젠테이션 레이어 (WPF)](#9-프레젠테이션-레이어-wpf)
10. [워크플로우](#10-워크플로우)
11. [빌드 및 배포](#11-빌드-및-배포)
12. [트러블슈팅](#12-트러블슈팅)
13. [재구현 시 개선 권장 사항](#13-재구현-시-개선-권장-사항)

---

## 1. 프로젝트 개요

**FlowMaster**는 테스트 결과 관리 및 전자결재를 위한 Windows 데스크톱 애플리케이션입니다.

### 배경 및 목적

기존에 엑셀로 관리하던 테스트 결과를 DB 기반 전자결재 시스템으로 전환하기 위해 개발되었습니다.

### 핵심 기능

1. **테스트 결과 입력**: BA1/BA2 테이블 타입별 체크리스트 기반 결과 입력
2. **전자결재 프로세스**: 임시저장 → 상신 → 승인/반려 워크플로우
3. **외부 DB 조회**: 기존 레거시 SQLite DB 연결 및 문서 클론
4. **결재 API 연동**: 외부 ApprovalService API와 선택적 연동

### 관련 시스템

| 시스템 | 포트/경로 | 용도 |
|--------|----------|------|
| **FlowMaster** (본 프로젝트) | Windows 앱 | 테스트 결과 + 전자결재 |
| ApprovalService | http://localhost:5001 | 선택적 결재 API 연동 |

---

## 2. 기술 스택

| 항목 | 기술 |
|------|------|
| **언어** | C# 8.0 |
| **프레임워크** | .NET Framework 4.7.2 (Desktop) |
| **UI** | WPF (Windows Presentation Foundation) |
| **MVVM** | CommunityToolkit.Mvvm 8.4.0 |
| **ORM** | Dapper 2.1.66 |
| **데이터베이스** | SQLite (Microsoft.Data.Sqlite 10.0.2) |
| **SQLite 드라이버** | SQLitePCLRaw.bundle_green 2.1.11 |
| **DI 컨테이너** | Microsoft.Extensions.DependencyInjection 10.0.2 |
| **JSON** | Newtonsoft.Json 13.0.3 |
| **개발 환경** | Visual Studio 2019+, Windows x64 |

---

## 3. 아키텍처

### 레이어 구조 (Clean Architecture 변형)

```
┌─────────────────────────────────────────┐
│  FlowMaster.Desktop (.NET FW 4.7.2)     │  ← WPF UI, ViewModels, DI 설정
├─────────────────────────────────────────┤
│  FlowMaster.Core (netstandard2.0)       │  ← 비즈니스 로직, 서비스
├─────────────────────────────────────────┤
│  FlowMaster.Infrastructure (ns2.0)      │  ← DB, 외부 API, Mock 구현
├─────────────────────────────────────────┤
│  FlowMaster.Domain (netstandard2.0)     │  ← 엔티티, DTO, 인터페이스
└─────────────────────────────────────────┘
```

**의존성 방향**: Desktop → Core → Domain, Desktop → Infrastructure → Domain

### 설계 원칙

- **인터페이스 기반 설계**: Core는 Domain 인터페이스에만 의존, 구현체는 Infrastructure
- **MVVM 패턴**: View ↔ ViewModel (CommunityToolkit.Mvvm), ViewModel → Service (Core)
- **DI 컨테이너**: `App.xaml.cs`에서 서비스 등록 (Singleton/Transient 구분)
- **선택적 API 연동**: ApprovalService 미실행 시에도 로컬 모드로 동작

---

## 4. 프로젝트 구조

```
FlowMaster/
├── FlowMaster.sln
├── CLAUDE.md                          # 개발 가이드라인
├── GEMINI.md                          # Gemini AI 협업 가이드
├── flowmaster_test.db                 # 로컬 테스트 DB
├── FlowMaster.Domain/                 # 도메인 레이어
│   ├── Models/
│   │   ├── ApprovalDocument.cs        # 주요 엔티티
│   │   ├── ApprovalLine.cs            # 결재 라인
│   │   ├── ChecklistItem.cs           # 체크리스트 항목
│   │   ├── TestResult.cs              # 테스트 결과
│   │   └── User.cs                    # 사용자
│   ├── Interfaces/
│   │   ├── IRepository.cs             # 저장소 인터페이스
│   │   └── INotificationService.cs    # 알림 서비스 인터페이스
│   └── DTOs/
│       └── ApprovalApiModels.cs       # API 요청/응답 DTO
├── FlowMaster.Core/
│   └── Services/
│       └── ApprovalService.cs         # 결재 비즈니스 로직
├── FlowMaster.Infrastructure/
│   ├── Repositories/
│   │   ├── SqliteApprovalRepository.cs # 내부 SQLite DB
│   │   └── ExternalDbRepository.cs     # 외부 레거시 DB
│   ├── Services/
│   │   ├── ApprovalApiClient.cs        # ApprovalService API 클라이언트
│   │   ├── MockUserRepository.cs       # 테스트 사용자 (Mock)
│   │   └── MockNotificationService.cs  # 테스트 알림 (Console 출력)
│   └── Utilities/
│       └── DbSchemaExtractor.cs        # DB 스키마 분석 유틸
├── FlowMaster.Desktop/
│   ├── App.xaml.cs                     # DI 설정 + 진입점
│   ├── MainWindow.xaml(.cs)            # 메인 윈도우 + 네비게이션
│   ├── ViewModels/
│   │   ├── MainViewModel.cs            # 네비게이션 허브
│   │   ├── DashboardViewModel.cs       # 대시보드
│   │   ├── WriteViewModel.cs           # 결재 작성
│   │   ├── DetailViewModel.cs          # 문서 상세 보기
│   │   ├── TestInputViewModel.cs       # BA1/BA2 테스트 입력 (핵심)
│   │   └── TypeSelectionViewModel.cs   # 테이블 타입 + DB 선택
│   └── Views/
│       ├── DashboardView.xaml
│       ├── WriteView.xaml
│       ├── DetailView.xaml
│       ├── TestInputView.xaml
│       └── TypeSelectionView.xaml
├── scripts/                            # 빌드/배포 스크립트
├── specs/
│   ├── SPECIFICATION.md               # 기능 명세
│   └── UI_DESIGN_CONCEPT.md           # UI 디자인 가이드
└── dev-guidelines/                    # 개발 가이드라인
```

---

## 5. 도메인 모델

### 5.1 ApprovalDocument — 핵심 엔티티

```csharp
public class ApprovalDocument
{
    public int DocId { get; set; }
    public string Title { get; set; }
    public string WriterId { get; set; }
    public string WriterName { get; set; }
    public DateTime CreateDate { get; set; }
    public DateTime? UpdateDate { get; set; }
    public ApprovalStatus Status { get; set; }

    // 결재 관련
    public string CurrentApproverId { get; set; }   // 현재 결재자
    public string ApprovalId { get; set; }           // 외부 API ID (APV-xxx)
    public string ApproverComment { get; set; }
    public DateTime? ApprovalTime { get; set; }

    // 테스트 문서 타입
    public string TableType { get; set; }  // BA1, BA2
    public string GenType { get; set; }    // 발전기 유형
    public string InjType { get; set; }    // 인젝터 유형
    public string Description { get; set; }  // BA2 전용
    public string Participants { get; set; } // 참여자 목록

    // Navigation Properties
    public List<ApprovalLine> ApprovalLines { get; set; }
    public List<TestResult> TestResults { get; set; }
    public List<ChecklistItem> ChecklistItems { get; set; }
}
```

### 5.2 ApprovalStatus (enum)

```csharp
public enum ApprovalStatus
{
    TempSaved = 0,  // 임시저장
    Pending   = 1,  // 결재 대기
    Approved  = 2,  // 승인
    Rejected  = 3,  // 반려
    Canceled  = 4   // 취소됨
}
```

### 5.3 ApprovalLine — 결재 라인

```csharp
public class ApprovalLine
{
    public int LineId { get; set; }
    public int DocId { get; set; }
    public string ApproverId { get; set; }
    public string ApproverName { get; set; }
    public int Sequence { get; set; }         // 결재 순서 (1, 2, 3...)
    public ApprovalStepStatus Status { get; set; }
    public DateTime? ActionDate { get; set; }
    public string Comment { get; set; }
}

public enum ApprovalStepStatus
{
    Waiting  = 0,  // 대기
    Approved = 1,  // 승인
    Rejected = 2   // 반려
}
```

### 5.4 ChecklistItem — 체크리스트 항목

```csharp
public class ChecklistItem
{
    public int ItemId { get; set; }
    public int DocId { get; set; }
    public string RowNo { get; set; }          // 항목 번호 ("1.1", "2.3" 등)
    public string CheckItem { get; set; }      // 항목명
    public string OutputContent { get; set; }  // 산출물
    public string EvaluationCode { get; set; } // +, (+), (-), -, nb
    public string Remarks { get; set; }        // 비고
    public int DisplayOrder { get; set; }
}
```

### 5.5 TestResult

```csharp
public class TestResult
{
    public int ResultId { get; set; }
    public int DocId { get; set; }
    public string ProjectName { get; set; }
    public string Version { get; set; }
    public DateTime TestDate { get; set; }
    public string TestCaseName { get; set; }
    public bool IsPass { get; set; }
    public string FailureReason { get; set; }
    public string Details { get; set; }
    public string BackupDbSource { get; set; }  // 소스 DB 경로
}
```

### 5.6 User

```csharp
public class User
{
    public string UserId { get; set; }
    public string AdAccount { get; set; }   // AD 계정명
    public string Name { get; set; }
    public string Email { get; set; }
    public UserRole Role { get; set; }
    public DateTime LastLoginDate { get; set; }
}

public enum UserRole
{
    GeneralUser = 0,  // 일반사용자
    Approver    = 1,  // 결재권자
    Admin       = 2   // 관리자
}
```

---

## 6. 데이터베이스 스키마

### 6.1 내부 DB (flowmaster_test.db)

`SqliteApprovalRepository`가 관리하는 로컬 SQLite DB입니다.
앱 최초 실행 시 자동 생성됩니다.

```sql
-- 결재 문서
CREATE TABLE IF NOT EXISTS ApprovalDocuments (
    DocId       INTEGER PRIMARY KEY AUTOINCREMENT,
    Title       TEXT,
    WriterId    TEXT,
    WriterName  TEXT,
    CreateDate  TEXT,
    UpdateDate  TEXT,
    Status      INTEGER,                  -- 0=TempSaved, 1=Pending, 2=Approved, 3=Rejected, 4=Canceled
    CurrentApproverId TEXT,
    TableType   TEXT,
    GenType     TEXT,
    InjType     TEXT,
    Description TEXT,
    ApproverComment TEXT,
    ApprovalId  TEXT                      -- 외부 API ID
);

-- 결재 라인
CREATE TABLE IF NOT EXISTS ApprovalLines (
    LineId      INTEGER PRIMARY KEY AUTOINCREMENT,
    DocId       INTEGER,                  -- FK → ApprovalDocuments
    ApproverId  TEXT,
    ApproverName TEXT,
    Sequence    INTEGER,
    Status      INTEGER,                  -- 0=Waiting, 1=Approved, 2=Rejected
    ActionDate  TEXT,
    Comment     TEXT
);

-- 테스트 결과
CREATE TABLE IF NOT EXISTS TestResults (
    ResultId        INTEGER PRIMARY KEY AUTOINCREMENT,
    DocId           INTEGER,
    ProjectName     TEXT,
    Version         TEXT,
    TestDate        TEXT,
    TestCaseName    TEXT,
    IsPass          INTEGER,              -- 0/1
    FailureReason   TEXT,
    Details         TEXT,
    BackupDbSource  TEXT
);

-- 체크리스트 항목
CREATE TABLE IF NOT EXISTS ChecklistItems (
    ItemId          INTEGER PRIMARY KEY AUTOINCREMENT,
    DocId           INTEGER,
    RowNo           TEXT,
    CheckItem       TEXT,
    OutputContent   TEXT,
    EvaluationCode  TEXT,                 -- +, (+), (-), -, nb
    Remarks         TEXT,
    DisplayOrder    INTEGER
);
```

### 6.2 외부 DB (레거시 호환)

기존 시스템과의 호환을 위해 다른 컬럼명 구조를 지원합니다.

```sql
-- 기존 테이블 구조 (snake_case)
CREATE TABLE approval_documents (
    doc_id          INTEGER PRIMARY KEY,
    issue_key       TEXT,
    table_type      TEXT,
    gen_type        TEXT,
    inj_type        TEXT,
    creator_name    TEXT,      -- WriterName에 매핑
    created_date    TEXT,      -- CreateDate에 매핑
    approver_name   TEXT,      -- CurrentApproverId에 매핑
    approval_time   TEXT,
    approver_comment TEXT,
    description     TEXT,
    participants    TEXT
);

CREATE TABLE checklist_items (
    doc_id          INTEGER,
    display_order   INTEGER,
    -- ...
);
```

**컬럼명 변환 매핑** (`ExternalDbRepository.cs`):
| 외부 DB 컬럼 | 내부 필드 |
|-------------|---------|
| doc_id | DocId |
| issue_key | IssueKey |
| creator_name | WriterName |
| created_date | CreateDate |
| approver_name | CurrentApproverId |

---

## 7. 비즈니스 로직 (Core Layer)

### 7.1 IApprovalService 인터페이스

**파일 위치**: `FlowMaster.Core/Interfaces/IApprovalService.cs` (Core 레이어에 정의)

```csharp
public interface IApprovalService
{
    Task<int> SubmitDocumentAsync(ApprovalDocument doc, List<string> approverIds);
    Task ApproveDocumentAsync(int docId, string approverId, string comment);
    Task RejectDocumentAsync(int docId, string approverId, string comment);
    Task<ApprovalDocument> GetDocumentDetailAsync(int docId);
}
```

### 7.2 ApprovalService 구현

**SubmitDocumentAsync**:
```
1. doc.Status = Pending, doc.CreateDate = 현재
2. doc.CurrentApproverId = approverIds[0]
3. 내부 DB에 문서 저장 → DocId 획득
4. 각 결재자별 ApprovalLine 생성 (Sequence: 1, 2, 3...)
5. 테스트 결과 저장
6. 첫 번째 결재자에게 Teams 알림 (현재 Mock → Console 출력)
```

**ApproveDocumentAsync**:
```
1. 현재 결재자의 ApprovalLine 조회 (Status = Waiting)
2. ApprovalLine.Status = Approved, ActionDate = 현재, Comment 저장
3. 다음 Sequence의 결재자 확인
   ├── 있으면: doc.CurrentApproverId = 다음 결재자, 알림 발송
   └── 없으면: doc.Status = Approved, 작성자에게 알림 발송
```

**RejectDocumentAsync**:
```
1. 현재 결재자의 ApprovalLine.Status = Rejected
2. doc.Status = Rejected, ApproverComment 저장
3. 작성자에게 반려 알림 (사유 포함)
```

---

## 8. 인프라 레이어

### 8.1 SqliteApprovalRepository

**위치**: `FlowMaster.Infrastructure/Repositories/SqliteApprovalRepository.cs`

**특징**:
- Dapper ORM 사용 (경량 쿼리 매핑)
- 앱 시작 시 자동 테이블 생성 (`EnsureTablesExist`)
- 컬럼 추가 마이그레이션 지원 (`EnsureColumns`)

**주요 메서드**:
```csharp
Task<int> CreateDocumentAsync(ApprovalDocument doc)
Task UpdateDocumentStatusAsync(int docId, ApprovalStatus status)
Task<ApprovalDocument> GetDocumentAsync(int docId)
Task<List<ApprovalDocument>> GetMyDraftsAsync(string userId)
Task<List<ApprovalDocument>> GetPendingApprovalsAsync(string approverId)
Task AddApprovalLineAsync(ApprovalLine line)
Task UpdateApprovalLineStatusAsync(int lineId, ApprovalStepStatus status, string comment)
Task SaveChecklistItemsAsync(int docId, List<ChecklistItem> items)  // DELETE → INSERT
Task<List<ChecklistItem>> GetChecklistItemsAsync(int docId)
Task<List<ApprovalDocument>> GetAllDocumentsAsync()
Task UpdateDocumentAsync(ApprovalDocument doc)
```

### 8.2 ExternalDbRepository

**위치**: `FlowMaster.Infrastructure/Repositories/ExternalDbRepository.cs`

**특징**:
- 런타임에 동적으로 DB 파일 연결/해제
- 읽기 전용 모드 지원 (`Mode=ReadOnly`)
- 레거시 컬럼명 자동 변환

**주요 메서드**:
```csharp
void Connect(string dbFilePath)
void Disconnect()
bool IsConnected { get; }
string CurrentDbPath { get; }

Task<List<ApprovalDocument>> GetAllDocumentsAsync()
Task<ApprovalDocument> GetDocumentWithChecklistAsync(int docId)
Task SaveDocumentAsync(ApprovalDocument doc)
Task SaveChecklistItemsAsync(int docId, List<ChecklistItem> items)
Task CloneChecklistFromDocumentAsync(int sourceDocId, int targetDocId)
```

### 8.3 ApprovalApiClient

**위치**: `FlowMaster.Infrastructure/Services/ApprovalApiClient.cs`

**기본 설정**:
```csharp
BaseAddress = "http://localhost:5001/api"
Timeout = TimeSpan.FromSeconds(5)
```

**연동 방식**: 서버 미실행 시 예외를 catch하여 로컬 모드로 폴백

**주요 메서드**:
```csharp
Task<bool> CheckConnectionAsync()
Task<ApprovalResponse> CreateApprovalAsync(CreateApprovalRequest request)
Task<ApprovalResponse> GetApprovalAsync(string approvalId)
Task<List<ApprovalResponse>> GetMyApprovalsAsync(string userId)
Task<List<ApprovalResponse>> GetMyRequestsAsync(string userId)
Task<ApprovalDecisionResponse> MakeDecisionAsync(ApprovalDecisionRequest request)
Task CancelApprovalAsync(string approvalId)
```

### 8.4 MockUserRepository

**테스트 사용자 (4명)**:
| UserId | AdAccount | 이름 | 역할 |
|--------|-----------|------|------|
| U001 | user | 일반사용자 | GeneralUser |
| U002 | approver | 김부장 | Approver |
| U003 | admin | 관리자 | Admin |
| U004 | approver2 | 이이사 | Approver |

### 8.5 DbSchemaExtractor

**목적**: SQLite DB 스키마 분석 및 텍스트 리포트 생성
- 테이블 목록, 컬럼 정보, 행 개수, 인덱스 정보
- 메인 윈도우 사이드바 "DB 스키마 추출" 버튼에 연결됨

---

## 9. 프레젠테이션 레이어 (WPF)

### 9.1 App.xaml.cs — DI 설정

```csharp
protected override void OnStartup(StartupEventArgs e)
{
    SQLitePCL.Batteries.Init();  // SQLite 드라이버 초기화

    var services = new ServiceCollection();

    // 인프라
    services.AddSingleton<SqliteApprovalRepository>();
    services.AddSingleton<IApprovalRepository>(sp =>
        sp.GetRequiredService<SqliteApprovalRepository>());
    services.AddSingleton<IUserRepository, MockUserRepository>();
    services.AddSingleton<INotificationService, MockNotificationService>();
    services.AddSingleton<ExternalDbRepository>();
    services.AddSingleton<ApprovalApiClient>();

    // 비즈니스 로직
    services.AddSingleton<IApprovalService, ApprovalService>();

    // UI
    services.AddTransient<MainViewModel>();
    services.AddTransient<DashboardViewModel>();
    services.AddTransient<WriteViewModel>();
    services.AddTransient<DetailViewModel>();
    services.AddTransient<MainWindow>();

    // ⚠️ 주의: TypeSelectionViewModel, TestInputViewModel은 DI 미등록
    //   → MainViewModel에서 직접 new로 생성 (생성 시점에 동적 인자 필요)

    var provider = services.BuildServiceProvider();
    provider.GetRequiredService<MainWindow>().Show();
}
```

### 9.2 MainWindow — 레이아웃

```
┌──────────────────────────────────────────┐
│  사이드바 (250px)    │  메인 콘텐츠       │
│  ─────────────────   │  ─────────────────  │
│  [로고: FlowMaster]  │                    │
│                      │  <ContentControl>  │
│  [📊 대시보드]       │  (MVVM 바인딩으로  │
│  [📝 결재 작성]      │   View 동적 로드)  │
│  [📋 테스트 입력]    │                    │
│  [🔍 스키마 추출]    │                    │
│                      │                    │
│  [사용자 선택 ▼]     │                    │
│  [역할 표시]         │                    │
└──────────────────────────────────────────┘
```

**색상 팔레트 (Hyundai Brand)**:
| 용도 | 색상 코드 |
|------|----------|
| Primary (사이드바 버튼 활성) | `#002c5f` (Hyundai Blue) |
| 사이드바 배경 | `#e4dcd3` (Hyundai Sand) |
| 메인 배경 | `#F6F3F2` (Hyundai Light Sand) |
| 성공/통과 | `#4CAF50` |
| 위험/반려 | `#f44336` |
| 경고/대기 | `#ff9800` |

### 9.3 ViewModel 상세

#### MainViewModel (네비게이션 허브)

**역할**: 전체 앱 상태 관리, View 간 전환

**핵심 메서드**:
```csharp
void LoadUsers()            // MockUserRepository에서 사용자 로드
void NavigateToDashboard()
void NavigateToDetail(ApprovalDocument doc)  // BA1/BA2 → TestInputView, 일반 → DetailView
void NavigateToWrite()
void NavigateToTestInput()  // TypeSelectionView 표시
void OnTypeSelected(string type, string dbPath, ApprovalDocument cloneSource)
void ExtractDbSchema()      // DbSchemaExtractor 실행
```

#### TestInputViewModel (가장 복잡한 ViewModel)

**두 가지 초기화 모드**:

1. **신규 문서** (`InitializeNewDocumentAsync`):
   - 기본 체크리스트 템플릿 로드
   - 외부 DB 연결 시 Clone 소스 선택 가능

2. **기존 문서 편집** (`InitializeExistingDocumentAsync`):
   - 외부 DB 연결 시 → 외부 DB에서 로드
   - 미연결 시 → 내부 DB에서 로드

**주요 바인딩 프로퍼티**:
```csharp
// 문서 정보
string Title, TableType, GenType, InjType
string WriterName, Description, Participants

// 상태
string StatusText, StatusColor
bool CanSubmit, CanApprove, IsReadOnly

// 데이터
ObservableCollection<ChecklistItem> ChecklistItems
ObservableCollection<User> ApproverCandidates
User SelectedApprover

// 결재 정보
string ApproverComment
```

**주요 커맨드**:
```csharp
ICommand SaveCommand      // 임시저장
ICommand SubmitCommand    // 결재 상신
ICommand ApproveCommand   // 승인
ICommand RejectCommand    // 반려
ICommand AddRowCommand    // 체크리스트 행 추가
ICommand DeleteRowCommand // 체크리스트 행 삭제
```

---

## 10. 워크플로우

### 10.1 신규 테스트 결과 작성 및 상신

```
[사용자 선택 (사이드바 드롭다운)]
  ↓
[📋 테스트 입력 클릭]
  ↓
[TypeSelectionView: BA1 또는 BA2 선택]
  ├── 외부 DB 연결 옵션 (Clone 소스 선택 가능)
  └── 타입 선택 → MainViewModel.OnTypeSelected()
  ↓
[TestInputView 표시]
  ├── 제목, 발전기/인젝터 유형 입력
  ├── 체크리스트 항목 입력 (RowNo, CheckItem, EvaluationCode, Remarks)
  └── 결재자 선택
  ↓
[저장 버튼] → TempSaved 상태로 내부 DB 저장
  ↓
[상신 버튼] → SubmitDocumentAsync()
  ├── 내부 DB에 Pending 상태 저장
  ├── ApprovalService API 호출 (가용 시)
  └── ApprovalId 저장 (API 응답)
```

### 10.2 결재 처리

```
[대시보드: 결재 대기 목록에 표시]
  ↓
[문서 클릭 → TestInputView (BA1/BA2) 또는 DetailView (일반)]
  ↓
[CanApprove = true (현재 결재자 == 로그인 사용자)]
  ↓
[승인/반려 버튼]
  ├── 승인: ApproveDocumentAsync()
  │   ├── 다음 결재자 있음 → 다음 결재자에게 넘김
  │   └── 없음 → Approved 최종 처리
  └── 반려: RejectDocumentAsync() → Rejected 처리
```

### 10.3 외부 DB 연동

```
[TypeSelectionView: DB 파일 선택 버튼]
  ↓
[OpenFileDialog → .db 파일 선택]
  ↓
[ExternalDbRepository.Connect(filePath)]
  ↓
[기존 문서 목록 표시]
  ↓
[Clone 대상 선택 (선택 사항)]
  ↓
[타입 선택 → TestInputViewModel 초기화]
  ├── Clone 시: CloneChecklistFromDocumentAsync()
  └── 새 문서: 빈 체크리스트
  ↓
[수정 후 저장 → 내부 DB에 저장 (외부 DB는 읽기 전용 권장)]
```

---

## 11. 빌드 및 배포

### 11.1 빌드 요구사항

- Visual Studio 2019+ 또는 MSBuild 16+
- .NET Framework 4.7.2 SDK
- Windows x64

### 11.2 로컬 빌드

```bash
# Visual Studio에서 열기
open FlowMaster.sln

# 또는 MSBuild (명령행)
msbuild FlowMaster.sln /p:Configuration=Release /p:Platform=x64
```

### 11.3 ApprovalService 연동 실행

```bat
rem scripts/start-approval-api.bat 실행
cd C:\Works\DevSuite\ApprovalSystem\src\ApprovalService\ApprovalService.API
dotnet run --urls "http://localhost:5001" --environment Development
```

ApprovalService가 실행되지 않으면 FlowMaster는 **로컬 전용 모드**로 동작 (정상).

### 11.4 CI/CD (GitHub Actions)

`.github/workflows/ci-cd.yml` 파이프라인:
- **트리거**: main/master/develop 브랜치 push 또는 PR
- **Build & Test Job**: NuGet restore → MSBuild Release 빌드 → 아티팩트 업로드 (30일)
- **Create Release Job** (main/master push만): ZIP 압축 → 자동 GitHub Release
  - 태그 형식: `v2026.02.11-abc1234` (날짜-커밋해시)
  - Release 파일명: `FlowMaster-Release.zip`

### 11.4 배포 패키지 구성

**포함**:
```
FlowMaster.Desktop.exe
FlowMaster.Core.dll
FlowMaster.Domain.dll
FlowMaster.Infrastructure.dll
SQLite 네이티브 바이너리 (e_sqlite3.dll 등)
```

**제외** (`.antigravityignore` 참고):
- bin/, obj/
- .git/
- *.db (테스트 DB)
- 민감 정보 파일

---

## 12. 트러블슈팅

### Q: 앱 시작 시 DB 오류가 발생할 때
- `flowmaster_test.db` 파일 삭제 → 앱 재시작 시 자동 재생성
- `SQLitePCL.Batteries.Init()` 호출 순서 확인 (App.xaml.cs 최상단)

### Q: ApprovalService API 연결 실패
- 정상 동작 — API 미실행 시 로컬 모드로 자동 폴백
- `ApprovalApiClient.CheckConnectionAsync()` 반환값으로 연결 상태 확인

### Q: 외부 DB 연결 후 데이터가 표시되지 않을 때
- 외부 DB의 테이블명 확인: `approval_documents`, `checklist_items`
- `ExternalDbRepository.GetAllDocumentsAsync()` 내 컬럼명 매핑 확인

### Q: 체크리스트 저장 시 데이터 손실
- `SaveChecklistItemsAsync`는 DELETE → INSERT 방식
- 저장 전 현재 체크리스트 캡처 필요

### Q: BA1/BA2 기본 체크리스트 항목이 비어있을 때
- `GetDefaultChecklistItems()`는 더미 데이터를 반환 (미구현 상태)
- BA1: 22항목, BA2: 24항목의 실제 체크리스트 내용을 코드에 직접 입력해야 함

### Q: WriteView에서 결재가 정상 동작하지 않을 때
- WriteView/WriteViewModel은 기본 구조만 구현된 미완성 화면
- 실제 테스트 결과 입력은 TypeSelectionView → TestInputView 경로를 사용해야 함

### Q: 다단계 결재선이 제대로 동작하지 않을 때
- Core 레이어의 ApprovalService는 다단계(Sequential) 결재를 지원하지만
- UI(WriteView)에서는 결재자를 1명만 선택하도록 구현되어 있음
- 다단계 결재를 사용하려면 WriteViewModel 수정 필요

---

## 12-A. 미구현/알려진 제한사항

현재 MVP 구현에서 미완성이거나 제한이 있는 항목입니다.

| 항목 | 상태 | 설명 |
|------|------|------|
| BA1/BA2 기본 체크리스트 | ⚠️ 더미 | `GetDefaultChecklistItems()`가 placeholder 반환, 실제 항목 하드코딩 필요 |
| WriteView | ⚠️ 미완성 | 기본 결재 작성 화면, BA1/BA2 테스트 입력과 별개로 분리 운영 |
| 다단계 결재 UI | ⚠️ 제한적 | Core 로직은 다단계 지원, WriteViewModel은 결재자 1명만 선택 |
| AD 인증 | ❌ 미구현 | MockUserRepository (4명 하드코딩), 실제 AD 연동 없음 |
| Teams/Email 알림 | ❌ 미구현 | MockNotificationService → Console.WriteLine만 출력 |
| 대시보드 검색/필터 | ❌ 미구현 | 전체 목록만 표시, 검색 기능 없음 |
| 오프라인 API 동기화 | ❌ 미구현 | API 미연결 중 생성된 문서는 ApprovalId=null, 나중에 동기화 불가 |

---

## 13. 재구현 시 개선 권장 사항

현재 구현은 MVP(최소 기능 제품) 수준입니다. 동일 기능을 새로 구현한다면 다음 개선이 권장됩니다.

### 13.1 .NET 최신 버전으로 전환

**현재 문제**: .NET Framework 4.7.2는 Windows 전용, 구버전 런타임

**권장 개선**:
```xml
<TargetFramework>net9.0-windows</TargetFramework>
```

- **이점**: C# 최신 기능 활용, 성능 향상, cross-platform 가능성
- **WPF**: .NET 9에서도 완전 지원됨
- **netstandard2.0** 라이브러리들도 .NET 9와 호환됨

### 13.2 Avalonia UI로 전환 (크로스 플랫폼)

**현재 문제**: WPF는 Windows 전용

**권장 개선**: Avalonia UI 11.x
- Windows/macOS/Linux 동시 지원
- XAML 문법 유사 → 학습 비용 낮음
- 활성화된 오픈소스 커뮤니티

### 13.3 진정한 CQRS 패턴 적용

**현재 문제**: ApprovalService가 읽기/쓰기 로직을 혼합하여 복잡도 증가

**권장 개선**: MediatR + Command/Query 분리
```csharp
// Command
public record SubmitDocumentCommand(ApprovalDocument Doc, List<string> ApproverIds);
public class SubmitDocumentHandler : IRequestHandler<SubmitDocumentCommand, int> { ... }

// Query
public record GetPendingApprovalsQuery(string ApproverId);
public class GetPendingApprovalsHandler : IRequestHandler<...> { ... }
```

### 13.4 실제 AD 연동 지원

**현재 문제**: 사용자 관리가 `MockUserRepository` (하드코딩 4명)

**권장 개선**: 실제 AD/LDAP 연동 인터페이스 구현
```csharp
// 개발 환경
services.AddScoped<IUserRepository, MockUserRepository>();

// 프로덕션 환경
services.AddScoped<IUserRepository, AdUserRepository>();

public class AdUserRepository : IUserRepository
{
    public async Task<User> GetByAdAccountAsync(string account)
    {
        // Windows AD or Emulator API 사용
        using var ctx = new PrincipalContext(ContextType.Domain);
        // ...
    }
}
```

### 13.5 결재 알림 실제 구현

**현재 문제**: `MockNotificationService`가 Console.WriteLine만 출력

**권장 개선**: 실제 Teams / Email 알림
```csharp
// Teams Webhook
public class TeamsNotificationService : INotificationService
{
    public async Task SendAsync(string userId, string message)
    {
        await _httpClient.PostAsJsonAsync(webhookUrl, new { text = message });
    }
}
```

### 13.6 체크리스트 버전 관리

**현재 문제**: ChecklistItems를 저장할 때 전체 삭제 후 재삽입 → 이력 추적 불가

**권장 개선**: 변경 이력 추적
```sql
CREATE TABLE ChecklistHistory (
    HistoryId   INTEGER PRIMARY KEY,
    ItemId      INTEGER,
    DocId       INTEGER,
    ChangedBy   TEXT,
    ChangedAt   TEXT,
    FieldName   TEXT,
    OldValue    TEXT,
    NewValue    TEXT
);
```

### 13.7 다중 결재자 지원 강화

**현재 문제**: WriteViewModel에서 결재자를 1명만 선택 가능

**권장 개선**: 결재 라인 설계 UI
- 단계별 결재자 추가/삭제
- 병렬 결재 (AND/OR) 지원
- 결재선 템플릿 저장

### 13.8 오프라인 동기화

**현재 문제**: API 미실행 시 ApprovalId가 null → 나중에 동기화 불가

**권장 개선**: 로컬 큐 기반 동기화
```csharp
// 오프라인 저장 큐
public class PendingSyncQueue
{
    Task<int> EnqueueAsync(PendingOperation op);
    Task<int> ProcessPendingAsync(); // 연결 복구 시 일괄 전송
}
```

### 13.9 테스트 코드 추가

**현재 상태**: 단위 테스트/통합 테스트 없음

**권장 개선**:
```csharp
// xUnit + Moq
public class ApprovalServiceTests
{
    [Fact]
    public async Task SubmitDocument_ShouldSetStatusPending()
    {
        var mockRepo = new Mock<IApprovalRepository>();
        var svc = new ApprovalService(mockRepo.Object, ...);
        var docId = await svc.SubmitDocumentAsync(doc, new[] { "approver1" });
        mockRepo.Verify(r => r.CreateDocumentAsync(
            It.Is<ApprovalDocument>(d => d.Status == ApprovalStatus.Pending)));
    }
}
```

### 13.10 EF Core + SQLite로 전환

**현재 문제**: Dapper 사용 시 스키마 변경에 수동 마이그레이션 코드 필요

**권장 개선**: EF Core + Code First
```csharp
// EF Core Migration으로 스키마 자동 관리
dotnet ef migrations add AddApprovalDocument
dotnet ef database update
```

### 13.11 설정 파일 체계화

**현재 문제**: DB 경로가 코드에 하드코딩 (`flowmaster_test.db`)

**권장 개선**: appsettings.json + `Microsoft.Extensions.Configuration`
```json
{
  "Database": {
    "LocalPath": "data/flowmaster.db"
  },
  "ApprovalApi": {
    "BaseUrl": "http://localhost:5001",
    "Timeout": 5
  }
}
```

### 13.12 접근성 및 국제화

**현재 문제**:
- UI 텍스트가 코드/XAML에 한국어로 하드코딩
- 접근성(스크린리더 등) 미고려

**권장 개선**:
```xml
<!-- XAML 리소스 딕셔너리 -->
<sys:String x:Key="Submit">결재 상신</sys:String>

<!-- 접근성 -->
<Button AutomationProperties.Name="{StaticResource Submit}" />
```

---

## 정리 요약

| 항목 | 내용 |
|------|------|
| **프로젝트 목적** | 테스트 결과 입력 및 전자결재 데스크톱 앱 |
| **언어** | C# 8.0 |
| **프레임워크** | .NET Framework 4.7.2, WPF |
| **아키텍처** | Clean Architecture (Domain/Core/Infrastructure/Desktop) |
| **DB** | SQLite (Dapper ORM) |
| **UI 패턴** | MVVM (CommunityToolkit.Mvvm) |
| **결재 API** | 선택적 연동 (미실행 시 로컬 폴백) |
| **사용자 관리** | Mock (개발용 4명) |
| **빌드** | Visual Studio / MSBuild, CI/CD (GitHub Actions) |
| **주요 화면** | 대시보드, 결재 작성, 테스트 입력 (BA1/BA2), 상세 보기 |

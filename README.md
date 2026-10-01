# MyExpenses

MyExpenses는 C#과 Blazor로 만든 다중 사용자 지출 기록 웹앱입니다. Google 계정으로 로그인한 사용자마다 독립된 지출·예산·정기 지출 공간을 사용하며, 데이터는 실행한 컴퓨터의 SQLite 파일에 저장됩니다.

## 기능

- Google 로그인: 로그인할 때 계정을 선택하고, 사용 후 로그아웃
- 사용자별 데이터 분리: 지출, 예산, 정기 지출 규칙, 즐겨찾기 템플릿, 통계, CSV 내보내기에 현재 로그인 사용자의 데이터만 사용
- 첫 로그인 안내: 신규 사용자에게 시작 화면을 보여준 뒤 빈 대시보드에서 시작
- 지출 추가·수정·삭제: 날짜, 원 단위 금액, 카테고리, 메모 관리
- 즐겨찾기 지출 템플릿: 자주 쓰는 이름·금액·카테고리·메모를 저장·수정·삭제하고 새 지출 입력 시 선택해 자동 입력
- 전체 삭제: 확인 단계를 거친 후 모든 지출 기록 삭제
- 지출 검색·상세 필터: 메모 키워드, 월·카테고리, 시작일·종료일, 최소·최대 금액으로 내역·건수·합계 조회; 최신순·오래된순·금액순 정렬
- 지출 요약: 이번 달 지출 합계와 기록 건수 표시
- 월별 예산: 월별 총예산과 카테고리별 예산을 각각 설정·변경·삭제하고 사용률·남은 금액·초과 금액 표시
- 정기 지출: 매월 지정일·금액·카테고리·메모를 저장하고 접속 시 도래한 지출 자동 생성; 규칙 수정·중지·삭제 가능
- 카테고리별 통계: 이번 달 또는 필터 결과의 금액·건수·비율 표시
- 월별 지출 추이: 선택한 월까지 6개월 합계 그래프와 전월 대비 금액·증감률, 카테고리별 비교
- CSV 내보내기: 현재 조회 결과 또는 전체 기록 다운로드
- CSV 가져오기: 내보낸 CSV의 날짜·금액·카테고리·메모를 검사하고 미리보기 후 내 계정에 저장; 기본값은 중복 건너뛰기
- 계정 및 데이터 삭제: 확인 절차 후 현재 사용자의 지출·예산·정기 지출 규칙·즐겨찾기 템플릿·로그인 연결 정보 삭제
- 공개 페이지: 로그인 없이 열람할 수 있는 서비스 소개, 개인정보 처리방침, 이용약관
- SQLite 저장: 앱을 종료하거나 페이지를 새로고침해도 기록 유지
- Docker 배포: 비루트 이미지, 비루트 사용자, 역방향 프록시, 영구 저장 경로 지원

## 기술 스택

- C# / .NET 10
- ASP.NET Core Blazor Web App (Interactive Server)
- ASP.NET Core Identity / Google OAuth 2.0 (쿠키 인증)
- Entity Framework Core 10 / SQLite
- HTML / CSS

## 프로젝트 구조

주요 파일과 폴더는 다음과 같습니다. `*.razor`는 Blazor 화면·컴포넌트이고, 같은 이름의 `*.razor.css`는 해당 컴포넌트에만 적용되는 스타일입니다.

```text
MyExpenses/
├── Program.cs                      # 앱 시작, 서비스·인증 설정, CSV 내보내기·상태 확인 엔드포인트
├── AccountEndpoints.cs             # Google 로그인 콜백, 로그아웃, 계정 삭제 엔드포인트
├── MyExpenses.csproj               # .NET 대상 버전과 NuGet 패키지 참조
├── Components/
│   ├── App.razor                   # HTML 문서와 앱 진입점
│   ├── Routes.razor                # 페이지 라우팅과 인증 처리
│   ├── _Imports.razor              # 컴포넌트 공통 네임스페이스
│   ├── Layout/                     # 공통 레이아웃, 메뉴, 연결 복구 UI
│   ├── Charts/
│   │   └── ExpenseTrendChart.razor  # 월별 지출 SVG 막대그래프
│   └── Pages/
│       ├── Home.razor              # 지출 CRUD, 검색·필터, 예산, 카테고리 통계
│       ├── Import.razor            # CSV 가져오기와 미리보기
│       ├── Recurring.razor         # 정기 지출 규칙 관리
│       ├── Templates.razor         # 즐겨찾기 지출 템플릿 관리
│       ├── Trends.razor            # 월별 추이와 전월 비교
│       ├── Login.razor             # Google 로그인 화면
│       ├── Welcome.razor           # 신규 사용자 시작 안내
│       ├── Account.razor           # 계정·데이터 삭제 화면
│       ├── About.razor             # 서비스 소개
│       ├── Privacy.razor           # 개인정보 처리방침
│       └── Terms.razor             # 이용약관
├── Data/
│   ├── ExpenseRecord.cs            # 지출 데이터 모델
│   ├── ExpenseTemplate.cs          # 즐겨찾기 템플릿 모델과 입력 검증
│   ├── ExpenseTemplateService.cs   # 사용자별 템플릿 조회·저장·삭제
│   ├── MonthlyBudget.cs            # 전체 월 예산 모델
│   ├── CategoryBudget.cs           # 카테고리별 월 예산 모델
│   ├── RecurringExpenseRule.cs     # 정기 지출 규칙과 월별 처리 이력 모델
│   ├── UserProfile.cs              # 사용자별 첫 시작 완료 상태
│   ├── ExpensesDbContext.cs        # 지출·예산·정기 지출·프로필 EF Core 컨텍스트
│   ├── AuthDbContext.cs            # Identity 계정 EF Core 컨텍스트
│   ├── BudgetSchema.cs             # 데이터 테이블 생성과 기존 스키마 보완
│   ├── ExpenseFilter.cs            # 조회 조건과 정렬을 EF Core 쿼리에 적용
│   ├── ExpenseSearchInput.cs       # 화면·CSV 요청의 검색 조건 검증
│   ├── ExpenseStatistics.cs        # 카테고리별 금액·건수·비율 계산
│   ├── ExpenseTrends.cs            # 월별 추이와 전월 비교 계산
│   ├── ExpenseTrendsService.cs     # 사용자별 추이 데이터 조회
│   ├── ExpenseCsvExporter.cs       # CSV 파일 생성
│   ├── ExpenseCsvImporter.cs       # CSV 파싱과 유효성 검사
│   ├── ExpenseCsvImportService.cs  # 중복 확인과 사용자별 CSV 저장
│   ├── RecurringExpenseService.cs  # 정기 지출 생성과 중복 처리 방지
│   ├── UserDataProvisioner.cs      # 사용자 공간 초기화와 시작 상태 관리
│   └── UserDataDeletionService.cs  # 현재 사용자 소유 데이터 삭제
├── wwwroot/                       # 공통 CSS, Bootstrap, favicon 등 정적 파일
├── Properties/launchSettings.json # 로컬 실행 프로필과 접속 주소
├── tests/
│   ├── ExpenseSearchChecks/       # 메모리 SQLite 기반 검색·필터 검증
│   └── ExpenseTemplateChecks/     # 템플릿 CRUD·사용자 격리·스키마 보완 검증
├── appsettings.json               # 공통 설정
├── appsettings.Development.json   # 개발 환경 설정
├── Dockerfile                     # .NET 빌드와 비루트 실행 이미지
├── .dockerignore                  # Docker 빌드에서 제외할 파일
├── .gitignore                     # DB·비밀 설정·빌드 결과 등의 Git 제외 규칙
└── README.md
```

화면과 사용자 입력은 `Components/`에서 처리하고, 데이터 모델·조회 조건·통계 계산·저장 서비스는 `Data/`에 있습니다. `Program.cs`에서 서비스와 인증을 구성하고, `AccountEndpoints.cs`에서 계정 관련 HTTP 요청을 처리합니다. 지출·예산 화면의 일부 저장·조회 로직은 현재 `Home.razor`의 C# 코드에 포함되어 있습니다.

실행 시 생성되는 `Data/myexpenses.db`, `Data/auth.db`, `Data/keys/`와 로컬 비밀 설정인 `.env.docker`는 Git에 포함되지 않습니다. 저장 경로를 별도로 설정하거나 Docker를 사용하면 DB와 키는 설정된 영구 저장 경로에 생성됩니다.

## 실행 방법

[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)가 필요합니다. macOS, Windows, Linux에서 .NET SDK가 설치된 터미널로 다음 명령을 실행하세요. VS Code는 선택 사항입니다.

```bash
git clone https://github.com/casey1425/MyExpenses.git
cd MyExpenses
dotnet restore
```

### Google OAuth 설정

1. [Google Cloud Console](https://console.cloud.google.com/apis/credentials)에서 프로젝트와 OAuth 동의 화면을 설정합니다. 여러 Google 사용자를 받으려면 대상을 **외부(External)**로 선택합니다.
2. OAuth 클라이언트를 **웹 애플리케이션** 유형으로 만듭니다.
3. 승인된 리디렉션 URI에 `https://localhost:7168/signin-google`을 등록합니다.
4. 발급된 Client ID와 Client Secret을 프로젝트 폴더에서 User Secrets에 저장합니다.

```bash
dotnet user-secrets set "Authentication:Google:ClientId" "발급받은 Client ID"
dotnet user-secrets set "Authentication:Google:ClientSecret" "발급받은 Client Secret"
dotnet user-secrets set "Support:Email" "사용자 문의를 받을 이메일"
dotnet dev-certs https --trust
dotnet run --launch-profile https
```

OAuth 앱이 테스트 상태라면 Google Cloud에 등록한 테스트 사용자만 로그인할 수 있습니다. 일반 사용자가 로그인하게 하려면 OAuth 동의 화면을 게시해야 합니다. Client Secret은 저장소 설정 파일에 작성하거나 Git에 커밋하지 마세요.

브라우저에서 [https://localhost:7168](https://localhost:7168)을 열면 됩니다. 앱을 종료하려면 터미널에서 `Ctrl+C`를 누르세요. 포트가 이미 사용 중이라면 `Properties/launchSettings.json`의 `applicationUrl`과 Google Cloud에 등록한 리디렉션 URI를 함께 변경해야 합니다.

### 즐겨찾기 지출 템플릿

로그인 후 **즐겨찾기 지출** (`/templates`)에서 이름(1~50자), 원 단위 금액, 카테고리, 메모(선택·최대 100자)를 저장합니다. 지출 기록 화면의 **새 지출 추가 → 즐겨찾기 템플릿**에서 선택하면 금액·카테고리·메모를 입력하고 날짜는 그대로 유지합니다. 선택 시 현재 입력된 금액·카테고리·메모를 덮어쓰므로 내용을 확인해 주세요. 필요한 내용을 수정한 후 **지출 기록하기**를 눌러야 실제 지출로 저장됩니다. 다른 탭에서 템플릿을 수정했다면 **목록 새로고침**으로 목록을 갱신할 수 있습니다.

템플릿은 현재 로그인 사용자만 조회·수정·삭제할 수 있고 정기 지출처럼 자동으로 기록을 생성하지 않습니다. 템플릿을 변경하거나 삭제해도 이미 기록한 지출은 유지됩니다. 지출 전체 삭제 시 템플릿은 유지되지만 계정 삭제 시에는 함께 삭제됩니다. CSV에는 실제 지출만 포함되며 템플릿은 포함되지 않습니다.

### 지출 검색과 상세 필터

지출 내역에서 메모 검색어, 월·카테고리, 시작일·종료일, 최소·최대 금액을 입력하고 **조회**를 누릅니다. 모든 조건은 함께 적용되므로 월과 날짜 범위를 동시에 지정하면 두 조건에 모두 해당하는 기록만 조회합니다. 시작일·종료일과 금액 경계값을 포함하며, 비워 둔 항목은 제한하지 않습니다. 메모 검색은 앞뒤 공백을 제거한 문자열을 그대로 포함하는지 확인하며 영문 대소문자를 구분합니다. 정렬은 최신순(기본), 오래된순, 금액 높은순·낮은순을 지원합니다. **초기화**하면 모든 조건과 정렬을 기본값으로 되돌립니다.

적용된 조건과 결과 건수·합계를 지출 내역 위에서 확인할 수 있고, 카테고리 통계와 **현재 조회 결과 CSV**도 같은 조회 조건을 사용합니다. CSV의 행 순서는 선택한 정렬과 같습니다. 예산 집계는 검색 조건에 영향을 받지 않습니다. 잘못된 날짜·금액 범위는 안내 후 이전 결과를 유지합니다. 검색·필터·정렬은 C#과 EF Core로 SQLite에서 처리하며 현재 로그인 사용자의 기록만 조회합니다.

### CSV 가져오기

로그인 후 메뉴의 **CSV 가져오기** 또는 지출 내역의 링크를 엽니다. MyExpenses에서 내보낸 UTF-8 CSV 파일을 선택하면 내용과 중복 후보를 먼저 확인할 수 있습니다. 오류가 있는 파일은 저장할 수 없으며, 확인 버튼을 누른 후에만 현재 로그인 계정에 지출이 추가됩니다. 기본값은 같은 날짜·금액·카테고리·메모를 가진 기록을 건너뛰는 방식이고, 필요하면 중복 후보를 포함할 수 있습니다. 파일은 최대 1MB, 지출은 한 번에 최대 1,000건까지 지원합니다. 전체/카테고리 예산은 CSV에 포함되지 않습니다.

### 정기 지출

로그인 후 **정기 지출** (`/recurring`)에서 매월 지정일·금액·카테고리·메모를 등록합니다. 등록한 달부터 적용되며 지정일이 이미 지났다면 즉시 기록합니다. 지출 기록 또는 정기 지출 페이지에 접속하면 누락된 달의 지출을 채우고, 같은 규칙·같은 달은 한 번만 처리합니다. 29~31일이 없는 달에는 말일에 기록합니다. 규칙을 중지했다가 다시 시작하면 중지 기간은 소급하지 않습니다. 규칙을 수정하거나 삭제해도 이미 생성된 지출은 바뀌지 않으며, 생성된 지출을 삭제해도 해당 달에 다시 생성되지는 않습니다. CSV에는 지출 기록만 포함되고 정기 지출 규칙은 포함되지 않습니다.

### 카테고리별 월 예산

지출 기록 화면의 월별 예산에서 월을 선택하면 아래에 식비·카페·교통·쇼핑·생활·기타 각각의 지출과 예산이 표시됩니다. 카테고리별로 예산을 설정·변경·삭제할 수 있으며, 사용률과 남은 금액 또는 초과 금액을 확인할 수 있습니다. 지출이 예산을 초과해도 기록은 계속할 수 있습니다. 카테고리 예산은 전체 월 예산과 독립적이며, 한쪽을 변경하거나 삭제해도 다른 쪽은 그대로입니다. 계정을 삭제하면 해당 계정의 카테고리 예산도 삭제됩니다. CSV에는 예산이 포함되지 않습니다.

### 월별 지출 추이

로그인 후 **월별 지출 추이** (`/trends`)에서 조회할 월을 선택합니다. 선택한 월까지 6개월의 지출 합계를 SVG 막대그래프로 표시하며, 기록이 없는 달도 0원으로 포함합니다. 이번 달은 한국 시간 기준 오늘까지의 지출을 전월의 같은 날짜까지와 비교하고, 전월에 해당 날짜가 없으면 말일까지 사용합니다. 과거 월은 전월과 각각 한 달 전체를 비교합니다. 전월 지출이 0원이면 증감률을 계산하지 않고 안내 문구를 표시합니다. 카테고리별 비교에도 같은 기간을 적용하며 미래 날짜의 지출은 이번 달 집계에서 제외합니다.

## 검증

검색·필터의 SQLite 검증은 `dotnet run --project tests/ExpenseSearchChecks`로 실행할 수 있습니다. 테스트는 메모리 데이터베이스를 사용하며 실제 사용자 데이터를 변경하지 않습니다.

템플릿의 저장·수정·삭제, 사용자 격리, 기존 DB 스키마 보완과 계정 삭제 검증은 `dotnet run --project tests/ExpenseTemplateChecks`로 실행합니다. 이 테스트도 메모리 데이터베이스만 사용합니다.

## 로컬 Docker 실행

Docker Desktop이 실행 중이어야 합니다. Google Cloud의 웹 애플리케이션 OAuth 클라이언트에 `http://localhost:10000/signin-google`을 승인된 리디렉션 URI로 추가하세요. 프로젝트 폴더에 Git이 무시하는 `.env.docker` 파일을 만들고 다음 값을 입력합니다.

```text
ASPNETCORE_ENVIRONMENT=Development
ReverseProxy__UseForwardedHeaders=false
Authentication__Google__ClientId=발급받은_Client_ID
Authentication__Google__ClientSecret=발급받은_Client_Secret
Support__Email=사용자_문의_이메일
```

```bash
docker build -t myexpenses:latest .
docker volume create myexpenses-data
docker run -d --name myexpenses-local --env-file .env.docker \
  -p 10000:10000 -v myexpenses-data:/app/Data myexpenses:latest
```

브라우저에서 `http://localhost:10000`을 엽니다. 컨테이너는 `docker stop myexpenses-local`로 중지하고 `docker start myexpenses-local`로 다시 시작할 수 있습니다.

코드를 변경한 뒤 Docker에 반영하려면 이미지를 다시 빌드하고 컨테이너를 교체합니다. 기존 데이터가 저장된 **같은 이름의 `myexpenses-data` 볼륨**을 계속 연결해야 합니다. 아래 명령은 컨테이너만 교체하며 볼륨은 삭제하지 않습니다.

```bash
docker build -t myexpenses:latest .
docker stop myexpenses-local
docker rm myexpenses-local
docker run -d --name myexpenses-local --env-file .env.docker \
  -p 10000:10000 -v myexpenses-data:/app/Data myexpenses:latest
curl http://localhost:10000/healthz
```

## Docker 배포

저장소의 `Dockerfile`은 .NET 10 앱을 Release 모드로 게시하고 운영 이미지에서 비루트 `app` 사용자로 실행합니다. 컨테이너는 `0.0.0.0:10000`을 사용하며 다음 경로를 제공합니다.

- 공개 홈페이지: `/about`
- 개인정보 처리방침: `/privacy`
- 이용약관: `/terms`
- 배포 상태 확인: `/healthz`

운영 환경에서는 다음 환경변수를 설정합니다.

```text
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_HTTP_PORTS=10000
Storage__DataDirectory=/app/Data
ReverseProxy__UseForwardedHeaders=true
Authentication__Google__ClientId=...
Authentication__Google__ClientSecret=...
Support__Email=...
```

`/app/Data`에는 SQLite 데이터베이스와 ASP.NET Core 데이터 보호 키가 저장됩니다. 이 경로는 호스팅 제공자의 암호화된 영구 디스크에 마운트해야 합니다. 임시 파일 시스템에 두면 재배포 시 계정과 지출 데이터가 사라질 수 있습니다. SQLite를 사용하는 동안은 앱 인스턴스를 하나로 유지하세요.

`ReverseProxy__UseForwardedHeaders=true`는 Render처럼 신뢰할 수 있는 로드 밸런서가 HTTPS를 종료하는 환경에서만 사용해야 합니다. 앱을 인터넷에 직접 노출할 때는 활성화하지 마세요.

## 데이터 보관 및 사용 범위

첫 실행 시 지출·전체/카테고리 예산·정기 지출 규칙을 저장하는 `Data/myexpenses.db`와 로그인 계정을 저장하는 `Data/auth.db`가 자동으로 생성됩니다. 두 파일과 `Data/keys`는 `.gitignore`에 의해 Git에 포함되지 않습니다. 백업하려면 앱을 중지한 뒤 두 DB 파일과 `Data/keys`를 함께 복사하세요. Docker에서는 `myexpenses-data` 볼륨에 저장됩니다.

계정 정보는 `auth.db`에, 지출·전체/카테고리 예산·정기 지출 규칙·즐겨찾기 템플릿·첫 시작 완료 여부는 `myexpenses.db`에 저장됩니다. 이 데이터는 ASP.NET Core Identity의 사용자 ID로 분리됩니다. 기존 단일 사용자 데이터베이스를 업그레이드하면 기존 데이터는 업그레이드 후 처음 로그인한 계정에 귀속됩니다.

로그인한 사용자는 `/account`에서 계정을 삭제할 수 있습니다. 삭제 시 해당 사용자의 지출, 전체/카테고리 예산, 정기 지출 규칙, 즐겨찾기 템플릿, 프로필과 Google 로그인 연결 정보가 제거됩니다. `/about`, `/privacy`, `/terms`는 공개 페이지입니다. 배포 운영자는 자신의 실제 운영·백업·법적 요구사항에 맞게 문서를 검토하고 `Support:Email`을 반드시 설정해야 합니다.

인터넷에 배포하려면 HTTPS 주소를 Google OAuth 리디렉션 URI에 별도로 등록하고, 비밀 키 관리와 데이터 백업을 구성해야 합니다. 전체 삭제는 현재 로그인한 사용자의 지출만 삭제하며 되돌릴 수 없습니다.

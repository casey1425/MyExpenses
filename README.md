# MyExpenses

MyExpenses는 C#과 Blazor로 만든 다중 사용자 지출 기록 웹앱입니다. Google 계정으로 로그인한 사용자마다 독립된 지출·예산 공간을 사용하며, 데이터는 실행한 컴퓨터의 SQLite 파일에 저장됩니다.

## 기능

- Google 로그인: 로그인할 때 계정을 선택하고, 사용 후 로그아웃
- 사용자별 데이터 분리: 지출, 예산, 통계, CSV 내보내기에 현재 로그인 사용자의 데이터만 사용
- 첫 로그인 안내: 신규 사용자에게 시작 화면을 보여준 뒤 빈 대시보드에서 시작
- 지출 추가·수정·삭제: 날짜, 원 단위 금액, 카테고리, 메모 관리
- 전체 삭제: 확인 단계를 거친 후 모든 지출 기록 삭제
- 월·카테고리 필터: 조건에 맞는 내역, 건수, 합계 조회
- 지출 요약: 이번 달 지출 합계와 기록 건수 표시
- 월별 예산: 월별 총예산 설정·변경·삭제 및 전체 지출 대비 사용률·남은 금액 표시
- 카테고리별 통계: 이번 달 또는 필터 결과의 금액·건수·비율 표시
- CSV 내보내기: 현재 조회 결과 또는 전체 기록 다운로드
- 계정 및 데이터 삭제: 확인 절차 후 현재 사용자의 지출·예산·로그인 연결 정보 삭제
- 공개 페이지: 로그인 없이 열람할 수 있는 서비스 소개, 개인정보 처리방침, 이용약관
- SQLite 저장: 앱을 종료하거나 페이지를 새로고침해도 기록 유지
- Docker 배포: 비루트 이미지, 비루트 사용자, 역방향 프록시, 영구 저장 경로 지원

## 기술 스택

- C# / .NET 10
- ASP.NET Core Blazor Web App (Interactive Server)
- ASP.NET Core Identity / Google OAuth 2.0 (쿠키 인증)
- Entity Framework Core 10 / SQLite
- HTML / CSS

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
docker run --name myexpenses-local --env-file .env.docker \
  -p 10000:10000 -v myexpenses-data:/app/Data myexpenses:latest
```

브라우저에서 `http://localhost:10000`을 엽니다. 컨테이너를 중지하려면 터미널에서 `Ctrl+C`를 누르세요. 다시 실행할 때는 `docker start -a myexpenses-local`을 사용합니다. 이미지를 새로 빌드해 컨테이너를 교체할 때도 같은 `myexpenses-data` 볼륨을 연결해야 계정과 지출이 유지됩니다.

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

첫 실행 시 지출·예산을 저장하는 `Data/myexpenses.db`와 로그인 계정을 저장하는 `Data/auth.db`가 자동으로 생성됩니다. 두 파일은 `.gitignore`에 의해 Git에 포함되지 않습니다. 백업하려면 앱을 종료한 뒤 두 파일을 다른 위치에 복사하세요.

계정 정보는 `auth.db`에, 지출·예산·첫 시작 완료 여부는 `myexpenses.db`에 저장됩니다. 모든 지출과 예산은 ASP.NET Core Identity의 사용자 ID로 분리됩니다. 기존 단일 사용자 데이터베이스를 업그레이드하면 기존 데이터는 업그레이드 후 처음 로그인한 계정에 귀속됩니다.

로그인한 사용자는 `/account`에서 계정을 삭제할 수 있습니다. 삭제 시 해당 사용자의 지출, 예산, 프로필과 Google 로그인 연결 정보가 제거됩니다. `/about`, `/privacy`, `/terms`는 공개 페이지입니다. 배포 운영자는 자신의 실제 운영·백업·법적 요구사항에 맞게 문서를 검토하고 `Support:Email`을 반드시 설정해야 합니다.

인터넷에 배포하려면 HTTPS 주소를 Google OAuth 리디렉션 URI에 별도로 등록하고, 비밀 키 관리와 데이터 백업을 구성해야 합니다. 전체 삭제는 현재 로그인한 사용자의 지출만 삭제하며 되돌릴 수 없습니다.

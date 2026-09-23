# MyExpenses

MyExpenses는 C#과 Blazor로 만든 개인 지출 기록 웹앱입니다. 지출을 기록하고 월·카테고리별로 조회하거나, 카테고리별 통계를 확인할 수 있습니다. 데이터는 실행한 컴퓨터의 SQLite 파일에 저장됩니다.

## 기능

- Google 로그인 보호: 지정한 Gmail 계정 하나로 로그인·로그아웃
- 지출 추가·수정·삭제: 날짜, 원 단위 금액, 카테고리, 메모 관리
- 전체 삭제: 확인 단계를 거친 후 모든 지출 기록 삭제
- 월·카테고리 필터: 조건에 맞는 내역, 건수, 합계 조회
- 지출 요약: 이번 달 지출 합계와 기록 건수 표시
- 월별 예산: 월별 총예산 설정·변경·삭제 및 전체 지출 대비 사용률·남은 금액 표시
- 카테고리별 통계: 이번 달 또는 필터 결과의 금액·건수·비율 표시
- CSV 내보내기: 현재 조회 결과 또는 전체 기록 다운로드
- SQLite 저장: 앱을 종료하거나 페이지를 새로고침해도 기록 유지

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

1. [Google Cloud Console](https://console.cloud.google.com/apis/credentials)에서 프로젝트와 OAuth 동의 화면을 설정합니다.
2. OAuth 클라이언트를 **웹 애플리케이션** 유형으로 만듭니다.
3. 승인된 리디렉션 URI에 `https://localhost:7168/signin-google`을 등록합니다.
4. 발급된 값과 로그인에 사용할 Gmail 주소를 프로젝트 폴더에서 User Secrets에 저장합니다.

```bash
dotnet user-secrets set "Authentication:Google:ClientId" "발급받은 Client ID"
dotnet user-secrets set "Authentication:Google:ClientSecret" "발급받은 Client Secret"
dotnet user-secrets set "Authentication:Google:AllowedEmail" "허용할 Gmail 주소"
dotnet dev-certs https --trust
dotnet run --launch-profile https
```

OAuth 앱이 테스트 상태라면 같은 Gmail 주소를 Google Cloud의 테스트 사용자에도 추가해야 합니다. Client Secret과 Gmail 주소는 저장소 설정 파일에 작성하거나 Git에 커밋하지 마세요.

브라우저에서 [https://localhost:7168](https://localhost:7168)을 열면 됩니다. 앱을 종료하려면 터미널에서 `Ctrl+C`를 누르세요. 포트가 이미 사용 중이라면 `Properties/launchSettings.json`의 `applicationUrl`과 Google Cloud에 등록한 리디렉션 URI를 함께 변경해야 합니다.

## 데이터 보관 및 사용 범위

첫 실행 시 지출·예산을 저장하는 `Data/myexpenses.db`와 로그인 계정을 저장하는 `Data/auth.db`가 자동으로 생성됩니다. 두 파일은 `.gitignore`에 의해 Git에 포함되지 않습니다. 백업하려면 앱을 종료한 뒤 두 파일을 다른 위치에 복사하세요.

기존 데이터베이스에 예산 테이블이 없는 경우, 앱 시작 시 지출 기록을 유지한 채 예산 테이블을 생성합니다.

`Authentication:Google:AllowedEmail`에 지정한 Google 계정만 로그인할 수 있습니다. 처음 로그인하면 해당 계정 정보가 `auth.db`에 자동으로 연결됩니다. 현재는 단일 사용자용으로 모든 지출 기록을 한 계정이 사용합니다. 인터넷에 배포하려면 HTTPS 주소를 Google OAuth 리디렉션 URI에 별도로 등록하고 비밀 키 관리와 데이터 백업을 구성해야 합니다. 전체 삭제는 되돌릴 수 없습니다.

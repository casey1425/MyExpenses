# MyExpenses

MyExpenses는 C#과 Blazor로 만든 개인 지출 기록 웹앱입니다. 지출을 기록하고 월·카테고리별로 조회하거나, 카테고리별 통계를 확인할 수 있습니다. 데이터는 실행한 컴퓨터의 SQLite 파일에 저장됩니다.

## 기능

- 로그인 보호: 최초 관리자 계정 생성, 로그인 상태 유지, 로그인 실패 잠금, 로그아웃
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
- ASP.NET Core Identity (쿠키 인증 및 비밀번호 해싱)
- Entity Framework Core 10 / SQLite
- HTML / CSS

## 실행 방법

[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)가 필요합니다. macOS, Windows, Linux에서 .NET SDK가 설치된 터미널로 다음 명령을 실행하세요. VS Code는 선택 사항입니다.

```bash
git clone https://github.com/casey1425/MyExpenses.git
cd MyExpenses
dotnet restore
dotnet run --launch-profile http
```

브라우저에서 [http://localhost:5168](http://localhost:5168)을 열면 됩니다. 앱을 종료하려면 터미널에서 `Ctrl+C`를 누르세요. 포트가 이미 사용 중이라면 `Properties/launchSettings.json`의 `applicationUrl`을 변경할 수 있습니다.

## 데이터 보관 및 사용 범위

첫 실행 시 지출·예산을 저장하는 `Data/myexpenses.db`와 로그인 계정을 저장하는 `Data/auth.db`가 자동으로 생성됩니다. 두 파일은 `.gitignore`에 의해 Git에 포함되지 않습니다. 백업하려면 앱을 종료한 뒤 두 파일을 다른 위치에 복사하세요.

기존 데이터베이스에 예산 테이블이 없는 경우, 앱 시작 시 지출 기록을 유지한 채 예산 테이블을 생성합니다.

처음 접속하면 관리자 계정을 한 번만 만들 수 있으며, 이후에는 추가 회원가입이 차단됩니다. 현재는 단일 사용자용으로 모든 지출 기록을 한 관리자 계정이 사용합니다. 인터넷에 배포하려면 HTTPS, 비밀 키 관리, 데이터 백업 등 운영 환경의 추가 보안 설정이 필요합니다. 전체 삭제는 되돌릴 수 없습니다.

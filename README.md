# MyExpenses

MyExpenses는 C#과 Blazor로 만든 개인 지출 기록 웹앱입니다. 지출을 기록하고 월·카테고리별로 조회하거나, 카테고리별 통계를 확인할 수 있습니다. 데이터는 실행한 컴퓨터의 SQLite 파일에 저장됩니다.

## 기능

- 지출 추가·수정·삭제: 날짜, 원 단위 금액, 카테고리, 메모 관리
- 전체 삭제: 확인 단계를 거친 후 모든 지출 기록 삭제
- 월·카테고리 필터: 조건에 맞는 내역, 건수, 합계 조회
- 지출 요약: 이번 달 지출 합계와 기록 건수 표시
- 카테고리별 통계: 이번 달 또는 필터 결과의 금액·건수·비율 표시
- CSV 내보내기: 현재 조회 결과 또는 전체 기록 다운로드
- SQLite 저장: 앱을 종료하거나 페이지를 새로고침해도 기록 유지

## 기술 스택

- C# / .NET 10
- ASP.NET Core Blazor Web App (Interactive Server)
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

첫 실행 시 `Data/myexpenses.db`가 자동으로 생성됩니다. 이 파일은 `.gitignore`에 의해 Git에 포함되지 않습니다. 백업하려면 앱을 종료한 뒤 파일을 다른 위치에 복사하세요.

이 앱에는 로그인과 사용자별 데이터 분리 기능이 없습니다. 개인 로컬 환경에서 사용하고, 인증 기능 없이 인터넷에 공개 배포하지 마세요. 전체 삭제는 되돌릴 수 없습니다.

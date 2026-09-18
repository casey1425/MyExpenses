# MyExpenses

C#과 Blazor로 만든 개인 지출 기록 웹앱입니다. macOS의 VS Code에서 시작한 개인 프로젝트로, 날짜·금액·카테고리·메모를 기록하고 월별 지출을 확인할 수 있습니다.

## 현재 기능

- 지출 추가: 날짜, 원 단위 금액, 카테고리, 메모 입력
- 지출 내역: 최신 날짜순 목록과 전체 기록 건수 표시
- 이번 달 지출 합계 표시
- 월·카테고리별 조회: 선택한 조건에 맞는 목록·건수·합계 표시
- 지출 수정: 기존 기록의 날짜·금액·카테고리·메모 변경
- 개별 지출 삭제
- 전체 삭제: 필터를 초기화한 상태에서만 가능하며, 삭제 건수와 주의 문구를 보여주는 확인 단계를 거친 뒤 실행
- SQLite에 저장: 앱을 종료하거나 페이지를 새로고침해도 기록 유지

전체 삭제는 되돌릴 수 없습니다. 아직 로그인이나 사용자별 데이터 분리 기능이 없으므로 개인 로컬 환경에서 사용하는 것을 전제로 합니다.

## 개발 경과

1. Blazor 기본 템플릿으로 프로젝트를 생성했습니다.
2. 기본 홈 화면을 지출 입력 폼, 요약 카드, 내역 목록으로 교체했습니다.
3. 메모리에만 있던 지출 기록을 EF Core와 SQLite에 저장하도록 변경했습니다.
4. 실수로 누르는 일을 줄이기 위해 확인 단계가 있는 전체 삭제 기능을 추가했습니다.
5. 월별·카테고리별 필터와 조회 결과 합계를 추가했습니다.
6. 기존 기록을 삭제하지 않고 수정할 수 있는 기능을 추가했습니다.

## 기술 구성

- C# / .NET 10
- ASP.NET Core Blazor Web App (Interactive Server)
- Entity Framework Core 10 + SQLite
- HTML / CSS

## 실행 방법

### 준비물

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- VS Code는 선택 사항이며, 사용할 경우 Microsoft의 C# Dev Kit 확장을 권장합니다.

```bash
git clone https://github.com/casey1425/MyExpenses.git
cd MyExpenses
dotnet restore
dotnet run --launch-profile http
```

브라우저에서 `http://localhost:5168`을 엽니다. 종료하려면 실행 중인 터미널에서 `Control + C`를 누릅니다. 포트가 이미 사용 중이라면 `Properties/launchSettings.json`의 `applicationUrl`을 변경할 수 있습니다.

## 데이터 보관

앱을 처음 실행하면 `Data/myexpenses.db`가 자동으로 만들어집니다. 데이터는 이 컴퓨터에만 저장되며 GitHub에는 업로드되지 않도록 `.gitignore`에 등록되어 있습니다. 백업하려면 앱을 종료한 뒤 이 파일을 별도 위치에 복사하세요.

현재 데이터베이스는 시작 시 `EnsureCreatedAsync`로 생성합니다. 스키마 변경을 위한 EF Core 마이그레이션은 아직 도입하지 않았습니다.

## 프로젝트 구조

```text
Components/Pages/Home.razor      지출 입력·조회·수정·삭제 화면과 이벤트 처리
Components/Pages/Home.razor.css  홈 화면 스타일
Data/ExpenseRecord.cs            지출 데이터 모델
Data/ExpenseFilter.cs            월·카테고리별 C# 조회 조건
Data/ExpensesDbContext.cs        EF Core 데이터베이스 컨텍스트
Program.cs                       SQLite 등록과 앱 시작 설정
```

## 검증 및 다음 단계

`dotnet build`로 빌드를 확인했고, 별도의 임시 SQLite 데이터베이스에서 저장·수정·다시 읽기·개별 삭제·전체 삭제와 월·카테고리별 필터를 검증했습니다. 자동화된 테스트 프로젝트는 아직 저장소에 포함되지 않았습니다.

다음 후보 기능은 카테고리별 통계와 CSV 내보내기입니다.

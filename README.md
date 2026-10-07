# MyExpenses

Google 계정으로 로그인해 **나만의 지출과 예산을 관리하는 웹앱**입니다.
C#과 Blazor로 구현했으며, 사용자별 데이터는 SQLite에 저장됩니다.

지출 기록부터 월별·카테고리별 예산, 통계, CSV 가져오기·내보내기까지 한곳에서 관리할 수 있습니다. 금융기관과 직접 연동하는 서비스는 아닙니다.

[주요 기능](#주요-기능) · [로컬 실행](#로컬-실행) · [Docker 실행](#docker-실행) · [사용 안내](#사용-안내) · [기술 스택](#기술-스택) · [프로젝트 구조](#프로젝트-구조) · [검증](#검증) · [운영 배포](#운영-배포) · [데이터와 보안](#데이터와-보안)

## 주요 기능

| 영역 | 구현된 기능 |
| --- | --- |
| 로그인·계정 | Google 로그인, 로그인 시 계정 선택, 사용자별 데이터 분리, 신규 사용자 시작 안내, 계정·데이터 삭제 |
| 지출 기록 | 날짜·금액·카테고리·메모·결제수단 관리, 추가·수정·삭제, 확인 후 전체 삭제 |
| 검색·필터 | 메모 검색, 월·카테고리·결제수단·날짜 범위·금액 범위 필터, 날짜·금액순 정렬 |
| 예산 | 전체 월 예산과 카테고리별 월 예산, 사용률·남은 금액·초과 금액 |
| 수입·순수지 | 수입 등록·수정·삭제(급여·부수입·용돈·이자·투자·기타), 월별 수입·지출·순수지·저축률, 6개월 수입·지출 추이, 수입 CSV 내보내기·가져오기 |
| 목표 저축 | 목표 금액·목표일 설정, 직접 기록하는 저축 내역, 진행률·남은 금액·필요 월 저축액·예상 달성 시점, 최근 3개월 평균 순수지와 비교 |
| 태그 | 지출에 태그를 여러 개 붙이고(최대 5개), 태그로 필터·태그별 합계(연간 통계)·이름 변경·합치기·삭제, 백업·CSV 포함 |
| 달력 | 월간 달력에 날짜별 지출·수입 합계를 표시, 날짜를 눌러 그날의 내역 확인, 그 날짜로 지출 추가 |
| 연간 통계 | 해를 골라 수입·지출·순수지·저축률 요약(전년도 같은 기간 비교), 월 평균 지출, 월별 표, 카테고리별 비중·전년 대비, 결제수단별·요일별 지출, 큰 지출 TOP 10, 자주 쓴 항목 |
| 통계 | 조회 결과의 합계·건수·카테고리 비율, 6개월 지출 추이, 전월 비교, 결제수단별 월 합계 |
| 카테고리 | 사용자별 추가·이름 변경·표시 순서 변경·보관·복원, 기존 데이터 유지 |
| 입력 편의 | 즐겨찾기 지출 템플릿, 접속 시 도래한 월별 정기 지출·정기 수입 자동 생성 |
| 백업·복원 | 내 모든 데이터를 JSON 파일 하나로 백업, 검증·미리보기 후 병합 또는 덮어쓰기 복원, 다른 계정으로 이전 |
| CSV | 조회 결과·전체 기록 내보내기, 검증·미리보기 후 가져오기, 중복 건너뛰기 |
| 모바일 | 홈 화면에 앱처럼 설치(PWA), 전체 화면 실행, 바로가기(지출·수입·목표 저축), 인터넷이 끊기면 안내 화면 |
| 실행·운영 | SQLite 영구 저장, Docker 지원, 공개 소개·개인정보 처리방침·이용약관, 상태 확인 엔드포인트 |

처음에는 **식비 · 카페 · 교통 · 쇼핑 · 생활 · 기타** 카테고리가 제공되며, 사용자별로 추가·변경할 수 있습니다. 금액은 원 단위입니다.

## 로컬 실행

**준비물:** [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), Git, Google OAuth 웹 클라이언트

macOS·Windows·Linux에서 실행할 수 있습니다. VS Code는 선택 사항입니다.

### 1. 프로젝트 받기

```bash
git clone https://github.com/casey1425/MyExpenses.git
cd MyExpenses
dotnet restore
```

### 2. Google OAuth 클라이언트 설정

[Google Cloud Console](https://console.cloud.google.com/apis/credentials)에서 다음을 설정합니다.

1. 프로젝트와 OAuth 동의 화면을 구성합니다. 여러 Google 사용자를 받으려면 대상을 **외부(External)**로 선택합니다.
2. **웹 애플리케이션** 유형의 OAuth 클라이언트를 만듭니다.
3. 클라이언트의 **승인된 리디렉션 URI**에 아래 주소를 등록합니다.

```text
https://localhost:7168/signin-google
```

앱이 테스트 상태라면 사용할 계정을 테스트 사용자로 등록하세요. 일반 사용자에게 공개하려면 OAuth 동의 화면 게시 설정도 필요합니다.

### 3. 비밀 설정 저장 및 실행

프로젝트 폴더에서 실행합니다. 아래 값은 발급받은 값과 실제 문의 이메일로 바꾸세요.

```bash
dotnet user-secrets set "Authentication:Google:ClientId" "발급받은 Client ID"
dotnet user-secrets set "Authentication:Google:ClientSecret" "발급받은 Client Secret"
dotnet user-secrets set "Support:Email" "사용자 문의를 받을 이메일"
dotnet dev-certs https --trust
dotnet run --launch-profile https
```

브라우저에서 **[https://localhost:7168](https://localhost:7168)**을 열어 Google 계정으로 로그인합니다. 첫 사용자는 시작 안내 후 빈 지출 화면에서 시작합니다. 종료는 터미널에서 `Ctrl+C`를 누릅니다.

> Client Secret은 저장소의 설정 파일이나 README에 적거나 Git에 커밋하지 마세요.
>
> 포트를 바꿀 때는 [실행 프로필](Properties/launchSettings.json)의 `applicationUrl`과 Google OAuth 리디렉션 URI를 함께 변경해야 합니다.

## Docker 실행

.NET SDK 대신 Docker로 실행할 수도 있습니다. **Docker Desktop이 실행 중**이어야 하며, 위의 Google OAuth 클라이언트에 다음 리디렉션 URI를 추가합니다.

```text
http://localhost:10000/signin-google
```

### 1. 환경변수 파일 만들기

프로젝트 폴더에 `.env.docker` 파일을 만들고 실제 값을 입력합니다. 이 파일은 Git에서 제외됩니다. 로컬 .NET 실행에 설정한 User Secrets는 컨테이너에 자동 전달되지 않습니다.

```text
ASPNETCORE_ENVIRONMENT=Development
ReverseProxy__UseForwardedHeaders=false
Authentication__Google__ClientId=발급받은_Client_ID
Authentication__Google__ClientSecret=발급받은_Client_Secret
Support__Email=사용자_문의_이메일
```

### 2. 이미지 빌드 및 실행

```bash
docker build -t myexpenses:latest .
docker volume create myexpenses-data
docker run -d --name myexpenses-local --env-file .env.docker \
  -p 10000:10000 -v myexpenses-data:/app/Data myexpenses:latest
```

접속 주소는 **[http://localhost:10000](http://localhost:10000)**입니다.

```bash
# 실행 상태 확인
curl http://localhost:10000/healthz

# 중지 / 다시 시작
docker stop myexpenses-local
docker start myexpenses-local
```

> 데이터는 `myexpenses-data` 볼륨에 저장됩니다. 컨테이너를 교체할 때도 **같은 볼륨을 연결**하세요. 볼륨을 삭제하면 저장 데이터도 사라질 수 있습니다.

<details>
<summary>코드 변경 후 Docker에 반영하기</summary>

이미지를 다시 빌드하고 기존 컨테이너를 교체합니다. 아래 명령은 컨테이너만 삭제하며 데이터 볼륨은 유지합니다.

```bash
docker build -t myexpenses:latest .
docker stop myexpenses-local
docker rm myexpenses-local
docker run -d --name myexpenses-local --env-file .env.docker \
  -p 10000:10000 -v myexpenses-data:/app/Data myexpenses:latest
curl http://localhost:10000/healthz
```

</details>

## 사용 안내

로그인 후 지출 기록 화면에서 기록·검색·예산·카테고리 통계를 확인할 수 있습니다. 위에서부터 이번 달 지출·기록 건수·월 예산 현황 요약, 새 지출 추가와 지출 내역(검색·필터는 접어 두었다가 펼쳐서 사용), 예산과 통계 순서로 배치했습니다. 왼쪽 메뉴는 기록·분석·계획·관리·데이터·계정으로 묶여 있습니다. 각 항목을 펼치면 세부 동작과 제한을 확인할 수 있습니다.

<details>
<summary>사용자별 카테고리 관리</summary>

**카테고리 관리** (`/categories`)에서 이름(1~30자)을 입력해 추가하고, 이름 변경·위/아래 이동·보관·복원을 할 수 있습니다. 같은 계정 안에서는 보관 중인 항목을 포함해 이름이 중복될 수 없습니다. 사용 중인 카테고리는 최소 하나 유지해야 합니다.

이름 변경은 기존 지출·카테고리 예산·템플릿·정기 지출 규칙에도 같은 트랜잭션으로 반영됩니다. 표시 순서는 입력 목록·필터·예산 목록·월별 추이의 카테고리 비교에 적용됩니다. 다른 탭은 새로고침해 최신 목록을 확인하세요.

보관한 카테고리는 새 지출·템플릿·정기 지출의 선택 목록에서 숨깁니다. 기존 기록과 예산·통계·필터는 유지되며 기존 기록의 다른 항목을 수정할 수 있습니다. 해당 카테고리의 템플릿을 새 지출에 사용하거나 CSV로 가져오려면 먼저 복원하거나 다른 사용 중인 카테고리로 변경해야 합니다. **기존 정기 지출 규칙은 보관 후에도 계속 생성**되므로 자동 생성을 멈추려면 정기 지출 화면에서 규칙을 중지하세요.

기존 DB는 카테고리 관리 테이블만 추가하고, 사용자별 기본 카테고리와 기존 데이터에 쓰인 분류를 처음 조회할 때 등록합니다. 이름을 바꾸거나 보관해도 기본 목록을 다시 생성하지 않습니다. CSV를 가져올 때에는 파일에 있는 이름과 같은 사용 중인 카테고리를 내 계정에 먼저 등록해야 합니다.

</details>

<details>
<summary>즐겨찾기 지출 템플릿</summary>

로그인 후 **즐겨찾기 지출** (`/templates`)에서 이름(1~50자), 원 단위 금액, 카테고리, 메모(선택·최대 100자)를 저장합니다. 지출 기록 화면의 **새 지출 추가 → 즐겨찾기 템플릿**에서 선택하면 금액·카테고리·메모·결제수단을 입력하고 날짜는 그대로 유지합니다. 선택 시 현재 입력된 금액·카테고리·메모·결제수단을 덮어쓰므로 내용을 확인해 주세요. 필요한 내용을 수정한 후 **지출 기록하기**를 눌러야 실제 지출로 저장됩니다. 다른 탭에서 템플릿을 수정했다면 **목록 새로고침**으로 목록을 갱신할 수 있습니다.

템플릿은 현재 로그인 사용자만 조회·수정·삭제할 수 있고 정기 지출처럼 자동으로 기록을 생성하지 않습니다. 템플릿을 변경하거나 삭제해도 이미 기록한 지출은 유지됩니다. 지출 전체 삭제 시 템플릿은 유지되지만 계정 삭제 시에는 함께 삭제됩니다. CSV에는 실제 지출만 포함되며 템플릿은 포함되지 않습니다.

</details>

<details>
<summary>수입 기록과 순수지</summary>

로그인 후 **수입 기록** (`/income`)에서 날짜·원 단위 금액·분류(급여·부수입·용돈·이자·투자·기타)·메모(선택·최대 100자)를 등록하고 수정·삭제할 수 있습니다. 조회 월을 선택하면 해당 월의 수입 목록과 함께 **수입 − 지출 = 순수지**, **저축률(순수지 ÷ 수입)** 을 같은 시점의 데이터로 계산해 보여 줍니다. 수입이 없는 달은 저축률을 표시하지 않고, 지출이 수입보다 많으면 순수지가 음수로 표시됩니다.

수입은 지출과 별도 테이블에 저장되므로 지출 통계·예산·지출 CSV에는 영향을 주지 않습니다. 현재 사용자만 조회·수정·삭제할 수 있고 계정 삭제 시 함께 삭제됩니다. 지출 전체 삭제는 수입을 지우지 않습니다. 기존 DB는 시작 시 수입 테이블만 추가하며 기존 데이터를 변경하지 않습니다.

</details>

<details>
<summary>전체 백업·복원</summary>

로그인 후 **백업·복원** (`/backup`, 계정 관리에서도 이동 가능)에서 내 모든 데이터를 파일 하나(`MyExpenses-backup-날짜.json`)로 내려받고, 같은 파일로 복원할 수 있습니다. 파일에는 지출, 수입, 카테고리(순서·보관 상태), 결제수단, 즐겨찾기 템플릿, 월·카테고리 예산, 정기 지출·수입(**자동 생성이 끝난 달의 이력 포함**), 목표 저축과 저축 내역이 들어갑니다. 계정 정보(이메일·내부 ID)는 포함하지 않고 결제수단은 이름으로 연결하므로, **다른 Google 계정으로 데이터를 옮길 때**도 쓸 수 있습니다. 파일에는 가계부 정보가 모두 담겨 있으므로 안전한 곳에 보관하세요. 서버는 이 파일을 캐시하지 않도록(`no-store`) 내려보냅니다.

복원은 파일을 선택하면 먼저 **형식과 내용을 검증하고 미리보기**를 보여 주며, 확인 버튼을 눌러야 저장합니다. 미리보기는 실제 복원과 같은 코드를 실행한 뒤 되돌려 만들기 때문에 결과가 정확히 일치합니다.

- **병합(권장):** 백업에만 있는 항목을 추가하고, 이미 있는 항목은 건너뛰며 현재 데이터는 바꾸지 않습니다. 내용이 같은 기록은 개수까지 세어 비교하므로 **같은 파일을 다시 복원해도 늘어나지 않고**, 같은 날 같은 커피 2잔처럼 정당한 중복 기록은 각각 별개로 보존합니다. 카테고리·결제수단·템플릿·예산·목표는 이름(또는 월)이 같으면 기존 항목을 유지합니다. 결제수단의 이름은 같지만 유형이 다르면 기존 항목을 쓰고 미리보기에서 알려 줍니다. 목표는 계정당 최대 20개를 넘으면 복원할 수 없습니다.
- **덮어쓰기:** 현재 데이터를 모두 지우고 백업 내용으로 교체합니다(로그인 프로필은 유지). 백업 이후에 기록한 내용은 사라지므로 삭제될 건수를 미리 알려 주고, **확인 문구 ‘덮어쓰기’를 직접 입력**해야 진행됩니다. 필요하면 먼저 현재 데이터를 백업하세요.

모든 작업은 한 트랜잭션에서 처리되어 **중간에 실패하면 아무것도 바뀌지 않습니다.** 파일은 최대 10MB·10만 개 항목까지 지원하며, 알 수 없거나 빠진 항목, 항목 수 불일치(잘린 파일), 범위를 벗어난 값 등은 어디가 잘못됐는지 알려 주고 복원을 막습니다. 기존 정기 규칙의 생성 이력을 함께 복원하므로 복원 후 과거 달의 지출·수입이 다시 생성되지 않으며, 백업 이후에 도래한 달만 다음 접속 때 자동 생성됩니다. 계정 삭제 시에는 복원한 데이터도 함께 삭제됩니다.

</details>

<details>
<summary>수입 CSV 내보내기·가져오기</summary>

**수입 기록** 화면에서 선택한 달 또는 전체 수입을 CSV로 내보낼 수 있습니다(계정 관리의 **전체 수입 CSV 백업**도 같습니다). 제목 행은 `날짜,금액(원),분류,메모`이고 UTF-8(BOM)이며, 수식으로 해석될 수 있는 메모는 지출 CSV와 같은 방식으로 보호합니다.

**수입 CSV 가져오기** (`/income/import`)는 내보낸 파일을 검증·미리보기한 뒤 확인 버튼을 눌러야 저장합니다. 오류가 하나라도 있으면 파일 전체를 저장하지 않으며, 한 번에 최대 1MB·1,000건까지 지원합니다. 날짜·금액·분류·메모가 같은 기록(파일 안의 중복 포함)은 기본적으로 건너뛰고, 필요하면 중복 후보도 포함할 수 있습니다. 지출 CSV와는 제목 행이 달라 서로 가져올 수 없으니 각각의 가져오기 화면을 사용하세요.

</details>

<details>
<summary>결제수단별 지출 관리</summary>

로그인 후 **결제수단** (`/payment-methods`)에서 ‘국민 체크카드’, ‘현금 지갑’처럼 알아보기 쉬운 이름(1~50자)과 유형을 등록합니다. 계정 안에서 결제수단 이름은 중복할 수 없습니다. 금융기관·카드사와 연동하지 않으며 카드번호·계좌번호·인증정보는 입력하지 마세요.

지출 추가·수정 시 결제수단을 선택하고, 즐겨찾기 템플릿에도 기본 결제수단을 설정할 수 있습니다. 템플릿을 선택하면 해당 수단도 입력하며 미지정 템플릿은 입력 중인 결제수단을 미지정으로 바꿉니다. 다른 탭에서 수단을 추가·수정했다면 목록을 새로고침하세요. 지출 내역의 필터는 개별 결제수단 또는 미지정을 선택할 수 있고, CSV 내보내기에도 같은 필터를 적용합니다.

결제수단 관리 화면에서 조회 월을 선택하면 해당 월 전체의 수단별 금액·건수와 미지정 합계를 표시합니다. 지출 화면의 검색 조건과는 독립적이며, 사용하지 않은 수단도 0원으로 표시합니다. 수단 이름·유형 변경은 연결된 기록 표시에도 적용됩니다. 삭제 시 확인 후 지출·템플릿의 연결만 미지정으로 변경하며 금액과 기록은 보존합니다. 계정 삭제 시에는 결제수단도 함께 삭제합니다. 기존 지출과 정기 지출은 기본적으로 미지정이며, 필요하면 개별 지출을 수정해 연결하세요.

기존 DB는 지출·템플릿 테이블을 재작성하지 않고 nullable 결제수단 열과 관리 테이블을 추가합니다. 신규 DB의 복합 외래 키와 기존 DB의 소유자 검사 트리거, 저장 시 C# 검증으로 다른 사용자의 결제수단 연결을 차단합니다.

</details>

<details>
<summary>지출 검색과 상세 필터</summary>

지출 내역에서 메모 검색어, 월·카테고리, 시작일·종료일, 최소·최대 금액을 입력하고 **조회**를 누릅니다. 모든 조건은 함께 적용되므로 월과 날짜 범위를 동시에 지정하면 두 조건에 모두 해당하는 기록만 조회합니다. 시작일·종료일과 금액 경계값을 포함하며, 비워 둔 항목은 제한하지 않습니다. 메모 검색은 앞뒤 공백을 제거한 문자열을 그대로 포함하는지 확인하며 영문 대소문자를 구분합니다. 정렬은 최신순(기본), 오래된순, 금액 높은순·낮은순을 지원합니다. **초기화**하면 모든 조건과 정렬을 기본값으로 되돌립니다.

적용된 조건과 결과 건수·합계를 지출 내역 위에서 확인할 수 있고, 카테고리 통계와 **현재 조회 결과 CSV**도 같은 조회 조건을 사용합니다. CSV의 행 순서는 선택한 정렬과 같습니다. 예산 집계는 검색 조건에 영향을 받지 않습니다. 잘못된 날짜·금액 범위는 안내 후 이전 결과를 유지합니다. 검색·필터·정렬은 C#과 EF Core로 SQLite에서 처리하며 현재 로그인 사용자의 기록만 조회합니다.

</details>

<details>
<summary>CSV 가져오기</summary>

로그인 후 메뉴의 **CSV 가져오기** 또는 지출 내역의 링크를 엽니다. MyExpenses에서 내보낸 UTF-8 CSV 파일을 선택하면 내용과 중복 후보를 먼저 확인할 수 있습니다. 오류가 있는 파일은 저장할 수 없으며, 확인 버튼을 누른 후에만 현재 로그인 계정에 지출이 추가됩니다. 기본값은 같은 날짜·금액·카테고리·메모·결제수단을 가진 기록을 건너뛰는 방식이고, 필요하면 중복 후보를 포함할 수 있습니다. 파일은 최대 1MB, 지출은 한 번에 최대 1,000건까지 지원합니다. 전체/카테고리 예산은 CSV에 포함되지 않습니다.

현재 내보내기 제목은 `날짜,금액(원),카테고리,메모,결제수단,결제유형`입니다. 미지정 기록은 마지막 두 열을 비워 둡니다. 가져올 때 이름·유형이 같은 결제수단을 내 계정에 먼저 등록해야 하며, 다른 계정의 결제수단 ID는 가져오지 않습니다. 등록되지 않은 수단은 안내 후 파일 전체 저장을 중단합니다. 기존 `날짜,금액(원),카테고리,메모` 4열 CSV도 지원하며 결제수단은 미지정으로 저장합니다. 이름·유형을 변경한 후 예전에 내보낸 파일을 가져오려면 파일의 두 열도 현재 이름·유형으로 맞춰 주세요.

</details>

<details>
<summary>화면 모드·글자 크기·모바일 하단 탭바</summary>

- **다크 모드:** 상단의 ☾/☀ 버튼으로 밝은 화면과 어두운 화면을 바꿉니다. **계정 관리 → 화면 설정**에서는 *시스템 설정 따르기*(기본), 밝게, 어둡게 중에서 고를 수 있고, **글자 크기**(보통/크게)도 바꿉니다. 설정은 그 기기의 브라우저에만 저장됩니다.
- **모바일 하단 탭바:** 폭이 좁은 화면(640px 이하)에서는 아래에 홈·달력·**+ 지출 추가**·통계·더보기 탭이 고정됩니다. `+`를 누르면 금액 칸으로 바로 이동하고, 더보기는 전체 메뉴를 엽니다.
- **키보드·접근성:** 키보드로 이동할 때 포커스 표시가 보이고, 본문으로 건너뛰기 링크가 있으며, 시스템에서 *동작 줄이기*를 켜면 애니메이션이 멈춥니다.
- **색을 추가·변경했다면:** 다크 모드 색은 `wwwroot/theme.css`에 있고 자동 생성됩니다. CSS에 새 색을 넣은 뒤 `python3 tools/theme/generate_theme.py` 를 실행하세요(검증이 최신 여부를 확인합니다). 글자 색은 밝은 배경에서 대비 4.5:1 이상이어야 하며 `python3 tools/theme/contrast_report.py` 로 점검, `--fix`로 수정할 수 있습니다.

</details>

<details>
<summary>모바일 홈 화면에 설치(PWA)</summary>

별도 앱을 설치하지 않고 브라우저에서 **홈 화면에 추가**하면 아이콘으로 바로 열 수 있고, 주소창 없이 전체 화면으로 실행됩니다. 설치 방법은 [서비스 소개](/about) 화면에도 안내되어 있습니다.

- **Android(Chrome):** 메뉴(⋮) → 앱 설치 또는 홈 화면에 추가
- **iPhone·iPad(Safari):** 공유 버튼 → 홈 화면에 추가
- 아이콘을 길게 누르면 **지출 기록·수입 기록·목표 저축** 바로가기를 쓸 수 있습니다(Android).

설치는 HTTPS 주소(또는 `localhost`)에서만 가능합니다. 이 앱은 서버와 실시간으로 연결되어 동작하므로 **인터넷이 없으면 사용할 수 없고**, 연결이 끊긴 상태에서 열면 안내 화면과 *다시 시도* 버튼이 표시됩니다. 연결이 돌아오면 자동으로 원래 화면으로 돌아옵니다. 로그인 정보와 가계부 데이터를 보호하기 위해 서비스 워커는 **페이지나 데이터를 기기에 저장(캐시)하지 않고** 오프라인 안내 화면만 저장합니다. 푸시 알림과 오프라인 입력은 지원하지 않습니다.

아이콘 원본은 `design/`의 SVG이며, 바꾸려면 PNG(`wwwroot/icons/`의 192·512·maskable 512·apple-touch 180)를 같은 크기로 다시 만들어 교체하면 됩니다. 앱 이름·색상·바로가기는 `wwwroot/manifest.webmanifest`에서 바꿉니다(`App.razor`의 `theme-color`와 같은 값을 유지하세요).

</details>

<details>
<summary>목표 저축</summary>

로그인 후 **목표 저축** (`/savings`)에서 ‘유럽 여행’, ‘비상금’처럼 이름(1~50자)·목표 금액·목표일(선택)을 정해 목표를 만듭니다. 목표는 사용자당 최대 20개이며 같은 이름은 쓸 수 없습니다. 목표일은 만들 때 오늘 이후여야 하고, 이미 기한이 지난 목표는 목표일을 바꾸지 않는 한 이름·금액을 수정할 수 있습니다.

저축은 **저축하기**로 날짜(오늘 이전)·금액·메모(선택)를 직접 기록합니다. 수입·지출과 자동으로 연결하지 않으므로 기록한 금액만 진행률에 반영되며, **내역**에서 잘못 기록한 저축을 삭제할 수 있습니다. 목표를 삭제하면 저축 내역도 함께 삭제됩니다.

각 목표에는 저축 합계·진행률(달성 전에는 100%로 보이지 않도록 소수 첫째 자리에서 내림)·남은 금액이 표시됩니다. 목표일이 있으면 **필요 월 저축액**(남은 금액 ÷ 이번 달 포함 목표일이 속한 달까지의 개월 수, 올림)과 D-day를 보여 주고, 기한이 지나면 남은 금액 전체가 필요하다고 표시합니다. 첫 저축이 있는 달부터 이번 달까지의 월평균 저축액으로 **예상 달성 시점**을 계산하며 목표일보다 늦으면 알려 줍니다.

‘한눈에 보기’는 모든 목표의 저축·목표 합계와 필요 월 저축액 합계를 **최근 3개월(이번 달 제외) 월평균 순수지**와 비교합니다. 순수지는 수입 기록과 지출 기록으로 계산하며, 기록이 없으면 비교하지 않습니다. 날짜는 한국 시간 기준이고 목표·저축은 CSV에 포함되지 않으며 계정 삭제 시 함께 삭제됩니다. 기존 DB는 시작 시 목표 저축 테이블만 추가합니다.

</details>

<details>
<summary>정기 수입</summary>

로그인 후 **정기 수입** (`/recurring-income`)에서 매월 지정일·금액·분류·메모를 등록하면 월급처럼 반복되는 수입을 자동으로 기록합니다. 동작 방식은 정기 지출과 같습니다. 등록한 달부터 적용되고 지정일이 이미 지났다면 즉시 기록하며, 지출 기록·월별 추이·수입 기록 화면에 접속할 때 지난 접속 이후 도래한 수입을 채웁니다. 같은 규칙·같은 달은 한 번만 처리하고 29~31일이 없는 달은 말일에 기록합니다. 날짜는 한국 시간 기준입니다.

규칙을 중지했다가 다시 시작하면 중지 기간은 소급하지 않습니다. 규칙을 수정하거나 삭제해도 이미 생성된 수입은 바뀌지 않으며, 생성된 수입을 삭제해도 해당 달에 다시 생성되지는 않습니다. 생성된 수입은 일반 수입처럼 수입 기록·월별 추이·수입 CSV에 포함됩니다. 정기 수입 규칙 자체는 CSV에 포함되지 않으며 계정 삭제 시 함께 삭제됩니다. 기존 DB는 시작 시 정기 수입 테이블만 추가합니다.

</details>

<details>
<summary>정기 지출</summary>

로그인 후 **정기 지출** (`/recurring`)에서 매월 지정일·금액·카테고리·메모를 등록합니다. 등록한 달부터 적용되며 지정일이 이미 지났다면 즉시 기록합니다. 지출 기록 또는 정기 지출 페이지에 접속하면 누락된 달의 지출을 채우고, 같은 규칙·같은 달은 한 번만 처리합니다. 29~31일이 없는 달에는 말일에 기록합니다. 규칙을 중지했다가 다시 시작하면 중지 기간은 소급하지 않습니다. 규칙을 수정하거나 삭제해도 이미 생성된 지출은 바뀌지 않으며, 생성된 지출을 삭제해도 해당 달에 다시 생성되지는 않습니다. CSV에는 지출 기록만 포함되고 정기 지출 규칙은 포함되지 않습니다.

</details>

<details>
<summary>카테고리별 월 예산</summary>

지출 기록 화면의 월별 예산에서 월을 선택하면 아래에 식비·카페·교통·쇼핑·생활·기타 각각의 지출과 예산이 표시됩니다. 카테고리별로 예산을 설정·변경·삭제할 수 있으며, 사용률과 남은 금액 또는 초과 금액을 확인할 수 있습니다. 지출이 예산을 초과해도 기록은 계속할 수 있습니다. 카테고리 예산은 전체 월 예산과 독립적이며, 한쪽을 변경하거나 삭제해도 다른 쪽은 그대로입니다. 계정을 삭제하면 해당 계정의 카테고리 예산도 삭제됩니다. CSV에는 예산이 포함되지 않습니다.

</details>

<details>
<summary>태그</summary>

- **붙이기**: 지출 입력·수정 화면의 ‘태그’ 칸에 `여행, 경조사`처럼 쉼표로 구분해 입력합니다(앞의 `#`은 있어도 됩니다). 한 지출에 최대 5개, 계정당 최대 100개이며 이름은 20자 이하, 쉼표·세미콜론은 쓸 수 없습니다. **대소문자는 같은 태그로 보고**(`Trip`=`trip`), 처음 입력한 표기를 보여 줍니다. 없는 태그는 자동으로 만들어집니다.
- **연속 입력**: 지출을 기록해도 태그는 남아 있어 같은 여행의 여러 건을 이어서 입력할 수 있습니다. 입력 칸 아래의 자주 쓰는 태그를 눌러 넣고 뺄 수도 있습니다.
- **보기**: 지출 내역의 태그를 누르면 그 태그의 지출만 보이고, 검색·필터에도 ‘태그’ 항목이 있습니다. 필터 결과 CSV 내보내기에도 같은 조건이 적용됩니다.
- **태그 관리** (`/tags`): 태그별 건수·합계, 이름 변경, 삭제를 합니다. 이미 있는 이름으로 바꾸면 **합치기**를 확인한 뒤 같은 지출에 중복 없이 하나로 합칩니다. 태그를 지워도 지출은 그대로 남습니다.
- **연간 통계**: ‘태그별 지출’에 태그별 금액·건수·비중과 ‘태그 없음’을 보여 줍니다. **한 지출에 태그가 여러 개면 각 태그에 모두 포함되므로 합계를 더하면 전체 지출보다 클 수 있습니다.**
- **백업·CSV**: 백업 파일 버전이 2로 올라가며 쓰지 않는 태그도 보존합니다. 버전 1 파일도 복원할 수 있습니다. 병합 복원은 이미 있는 지출의 태그를 바꾸지 않고, 새로 만드는 지출에만 태그를 붙입니다. CSV는 `태그` 열(세미콜론 구분)이 추가되었고 4열·6열 파일도 가져올 수 있습니다.
- 정기 지출·즐겨찾기 템플릿은 태그를 지원하지 않습니다. 기존 DB는 시작 시 태그 테이블만 추가합니다.

</details>

<details>
<summary>빠른 입력·되돌리기·달력</summary>

- **연속 입력**: 지출을 기록하면 금액·메모만 비우고 날짜·카테고리·결제수단은 남겨 둔 채 금액 칸으로 포커스를 옮깁니다. 금액을 입력하고 Enter만 눌러도 기록됩니다.
- **메모 자동 완성**: 최근 1년 기록에서 많이 쓴 메모 30개를 추천합니다(대소문자·앞뒤 공백 무시). 이전에 쓴 메모를 고르면 그 메모로 가장 최근에 기록한 카테고리·결제수단을 채우고, 금액이 비어 있으면 금액도 채웁니다. 무엇을 채웠는지 알려 주며, 이미 입력한 금액은 바꾸지 않습니다.
- **월 이동**: 지출 내역 위의 ‹ › 버튼으로 앞뒤 달로 넘기고 ‘이번 달’로 돌아옵니다. 월 이동만으로는 검색·필터 상자가 펼쳐지지 않으며, 다른 조건과 함께 쓸 수 있습니다.
- **날짜별 묶음**: 최신순·오래된순으로 볼 때 날짜 머리글에 그날의 건수와 합계를 보여 줍니다(표시된 목록 기준). 금액순에서는 묶지 않습니다.
- **나눠 보기**: 지출 내역은 처음에 50건만 그리고 목록 아래의 ‘50건 더 보기’(남은 건수가 한 번에 보여 줄 양보다 많으면 ‘모두 보기’도)로 늘립니다. 건수·합계·카테고리 통계·날짜별 합계는 숨겨진 항목까지 포함한 전체 기준이라 달라지지 않습니다. 새 검색·월 이동·초기화를 하면 첫 쪽으로 돌아가고, 방금 추가·수정·되돌린 기록이 숨은 위치에 있으면 그 기록까지 자동으로 보여 줍니다. 데이터는 한 번에 읽고 화면에 그리는 양만 나누므로, 수만 건을 넘는 기록에서는 읽는 시간이 늘 수 있습니다.
- **삭제 되돌리기**: 지출 하나를 삭제하면 ‘되돌리기’를 누를 수 있습니다. 다른 검색·수정·추가를 하면 사라지며, 되돌리는 사이 카테고리가 보관됐거나 결제수단이 삭제됐다면 안내하고 거절합니다. 전체 삭제는 되돌릴 수 없습니다.
- **달력** (`/calendar`): 일요일부터 시작하는 월간 달력에 날짜별 지출(빨강)과 수입(초록)을 표시하고(1만 이상은 만·억 단위로 줄임), 날짜를 누르면 그날의 내역과 ‘이 날짜에 지출 추가’ 링크(`/?date=YYYY-MM-DD`)가 나옵니다. 잘못된 날짜 값은 무시합니다.

</details>

<details>
<summary>연간 통계</summary>

로그인 후 **연간 통계** (`/statistics`)에서 기록이 있는 해부터 올해까지 하나를 골라 봅니다. 올해는 한국 시간 기준 오늘까지를 전년도 같은 날짜까지와 비교하고(2월 29일이면 전년도 2월 28일까지), 지난 해는 한 해 전체를 그 전 해와 비교합니다. 요약에는 수입·지출·순수지·저축률이 있고, **월 평균 지출은 끝난 달만으로 계산**해 진행 중인 달 때문에 평균이 낮아지지 않게 합니다(끝난 달이 없으면 표시하지 않음). 월별 표, 카테고리별 비중과 전년 대비 증감(작년에만 있던 카테고리 포함), 결제수단별·요일별 지출, 가장 큰 지출 10건, 같은 메모가 2번 이상 기록된 항목(대소문자·앞뒤 공백 무시)을 보여 줍니다. 접속하면 밀린 정기 지출·수입을 먼저 생성하고, 지출·수입·결제수단은 같은 시점에 읽습니다. 좁은 화면에서는 전년도 금액 열을 숨깁니다.

</details>

<details>
<summary>월별 수입·지출 추이</summary>

로그인 후 **월별 수입·지출 추이** (`/trends`)에서 조회할 월을 선택합니다. 선택한 월까지 6개월의 지출 합계를 SVG 막대그래프로 표시하며, 기록이 없는 달도 0원으로 포함합니다. 이번 달은 한국 시간 기준 오늘까지의 지출을 전월의 같은 날짜까지와 비교하고, 전월에 해당 날짜가 없으면 말일까지 사용합니다. 과거 월은 전월과 각각 한 달 전체를 비교합니다. 전월 지출이 0원이면 증감률을 계산하지 않고 안내 문구를 표시합니다. 카테고리별 비교에도 같은 기간을 적용하며 미래 날짜의 지출은 이번 달 집계에서 제외합니다.

같은 6개월 구간의 **수입·지출·순수지** 섹션에서 선택한 월의 수입·지출·순수지·저축률 카드와 수입/지출 묶음 막대그래프를 보여 줍니다. 이번 달은 지출과 마찬가지로 오늘까지의 수입만 집계하며, 수입이 없는 달은 저축률을 표시하지 않고 지출이 수입보다 많으면 순수지를 음수로 표시합니다. 수입은 `수입 기록`에서 등록합니다.

</details>

## 기술 스택

| 구분 | 기술 |
| --- | --- |
| 언어·런타임 | C# / .NET 10 |
| 웹 UI | ASP.NET Core Blazor Web App — Interactive Server |
| 인증 | ASP.NET Core Identity / Google OAuth 2.0 / 쿠키 인증 |
| 데이터 | Entity Framework Core 10 / SQLite / EF Core Migrations |
| 스타일 | HTML / CSS / Bootstrap |
| 컨테이너 | Docker — Release 빌드, 비루트 사용자 실행 |
| CI | GitHub Actions — 빌드·전체 검증·Docker 이미지 빌드 |

## 프로젝트 구조

화면, 데이터 처리, 모델·계산 로직을 역할별로 나눴습니다.

```text
MyExpenses/
├── Program.cs                 # 앱 시작과 서비스·인증·DB 설정
├── Endpoints/                 # 로그인·로그아웃·CSV·백업 등 HTTP 요청 처리
├── Components/                # 화면·레이아웃·차트
│   └── Pages/                 # 기능별 폴더(지출·수입·저축·분석·관리·공개)의 Razor 화면
├── Services/                  # 사용자별 조회·저장·삭제
├── Data/                      # 모델·EF Core·검증·통계·CSV 로직
├── wwwroot/                   # 정적 파일과 공통 스타일
├── tests/                     # 기능·사용자 격리·회귀 검증
└── Dockerfile                 # 컨테이너 빌드·실행
```

- **화면:** `*.razor`는 마크업, `*.razor.cs`는 상태·이벤트, `*.razor.css`는 컴포넌트 스타일입니다. 지출 화면의 C# 코드는 지출·예산·템플릿별 partial class로 나눴습니다.
- **서비스:** 인증된 사용자 ID를 받아 소유자 조건을 적용합니다. 지출 화면과 CSV 내보내기는 같은 `ExpenseService` 조회 로직을 사용합니다.
- **데이터:** 모델·입력 검증·통계 계산을 두며, 기본 카테고리와 아이콘은 `ExpenseCategories`에, 사용자별 목록·검증은 `CategoryService`에 둡니다. 기존 DB 스키마 보완은 `ExpensesSchema`에서 처리합니다.

<details>
<summary>주요 파일 전체 보기</summary>

```text
MyExpenses/
├── Program.cs                     # 앱 시작, 서비스·인증·DB 초기화, 엔드포인트 연결
├── Endpoints/                     # 화면이 아닌 HTTP 요청 처리
│   ├── AccountEndpoints.cs        # Google 로그인 콜백, 로그아웃, 계정 삭제
│   ├── ExpenseExportEndpoints.cs  # 인증된 사용자의 CSV 내보내기 요청 처리
│   ├── IncomeExportEndpoints.cs   # 수입 CSV 내보내기 요청 처리
│   └── BackupEndpoints.cs         # 전체 백업 내려받기 요청 처리
├── MyExpenses.csproj              # .NET 대상 버전과 NuGet 패키지 참조
├── Components/
│   ├── App.razor                  # HTML 문서와 앱 진입점
│   ├── Routes.razor               # 페이지 라우팅과 인증 처리
│   ├── _Imports.razor             # 컴포넌트 공통 네임스페이스
│   ├── Layout/                    # 공통 레이아웃, 그룹별 메뉴(NavMenu·NavGroup·NavItem·NavIcon), 연결 복구 UI
│   ├── Shared/PageHeader.razor    # 모든 화면이 쓰는 제목·설명 머리글
│   ├── Charts/
│   │   ├── ExpenseTrendChart.razor # 월별 지출 SVG 막대그래프
│   │   └── CashflowChart.razor    # 월별 수입·지출 묶음 막대그래프
│   ├── Expenses/                  # 지출 기록 화면을 이루는 컴포넌트(각자 .razor.css 보유)
│   │   ├── BudgetPanel.razor      # 월별 예산
│   │   ├── CategoryBudgetPanel.razor # 카테고리별 월 예산
│   │   ├── CategoryStatsPanel.razor  # 카테고리별 지출 통계
│   │   ├── ExpenseForm.razor      # 새 지출 입력과 템플릿 선택
│   │   ├── ExpenseFilterForm.razor # 조회 조건 입력
│   │   └── ExpenseList.razor      # 지출 목록과 수정 폼
│   └── Pages/                     # 화면(라우트). 기능별 폴더로 나누고 네임스페이스는 모두 MyExpenses.Components.Pages
│       ├── Expenses/              # 지출
│       │   ├── Home.razor         # 지출 기록 화면: 요약과 위 컴포넌트 배치, 내역 머리글
│       │   ├── Home.razor.cs      # 화면 상태, 초기화, 요약 표시(상태와 로직은 Home에 둠)
│       │   ├── Home.Expenses.cs   # 조회 조건과 지출 CRUD 이벤트
│       │   ├── Home.Budgets.cs    # 월별·카테고리별 예산 이벤트
│       │   ├── Home.Templates.cs  # 템플릿·결제수단 선택 이벤트
│       │   ├── Templates.razor    # 즐겨찾기 지출 템플릿 관리
│       │   ├── Recurring.razor    # 정기 지출 규칙 관리
│       │   └── Import.razor       # CSV 가져오기와 미리보기
│       ├── Income/                # 수입
│       │   ├── Income.razor       # 수입 기록과 월별 순수지
│       │   ├── RecurringIncome.razor # 정기 수입 규칙 관리
│       │   └── ImportIncome.razor # 수입 CSV 가져오기와 미리보기
│       ├── Savings/Savings.razor  # 목표 저축과 저축 내역
│       ├── Insights/              # 분석
│       │   ├── Calendar.razor     # 월간 달력
│       │   ├── Statistics.razor   # 연간 통계
│       │   └── Trends.razor       # 월별 수입·지출 추이와 전월 비교
│       ├── Settings/              # 관리
│       │   ├── Categories.razor   # 사용자별 카테고리 관리
│       │   ├── Tags.razor         # 태그 관리(이름 변경·합치기·삭제)
│       │   ├── PaymentMethods.razor # 결제수단 관리와 월별 합계
│       │   ├── Backup.razor       # 전체 백업·복원과 미리보기
│       │   ├── Account.razor      # 계정·데이터 삭제 화면
│       │   └── AccountDeleted.razor
│       └── Public/                # 로그인 전에도 보이는 화면
│           ├── Login.razor        # Google 로그인 화면
│           ├── Welcome.razor      # 신규 사용자 시작 안내
│           ├── About.razor        # 서비스 소개
│           ├── Privacy.razor      # 개인정보 처리방침
│           ├── Terms.razor        # 이용약관
│           ├── NotFound.razor
│           └── Error.razor
├── Data/
│   ├── ExpenseRecord.cs           # 지출 모델
│   ├── UserCategory.cs            # 사용자별 카테고리·순서·보관 상태
│   ├── ExpenseTemplate.cs         # 템플릿 모델과 입력 검증
│   ├── PaymentMethod.cs           # 결제수단 모델과 월별 합계 결과
│   ├── IncomeRecord.cs            # 수입 모델과 월별 현금흐름 결과
│   ├── SavingsGoal.cs             # 목표·저축 모델과 진행률 계산
│   ├── MonthlyBudget.cs           # 전체 월 예산 모델
│   ├── CategoryBudget.cs          # 카테고리별 월 예산 모델
│   ├── RecurringExpenseRule.cs    # 정기 지출 규칙과 월별 처리 이력
│   ├── RecurringIncomeRule.cs     # 정기 수입 규칙과 월별 처리 이력
│   ├── UserProfile.cs             # 사용자별 첫 시작 완료 상태
│   ├── ExpensesDbContext.cs       # 지출 데이터의 EF Core 컨텍스트
│   ├── AuthDbContext.cs           # Identity 계정 EF Core 컨텍스트
│   ├── ExpensesSchema.cs          # 마이그레이션 도입 이전 DB를 최신 상태로 올리는 용도(더 이상 확장하지 않음)
│   ├── DatabaseMigrator.cs        # 시작 시 마이그레이션 적용과 기존 DB 기준선 기록
│   ├── DesignTimeDbContextFactories.cs # dotnet ef 전용 DbContext 팩토리
│   ├── Migrations/
│   │   ├── Expenses/              # 지출 DB(myexpenses.db) 마이그레이션
│   │   └── Auth/                  # 로그인 DB(auth.db) 마이그레이션
│   ├── ExpenseCategories.cs       # 최초 기본 카테고리와 아이콘
│   ├── ExpenseFilter.cs           # EF Core 조회 조건과 정렬
│   ├── ExpenseSearchInput.cs      # 화면·CSV 요청의 검색 조건 검증
│   ├── ExpenseStatistics.cs       # 카테고리별 금액·건수·비율 계산
│   ├── Tag.cs                     # 태그 모델과 이름 규칙·입력 해석
│   ├── CalendarMonth.cs           # 월간 달력 계산·금액 줄여 쓰기
│   ├── YearlyStatistics.cs        # 연간 통계 계산
│   ├── ExpenseTrends.cs           # 월별 추이와 전월 비교 계산
│   ├── ExpenseCsvExporter.cs      # CSV 파일 생성
│   ├── IncomeCsv.cs               # 수입 CSV 생성·검증·파싱
│   ├── Backup.cs                  # 백업 파일 형식·직렬화·검증
│   └── ExpenseCsvImporter.cs      # CSV 파싱과 유효성 검사
├── Services/
│   ├── ExpenseService.cs          # 지출 조회·CRUD·입력·결제수단 검증
│   ├── CategoryService.cs         # 카테고리 관리·소유자 검증·참조 일괄 변경
│   ├── BudgetService.cs           # 월별·카테고리별 예산 저장과 집계
│   ├── ExpenseTemplateService.cs  # 사용자별 템플릿 조회·저장·삭제
│   ├── PaymentMethodService.cs    # 결제수단 CRUD·소유자 검증·월별 집계
│   ├── IncomeService.cs           # 수입 CRUD·소유자 검증·월별 순수지 집계
│   ├── SavingsGoalService.cs      # 목표·저축 CRUD·소유자 검증·평균 순수지
│   ├── KoreanClock.cs             # 한국 시간 기준 오늘 날짜·현재 시각(앱 전체가 사용)
│   ├── TagService.cs              # 태그 생성·재사용·이름 변경·합치기·삭제
│   ├── CalendarService.cs         # 달력 데이터 조회
│   ├── YearlyStatisticsService.cs # 연간 통계 데이터 조회
│   ├── ExpenseTrendsService.cs    # 사용자별 추이 데이터 조회
│   ├── ExpenseCsvImportService.cs # 중복 확인과 CSV 저장
│   ├── IncomeCsvImportService.cs  # 수입 CSV 중복 확인과 저장
│   ├── BackupService.cs           # 전체 백업 내보내기와 병합·덮어쓰기 복원
│   ├── Backups/                   # 서버 자동 백업(DB 파일 복사)
│   │   ├── DatabaseBackupOptions.cs   # 백업 설정과 잘못된 값 처리
│   │   ├── DatabaseBackupService.cs   # 온라인 복사·점검·이름·목록·보관 정책
│   │   └── DatabaseBackupWorker.cs    # 주기 실행(백그라운드 서비스)
│   ├── RecurringExpenseService.cs # 정기 지출 생성과 중복 처리 방지
│   ├── RecurringIncomeService.cs  # 정기 수입 규칙 관리와 생성·중복 처리 방지
│   ├── UserDataProvisioner.cs     # 사용자 공간 초기화와 시작 상태 관리
│   └── UserDataDeletionService.cs # 현재 사용자 소유 데이터 삭제
├── tools/theme/                  # 다크 모드 색 토큰 생성기와 글자 대비 점검 스크립트
├── wwwroot/                      # 공통 CSS, Bootstrap, favicon 등 정적 파일
│   ├── theme.css / theme.js      # 다크 모드 색 토큰(자동 생성)과 화면 모드·글자 크기 적용
│   ├── quick-add.js              # 하단 탭바 "지출 추가"의 입력 칸 이동
│   ├── manifest.webmanifest      # 홈 화면 설치(PWA) 정보와 바로가기
│   ├── service-worker.js         # 설치 지원과 오프라인 안내(페이지는 캐시하지 않음)
│   ├── offline.html              # 인터넷이 없을 때 보여 주는 안내 화면
│   ├── pwa.js                    # 서비스 워커 등록
│   └── icons/                    # 앱 아이콘 PNG
├── Properties/launchSettings.json # 로컬 실행 프로필과 접속 주소
├── tests/
│   ├── ExpenseSearchChecks/      # 검색·필터 검증
│   ├── ExpenseTemplateChecks/    # 템플릿 CRUD·사용자 격리 검증
│   ├── PaymentMethodChecks/      # 결제수단·지출·템플릿·CSV 통합 검증
│   ├── ServiceChecks/            # 지출·예산 서비스와 화면 이벤트 회귀 검증
│   ├── CategoryChecks/           # 카테고리·기존 DB·통합·사용자 격리 검증
│   ├── IncomeChecks/             # 수입 CRUD·순수지·월별 추이·CSV·사용자 격리 검증
│   ├── RecurringIncomeChecks/    # 정기 수입 생성·소급 방지·말일 보정·사용자 격리 검증
│   ├── SavingsGoalChecks/        # 목표 저축 계산·CRUD·사용자 격리·화면 로직 검증
│   ├── MigrationChecks/          # 마이그레이션·기존 DB 기준선·모델 일치 검증
│   ├── PwaChecks/                # 매니페스트·아이콘·서비스 워커·오프라인 화면 검증
│   ├── DatabaseBackupChecks/     # 서버 자동 백업: 복사·점검·원자성·보관·주기·업그레이드 전 백업 검증
│   ├── TagChecks/                # 태그 규칙·서비스·백업·CSV·통계·화면 검증
│   ├── UsabilityChecks/          # 메모 자동 완성·연속 입력·월 이동·되돌리기·달력 계산과 화면 검증
│   ├── StatisticsChecks/         # 연간 통계 계산·기간 경계·서비스 격리·화면 로직 검증
│   ├── BackupChecks/             # 백업·복원 왕복 일치·병합 멱등성·롤백·화면 로직 검증
│   ├── TestSupport/              # 공통 DB·인증·컴포넌트 테스트 도구
│   └── run-checks.sh             # 전체 빌드와 검증 실행
├── .github/workflows/ci.yml      # GitHub Actions — 빌드·검증·Docker 빌드
├── .config/dotnet-tools.json     # 로컬 도구(dotnet-ef) 버전 고정
├── design/                       # 앱 아이콘 원본(SVG)
├── appsettings.json              # 공통 설정
├── appsettings.Development.json  # 개발 환경 설정
├── Dockerfile                    # .NET 빌드와 비루트 실행 이미지
├── .dockerignore                 # Docker 빌드에서 제외할 파일
├── .gitignore                    # DB·비밀 설정·빌드 결과 등의 Git 제외 규칙
└── README.md
```

</details>

## 검증

macOS/Linux에서는 프로젝트 폴더에서 다음 명령을 실행합니다. 앱 빌드와 모든 검증을 순서대로 실행하고, 실패하면 중단합니다.

```bash
bash tests/run-checks.sh
```

Windows 또는 개별 검증 시에는 다음 명령을 사용합니다.

```bash
dotnet build
dotnet run --project tests/ExpenseSearchChecks
dotnet run --project tests/ExpenseTemplateChecks
dotnet run --project tests/PaymentMethodChecks
dotnet run --project tests/ServiceChecks
dotnet run --project tests/CategoryChecks
dotnet run --project tests/IncomeChecks
dotnet run --project tests/RecurringIncomeChecks
dotnet run --project tests/SavingsGoalChecks
dotnet run --project tests/MigrationChecks
dotnet run --project tests/PwaChecks
dotnet run --project tests/BackupChecks
dotnet run --project tests/StatisticsChecks
dotnet run --project tests/UsabilityChecks
dotnet run --project tests/TagChecks
dotnet run --project tests/DatabaseBackupChecks
```

| 검증 프로젝트 | 주요 검증 범위 |
| --- | --- |
| ExpenseSearchChecks | 검색·필터·정렬 |
| ExpenseTemplateChecks | 템플릿 CRUD, 사용자 격리, 기존 DB 보완, 계정 삭제 |
| PaymentMethodChecks | 결제수단·지출·템플릿 연결, 월별 집계, 사용자 격리, CSV 4열·6열 호환, DB 업그레이드 |
| ServiceChecks | 지출·예산 CRUD·입력 검증, 예산 간 독립성, 로그인 계정 변경 시 화면 저장 차단, **서버 시간(`DateTime.Today`·`Now`) 사용 금지 확인** |
| CategoryChecks | 기존 DB 보완, 사용자 격리, 이름 일괄 변경, 순서·보관·복원, 지출·예산·템플릿·정기 지출·통계·CSV 연동, 계정 삭제 |
| IncomeChecks | 수입 CRUD·입력 검증, 월 경계, 순수지·저축률, 월별 추이(오늘까지·소유자 격리), 차트 렌더링, 수입 CSV 내보내기·가져오기(중복·원자성), 기존 DB 업그레이드, 계정 삭제 |
| RecurringIncomeChecks | 정기 수입 규칙 CRUD, 지정일 생성·누락 월 보충·중복 방지, 말일 보정, 중지·재시작 소급 방지, 사용자 격리, 입력 검증, 기존 DB 업그레이드, 계정 삭제 |
| SavingsGoalChecks | 진행률(내림)·필요 월 저축액(올림)·기한 지남·예상 달성 시점 계산, 목표·저축 CRUD와 입력 검증, 사용자 격리, 목표 삭제 연쇄, 평균 순수지, 화면 로직, 기존 DB 업그레이드, 계정 삭제 |
| MigrationChecks | 새·기존·아주 오래된 DB의 마이그레이션, 데이터 손실 없는 기준선 기록, 반복 시작 멱등성, 마이그레이션 결과와 모델 스키마 일치(외래 키 포함), 모델 변경 시 마이그레이션 누락 감지, 로그인 DB |
| PwaChecks | 매니페스트 필수 항목, 아이콘 파일·크기·투명 채널, 바로가기가 실제 화면 경로인지, `App.razor` 연결, 서비스 워커의 개인정보 보호 규칙(페이지 미캐시)과 동작(Node로 시뮬레이션: 온라인/오프라인 이동, 비이동 요청·POST 미가로채기, 캐시 정리), 자체 완결형 오프라인 화면 |
| DatabaseBackupChecks | 설정 검증·기본값, 다음 백업까지의 시간(경계·시계 역행), 실행 중 쓰기와 겹쳐도 일관된 복사본, 잠금(BUSY/LOCKED) 재시도의 횟수 제한과 그 밖 오류 비재시도, 손상된 DB 거절, 실패·취소 시 흔적 없음, 같은 초 이름 충돌, 우리 형식이 아닌 폴더·심볼릭 링크 보호, 보관 정책(정기/업그레이드 전 분리), 주기 실행(실패 재시도·취소·정리), 업그레이드 전 백업 필요 판단과 옛 스키마 스냅숏 복원 |
| TagChecks | 태그 이름 규칙(대소문자·공백·#), 생성·재사용·계정별 분리, 수정 시 유지(null)·삭제(빈 목록)·교체, 태그 필터, 사용 현황, 이름 변경·합치기(중복 없이 이동), 삭제 시 연결 정리와 지출 보존, 계정당 100개 제한, 백업 v2 왕복 바이트 일치·병합 멱등·버전 1 호환·손상 거절, CSV 왕복·옛 형식 호환, 연간 통계, 지출·태그 관리 화면 논리와 렌더링 |
| UsabilityChecks | 메모 추천(기간·소유자·대소문자·최신 기록), 연속 입력 후 유지·초기화, 자동 완성 규칙, 월 이동 경계, 삭제 되돌리기(보관 카테고리·삭제된 결제수단·계정 변경 거절), 내역 나눠 보기(쪽 크기·경계·합계 불변·추가/수정/되돌림 후 표시), 달력 계산(주 배치·월말·윤년·인접 월 제외)·금액 줄이기·서비스 소유자 격리, 컴포넌트 렌더링, 입력 칩(카테고리·메모)·줄 눌러 수정·`⋯` 메뉴·수정 시트, 로딩·빈 화면 컴포넌트, 하단 탭바·화면 설정·건너뛰기 링크 연결, **문자열 매개변수를 변수 이름 그대로 넘기는 실수 방지** |
| 화면 색 점검(`tools/theme`) | 글자 대비 4.5:1 이상, 모든 CSS 색에 다크 모드 토큰이 있는지(새 색을 넣고 생성기를 안 돌리면 실패) |
| StatisticsChecks | 올해·지난 해 기간과 전년 비교(윤일), 월별·카테고리·결제수단·요일 집계, 월 평균(끝난 달만), TOP·자주 쓴 항목 정렬, 소유자 격리, 오늘 끝까지 포함, 연도 목록, 화면 로직 |
| BackupChecks | 내보내기(계정 정보 미포함·결정적 출력), **다른 계정으로 복원 후 내보내기가 바이트 단위로 일치**, 병합 반복 시 변화 없음과 정당한 중복 보존, 일부 손실 후 빠진 것만 채움, 미리보기와 실제 결과 일치, 덮어쓰기가 모든 종류의 데이터와 연결 행을 정리, 결제수단 이름 연결·충돌 알림, 정기 규칙 이력으로 중복 생성 방지, 중간 실패 시 전체 롤백, 목표 개수 제한, 다운로드 헤더·인증, 화면 로직(확인 문구·계정 변경 차단·크기 제한) |

검증은 외부 테스트 프레임워크 없이 실행하는 콘솔 프로그램입니다. **메모리 SQLite만 사용하므로 실제 사용자 데이터를 변경하지 않습니다.** 실패 시 오류와 0이 아닌 종료 코드를 반환하며, 공통 도구는 `tests/TestSupport/`에서 관리합니다.

### 자동 검증(CI)

`main` 푸시와 풀 리퀘스트마다 [GitHub Actions](.github/workflows/ci.yml)가 **앱 빌드 → `tests/run-checks.sh`(전체 검증) → Docker 이미지 빌드**를 실행합니다. 모델을 바꾸고 마이그레이션을 추가하지 않으면 `MigrationChecks`가 실패해 배포 전에 알 수 있습니다.

## DB 마이그레이션

스키마는 [EF Core Migrations](https://learn.microsoft.com/ef/core/managing-schemas/migrations/)로 관리합니다. 지출 DB(`ExpensesDbContext`, `myexpenses.db`)와 로그인 DB(`AuthDbContext`, `auth.db`)가 각각 독립된 마이그레이션을 가지며, 앱은 시작할 때 아직 적용하지 않은 마이그레이션만 적용합니다.

- **새 DB:** 마이그레이션을 처음부터 적용합니다.
- **마이그레이션 도입 이전에 만든 DB:** 기존 방식(`ExpensesSchema`)으로 최신 상태까지 맞춘 뒤 `InitialCreate`를 **적용된 것으로 기록만** 합니다. 테이블을 다시 만들거나 데이터를 변경하지 않으며, 이후부터는 새 마이그레이션만 적용합니다. 이 DB는 외래 키 대신 소유자 검사 트리거를 쓰는 등 새 DB와 세부 구조가 조금 다를 수 있지만, 이전부터 동일한 구조입니다.
- **모델과 마이그레이션이 어긋난 경우:** 앱이 시작을 거부하고 오류를 냅니다(`PendingModelChangesWarning`). 마이그레이션을 빠뜨린 채 배포하는 일을 막기 위한 동작입니다.

### 스키마를 바꾸는 방법

로컬 도구(`dotnet-ef`)는 버전을 고정해 두었습니다.

```bash
dotnet tool restore

# 지출 DB: 모델(Data/*.cs, ExpensesDbContext)을 수정한 뒤
dotnet ef migrations add 변경내용 --context ExpensesDbContext --output-dir Data/Migrations/Expenses

# 로그인 DB를 바꾸는 경우(드물게 필요)
dotnet ef migrations add 변경내용 --context AuthDbContext --output-dir Data/Migrations/Auth

bash tests/run-checks.sh   # MigrationChecks가 모델과 마이그레이션의 일치를 확인합니다
```

`ExpensesSchema.cs`에는 더 이상 테이블·열을 추가하지 마세요. 생성된 마이그레이션 파일은 반드시 함께 커밋합니다. 마이그레이션을 만드는 명령은 DB에 연결하지 않으므로 로컬 데이터가 바뀌지 않습니다.

## 운영 배포

[Dockerfile](Dockerfile)은 .NET 10 앱을 Release 모드로 게시하고 비루트 `app` 사용자로 실행합니다. 컨테이너의 수신 주소는 `0.0.0.0:10000`입니다.

운영 환경변수 예시는 다음과 같습니다. OAuth 리디렉션 URI에는 **실제 HTTPS 배포 주소 + `/signin-google`**을 별도로 등록해야 합니다.

```text
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_HTTP_PORTS=10000
Storage__DataDirectory=/app/Data
ReverseProxy__UseForwardedHeaders=true
Authentication__Google__ClientId=...
Authentication__Google__ClientSecret=...
Support__Email=...
```

- **영구 저장:** `/app/Data`를 호스팅 제공자의 암호화된 영구 디스크에 마운트하세요. 임시 파일 시스템을 사용하면 재배포 시 계정·지출 데이터가 사라질 수 있습니다.
- **인스턴스 수:** SQLite를 사용하는 동안은 앱 인스턴스를 하나로 유지하세요.
- **백업과 업그레이드:** 앱을 새 버전으로 배포하면 시작 시 DB 마이그레이션이 자동으로 적용되며 **되돌리는(다운그레이드) 마이그레이션은 제공하지 않습니다.** 대신 **스키마가 바뀌는 업그레이드 직전에 DB를 자동으로 백업**하고 매일 정기 백업도 만듭니다([서버 자동 백업](#서버-자동-백업)). 그래도 중요한 배포 전에는 영구 디스크의 `/app/Data`(특히 `myexpenses.db`, `auth.db`)를 따로 복사해 두는 편이 안전합니다. 사용자는 앱의 **백업·복원**(`/backup`)으로 자신의 데이터를 직접 내려받을 수 있습니다(서버 DB 백업을 대신하지는 않습니다). 백업에는 사용자 데이터가 포함되므로 계정 삭제·개인정보 처리 정책에 맞게 보관 기간을 관리하세요.
- **프록시:** `ReverseProxy__UseForwardedHeaders=true`는 신뢰할 수 있는 HTTPS 종료 역방향 프록시 환경에서만 사용합니다. 앱을 인터넷에 직접 노출할 때는 활성화하지 마세요.
- **운영 문서:** 실제 운영·백업·법적 요구사항에 맞게 개인정보 처리방침과 이용약관을 검토하고, `Support:Email`을 설정하세요.

### 서버 자동 백업

서버가 `myexpenses.db`와 `auth.db`를 주기적으로 복사해 `백업 폴더/yyyyMMdd-HHmmss/`(한국 시간)에 둡니다. **모든 사용자의 데이터가 들어 있으므로 화면으로 내려받는 기능은 없고**, 서버 관리자가 파일로 복원합니다. 사용자가 자기 데이터를 내려받는 `/backup`과는 별개입니다.

- **언제 만드나:** 앱이 시작되고 `StartDelaySeconds`(기본 30초) 뒤에 마지막 정기 백업으로부터 `IntervalHours`(기본 24시간)가 지났으면 만들고, 이후 그 주기로 반복합니다. 앱을 껐다 켜도 최근 백업이 있으면 바로 또 만들지 않습니다. **스키마가 바뀌는 업그레이드 직전**(마이그레이션 이전 DB 또는 적용하지 않은 마이그레이션이 있을 때)에는 `-premigrate` 백업을 따로 만듭니다. 새 DB와 이미 최신인 DB는 만들지 않습니다.
- **어떻게 만드나:** SQLite 온라인 백업 API로 실행 중에도 일관된 복사본을 만들고, 복사본을 `PRAGMA integrity_check`로 점검한 뒤에만 완성된 백업으로 인정합니다. 임시 폴더에서 만들어 이름을 바꾸므로 **실패하거나 중단돼도 반쯤 만들어진 백업이 남지 않습니다.** 정기 백업이 실패하면 오류를 남기고 1시간 뒤에 다시 시도하며, 업그레이드 전 백업이 실패해도 앱은 시작합니다(오류 기록).
- **얼마나 보관하나:** 정기 백업은 최근 `Keep`개(기본 14), 업그레이드 전 백업은 최근 `KeepBeforeMigration`개(기본 3, 0이면 만들지 않음)만 남기고 지웁니다. 이름 형식이 다른 폴더·파일과 심볼릭 링크는 건드리지 않습니다. 디스크 사용량은 대략 DB 크기 × 보관 개수입니다.
- **설정(환경변수 또는 appsettings):**

| 키 | 기본값 | 설명 |
| --- | --- | --- |
| `Backup__Enabled` | `true` | `false`면 자동 백업과 업그레이드 전 백업을 모두 끕니다 |
| `Backup__IntervalHours` | `24` | 정기 백업 주기(1~720시간) |
| `Backup__Keep` | `14` | 보관할 정기 백업 개수(1~365) |
| `Backup__KeepBeforeMigration` | `3` | 보관할 업그레이드 전 백업 개수(0~20) |
| `Backup__Directory` | `Data/backups` | 백업 폴더(상대 경로는 데이터 폴더 기준) |
| `Backup__StartDelaySeconds` | `30` | 앱 시작 후 첫 확인까지 기다리는 시간 |

  잘못된 값은 기본값으로 바꾸고 시작 로그에 경고를 남깁니다.
- **로그로 확인:** `docker logs myexpenses-local | grep 백업`. `데이터베이스 백업을 만들었습니다`가 보이면 정상입니다.
- **데이터와 다른 곳에 두기(권장):** 기본 백업 폴더는 데이터와 **같은 볼륨**에 있어 볼륨을 잃으면 백업도 잃습니다. 가능하면 별도 볼륨(또는 다른 디스크)에 두세요.

```bash
docker volume create myexpenses-backups
docker run -d --name myexpenses-local --env-file .env.docker \
  -p 10000:10000 -v myexpenses-data:/app/Data \
  -v myexpenses-backups:/app/Backups -e Backup__Directory=/app/Backups myexpenses:latest
```

- **복원(Docker):** 앱을 멈춘 뒤 원하는 백업의 두 파일을 데이터 볼륨으로 복사하고 다시 시작합니다. 옛 스키마의 백업을 복원해도 시작할 때 다시 업그레이드됩니다. 컨테이너의 `app` 사용자 번호(보통 1654)로 복사해야 앱이 읽고 쓸 수 있습니다(`docker run --rm --entrypoint id myexpenses:latest app`으로 확인).

```bash
docker run --rm -v myexpenses-data:/data alpine ls -l /data/backups      # 백업 목록(기본 폴더인 경우)
docker stop myexpenses-local
docker run --rm --user 1654:1654 -v myexpenses-data:/data alpine sh -c '
  cp /data/backups/20261006-120000/myexpenses.db /data/myexpenses.db &&
  cp /data/backups/20261006-120000/auth.db /data/auth.db &&
  rm -f /data/myexpenses.db-wal /data/myexpenses.db-shm /data/auth.db-wal /data/auth.db-shm'
docker start myexpenses-local
```

- **복원(로컬 실행):** 앱을 종료하고 `Data/backups/<이름>/`의 `myexpenses.db`, `auth.db`를 `Data/`에 덮어쓴 뒤 다시 실행합니다.
- **한계:** 같은 서버 안의 복사본이라 서버·디스크 자체가 망가지는 사고(도난, 화재 등)에는 도움이 되지 않습니다. 중요한 데이터라면 백업 폴더를 정기적으로 다른 곳에 복사하세요. 로그인 키(`Data/keys/`)는 백업하지 않으므로 키가 없으면 로그인 쿠키만 무효가 되어 다시 로그인하면 됩니다.

로그인 없이 접근할 수 있는 경로는 다음과 같습니다.

| 경로 | 용도 |
| --- | --- |
| `/about` | 서비스 소개 |
| `/privacy` | 개인정보 처리방침 |
| `/terms` | 이용약관 |
| `/healthz` | 실행 상태 확인 |

## 데이터와 보안

| 기본 저장 경로 | 내용 |
| --- | --- |
| `Data/myexpenses.db` | 지출·예산·정기 지출·템플릿·결제수단·사용자 카테고리·첫 시작 완료 여부 |
| `Data/auth.db` | Identity 계정과 Google 로그인 연결 |
| `Data/keys/` | ASP.NET Core 데이터 보호 키 |
| `Data/backups/` | 서버 자동 백업(두 DB 파일의 정기 복사본) |

DB와 키는 실행 시 생성되며 Git에서 제외됩니다. Docker에서는 `myexpenses-data` 볼륨에, 별도 저장 경로를 설정한 경우에는 해당 경로에 저장됩니다.

- **사용자 분리:** 데이터는 ASP.NET Core Identity 사용자 ID로 구분합니다. 결제수단 연결도 서비스 검증과 DB 제약·트리거로 다른 사용자의 연결을 차단합니다.
- **백업:** 서버가 DB를 자동으로 복사해 둡니다([서버 자동 백업](#서버-자동-백업)). 직접 백업하려면 앱을 중지한 뒤 **두 DB 파일과 `Data/keys/`를 함께 복사**하세요. CSV는 실제 지출만 포함하므로 전체 데이터 백업을 대신하지 않습니다.
- **삭제:** 지출 전체 삭제는 현재 사용자의 지출만 삭제합니다. `/account`의 계정 삭제는 해당 사용자의 예산·규칙·템플릿·결제수단·사용자 카테고리·프로필·로그인 연결까지 삭제하며, Google 계정 자체를 삭제하지는 않습니다. 삭제한 데이터는 되돌릴 수 없습니다.
- **기존 데이터:** 이전 단일 사용자 DB를 업그레이드하면 기존 데이터는 업그레이드 후 처음 로그인한 계정에 귀속됩니다.
- **비밀 정보:** Client Secret과 `.env.docker`를 공유하거나 커밋하지 마세요. 결제수단에는 카드번호·계좌번호·금융 인증정보를 입력하지 마세요.

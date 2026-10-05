using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

var root = AppContext.BaseDirectory;
while (root is not null && !File.Exists(Path.Combine(root, "MyExpenses.csproj")))
    root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar));
Check(root is not null, "MyExpenses.csproj를 찾을 수 없습니다.");
string P(string relative) => Path.Combine(root!, relative.Replace('/', Path.DirectorySeparatorChar));

// ---- 매니페스트 ----
using var manifestDocument = JsonDocument.Parse(File.ReadAllText(P("wwwroot/manifest.webmanifest")));
var manifest = manifestDocument.RootElement;
string Text(JsonElement element, string name) => element.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";
Check(Text(manifest, "name") != "" && Text(manifest, "short_name") != "", "name/short_name 누락");
Check(Text(manifest, "short_name").Length <= 12, "short_name은 홈 화면 아이콘 아래에 잘리지 않도록 12자 이하여야 합니다.");
Check(Text(manifest, "display") == "standalone", "display는 standalone이어야 합니다.");
Check(Text(manifest, "start_url") == "/" && Text(manifest, "scope") == "/", "start_url/scope는 / 여야 합니다.");
Check(Regex.IsMatch(Text(manifest, "theme_color"), "^#[0-9a-fA-F]{6}$") && Regex.IsMatch(Text(manifest, "background_color"), "^#[0-9a-fA-F]{6}$"), "색상 형식 오류");
Check(Text(manifest, "lang") == "ko", "lang은 ko여야 합니다.");

// ---- 아이콘: 파일 존재, PNG 형식, 선언한 크기와 실제 크기 일치, 필요한 용도 구비 ----
static (int Width, int Height) PngSize(byte[] bytes)
{
    byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    if (bytes.Length < 24 || !bytes.AsSpan(0, 8).SequenceEqual(signature)) throw new Exception("PNG 파일이 아닙니다.");
    int Read(int offset) => (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
    return (Read(16), Read(20));
}
var icons = manifest.GetProperty("icons").EnumerateArray().ToList();
foreach (var icon in icons)
{
    var src = Text(icon, "src");
    var file = P("wwwroot/" + src);
    Check(File.Exists(file), $"아이콘 파일 없음: {src}");
    var (width, height) = PngSize(File.ReadAllBytes(file));
    Check(Text(icon, "type") == "image/png" && Text(icon, "sizes") == $"{width}x{height}" && width == height, $"아이콘 크기 불일치: {src} ({width}x{height}, 선언 {Text(icon, "sizes")})");
}
bool HasIcon(string purpose, string sizes) => icons.Any(icon => Text(icon, "purpose") == purpose && Text(icon, "sizes") == sizes);
Check(HasIcon("any", "192x192") && HasIcon("any", "512x512") && HasIcon("maskable", "512x512"), "any 192·512와 maskable 512 아이콘이 모두 필요합니다.");
var appleIcon = P("wwwroot/icons/apple-touch-icon.png");
Check(File.Exists(appleIcon) && PngSize(File.ReadAllBytes(appleIcon)) == (180, 180), "apple-touch-icon.png(180x180) 누락 또는 크기 오류");
// iOS는 투명 영역을 검게 칠하므로 apple-touch-icon과 maskable 아이콘은 투명 픽셀이 없어야 합니다(PNG 색상 유형 2 = RGB).
Check(File.ReadAllBytes(appleIcon)[25] == 2 && File.ReadAllBytes(P("wwwroot/icons/icon-maskable-512.png"))[25] == 2, "apple-touch-icon/maskable 아이콘에 투명 채널이 있습니다.");

// ---- 바로가기는 실제로 존재하는 화면 경로여야 합니다 ----
var routes = Directory.GetFiles(P("Components/Pages"), "*.razor", SearchOption.AllDirectories)
    .SelectMany(file => Regex.Matches(File.ReadAllText(file), "^@page \"([^\"]+)\"", RegexOptions.Multiline).Select(match => match.Groups[1].Value))
    .ToHashSet();
foreach (var shortcut in manifest.GetProperty("shortcuts").EnumerateArray())
    Check(routes.Contains(Text(shortcut, "url")), $"바로가기 경로가 존재하지 않습니다: {Text(shortcut, "url")}");
Check(routes.Contains(Text(manifest, "start_url")), "start_url 경로가 존재하지 않습니다.");

// ---- 문서(head)와 등록 스크립트 ----
var app = File.ReadAllText(P("Components/App.razor"));
Check(Regex.IsMatch(app, "<link rel=\"manifest\" href=\"manifest\\.webmanifest\""), "App.razor에 manifest 링크가 없습니다.");
Check(app.Contains($"<meta name=\"theme-color\" content=\"{Text(manifest, "theme_color")}\""), "App.razor의 theme-color가 매니페스트와 다릅니다.");
Check(app.Contains("rel=\"apple-touch-icon\" href=\"icons/apple-touch-icon.png\"") && app.Contains("<script src=\"pwa.js\"></script>"), "apple-touch-icon 또는 pwa.js 참조가 없습니다.");
var register = File.ReadAllText(P("wwwroot/pwa.js"));
Check(register.Contains("register('/service-worker.js', { scope: '/' })") && register.Contains("'serviceWorker' in navigator"), "서비스 워커 등록 코드가 올바르지 않습니다.");

// ---- 서비스 워커 정적 규칙(개인정보·안전성) ----
var worker = File.ReadAllText(P("wwwroot/service-worker.js"));
Check(worker.Contains("request.mode !== 'navigate'") && worker.Contains("request.method !== 'GET'"), "서비스 워커는 GET 페이지 이동만 처리해야 합니다.");
Check(!worker.Contains("cache.put(") && !worker.Contains(".addAll("), "서비스 워커가 페이지·데이터를 캐시에 저장하면 안 됩니다(로그인·가계부 정보 보호).");
Check(worker.Contains("const OFFLINE_URL = '/offline.html'") && File.Exists(P("wwwroot/offline.html")), "오프라인 안내 화면 연결이 올바르지 않습니다.");

// ---- 오프라인 화면은 외부 파일 없이 그 자체로 표시되어야 합니다 ----
var offline = File.ReadAllText(P("wwwroot/offline.html"));
Check(!Regex.IsMatch(offline, "<link\\b|<script\\s+src=|<img\\b|url\\(|@import", RegexOptions.IgnoreCase), "offline.html이 외부 파일에 의존합니다(오프라인에서 깨집니다).");
Check(offline.Contains("id=\"retry\"") && offline.Contains("location.reload()"), "offline.html에 다시 시도 동작이 없습니다.");

Console.WriteLine($"PASS: manifest, {icons.Count} icons (+apple-touch), shortcuts/routes, head tags, service worker rules, self-contained offline page");

// ---- 서비스 워커 동작 시뮬레이션(Node) ----
var requireNode = Environment.GetEnvironmentVariable("PWA_REQUIRE_NODE") == "1";
try
{
    using var process = Process.Start(new ProcessStartInfo("node", $"\"{P("tests/PwaChecks/service-worker.test.mjs")}\"")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true
    })!;
    var output = process.StandardOutput.ReadToEnd();
    var error = process.StandardError.ReadToEnd();
    process.WaitForExit();
    Check(process.ExitCode == 0, "서비스 워커 동작 검증 실패:\n" + error + output);
    Console.Write(output);
}
catch (Win32Exception) when (!requireNode)
{
    Console.WriteLine("SKIP: node를 찾을 수 없어 서비스 워커 동작 시뮬레이션을 건너뜁니다(CI에서는 필수로 실행됩니다).");
}

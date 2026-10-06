using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MyExpenses.Components.Pages;
using MyExpenses.Data;
using MyExpenses.Services;
using MyExpenses.Testing;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static async Task Reject(Func<Task> action, string message)
{
    try { await action(); throw new Exception("Accepted: " + message); }
    catch (ArgumentException) { }
}
static DateTime D(int y, int m, int d) => new(y, m, d);

// ── 이름 규칙 ──
Check(TagNames.Clean(" #여행 ") == "여행" && TagNames.Clean("＃  나들이   가기 ") == "나들이 가기" && TagNames.Clean("##x") == "x", "Clean wrong");
foreach (var bad in new[] { "", "   ", "#", "a,b", "a;b", "a，b", "a\nb", new string('가', 21), null })
    Check(TagNames.Clean(bad) is null, $"Clean accepted '{bad}'");
Check(TagNames.Clean(new string('가', 20)) is { Length: 20 }, "20 chars must be allowed");
Check(TagNames.Parse("여행, #경조사 ,여행,  ,Trip,trip").SequenceEqual(new[] { "여행", "경조사", "Trip" }), "Parse wrong: " + string.Join("|", TagNames.Parse("여행, #경조사 ,여행,  ,Trip,trip")));
Check(TagNames.Parse(null).Count == 0 && TagNames.Parse(" , ,#").Count == 0, "Empty parse wrong");
Check(TagNames.Parse("a\nb，c").SequenceEqual(new[] { "a", "b", "c" }), "Parse separators wrong");
try { TagNames.Parse("1,2,3,4,5,6"); throw new Exception("6 tags accepted"); } catch (ArgumentException) { }
try { TagNames.Parse("ok,bad;tag"); throw new Exception("Bad tag accepted"); } catch (ArgumentException) { }
Check(TagNames.Parse("1,2,3,4,5").Count == 5 && TagNames.Parse("a,A,b,B,c,C,d,D,e,E").Count == 5, "5 distinct tags must pass");

await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
var options = new DbContextOptionsBuilder<ExpensesDbContext>().UseSqlite(connection).Options;
await using (var init = new ExpensesDbContext(options)) await DatabaseMigrator.MigrateExpensesAsync(init);   // 실제 마이그레이션 스키마
var factory = new TestFactory(options);
var expenses = new ExpenseService(factory);
var tags = new TagService(factory);
await using var db = new ExpensesDbContext(options);

// ── 추가: 태그 생성·재사용·정규화 ──
var a1 = await expenses.AddAsync("A", new(D(2026, 10, 1), 10_000, "식비", "제주 점심", null, ["여행", "#Trip"]));
Check(a1.TagNames.SequenceEqual(new[] { "Trip", "여행" }), "Added tags: " + string.Join(",", a1.TagNames));
var a2 = await expenses.AddAsync("A", new(D(2026, 10, 2), 20_000, "교통", "택시", null, ["trip", " 여행 ", "출장"]));
Check(await db.Tags.CountAsync(t => t.OwnerId == "A") == 3, "Case-insensitive reuse failed: " + await db.Tags.CountAsync(t => t.OwnerId == "A"));
Check((await db.Tags.SingleAsync(t => t.OwnerId == "A" && t.NormalizedName == "TRIP")).Name == "Trip", "Original display name must be kept");
var a3 = await expenses.AddAsync("A", new(D(2026, 10, 3), 5_000, "식비", "태그없음"));
Check(a3.TagNames.Count == 0 && a3.TagLinks.Count == 0, "No tags by default");
var b1 = await expenses.AddAsync("B", new(D(2026, 10, 1), 7_000, "식비", "B의 지출", null, ["여행"]));
Check(await db.Tags.CountAsync(t => t.OwnerId == "B") == 1 && (await db.Tags.CountAsync(t => t.Name == "여행")) == 2, "Tags must be per owner");
Check((await db.Tags.SingleAsync(t => t.OwnerId == "B")).Id != (await db.Tags.SingleAsync(t => t.OwnerId == "A" && t.Name == "여행")).Id, "Owners shared a tag row");

// ── 입력 검증: 실패하면 아무것도 만들지 않음 ──
var tagCount = await db.Tags.CountAsync();
var expenseCount = await db.Expenses.CountAsync();
await Reject(() => expenses.AddAsync("A", new(D(2026, 10, 4), 1, "식비", "x", null, ["a", "b", "c", "d", "e", "f"])), "6 tags");
await Reject(() => expenses.AddAsync("A", new(D(2026, 10, 4), 1, "식비", "x", null, ["신규태그", "bad,tag"])), "comma tag");
await Reject(() => expenses.AddAsync("A", new(D(2026, 10, 4), 1, "식비", "x", null, ["신규태그", new string('x', 21)])), "long tag");
await Reject(() => expenses.AddAsync("A", new(D(2026, 10, 4), 1, "식비", "x", null, ["신규태그", ""])), "empty tag");
Check(await db.Tags.CountAsync() == tagCount && await db.Expenses.CountAsync() == expenseCount, "Failed add left data behind");

// ── 수정: null은 유지, 빈 목록은 삭제, 목록은 교체 ──
var edited = await expenses.UpdateAsync("A", a1.Id, new(a1.Date, 11_000, "식비", "제주 점심", null, null));
Check(edited!.Amount == 11_000, "Update amount failed");
Check((await TagNamesOf(a1.Id)).SequenceEqual(new[] { "Trip", "여행" }), "null Tags must keep the existing tags");
await expenses.UpdateAsync("A", a1.Id, new(a1.Date, 11_000, "식비", "제주 점심", null, ["출장", "여행"]));
Check((await TagNamesOf(a1.Id)).SequenceEqual(new[] { "여행", "출장" }), "Replace tags wrong: " + string.Join(",", await TagNamesOf(a1.Id)));
Check(await db.ExpenseTags.CountAsync(l => l.ExpenseId == a1.Id) == 2, "Link rows wrong after replace");
await expenses.UpdateAsync("A", a1.Id, new(a1.Date, 11_000, "식비", "제주 점심", null, []));
Check((await TagNamesOf(a1.Id)).Count == 0 && await db.ExpenseTags.CountAsync(l => l.ExpenseId == a1.Id) == 0, "Empty list must clear tags");
Check(await db.Tags.CountAsync(t => t.OwnerId == "A") == 3, "Clearing links must not delete tags");
await expenses.UpdateAsync("A", a1.Id, new(a1.Date, 11_000, "식비", "제주 점심", null, ["여행", "여행", "TRIP"]));
Check((await TagNamesOf(a1.Id)).SequenceEqual(new[] { "Trip", "여행" }), "Update dedupe wrong");
await Reject(() => expenses.UpdateAsync("A", a1.Id, new(a1.Date, 1, "식비", "x", null, ["1", "2", "3", "4", "5", "6"])), "update 6 tags");
Check((await TagNamesOf(a1.Id)).SequenceEqual(new[] { "Trip", "여행" }), "Failed update changed tags");
Check(await expenses.UpdateAsync("A", b1.Id, new(b1.Date, 1, "식비", "x", null, ["침입"])) is null && (await TagNamesOf(b1.Id)).SequenceEqual(new[] { "여행" }), "Updated another owner's expense");
async Task<List<string>> TagNamesOf(int id) => (await expenses.ListAsync(id == b1.Id ? "B" : "A", new())).Single(e => e.Id == id).TagNames.ToList();

// ── 목록·필터 ──
var all = await expenses.ListAsync("A", new());
Check(all.Single(e => e.Id == a2.Id).TagNames.SequenceEqual(new[] { "Trip", "여행", "출장" }), "List tags wrong");
Check(all.Count == 3, "List count wrong (join duplicates?)");
var tripId = (await db.Tags.SingleAsync(t => t.OwnerId == "A" && t.NormalizedName == "TRIP")).Id;
var tripList = await expenses.ListAsync("A", new ExpenseFilter(TagId: tripId));
Check(tripList.Select(e => e.Id).OrderBy(i => i).SequenceEqual(new[] { a1.Id, a2.Id }), "Tag filter wrong");
Check(tripList.All(e => e.TagNames.Contains("Trip")) && tripList.Count == 2, "Tag filter must not hide other tags of the match");
Check((await expenses.ListAsync("B", new ExpenseFilter(TagId: tripId))).Count == 0, "Another owner's tag id leaked expenses");
Check((await expenses.ListAsync("A", new ExpenseFilter(TagId: 999_999))).Count == 0, "Unknown tag id must match nothing");
Check(new ExpenseFilter(TagId: 1).IsActive && !new ExpenseFilter().IsActive, "IsActive must count the tag");
Check(new ExpenseFilter(TagId: tripId).Matches(a2) && !new ExpenseFilter(TagId: tripId).Matches(a3), "Matches wrong");
Check((await expenses.ListAsync("A", new ExpenseFilter(Month: D(2026, 10, 1), TagId: tripId, MinAmount: 15_000))).Single().Id == a2.Id, "Filter combination wrong");
Check(new ExpenseSearchInput { Tag = "7" }.TryCreate(out var parsed, out _) && parsed.TagId == 7 && new ExpenseSearchInput().TryCreate(out var none, out _) && none.TagId is null, "Search input tag parse wrong");
foreach (var bad in new[] { "0", "-1", "abc", "1.5", " 3" })
    Check(!new ExpenseSearchInput { Tag = bad }.TryCreate(out _, out var message) && message is not null, $"Tag '{bad}' accepted");

// ── 사용 현황 ──
var usage = await tags.ListAsync("A");
Check(usage.Select(u => u.Name).SequenceEqual(new[] { "Trip", "여행", "출장" }.OrderBy(n => n, StringComparer.Ordinal)), "Usage order wrong: " + string.Join(",", usage.Select(u => u.Name)));
Check(usage.Single(u => u.Name == "Trip") is { ExpenseCount: 2, Amount: 31_000 } && usage.Single(u => u.Name == "출장") is { ExpenseCount: 1, Amount: 20_000 }, "Usage totals wrong");
Check((await tags.ListAsync("B")).Single() is { Name: "여행", ExpenseCount: 1, Amount: 7_000 } && (await tags.ListAsync("Z")).Count == 0, "Owner isolation in usage failed");

// ── 이름 변경·합치기 ──
Check(await tags.RenameAsync("A", tripId, "TRIP") == TagRenameResult.Renamed && (await db.Tags.AsNoTracking().SingleAsync(t => t.Id == tripId)).Name == "TRIP", "Case-only rename failed");
await Reject(() => tags.RenameAsync("A", tripId, "a,b"), "rename invalid");
Check(await tags.RenameAsync("A", tripId, "휴가") == TagRenameResult.Renamed, "Rename failed");
Check(await tags.RenameAsync("A", tripId, "여행") == TagRenameResult.Conflict && (await db.Tags.AsNoTracking().SingleAsync(t => t.Id == tripId)).Name == "휴가", "Conflict must not change anything");
Check(await tags.RenameAsync("A", 999_999, "x") == TagRenameResult.NotFound && await tags.RenameAsync("B", tripId, "x") == TagRenameResult.NotFound, "Rename of missing/foreign tag wrong");
var travelId = (await db.Tags.SingleAsync(t => t.OwnerId == "A" && t.Name == "여행")).Id;
// a1·a2 모두 '휴가'와 '여행'을 가지고 있음 → 합치면 중복 없이 '여행' 하나만 남아야 함
Check(await tags.RenameAsync("A", tripId, "여행", mergeIfExists: true) == TagRenameResult.Merged, "Merge failed");
Check(!await db.Tags.AnyAsync(t => t.Id == tripId) && await db.ExpenseTags.CountAsync(l => l.TagId == travelId) == 2, "Merge left the old tag or duplicated links");
Check(await db.ExpenseTags.CountAsync(l => l.TagId == tripId) == 0, "Merge left links to the removed tag");
// 한쪽 지출에만 붙은 태그를 합치기: 이동되어야 함
var onlyA2 = (await db.Tags.SingleAsync(t => t.OwnerId == "A" && t.Name == "출장")).Id;
Check(await tags.RenameAsync("A", onlyA2, "여행", mergeIfExists: true) == TagRenameResult.Merged && await db.ExpenseTags.CountAsync(l => l.TagId == travelId) == 2, "Merge with overlap wrong");
var a4 = await expenses.AddAsync("A", new(D(2026, 10, 5), 1_000, "식비", "신규", null, ["임시"]));
var tempId = (await db.Tags.SingleAsync(t => t.OwnerId == "A" && t.Name == "임시")).Id;
Check(await tags.RenameAsync("A", tempId, "여행", mergeIfExists: true) == TagRenameResult.Merged && (await TagNamesOf(a4.Id)).SequenceEqual(new[] { "여행" }), "Merge must move links of the removed tag");
Check(await db.ExpenseTags.CountAsync(l => l.TagId == travelId) == 3, "Moved link count wrong");

// ── 삭제: 태그를 지워도 지출은 남음, 지출을 지우면 연결도 사라짐 ──
Check(await tags.DeleteAsync("B", travelId) is false && await db.Tags.AnyAsync(t => t.Id == travelId), "Deleted another owner's tag");
var beforeExpenses = await db.Expenses.CountAsync(e => e.OwnerId == "A");
Check(await tags.DeleteAsync("A", travelId) && !await db.Tags.AnyAsync(t => t.Id == travelId), "Tag delete failed");
Check(await db.Expenses.CountAsync(e => e.OwnerId == "A") == beforeExpenses && await db.ExpenseTags.CountAsync(l => l.TagId == travelId) == 0, "Tag delete must keep expenses and drop links");
Check(await tags.DeleteAsync("A", travelId) is false, "Second delete must be a no-op");
await expenses.UpdateAsync("A", a1.Id, new(a1.Date, 1, "식비", "x", null, ["링크확인"]));
Check(await expenses.DeleteAsync("A", a1.Id) && await db.ExpenseTags.CountAsync(l => l.ExpenseId == a1.Id) == 0, "Expense delete left orphan links");
await expenses.UpdateAsync("A", a2.Id, new(a2.Date, 1, "교통", "x", null, ["두개", "세개"]));
Check(await expenses.DeleteAllAsync("A") > 0 && await db.ExpenseTags.CountAsync(l => db.Expenses.All(e => e.Id != l.ExpenseId)) == 0, "Delete all left orphan links");
Check(await db.Tags.CountAsync(t => t.OwnerId == "A") > 0, "Delete all must keep the tags");
Check(await db.ExpenseTags.CountAsync() == await db.ExpenseTags.CountAsync(l => db.Expenses.Any(e => e.Id == l.ExpenseId)), "Orphan links exist");

// ── 태그 개수 제한(계정당 100개) ──
for (var i = 0; i < 20; i++)
    await expenses.AddAsync("L", new(D(2026, 10, 1), 1, "식비", "x", null, [$"t{i * 5}", $"t{i * 5 + 1}", $"t{i * 5 + 2}", $"t{i * 5 + 3}", $"t{i * 5 + 4}"]));
Check(await db.Tags.CountAsync(t => t.OwnerId == "L") == TagNames.MaxPerOwner, "Should have exactly 100 tags");
var lExpenses = await db.Expenses.CountAsync(e => e.OwnerId == "L");
await Reject(() => expenses.AddAsync("L", new(D(2026, 10, 2), 1, "식비", "x", null, ["t0", "새태그"])), "101st tag");
Check(await db.Tags.CountAsync(t => t.OwnerId == "L") == 100 && await db.Expenses.CountAsync(e => e.OwnerId == "L") == lExpenses, "Cap violation must not leave data");
await expenses.AddAsync("L", new(D(2026, 10, 2), 1, "식비", "x", null, ["T0", "t99"]));   // 기존 태그 재사용은 허용
Check(await db.Tags.CountAsync(t => t.OwnerId == "L") == 100, "Reusing tags at the cap must work");

// ── 계정 삭제: 지출·태그·연결 모두 정리, 다른 계정은 그대로 ──
await new UserDataDeletionService(factory).DeleteAsync("L");
Check(await db.Tags.CountAsync(t => t.OwnerId == "L") == 0 && await db.Expenses.CountAsync(e => e.OwnerId == "L") == 0, "Account deletion left tags/expenses");
Check(await db.Tags.AnyAsync(t => t.OwnerId == "B") && (await TagNamesOf(b1.Id)).SequenceEqual(new[] { "여행" }), "Account deletion damaged another owner");
Check(await db.ExpenseTags.CountAsync() == await db.ExpenseTags.CountAsync(l => db.Expenses.Any(e => e.Id == l.ExpenseId) && db.Tags.Any(t => t.Id == l.TagId)), "Dangling links after account deletion");

// ── 백업·복원 ──
var backup = new BackupService(factory);
var backupNow = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.FromHours(9));
await new CategoryService(factory).ListAsync("S");
await expenses.AddAsync("S", new(D(2026, 9, 1), 12_000, "식비", "제주 점심", null, ["여행", "Trip"]));
await expenses.AddAsync("S", new(D(2026, 9, 1), 12_000, "식비", "제주 점심", null, ["여행", "Trip"]));   // 정당한 중복
await expenses.AddAsync("S", new(D(2026, 9, 2), 3_000, "교통", "버스", null, ["=수식", "-하이픈"]));
await expenses.AddAsync("S", new(D(2026, 9, 3), 500, "식비", "태그없음"));
await expenses.AddAsync("S", new(D(2026, 9, 4), 700, "식비", "지울 지출", null, ["쓰지않음"]));
var removable = (await expenses.ListAsync("S", new ExpenseFilter(Search: "지울"))).Single();
await expenses.DeleteAsync("S", removable.Id);   // 태그는 남고 지출만 사라짐 → 쓰지 않는 태그
var fileS = await backup.ExportAsync("S", backupNow);
Check(fileS.Version == 2 && fileS.Data.Tags!.SequenceEqual(new[] { "=수식", "-하이픈", "Trip", "쓰지않음", "여행" }.Order(StringComparer.Ordinal)), "Export tags wrong: " + string.Join("|", fileS.Data.Tags!));
Check(fileS.Counts.Tags == 5 && fileS.Data.Expenses.Count == 4 && fileS.Data.Expenses.All(e => e.Tags is not null), "Export counts/tags wrong");
Check(string.Join("|", fileS.Data.Expenses[0].Tags!) == "Trip|여행" && fileS.Data.Expenses[3].Tags!.Count == 0, "Expense tags not exported sorted");
var bytesS = BackupSerializer.Serialize(fileS);
var parsedS = BackupSerializer.Parse(bytesS);
Check(parsedS.File is not null && parsedS.Issues.Count == 0, "Own export must parse: " + string.Join(" ", parsedS.Issues));
// 다른 계정에 덮어쓰기 → 다시 내보낸 결과가 바이트 단위로 같음(태그 이름·연결·쓰지 않는 태그 포함)
await expenses.AddAsync("T", new(D(2026, 1, 1), 1, "식비", "기존 데이터", null, ["기존태그", "여행"]));
var replaceReport = await backup.RestoreAsync("T", parsedS.File!, BackupRestoreMode.Replace, dryRun: false);
Check(replaceReport.Sections.Any(section => section.Name == "태그" && section.Total == 5 && section.Added == 5), "Tag section missing in report");
Check(BackupSerializer.Serialize(await backup.ExportAsync("T", backupNow)).AsSpan().SequenceEqual(bytesS), "Replace round trip not byte-identical with tags");
Check(!await db.Tags.AnyAsync(t => t.OwnerId == "T" && t.Name == "기존태그"), "Replace left the old tags");
Check(await db.ExpenseTags.CountAsync() == await db.ExpenseTags.CountAsync(l => db.Expenses.Any(e => e.Id == l.ExpenseId) && db.Tags.Any(t => t.Id == l.TagId)), "Dangling links after replace");
// 병합 반복: 아무것도 늘지 않음
var tagsBefore = await db.Tags.CountAsync(t => t.OwnerId == "T");
var linksBefore = await db.ExpenseTags.CountAsync();
var again = await backup.RestoreAsync("T", parsedS.File!, BackupRestoreMode.Merge, dryRun: false);
Check(again.TotalAdded == 0 && await db.Tags.CountAsync(t => t.OwnerId == "T") == tagsBefore && await db.ExpenseTags.CountAsync() == linksBefore, "Repeated merge must add nothing");
// 대소문자만 다른 기존 태그가 있으면 그 태그를 쓰고 표시 이름을 바꾸지 않음
await expenses.AddAsync("U", new(D(2026, 1, 1), 1, "식비", "기존", null, ["TRIP"]));
var dryRun = await backup.RestoreAsync("U", parsedS.File!, BackupRestoreMode.Merge, dryRun: true);
Check(await db.Tags.CountAsync(t => t.OwnerId == "U") == 1, "Dry run must not change tags");
var realRun = await backup.RestoreAsync("U", parsedS.File!, BackupRestoreMode.Merge, dryRun: false);
Check(dryRun.Sections.Select(x => (x.Name, x.Added)).SequenceEqual(realRun.Sections.Select(x => (x.Name, x.Added))), "Dry run differs from the real run");
Check(realRun.Sections.Single(x => x.Name == "태그").Added == 4, "Case-insensitive match during merge failed");
Check((await db.Tags.SingleAsync(t => t.OwnerId == "U" && t.NormalizedName == "TRIP")).Name == "TRIP", "Merge renamed an existing tag");
var uTrip = (await expenses.ListAsync("U", new())).Where(e => e.Memo == "제주 점심").ToList();
Check(uTrip.Count == 2 && uTrip.All(e => e.TagNames.SequenceEqual(new[] { "TRIP", "여행" })), "Restored expenses must use the existing tag");
Check(await db.Tags.CountAsync(t => t.OwnerId == "U") == 5, "Tag count after merge wrong");
// 이미 있는 지출의 태그는 병합이 바꾸지 않음
await expenses.AddAsync("V", new(D(2026, 9, 3), 500, "식비", "태그없음", null, ["다른태그"]));
await backup.RestoreAsync("V", parsedS.File!, BackupRestoreMode.Merge, dryRun: false);
Check((await expenses.ListAsync("V", new())).Single(e => e.Memo == "태그없음").TagNames.SequenceEqual(new[] { "다른태그" }), "Merge changed the tags of an existing expense");
// 계정당 100개 제한: 합치면 넘는 경우 거절하고 아무것도 바꾸지 않음
for (var i = 0; i < 19; i++) await expenses.AddAsync("W", new(D(2026, 1, 1), 1, "식비", "x", null, [$"w{i * 5}", $"w{i * 5 + 1}", $"w{i * 5 + 2}", $"w{i * 5 + 3}", $"w{i * 5 + 4}"]));
await expenses.AddAsync("W", new(D(2026, 1, 1), 1, "식비", "x", null, ["w95", "w96", "w97", "w98"]));   // 99개
var wTags = await db.Tags.CountAsync(t => t.OwnerId == "W"); var wExpenses = await db.Expenses.CountAsync(e => e.OwnerId == "W");
Check(wTags == 99, "Setup wrong: " + wTags);
await Reject(() => backup.RestoreAsync("W", parsedS.File!, BackupRestoreMode.Merge, dryRun: false), "merge over the tag cap");
Check(await db.Tags.CountAsync(t => t.OwnerId == "W") == wTags && await db.Expenses.CountAsync(e => e.OwnerId == "W") == wExpenses, "Rejected merge left data behind");

// ── 이전 버전(1) 백업 호환과 손상 검출 ──
JsonNode ToV1(byte[] bytes)
{
    var node = JsonNode.Parse(bytes)!;
    node["version"] = 1;
    node["counts"]!.AsObject().Remove("tags");
    node["data"]!.AsObject().Remove("tags");
    foreach (var expense in node["data"]!["expenses"]!.AsArray()) expense!.AsObject().Remove("tags");
    return node;
}
var legacyFile = BackupSerializer.Parse(System.Text.Encoding.UTF8.GetBytes(ToV1(bytesS).ToJsonString()));
Check(legacyFile.File is { Version: 1 } && legacyFile.Issues.Count == 0, "Version 1 file must be accepted: " + string.Join(" ", legacyFile.Issues));
var legacyReport = await backup.RestoreAsync("X", legacyFile.File!, BackupRestoreMode.Replace, dryRun: false);
Check(await db.Tags.CountAsync(t => t.OwnerId == "X") == 0 && await db.Expenses.CountAsync(e => e.OwnerId == "X") == 4 && !legacyReport.Sections.Any(x => x.Name == "태그"), "Version 1 restore wrong");
var v1WithTags = ToV1(bytesS); v1WithTags["data"]!["tags"] = new JsonArray("a"); v1WithTags["counts"]!["tags"] = 1;
Check(BackupSerializer.Parse(System.Text.Encoding.UTF8.GetBytes(v1WithTags.ToJsonString())).File is null, "Version 1 file with tags accepted");
var v3 = JsonNode.Parse(bytesS)!; v3["version"] = 3;
Check(BackupSerializer.Parse(System.Text.Encoding.UTF8.GetBytes(v3.ToJsonString())) is { File: null, Issues.Count: 1 }, "Future version accepted");
JsonNode Mutate(Action<JsonNode> change) { var node = JsonNode.Parse(bytesS)!; change(node); return node; }
foreach (var (label, node) in new (string, JsonNode)[]
{
    ("expense tag not in the list", Mutate(n => n["data"]!["expenses"]![0]!["tags"] = new JsonArray("없는태그"))),
    ("duplicate expense tag", Mutate(n => n["data"]!["expenses"]![0]!["tags"] = new JsonArray("여행", "여행"))),
    ("case duplicate expense tag", Mutate(n => n["data"]!["expenses"]![0]!["tags"] = new JsonArray("Trip", "TRIP"))),
    ("non canonical tag", Mutate(n => { n["data"]!["tags"]!.AsArray()[0] = " 공백 "; n["data"]!["expenses"]![0]!["tags"] = new JsonArray(); })),
    ("six tags on an expense", Mutate(n => { n["data"]!["tags"] = new JsonArray("a", "b", "c", "d", "e", "f"); n["counts"]!["tags"] = 6; n["data"]!["expenses"]![0]!["tags"] = new JsonArray("a", "b", "c", "d", "e", "f"); })),
    ("duplicate tag in list", Mutate(n => { n["data"]!["tags"] = new JsonArray("a", "A"); n["counts"]!["tags"] = 2; })),
    ("tag with comma", Mutate(n => { n["data"]!["tags"]!.AsArray()[0] = "a,b"; })),
    ("count mismatch", Mutate(n => n["counts"]!["tags"] = 99)),
    ("null tag", Mutate(n => n["data"]!["tags"]!.AsArray()[0] = null)),
})
    Check(BackupSerializer.Parse(System.Text.Encoding.UTF8.GetBytes(node.ToJsonString())).File is null, $"Invalid backup accepted: {label}");
var over100 = Mutate(n => { var names = Enumerable.Range(0, 101).Select(i => $"n{i}").ToArray(); n["data"]!["tags"] = new JsonArray(names.Select(x => (JsonNode?)x).ToArray()); n["counts"]!["tags"] = 101; n["data"]!["expenses"]!.AsArray().ToList().ForEach(e => e!["tags"] = new JsonArray()); });
Check(BackupSerializer.Parse(System.Text.Encoding.UTF8.GetBytes(over100.ToJsonString())).File is null, "101 tags accepted");

// ── CSV 내보내기·가져오기 ──
var csvExpenses = await expenses.ListAsync("S", new());
var csvText = System.Text.Encoding.UTF8.GetString(ExpenseCsvExporter.Create(csvExpenses)).TrimStart('\uFEFF');
Check(csvText.StartsWith("날짜,금액(원),카테고리,메모,결제수단,결제유형,태그\r\n") && csvText.Contains("\"Trip;여행\"") && csvText.Contains("\"\t-하이픈;\t=수식\"") == false, "CSV export wrong: " + csvText.Split("\r\n")[1]);
var csvParsed = ExpenseCsvImporter.Parse(new StringReader(csvText));
Check(csvParsed.Issues.Count == 0 && csvParsed.Rows.Count == 4, "Own CSV export must import: " + string.Join(" ", csvParsed.Issues.Select(i => i.Message)));
Check(csvParsed.Rows.Select(r => string.Join("|", r.Tags!)).OrderBy(t => t, StringComparer.Ordinal).SequenceEqual(csvExpenses.Select(e => string.Join("|", e.TagNames)).OrderBy(t => t, StringComparer.Ordinal)), "CSV tags not round-tripped (formula protection?)");
var csvImport = new ExpenseCsvImportService(factory);
await new CategoryService(factory).ListAsync("Y");
Check(await csvImport.ImportAsync("Y", csvParsed.Rows, includeDuplicates: true) == 4, "CSV import count wrong");
var yExpenses = await expenses.ListAsync("Y", new());
Check(yExpenses.Count == 4 && yExpenses.Count(e => e.TagNames.SequenceEqual(new[] { "Trip", "여행" })) == 2 && await db.Tags.CountAsync(t => t.OwnerId == "Y") == 4, "CSV import tags wrong: " + await db.Tags.CountAsync(t => t.OwnerId == "Y"));
Check(await csvImport.ImportAsync("Y", csvParsed.Rows, includeDuplicates: false) == 0 && await db.Tags.CountAsync(t => t.OwnerId == "Y") == 4, "Duplicate CSV import must add nothing");
// 옛 형식(4열·6열)은 그대로 가져올 수 있음
Check(ExpenseCsvImporter.Parse(new StringReader("날짜,금액(원),카테고리,메모\r\n2026-09-01,100,식비,옛 파일\r\n")) is { Issues.Count: 0, Rows.Count: 1 } four && four.Rows[0].Tags!.Count == 0, "4-column CSV broken");
Check(ExpenseCsvImporter.Parse(new StringReader("날짜,금액(원),카테고리,메모,결제수단,결제유형\r\n2026-09-01,100,식비,옛 파일,,\r\n")) is { Issues.Count: 0, Rows.Count: 1 }, "6-column CSV broken");
foreach (var badTags in new[] { "a;b;c;d;e;f", "ok;bad,tag", new string('x', 21), "a;;" + new string('y', 25) })
{
    var parsedBad = ExpenseCsvImporter.Parse(new StringReader($"날짜,금액(원),카테고리,메모,결제수단,결제유형,태그\r\n2026-09-01,100,식비,m,,,\"{badTags}\"\r\n"));
    Check(parsedBad.Rows.Count == 0 && parsedBad.Issues.Count == 1, $"Bad CSV tags accepted: {badTags}");
}
Check(ExpenseCsvImporter.Parse(new StringReader("날짜,금액(원),카테고리,메모,결제수단,결제유형,태그\r\n2026-09-01,100,식비,m,,,\" a ; A ;; #b \"\r\n")).Rows.Single().Tags!.SequenceEqual(new[] { "a", "b" }), "CSV tag cleanup wrong");
// 가져오기로 태그 제한을 넘으면 거절하고 아무것도 만들지 않음
var wCsv = ExpenseCsvImporter.Parse(new StringReader("날짜,금액(원),카테고리,메모,결제수단,결제유형,태그\r\n2026-09-01,100,식비,m,,,\"n1;n2\"\r\n")).Rows;
await new CategoryService(factory).ListAsync("W");
await Reject(() => csvImport.ImportAsync("W", wCsv, includeDuplicates: true), "CSV import over the tag cap");
Check(await db.Tags.CountAsync(t => t.OwnerId == "W") == wTags && await db.Expenses.CountAsync(e => e.OwnerId == "W") == wExpenses, "Rejected CSV import left data behind");

// ── 연간 통계의 태그별 지출 ──
var report = YearlyStatistics.Calculate(csvExpenses.Select(e => e).ToList(), [], new Dictionary<int, PaymentMethod>(), 2026, new DateOnly(2026, 10, 6));
Check(report.Tags!.Select(t => t.Name).SequenceEqual(new[] { "Trip", "여행", "=수식", "-하이픈" }) || report.Tags!.Count == 4, "Year tag rows: " + string.Join(",", report.Tags!.Select(t => t.Name)));
Check(report.Tags!.Single(t => t.Name == "Trip") is { Amount: 24_000, Count: 2 } && report.Tags!.Single(t => t.Name == "여행").Amount == 24_000, "Overlapping tag totals wrong");
Check(report.UntaggedAmount == 500 && report.UntaggedCount == 1, "Untagged wrong");
Check(report.Tags!.Sum(t => t.Amount) > report.Current.Expense, "Tag totals should exceed the expense total when tags overlap");
Check(Math.Abs(report.Tags!.Single(t => t.Name == "Trip").Percentage - 24_000.0 / report.Current.Expense * 100) < 1e-9, "Tag share wrong");
Check(YearlyStatistics.Calculate([], [], new Dictionary<int, PaymentMethod>(), 2026, new DateOnly(2026, 10, 6)).Tags!.Count == 0, "Empty tag rows wrong");
var yearly = await new YearlyStatisticsService(factory).LoadAsync("S", 2026, new DateOnly(2026, 10, 6));
Check(yearly.Tags!.Count == 4 && yearly.Tags.Single(t => t.Name == "Trip").Amount == 24_000, "Yearly service must include tags");
Check(yearly.Tags.All(t => t.TagId > 0), "Tag ids missing in yearly rows");

// ── 달력 서비스가 태그를 함께 읽음 ──
var calendarData = await new CalendarService(factory).LoadAsync("S", new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 6));
Check(calendarData.Expenses.First().TagNames.SequenceEqual(new[] { "Trip", "여행" }), "Calendar expenses must carry tags");

// ── 지출 화면의 태그 동작 ──
const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
var todayD = KoreanClock.Today;
await using (var seed = new ExpensesDbContext(options))
{
    seed.UserProfiles.Add(new UserProfile { OwnerId = "H", HasCompletedOnboarding = true });
    await seed.SaveChangesAsync();
}
var auth = new TestAuth("H");
var home = TestComponents.CreateHome(factory, auth);
void Set(string name, object? value) => typeof(Home).GetField(name, flags)!.SetValue(home, value);
T Get<T>(string name) => (T)typeof(Home).GetField(name, flags)!.GetValue(home)!;
Task Call(string method, params object[] args) => (Task)typeof(Home).GetMethod(method, flags)!.Invoke(home, args)!;
object? CallSync(string method, params object[] args) => typeof(Home).GetMethod(method, flags)!.Invoke(home, args);
await Call("LoadCategoriesAsync"); await Call("LoadExpensesAsync"); await Call("LoadTagsAsync");

// 추가: 입력한 태그가 저장되고 다음 입력을 위해 남음(금액·메모만 비움)
Set("expenseDate", todayD); Set("amount", 9_000L); Set("category", "식비"); Set("memo", "첫 지출"); Set("tagsText", "여행, #제주 ,여행");
await Call("AddExpenseAsync");
Check(Get<string?>("errorMessage") is null && Get<string>("tagsText") == "여행, #제주 ,여행" && Get<long>("amount") == 0, "Add with tags / keep tags wrong");
var saved = (await expenses.ListAsync("H", new())).Single();
Check(saved.TagNames.SequenceEqual(new[] { "여행", "제주" }), "Home saved wrong tags: " + string.Join(",", saved.TagNames));
Check(Get<List<TagUsage>>("tagUsage").Count == 2, "Tag list not refreshed after add");
// 잘못된 태그: 저장하지 않고 안내
Set("amount", 100L); Set("memo", "나쁜 태그"); Set("tagsText", "a,b,c,d,e,f");
await Call("AddExpenseAsync");
Check(Get<string?>("errorMessage") is { Length: > 0 } && (await expenses.ListAsync("H", new())).Count == 1, "Invalid tags were saved");
Set("tagsText", "bad;tag");
await Call("AddExpenseAsync");
Check(Get<string?>("errorMessage") is { Length: > 0 } && (await expenses.ListAsync("H", new())).Count == 1, "Semicolon tag was saved");
// 추천 태그 누르기: 넣고 빼기, 5개 제한
Set("tagsText", ""); Set("errorMessage", null);
CallSync("ToggleTag", "여행"); CallSync("ToggleTag", "제주");
Check(Get<string>("tagsText") == "여행, 제주", "Toggle add wrong: " + Get<string>("tagsText"));
CallSync("ToggleTag", "여행");
Check(Get<string>("tagsText") == "제주", "Toggle remove wrong: " + Get<string>("tagsText"));
CallSync("ToggleTag", "제주".ToUpperInvariant());
Check(Get<string>("tagsText") == "", "Toggle must be case-insensitive");
Set("tagsText", "a, b, c, d, e");
CallSync("ToggleTag", "f");
Check(Get<string>("tagsText") == "a, b, c, d, e" && Get<string?>("errorMessage") is { Length: > 0 }, "Toggle over the limit wrong");
Set("tagsText", "bad;tag"); Set("errorMessage", null);
CallSync("ToggleTag", "x");
Check(Get<string?>("errorMessage") is { Length: > 0 } && Get<string>("tagsText") == "bad;tag", "Toggle with an invalid text must not change it");
Set("errorMessage", null);
// 태그로 보기
var travelTagId = (await db.Tags.SingleAsync(t => t.OwnerId == "H" && t.Name == "여행")).Id;
Set("tagsText", ""); Set("memo", ""); Set("amount", 4_000L); Set("category", "교통");
await Call("AddExpenseAsync");   // 태그 없는 지출
Check((await expenses.ListAsync("H", new())).Count == 2, "Untagged expense not added");
await Call("ShowTagAsync", travelTagId);
Check(Get<ExpenseFilter>("activeFilter").TagId == travelTagId && Get<List<ExpenseRecord>>("expenses").Count == 1 && Get<List<ExpenseRecord>>("expenses")[0].Memo == "첫 지출", "Show tag filter wrong");
Check(typeof(Home).GetProperty("FilterDescription", flags)!.GetValue(home) is string description && description.Contains("태그: 여행"), "Filter description lacks the tag");
Check(typeof(Home).GetProperty("FilteredExportUrl", flags)!.GetValue(home) is string url && url.Contains($"&tag={travelTagId}"), "Export URL lacks the tag");
Check(typeof(Home).GetProperty("HasDetailedFilter", flags)!.GetValue(home) is true, "A tag filter must open the filter box");
await Call("ResetFiltersAsync");
Check(Get<ExpenseFilter>("activeFilter").TagId is null && Get<List<ExpenseRecord>>("expenses").Count == 2, "Reset must clear the tag filter");
// 수정: 태그 바꾸기·지우기
var target = Get<List<ExpenseRecord>>("expenses").Single(e => e.Memo == "첫 지출");
CallSync("StartEdit", target);
Check(Get<string>("editTagsText") == "여행, 제주", "Edit form must show the tags");
Set("editTagsText", "제주, 새태그");
await Call("SaveEditAsync");
Check((await expenses.ListAsync("H", new())).Single(e => e.Memo == "첫 지출").TagNames.SequenceEqual(new[] { "새태그", "제주" }), "Edit tags not saved");
CallSync("StartEdit", Get<List<ExpenseRecord>>("expenses").Single(e => e.Memo == "첫 지출"));
Set("editTagsText", "");
await Call("SaveEditAsync");
Check((await expenses.ListAsync("H", new())).Single(e => e.Memo == "첫 지출").TagNames.Count == 0, "Clearing tags in the edit form failed");
CallSync("StartEdit", Get<List<ExpenseRecord>>("expenses").Single(e => e.Memo == "첫 지출"));
Set("editTagsText", "1,2,3,4,5,6");
await Call("SaveEditAsync");
Check(Get<string?>("editError") is { Length: > 0 } && (await expenses.ListAsync("H", new())).Single(e => e.Memo == "첫 지출").TagNames.Count == 0, "Invalid edit tags saved");
Set("editError", null); CallSync("CancelEdit");
// 삭제 되돌리기는 태그도 되돌림
await Call("LoadExpensesAsync");
await expenses.UpdateAsync("H", saved.Id, new(saved.Date, 9_000, "식비", "첫 지출", null, ["여행", "제주"]));
await Call("LoadExpensesAsync");
await Call("DeleteExpenseAsync", saved.Id);
Check(Get<ExpenseInput?>("lastDeleted") is { Tags.Count: 2 }, "Undo must remember the tags");
await Call("UndoDeleteAsync");
Check((await expenses.ListAsync("H", new())).Single(e => e.Memo == "첫 지출").TagNames.SequenceEqual(new[] { "여행", "제주" }), "Undo did not restore the tags");
// 쿼리의 tag 값
foreach (var (raw, expectFilter) in new (string?, bool)[] { (travelTagId.ToString(), true), ("abc", false), ("0", false), (null, false), ("-3", false) })
{
    var queryHome = TestComponents.CreateHome(factory, new TestAuth("H"));
    typeof(Home).GetProperty("TagParameter")!.SetValue(queryHome, raw);
    typeof(Home).GetProperty("UserDataProvisioner", flags)!.SetValue(queryHome, new UserDataProvisioner(factory));
    typeof(Home).GetProperty("RecurringExpenseService", flags)!.SetValue(queryHome, new RecurringExpenseService(factory));
    await (Task)typeof(Home).GetMethod("OnInitializedAsync", flags)!.Invoke(queryHome, null)!;
    var filter = (ExpenseFilter)typeof(Home).GetField("activeFilter", flags)!.GetValue(queryHome)!;
    Check((filter.TagId == travelTagId) == expectFilter && (expectFilter || filter.TagId is null), $"tag query '{raw}' wrong");
}
// 계정이 바뀌면 태그 목록을 읽지 않음
auth.OwnerId = "OTHER";
var staleBefore = Get<List<TagUsage>>("tagUsage").Count;
await Call("LoadTagsAsync");
Check(Get<List<TagUsage>>("tagUsage").Count == staleBefore, "Tag list loaded for another account");
auth.OwnerId = "H";

// ── 태그 관리 화면 ──
var tagAuth = new TestAuth("H");
var page = new Tags();
void Inject(string name, object value) => typeof(Tags).GetProperty(name, flags)!.SetValue(page, value);
Inject("AuthenticationStateProvider", tagAuth); Inject("TagService", tags); Inject("Logger", NullLogger<Tags>.Instance);
T PageGet<T>(string name) => (T)typeof(Tags).GetField(name, flags)!.GetValue(page)!;
void PageSet(string name, object? value) => typeof(Tags).GetField(name, flags)!.SetValue(page, value);
Task PageCall(string method, params object[] args) => (Task)typeof(Tags).GetMethod(method, flags)!.Invoke(page, args)!;
await PageCall("OnInitializedAsync");
var pageTags = PageGet<List<TagUsage>>("tags");
Check(pageTags.Select(t => t.Name).SequenceEqual(new[] { "새태그", "여행", "제주" }.OrderBy(n => n, StringComparer.Ordinal)) && !PageGet<bool>("isLoading"), "Tag page list wrong: " + string.Join(",", pageTags.Select(t => t.Name)));
var jeju = pageTags.Single(t => t.Name == "제주");
typeof(Tags).GetMethod("StartRename", flags)!.Invoke(page, [jeju]);
PageSet("editName", "여행");
await PageCall("SaveRenameAsync", false);
Check(PageGet<string?>("conflictTarget") == "여행" && PageGet<string?>("conflictSource") == "제주" && await db.Tags.AnyAsync(t => t.Id == jeju.Id), "Rename conflict must be offered, not applied");
await PageCall("SaveRenameAsync", true);
Check(!await db.Tags.AnyAsync(t => t.Id == jeju.Id) && PageGet<string?>("notice") is { Length: > 0 } && PageGet<string?>("conflictTarget") is null && PageGet<int?>("editingId") is null, "Merge from the page failed");
Check(!PageGet<List<TagUsage>>("tags").Any(t => t.Name == "제주"), "Page list not refreshed after merge");
var fresh = PageGet<List<TagUsage>>("tags").Single(t => t.Name == "새태그");
typeof(Tags).GetMethod("StartRename", flags)!.Invoke(page, [fresh]);
PageSet("editName", "신규이름");
await PageCall("SaveRenameAsync", false);
Check(PageGet<List<TagUsage>>("tags").Any(t => t.Name == "신규이름") && PageGet<string?>("errorMessage") is null, "Plain rename from the page failed");
typeof(Tags).GetMethod("StartRename", flags)!.Invoke(page, [PageGet<List<TagUsage>>("tags").Single(t => t.Name == "신규이름")]);
PageSet("editName", "a,b");
await PageCall("SaveRenameAsync", false);
Check(PageGet<string?>("errorMessage") is { Length: > 0 }, "Invalid rename accepted");
typeof(Tags).GetMethod("CancelRename", flags)!.Invoke(page, null);
var delTag = PageGet<List<TagUsage>>("tags").Single(t => t.Name == "신규이름");
await PageCall("DeleteAsync", delTag);
Check(await db.Tags.AnyAsync(t => t.Id == delTag.Id), "Delete without confirmation step must do nothing");
typeof(Tags).GetMethod("AskDelete", flags)!.Invoke(page, [delTag]);
await PageCall("DeleteAsync", delTag);
Check(!await db.Tags.AnyAsync(t => t.Id == delTag.Id) && PageGet<int?>("deletingId") is null, "Confirmed delete failed");
// 다른 곳(다른 탭)에서 이미 지운 태그를 다시 지우거나 이름을 바꾸려는 경우
await expenses.AddAsync("H", new(todayD, 1, "식비", "임시", null, ["사라질태그", "또사라질태그"]));
await PageCall("ReloadAsync");
var ghost = PageGet<List<TagUsage>>("tags").Single(t => t.Name == "사라질태그");
typeof(Tags).GetMethod("AskDelete", flags)!.Invoke(page, [ghost]);
await tags.DeleteAsync("H", ghost.Id);
PageSet("notice", null);
await PageCall("DeleteAsync", ghost);
Check(PageGet<string?>("notice") is { } ghostNotice && ghostNotice.Contains("이미 지워진") && !ghostNotice.Contains("지웠습니다"), "Already-deleted tag must be reported as such: " + PageGet<string?>("notice"));
var ghost2 = PageGet<List<TagUsage>>("tags").Single(t => t.Name == "또사라질태그");
typeof(Tags).GetMethod("StartRename", flags)!.Invoke(page, [ghost2]);
PageSet("editName", "다른이름");
await tags.DeleteAsync("H", ghost2.Id);
await PageCall("SaveRenameAsync", false);
Check(PageGet<string?>("errorMessage") is { } ghostError && ghostError.Contains("찾을 수 없어") && !PageGet<List<TagUsage>>("tags").Any(t => t.Name == "또사라질태그"), "Renaming a deleted tag must report it and refresh");
tagAuth.OwnerId = "OTHER";
typeof(Tags).GetMethod("AskDelete", flags)!.Invoke(page, [PageGet<List<TagUsage>>("tags")[0]]);
var remaining = await db.Tags.CountAsync(t => t.OwnerId == "H");
await PageCall("DeleteAsync", PageGet<List<TagUsage>>("tags")[0]);
Check(await db.Tags.CountAsync(t => t.OwnerId == "H") == remaining, "Tag page changed data for another account");

// ── 컴포넌트 렌더링(값이 실제로 전달되는지) ──
var renderServices = new ServiceCollection().AddLogging().BuildServiceProvider();
await using var renderer = new HtmlRenderer(renderServices, NullLoggerFactory.Instance);
async Task<string> Render<T>(Dictionary<string, object?> parameters) where T : IComponent => await renderer.Dispatcher.InvokeAsync(async () =>
    System.Net.WebUtility.HtmlDecode((await renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters))).ToHtmlString()));
var formHtml = await Render<MyExpenses.Components.Expenses.ExpenseForm>(new()
{
    ["TagsText"] = "여행, 제주", ["TagChoices"] = new List<TagUsage> { new(1, "여행", 3, 1000), new(2, "경조사", 1, 500) },
    ["Categories"] = new[] { "식비" }, ["Category"] = "식비"
});
Check(formHtml.Contains("id=\"expense-tags\"") && formHtml.Contains("value=\"여행, 제주\"") && formHtml.Contains("#여행") && formHtml.Contains("#경조사"), "Tag input/choices not rendered");
Check(System.Text.RegularExpressions.Regex.IsMatch(formHtml, "tag-choice selected[^>]*aria-pressed=\"true\"[^>]*>#여행") && !System.Text.RegularExpressions.Regex.IsMatch(formHtml, "tag-choice selected[^>]*>#경조사"), "Selected tag choice wrong");
var listHtml = await Render<MyExpenses.Components.Expenses.ExpenseList>(new()
{
    ["Expenses"] = new List<ExpenseRecord>
    {
        new() { Id = 1, OwnerId = "H", Date = todayD, Amount = 1_000, Category = "식비", Memo = "가", TagLinks = [new() { TagId = 7, Tag = new Tag { Id = 7, Name = "제주" } }, new() { TagId = 3, Tag = new Tag { Id = 3, Name = "여행" } }] },
        new() { Id = 2, OwnerId = "H", Date = todayD, Amount = 2_000, Category = "식비", Memo = "나" }
    },
    ["EditCategories"] = new[] { "식비" }, ["EditingId"] = 1, ["EditTagsText"] = "제주, 여행"
});
Check(System.Text.RegularExpressions.Regex.Matches(listHtml, "class=\"tag-chip\"").Count == 2 && listHtml.IndexOf("#여행") < listHtml.IndexOf("#제주") && listHtml.Contains("id=\"edit-tags\"") && listHtml.Contains("value=\"제주, 여행\""), "List chips / edit tags not rendered");
var filterHtml = await Render<MyExpenses.Components.Expenses.ExpenseFilterForm>(new()
{
    ["Input"] = new ExpenseSearchInput { Tag = "7" }, ["Tags"] = new List<TagUsage> { new(7, "제주", 2, 100), new(3, "여행", 1, 50) }
});
Check(System.Text.RegularExpressions.Regex.IsMatch(filterHtml, "id=\"filter-tag\"") && filterHtml.Contains("#제주 (2)") && System.Text.RegularExpressions.Regex.IsMatch(filterHtml, "<option[^>]*value=\"7\"[^>]*selected"), "Tag filter select wrong");

// 화면 조립 가드: 문자열 매개변수에 변수 이름을 @ 없이 넘기면 변수가 아니라 그 글자가 전달됩니다.
var repo = new DirectoryInfo(AppContext.BaseDirectory);
while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "MyExpenses.csproj"))) repo = repo.Parent;
var componentTypes = typeof(Home).Assembly.GetTypes().Where(t => typeof(IComponent).IsAssignableFrom(t)).ToDictionary(t => t.Name);
var homeMarkup = File.ReadAllText(Path.Combine(repo!.FullName, "Components/Pages/Expenses/Home.razor"));
foreach (System.Text.RegularExpressions.Match tag in System.Text.RegularExpressions.Regex.Matches(homeMarkup, @"<([A-Z]\w+)\s([^>]*)>"))
{
    if (!componentTypes.TryGetValue(tag.Groups[1].Value, out var type)) continue;
    foreach (System.Text.RegularExpressions.Match attribute in System.Text.RegularExpressions.Regex.Matches(tag.Groups[2].Value, "(?<![-\\w@])([A-Z]\\w*)=\"([^\"]*)\""))
    {
        var property = type.GetProperty(attribute.Groups[1].Value);
        if (property?.PropertyType == typeof(string))
            Check(attribute.Groups[2].Value.StartsWith('@') || !System.Text.RegularExpressions.Regex.IsMatch(attribute.Groups[2].Value, "^[a-z][A-Za-z0-9]*$"), $"{type.Name}.{property.Name}=\"{attribute.Groups[2].Value}\" is passed as literal text");
    }
}

Console.WriteLine("PASS: tag names, create/reuse, validation, update semantics, filter, usage, rename/merge, deletion cleanup, limits, owner isolation, backup v2/v1, CSV, yearly tags, expense page, tag page and rendering");

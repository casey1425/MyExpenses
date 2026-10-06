using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MyExpenses.Components.Pages;
using MyExpenses.Data;
using MyExpenses.Services;
using MyExpenses.Testing;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static async Task Reject(Func<Task> action)
{
    try { await action(); throw new Exception("Invalid input accepted"); }
    catch (ArgumentException) { }
}
static DateTime D(int y, int m, int d) => new(y, m, d);
static SavingsDeposit Dep(DateTime date, long amount) => new() { Date = date, Amount = amount };

// ---- 계산 로직(DB 없이) ----
var today = D(2026, 10, 15);
var goal = new SavingsGoal { TargetAmount = 3_000_000, TargetDate = D(2026, 12, 31) };
var p = SavingsGoalCalculator.Calculate(goal, [Dep(D(2026, 8, 10), 400_000), Dep(D(2026, 10, 1), 800_000)], today);
Check(p is { Saved: 1_200_000, Remaining: 1_800_000, Percent: 40.0, IsAchieved: false, IsOverdue: false, DaysLeft: 77 }, "Basic progress wrong");
Check(p is { RemainingMonths: 3, RequiredPerMonth: 600_000 }, "Required per month wrong");
Check(p is { AverageMonthlyDeposit: 400_000, MonthsToGo: 5 } && p.ProjectedMonth == new DateOnly(2027, 3, 1) && p.ProjectedAfterTarget, "Projection wrong");

var achieved = SavingsGoalCalculator.Calculate(goal, [Dep(D(2026, 9, 1), 3_000_000), Dep(D(2026, 10, 1), 1)], today);
Check(achieved is { IsAchieved: true, Percent: 100.0, Remaining: 0, IsOverdue: false, RequiredPerMonth: null, ProjectedMonth: null, DaysLeft: null }, "Achieved goal wrong");
var exact = SavingsGoalCalculator.Calculate(goal, [Dep(D(2026, 9, 1), 3_000_000)], today);
Check(exact.IsAchieved && exact.Remaining == 0, "Exact target should be achieved");

// 달성 전에는 100%로 보이면 안 됩니다(내림).
Check(SavingsGoalCalculator.Calculate(new SavingsGoal { TargetAmount = 1_000_000 }, [Dep(today, 999_999)], today) is { Percent: 99.9, IsAchieved: false }, "Percent must floor");
Check(SavingsGoalCalculator.Calculate(new SavingsGoal { TargetAmount = 3 }, [Dep(today, 1)], today).Percent == 33.3, "Percent floor wrong");
Check(SavingsGoalCalculator.Calculate(new SavingsGoal { TargetAmount = 1000 }, [], today) is { Percent: 0.0, Saved: 0, AverageMonthlyDeposit: 0, MonthsToGo: null, ProjectedMonth: null }, "Empty goal wrong");

var overdue = SavingsGoalCalculator.Calculate(new SavingsGoal { TargetAmount = 1_000_000, TargetDate = D(2026, 9, 30) }, [Dep(D(2026, 9, 1), 100_000)], today);
Check(overdue is { IsOverdue: true, RemainingMonths: 0, RequiredPerMonth: 900_000, DaysLeft: -15 }, "Overdue goal wrong");
var sameMonth = SavingsGoalCalculator.Calculate(new SavingsGoal { TargetAmount = 1_000_000, TargetDate = D(2026, 10, 31) }, [], today);
Check(sameMonth is { IsOverdue: false, RemainingMonths: 1, RequiredPerMonth: 1_000_000, DaysLeft: 16 }, "Same-month target wrong");
Check(SavingsGoalCalculator.Calculate(new SavingsGoal { TargetAmount = 1_000_000, TargetDate = today }, [], today) is { IsOverdue: false, DaysLeft: 0 }, "Target today is not overdue");
Check(SavingsGoalCalculator.Calculate(new SavingsGoal { TargetAmount = 1_000_000, TargetDate = D(2026, 12, 31) }, [], today).RequiredPerMonth == 333_334, "Required per month must round up");
Check(SavingsGoalCalculator.Calculate(new SavingsGoal { TargetAmount = 1_000_000 }, [], today) is { RequiredPerMonth: null, DaysLeft: null, RemainingMonths: null }, "Goal without date wrong");
Check(SavingsGoalCalculator.Calculate(new SavingsGoal { TargetAmount = 1_000_000_000_000 }, [Dep(today, 1)], today).ProjectedMonth is null, "Absurd projection should be skipped");
var onPace = SavingsGoalCalculator.Calculate(new SavingsGoal { TargetAmount = 1_000_000, TargetDate = D(2027, 12, 31) }, [Dep(D(2026, 10, 1), 500_000)], today);
Check(onPace is { MonthsToGo: 1, ProjectedAfterTarget: false } && onPace.ProjectedMonth == new DateOnly(2026, 11, 1), "On-pace projection wrong");
Check(SavingsGoalCalculator.Calculate(new SavingsGoal { TargetAmount = 1000 }, [Dep(D(2026, 12, 1), 100)], today).MonthsToGo == 9, "Future-dated deposit must not break span");

// ---- 서비스: 신규 DB와 기존 DB 업그레이드 ----
foreach (var legacy in new[] { false, true })
{
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    var options = new DbContextOptionsBuilder<ExpensesDbContext>().UseSqlite(connection).Options;
    await using var db = new ExpensesDbContext(options);
    if (legacy)
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE Expenses (Id INTEGER PRIMARY KEY AUTOINCREMENT, OwnerId TEXT NOT NULL, Date TEXT NOT NULL, Amount INTEGER NOT NULL, Category TEXT NOT NULL, Memo TEXT NOT NULL);");
    else
        await db.Database.EnsureCreatedAsync();
    // 오래된 DB는 앱과 같은 방식(옛 스키마 보강 → 기준선 → 이후 마이그레이션)으로 올립니다.
    if (legacy) { await DatabaseMigrator.MigrateExpensesAsync(db); await DatabaseMigrator.MigrateExpensesAsync(db); }
    else { await ExpensesSchema.EnsureCreatedAsync(db); await ExpensesSchema.EnsureCreatedAsync(db); }

    var factory = new TestFactory(options);
    var service = new SavingsGoalService(factory);

    var trip = await service.CreateAsync("A", today, "  여행 자금  ", 3_000_000, D(2026, 12, 31));
    var other = await service.CreateAsync("B", today, "여행 자금", 500_000, null);
    Check(trip.Name == "여행 자금" && trip.CreatedDate == today && trip.TargetDate == D(2026, 12, 31), "Goal normalization failed");
    await Reject(() => service.CreateAsync("A", today, "여행 자금", 1, null));            // 같은 사용자 중복 이름
    await Reject(() => service.CreateAsync("A", today, " ", 1000, null));
    await Reject(() => service.CreateAsync("A", today, new string('a', 51), 1000, null));
    await Reject(() => service.CreateAsync("A", today, "x", 0, null));
    await Reject(() => service.CreateAsync("A", today, "x", -1, null));
    await Reject(() => service.CreateAsync("A", today, "x", SavingsGoalService.MaxAmount + 1, null));
    await Reject(() => service.CreateAsync("A", today, "x", 1000, D(2026, 10, 14)));        // 과거 목표일
    await Reject(() => service.CreateAsync("A", today, "x", 1000, D(2101, 1, 1)));
    await Reject(() => service.CreateAsync("A", today, "x", 1000, D(1999, 12, 31)));
    await Reject(() => service.CreateAsync("", today, "x", 1000, null));
    Check((await service.CreateAsync("A", today, "오늘이 목표일", 1000, today)).TargetDate == today, "Target today should be allowed");

    // 목표 개수 제한
    for (var i = 0; i < SavingsGoalService.MaxGoals - 2; i++) await service.CreateAsync("A", today, $"목표{i}", 1000, null);
    Check((await service.ListAsync("A", today)).Count == SavingsGoalService.MaxGoals, "Goal count wrong");
    await Reject(() => service.CreateAsync("A", today, "초과", 1000, null));
    Check((await service.CreateAsync("B", today, "다른 사용자는 별개", 1000, null)).Id > 0, "Limit leaked across owners");

    // 저축하기
    var first = await service.AddDepositAsync("A", trip.Id, today, D(2026, 8, 10), 400_000, " 첫 저축 ");
    await service.AddDepositAsync("A", trip.Id, today, D(2026, 10, 1), 800_000, "");
    await service.AddDepositAsync("A", trip.Id, today, today, 1, "오늘");
    Check(first.Memo == "첫 저축" && first.Date == D(2026, 8, 10), "Deposit normalization failed");
    await Reject(() => service.AddDepositAsync("A", trip.Id, today, D(2026, 10, 16), 1000, ""));   // 미래 날짜
    await Reject(() => service.AddDepositAsync("A", trip.Id, today, D(1999, 1, 1), 1000, ""));
    await Reject(() => service.AddDepositAsync("A", trip.Id, today, default, 1000, ""));
    await Reject(() => service.AddDepositAsync("A", trip.Id, today, today, 0, ""));
    await Reject(() => service.AddDepositAsync("A", trip.Id, today, today, -100, ""));
    await Reject(() => service.AddDepositAsync("A", trip.Id, today, today, SavingsGoalService.MaxAmount + 1, ""));
    await Reject(() => service.AddDepositAsync("A", trip.Id, today, today, 1000, new string('a', 101)));
    await Reject(() => service.AddDepositAsync("B", trip.Id, today, today, 1000, ""));              // 타 계정 목표
    await Reject(() => service.AddDepositAsync("A", 999_999, today, today, 1000, ""));              // 없는 목표
    await Reject(() => service.AddDepositAsync("", trip.Id, today, today, 1000, ""));
    var summary = (await service.ListAsync("A", today)).Single(item => item.Goal.Id == trip.Id);
    Check(summary.Progress.Saved == 1_200_001 && summary.Progress.RequiredPerMonth == 600_000, $"Summary progress wrong: {summary.Progress.Saved}/{summary.Progress.RequiredPerMonth}");
    Check((await service.ListAsync("B", today)).Single(item => item.Goal.Id == other.Id).Progress.Saved == 0, "Deposit leaked to B");
    Check((await service.ListDepositsAsync("A", trip.Id)).Select(d => d.Date).SequenceEqual(new[] { today, D(2026, 10, 1), D(2026, 8, 10) }), "Deposit ordering wrong");
    Check((await service.ListDepositsAsync("B", trip.Id)).Count == 0, "Foreign deposits visible");

    // 수정
    Check(!await service.UpdateAsync("B", trip.Id, today, "탈취", 1, null), "Foreign update accepted");
    await Reject(() => service.UpdateAsync("A", trip.Id, today, "목표0", 3_000_000, D(2026, 12, 31)));   // 다른 목표와 이름 충돌
    await Reject(() => service.UpdateAsync("A", trip.Id, today, "x", 0, D(2026, 12, 31)));
    await Reject(() => service.UpdateAsync("A", trip.Id, today, "x", 1000, D(2026, 10, 1)));             // 과거로 변경
    Check(await service.UpdateAsync("A", trip.Id, today, "해외여행", 4_000_000, D(2027, 1, 31)), "Update failed");
    var updatedSummary = (await service.ListAsync("A", today)).Single(item => item.Goal.Id == trip.Id);
    Check(updatedSummary.Goal is { Name: "해외여행", TargetAmount: 4_000_000 } && updatedSummary.Progress.Saved == 1_200_001, "Update changed deposits or ignored fields");
    Check(await service.UpdateAsync("A", trip.Id, today, "해외여행", 4_000_000, null) && (await service.ListAsync("A", today)).Single(i => i.Goal.Id == trip.Id).Goal.TargetDate is null, "Clearing target date failed");
    // 기한이 지난 목표도 목표일을 바꾸지 않으면 이름·금액은 수정할 수 있습니다.
    var late = D(2026, 11, 1);
    Check(await service.UpdateAsync("A", trip.Id, today, "해외여행", 4_000_000, D(2026, 10, 20)), "Setting date failed");
    Check(await service.UpdateAsync("A", trip.Id, late, "해외여행 수정", 4_500_000, D(2026, 10, 20)), "Editing an overdue goal without changing the date failed");
    await Reject(() => service.UpdateAsync("A", trip.Id, late, "해외여행 수정", 4_500_000, D(2026, 10, 25)));  // 다른 과거 날짜로 변경은 거부
    Check((await service.ListAsync("A", late)).Single(i => i.Goal.Id == trip.Id).Progress.IsOverdue, "Overdue not detected via service");

    // 저축 내역 삭제
    Check(!await service.DeleteDepositAsync("B", first.Id), "Foreign deposit delete accepted");
    Check(await service.DeleteDepositAsync("A", first.Id) && !await service.DeleteDepositAsync("A", first.Id), "Deposit delete failed");
    Check((await service.ListAsync("A", today)).Single(i => i.Goal.Id == trip.Id).Progress.Saved == 800_001, "Progress not updated after deposit delete");

    // 스키마: 신규·업그레이드 DB 모두 존재하지 않는 목표에 저축을 직접 넣을 수 없고, 목표를 지우면 저축도 지워집니다.
    try
    {
        await db.Database.ExecuteSqlRawAsync("INSERT INTO SavingsDeposits (OwnerId,GoalId,Date,Amount,Memo) VALUES ('A',999999,'2026-10-01',1,'bad')");
        throw new Exception("Orphan deposit accepted");
    }
    catch (SqliteException) { }
    var cascadeGoal = await service.CreateAsync("C", today, "cascade", 1000, null);
    await service.AddDepositAsync("C", cascadeGoal.Id, today, today, 10, "");
    await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM SavingsGoals WHERE Id = {cascadeGoal.Id}");
    Check(await db.SavingsDeposits.CountAsync(d => d.OwnerId == "C") == 0, "Cascade delete missing");
    Check((await service.CreateAsync("C", today, "cascade", 1000, null)).Id > 0, "Name should be reusable after the goal is deleted");

    // 목표 삭제는 저축 내역을 함께 지우고 다른 목표·타 계정은 건드리지 않습니다.
    await service.AddDepositAsync("B", other.Id, today, today, 7_000, "B 저축");
    Check(!await service.DeleteAsync("B", trip.Id) && await db.SavingsDeposits.CountAsync(d => d.GoalId == trip.Id) == 2, "Foreign goal delete accepted");
    Check(await service.DeleteAsync("A", trip.Id) && !await service.DeleteAsync("A", trip.Id), "Goal delete failed");
    Check(await db.SavingsDeposits.CountAsync(d => d.GoalId == trip.Id) == 0, "Goal delete left deposits");
    Check((await service.ListAsync("B", today)).Single(i => i.Goal.Id == other.Id).Progress.Saved == 7_000, "Goal delete affected another owner");
    Check((await service.ListAsync("A", today)).Count == SavingsGoalService.MaxGoals - 1, "Goal delete count wrong");

    // 최근 3개월(이번 달 제외) 평균 순수지
    db.Incomes.AddRange(
        new IncomeRecord { OwnerId = "M", Date = D(2026, 7, 1), Amount = 1_000_000, Source = "급여" },
        new IncomeRecord { OwnerId = "M", Date = D(2026, 8, 31), Amount = 1_000_000, Source = "급여" },
        new IncomeRecord { OwnerId = "M", Date = D(2026, 9, 30), Amount = 1_000_000, Source = "급여" },
        new IncomeRecord { OwnerId = "M", Date = D(2026, 10, 1), Amount = 8_888_888, Source = "급여" },   // 이번 달은 제외
        new IncomeRecord { OwnerId = "M", Date = D(2026, 6, 30), Amount = 7_777_777, Source = "급여" },   // 범위 이전
        new IncomeRecord { OwnerId = "X", Date = D(2026, 8, 1), Amount = 5_000_000, Source = "급여" });  // 타 계정
    db.Expenses.AddRange(
        new ExpenseRecord { OwnerId = "M", Date = D(2026, 7, 5), Amount = 500_000, Category = "식비", Memo = "" },
        new ExpenseRecord { OwnerId = "M", Date = D(2026, 8, 5), Amount = 700_000, Category = "식비", Memo = "" },
        new ExpenseRecord { OwnerId = "M", Date = D(2026, 9, 5), Amount = 600_000, Category = "식비", Memo = "" },
        new ExpenseRecord { OwnerId = "M", Date = D(2026, 6, 30), Amount = 9_999_999, Category = "식비", Memo = "" },
        new ExpenseRecord { OwnerId = "M", Date = D(2026, 10, 2), Amount = 9_999_999, Category = "식비", Memo = "" },
        new ExpenseRecord { OwnerId = "N", Date = D(2026, 9, 5), Amount = 300_000, Category = "식비", Memo = "" });
    await db.SaveChangesAsync();
    Check(await service.AverageMonthlyNetAsync("M", today) == 400_000, "Average net wrong");
    Check(await service.AverageMonthlyNetAsync("N", today) == -100_000, "Negative average net wrong");
    Check(await service.AverageMonthlyNetAsync("nobody", today) is null, "No data should be null");
    Check(await service.AverageMonthlyNetAsync("M", D(2026, 1, 20)) is null, "Window without data should be null");
    await Reject(() => service.AverageMonthlyNetAsync(" ", today));
    await Reject(() => service.ListAsync("", today));
    await Reject(() => service.ListDepositsAsync("", 1));
    await Reject(() => service.DeleteAsync("", 1));

    // ---- 화면 로직(리플렉션) ----
    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    var auth = new TestAuth("P");
    var page = new Savings();
    void SetProp(string name, object? value) => typeof(Savings).GetProperty(name, flags)!.SetValue(page, value);
    object? Field(string name) => typeof(Savings).GetField(name, flags)!.GetValue(page);
    void SetField(string name, object? value) => typeof(Savings).GetField(name, flags)!.SetValue(page, value);
    Task Call(string method, params object[] args)
    {
        var result = typeof(Savings).GetMethod(method, flags)!.Invoke(page, args);   // void 메서드는 null을 반환합니다.
        return result as Task ?? Task.CompletedTask;
    }
    List<SavingsGoalSummary> PageGoals() => (List<SavingsGoalSummary>)Field("goals")!;
    string? PageError() => (string?)Field("error");
    string Overview() => (string)typeof(Savings).GetProperty("OverviewMessage", flags)!.GetValue(page)!;
    SetProp("AuthenticationStateProvider", auth); SetProp("SavingsGoalService", service); SetProp("Logger", NullLogger<Savings>.Instance);
    var realToday = KoreanClock.Today;

    await Call("OnInitializedAsync");
    Check(PageError() is null && PageGoals().Count == 0 && !(bool)Field("loading")!, "Page did not initialize");
    SetField("goalName", "비상금"); SetField("goalTarget", 0L);
    await Call("SaveGoalAsync");
    Check(PageError() is string && PageGoals().Count == 0, "Page accepted zero target");
    SetField("goalTarget", 400_000L); SetField("goalDate", realToday.AddDays(-1));
    await Call("SaveGoalAsync");
    Check(PageError() is string && PageGoals().Count == 0, "Page accepted past target date");
    SetField("goalDate", realToday.AddDays(90));
    await Call("SaveGoalAsync");
    Check(PageError() is null && PageGoals().Count == 1 && (string)Field("goalName")! == "", "Page create failed");
    var pageGoal = PageGoals().Single().Goal;

    // 평균 순수지가 없으면 비교하지 않고, 있으면 필요 월 저축액과 비교합니다.
    Check(Overview().Contains("비교할 수 없어요"), "Overview without data wrong");
    var prevMonth = new DateTime(realToday.Year, realToday.Month, 1).AddMonths(-1);
    db.Incomes.Add(new IncomeRecord { OwnerId = "P", Date = prevMonth, Amount = 1_000_000, Source = "급여" });
    db.Expenses.Add(new ExpenseRecord { OwnerId = "P", Date = prevMonth, Amount = 400_000, Category = "식비", Memo = "" });
    await db.SaveChangesAsync();
    await Call("RefreshAsync");
    Check((long?)Field("averageNet") == 200_000, "Page average net wrong");
    Check(Overview().Contains("달성할 수 있어요"), $"Overview should be reachable: {Overview()}");
    SetField("busy", false); SetField("editingId", pageGoal.Id); SetField("goalName", "비상금"); SetField("goalTarget", 90_000_000L); SetField("goalDate", realToday.AddDays(90));
    await Call("SaveGoalAsync");
    Check(PageError() is null && Overview().Contains("부족해요"), $"Overview should show a shortfall: {Overview()}");
    Check(PageGoals().Single().Goal.TargetAmount == 90_000_000, "Page edit failed");

    // 저축 기록
    await Call("StartDeposit", pageGoal.Id);
    SetField("depositAmount", 100_000L);
    await Call("SaveDepositAsync");
    Check(PageError() is null && PageGoals().Single().Progress.Saved == 100_000 && Field("depositGoalId") is null, "Page deposit failed");
    await Call("StartDeposit", pageGoal.Id);
    SetField("depositAmount", 5L); SetField("depositDate", realToday.AddDays(1));
    await Call("SaveDepositAsync");
    Check(PageError() is string && PageGoals().Single().Progress.Saved == 100_000, "Page accepted future deposit");

    // 내역 보기·삭제
    await Call("ToggleHistoryAsync", pageGoal.Id);
    var pageDeposits = (List<SavingsDeposit>)Field("deposits")!;
    Check(pageDeposits.Count == 1 && (int?)Field("historyGoalId") == pageGoal.Id, "Page history failed");
    await Call("DeleteDepositAsync", pageDeposits[0].Id);          // 삭제 확인 상태가 아니므로 무시되어야 합니다.
    Check(PageGoals().Single().Progress.Saved == 100_000, "Delete without confirmation ran");
    SetField("deletingDepositId", pageDeposits[0].Id);
    await Call("DeleteDepositAsync", pageDeposits[0].Id);
    Check(PageGoals().Single().Progress.Saved == 0 && ((List<SavingsDeposit>)Field("deposits")!).Count == 0, "Page deposit delete failed");

    // 로그인 계정이 바뀌면 저장하지 않습니다.
    auth.OwnerId = "Q";
    SetField("editingId", null); SetField("goalName", "탈취 시도"); SetField("goalTarget", 1000L); SetField("goalDate", null);
    await Call("SaveGoalAsync");
    Check(PageError() is string && (await service.ListAsync("Q", realToday)).Count == 0 && (await service.ListAsync("P", realToday)).Count == 1, "Identity change not blocked");
    auth.OwnerId = "P";

    // 목표 삭제는 확인 상태에서만 실행됩니다.
    await Call("DeleteGoalAsync", pageGoal.Id);
    Check(PageGoals().Count == 1, "Goal delete ran without confirmation");
    SetField("deletingGoalId", pageGoal.Id);
    await Call("DeleteGoalAsync", pageGoal.Id);
    Check(PageGoals().Count == 0 && PageError() is null, "Page goal delete failed");

    // 계정 삭제 시 목표와 저축을 모두 지우고 다른 계정은 유지합니다.
    await new UserDataDeletionService(factory).DeleteAsync("A");
    Check((await service.ListAsync("A", today)).Count == 0 && await db.SavingsDeposits.CountAsync(d => d.OwnerId == "A") == 0, "Account deletion left savings data");
    Check((await service.ListAsync("B", today)).Count == 2 && (await service.ListAsync("B", today)).Sum(i => i.Progress.Saved) == 7_000, "Account deletion crossed owners");

    Console.WriteLine($"PASS ({(legacy ? "upgraded" : "new")} DB): goal CRUD, validation, ownership, deposits, progress, overdue edits, cascade, average net and account deletion");
}

Console.WriteLine("PASS: progress calculation (percent floor, required per month, overdue, projection)");

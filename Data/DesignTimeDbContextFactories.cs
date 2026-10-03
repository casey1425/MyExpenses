using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace MyExpenses.Data;

// `dotnet ef migrations ...` 전용입니다. 앱 시작 코드(DB 초기화 등)를 실행하지 않고 모델만 만들며,
// 마이그레이션 생성은 DB에 연결하지 않으므로 아래 파일은 만들어지지 않습니다.
public sealed class ExpensesDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ExpensesDbContext>
{
    public ExpensesDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ExpensesDbContext>().UseSqlite("Data Source=design-time-expenses.db").Options);
}

public sealed class AuthDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
    public AuthDbContext CreateDbContext(string[] args)
    {
        // Identity 모델은 IdentityOptions(스키마 버전 등)에 따라 달라지므로 런타임과 같은 기본 설정을 제공합니다.
        var services = new ServiceCollection();
        services.AddIdentityCore<IdentityUser>();
        return new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>()
            .UseSqlite("Data Source=design-time-auth.db")
            .UseApplicationServiceProvider(services.BuildServiceProvider())
            .Options);
    }
}

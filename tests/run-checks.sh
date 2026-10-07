#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet build "$project_root/MyExpenses.csproj"
for check in ExpenseSearchChecks ExpenseTemplateChecks PaymentMethodChecks ServiceChecks CategoryChecks IncomeChecks RecurringIncomeChecks SavingsGoalChecks MigrationChecks PwaChecks BackupChecks StatisticsChecks UsabilityChecks TagChecks DatabaseBackupChecks; do
    dotnet run --project "$project_root/tests/$check/$check.csproj"
done

# 화면 색: 글자 대비(4.5:1)와 다크 모드 색 토큰이 최신인지 확인합니다.
if command -v python3 >/dev/null 2>&1; then
    python3 "$project_root/tools/theme/contrast_report.py"
    python3 "$project_root/tools/theme/generate_theme.py" --check
else
    echo "WARN: python3 not found; skipped theme checks" >&2
fi

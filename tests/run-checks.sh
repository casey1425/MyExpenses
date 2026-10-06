#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet build "$project_root/MyExpenses.csproj"
for check in ExpenseSearchChecks ExpenseTemplateChecks PaymentMethodChecks ServiceChecks CategoryChecks IncomeChecks RecurringIncomeChecks SavingsGoalChecks MigrationChecks PwaChecks BackupChecks StatisticsChecks UsabilityChecks TagChecks; do
    dotnet run --project "$project_root/tests/$check/$check.csproj"
done

namespace MyExpenses.Data;

public sealed class UserProfile
{
    public string OwnerId { get; set; } = string.Empty;
    public bool HasCompletedOnboarding { get; set; }
}

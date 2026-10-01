namespace MyExpenses.Data;

public sealed class UserCategory
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = "";
    public string Name { get; set; } = "";
    public int Position { get; set; }
    public bool IsArchived { get; set; }
}

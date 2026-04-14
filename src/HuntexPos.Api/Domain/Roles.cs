namespace HuntexPos.Api.Domain;

public static class Roles
{
    public const string Dev = "Dev";
    public const string Owner = "Owner";
    public const string Admin = "Admin";
    public const string Sales = "Sales";

    public static readonly string[] All = { Dev, Owner, Admin, Sales };
}

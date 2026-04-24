namespace AIChat.Api.Models;

public class UserConfig
{
    public string Id { get; set; } = "";
    public List<string> AuthCodes { get; set; } = new();
}

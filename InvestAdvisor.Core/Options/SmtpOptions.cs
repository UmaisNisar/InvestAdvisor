namespace InvestAdvisor.Core.Options;

public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string? Password { get; set; }
}

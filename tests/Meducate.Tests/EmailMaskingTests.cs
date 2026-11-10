using Meducate.Infrastructure.Email;

namespace Meducate.Tests;

public class EmailMaskingTests
{
    [Fact]
    public void MaskEmail_MasksLocalPart()
    {
        var result = EmailService.MaskEmail("james@example.com");

        Assert.Equal("j***@example.com", result);
    }

    [Fact]
    public void MaskEmail_HandlesShortLocalPart()
    {
        var result = EmailService.MaskEmail("a@example.com");

        Assert.Equal("***@***", result);
    }

    [Fact]
    public void MaskEmail_PreservesDomain()
    {
        var result = EmailService.MaskEmail("longname@subdomain.example.co.uk");

        Assert.Equal("l***@subdomain.example.co.uk", result);
    }

    [Fact]
    public void MaskEmail_HandlesNoAtSign()
    {
        var result = EmailService.MaskEmail("notanemail");

        Assert.Equal("***@***", result);
    }
}

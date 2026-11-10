using System.ComponentModel.DataAnnotations;
using Meducate.Application.DTOs;

namespace Meducate.Tests;

public class DtoValidationTests
{
    private static List<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(model);
        Validator.TryValidateObject(model, context, results, validateAllProperties: true);
        return results;
    }

    // --- CreateOrganisationRequest ---

    [Fact]
    public void CreateOrg_ValidName_Passes()
    {
        var request = new CreateOrganisationRequest { OrganisationName = "My Organisation" };
        Assert.Empty(Validate(request));
    }

    [Fact]
    public void CreateOrg_EmptyName_Fails()
    {
        var request = new CreateOrganisationRequest { OrganisationName = "" };
        Assert.NotEmpty(Validate(request));
    }

    [Fact]
    public void CreateOrg_TooShortName_Fails()
    {
        var request = new CreateOrganisationRequest { OrganisationName = "A" };
        Assert.NotEmpty(Validate(request));
    }

    [Fact]
    public void CreateOrg_TooLongName_Fails()
    {
        var request = new CreateOrganisationRequest { OrganisationName = new string('A', 201) };
        Assert.NotEmpty(Validate(request));
    }

    [Theory]
    [InlineData("Org<script>")]
    [InlineData("Org;DROP TABLE")]
    [InlineData("Name\"injection")]
    [InlineData("Test{brackets}")]
    public void CreateOrg_InvalidCharacters_Fails(string name)
    {
        var request = new CreateOrganisationRequest { OrganisationName = name };
        var results = Validate(request);
        Assert.Contains(results, r => r.ErrorMessage!.Contains("invalid characters"));
    }

    [Theory]
    [InlineData("St. Mary's Hospital")]
    [InlineData("Smith & Jones")]
    [InlineData("Health Corp.")]
    [InlineData("My Org (UK)")]
    [InlineData("Two-Word Name")]
    public void CreateOrg_ValidSpecialCharacters_Passes(string name)
    {
        var request = new CreateOrganisationRequest { OrganisationName = name };
        Assert.Empty(Validate(request));
    }

    // --- CreateApiKeyRequest ---

    [Fact]
    public void CreateKey_NullName_Passes()
    {
        var request = new CreateApiKeyRequest { Name = null };
        Assert.Empty(Validate(request));
    }

    [Fact]
    public void CreateKey_ValidName_Passes()
    {
        var request = new CreateApiKeyRequest { Name = "My API Key" };
        Assert.Empty(Validate(request));
    }

    [Fact]
    public void CreateKey_TooLongName_Fails()
    {
        var request = new CreateApiKeyRequest { Name = new string('A', 101) };
        Assert.NotEmpty(Validate(request));
    }

    [Fact]
    public void CreateKey_InvalidCharacters_Fails()
    {
        var request = new CreateApiKeyRequest { Name = "key<script>" };
        var results = Validate(request);
        Assert.Contains(results, r => r.ErrorMessage!.Contains("invalid characters"));
    }

    // --- UpdateApiKeyRequest ---

    [Fact]
    public void UpdateKey_ValidName_Passes()
    {
        var request = new UpdateApiKeyRequest { Name = "Production Key" };
        Assert.Empty(Validate(request));
    }

    [Fact]
    public void UpdateKey_EmptyName_Fails()
    {
        var request = new UpdateApiKeyRequest { Name = "" };
        Assert.NotEmpty(Validate(request));
    }

    [Fact]
    public void UpdateKey_TooLongName_Fails()
    {
        var request = new UpdateApiKeyRequest { Name = new string('A', 101) };
        Assert.NotEmpty(Validate(request));
    }

    [Fact]
    public void UpdateKey_InvalidCharacters_Fails()
    {
        var request = new UpdateApiKeyRequest { Name = "key;DROP" };
        var results = Validate(request);
        Assert.Contains(results, r => r.ErrorMessage!.Contains("invalid characters"));
    }
}

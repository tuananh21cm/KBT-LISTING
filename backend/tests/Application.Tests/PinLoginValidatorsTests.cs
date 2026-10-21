using Application.DTOs.Auth;
using Application.Validators.Auth;
using FluentValidation.TestHelper;
using Xunit;

namespace Application.Tests;

public class PinLoginValidatorsTests
{
    [Fact]
    public void PinLogin_AcceptsFourDigits()
    {
        var validator = new PinLoginRequestValidator();
        var result = validator.TestValidate(new PinLoginRequestDto { Pin = "4829" });
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("123")]
    [InlineData("12345")]
    public void PinLogin_RejectsInvalid(string pin)
    {
        var validator = new PinLoginRequestValidator();
        var result = validator.TestValidate(new PinLoginRequestDto { Pin = pin });
        result.ShouldHaveValidationErrorFor(x => x.Pin);
    }

    [Fact]
    public void CreateAdminUser_AcceptsOptionalFourDigitPin()
    {
        var validator = new CreateAdminUserValidator();
        var result = validator.TestValidate(new CreateAdminUserDto
        {
            UserName = "fulfil1",
            Email = "f@example.com",
            FullName = "Fulfil One",
            Password = "Password1!",
            Roles = ["Fulfil"],
            FulfilPin = "1234",
        });
        result.ShouldNotHaveValidationErrorFor(x => x.FulfilPin);
    }

    [Fact]
    public void CreateAdminUser_RejectsNonFourDigitPin()
    {
        var validator = new CreateAdminUserValidator();
        var result = validator.TestValidate(new CreateAdminUserDto
        {
            UserName = "fulfil1",
            Email = "f@example.com",
            FullName = "Fulfil One",
            Password = "Password1!",
            Roles = ["Fulfil"],
            FulfilPin = "12",
        });
        result.ShouldHaveValidationErrorFor(x => x.FulfilPin);
    }

    [Fact]
    public void UpdateAdminUser_AllowsEmptyPinToClear()
    {
        var validator = new UpdateAdminUserValidator();
        var result = validator.TestValidate(new UpdateAdminUserDto
        {
            Email = "f@example.com",
            FullName = "Fulfil One",
            Roles = ["Fulfil"],
            FulfilPin = "",
        });
        result.ShouldNotHaveValidationErrorFor(x => x.FulfilPin);
    }

    [Fact]
    public void UpdateAdminUser_RejectsInvalidPin()
    {
        var validator = new UpdateAdminUserValidator();
        var result = validator.TestValidate(new UpdateAdminUserDto
        {
            Email = "f@example.com",
            FullName = "Fulfil One",
            Roles = ["Fulfil"],
            FulfilPin = "abcd",
        });
        result.ShouldHaveValidationErrorFor(x => x.FulfilPin);
    }
}

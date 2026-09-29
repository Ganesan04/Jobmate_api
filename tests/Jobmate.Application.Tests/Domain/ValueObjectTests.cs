using FluentAssertions;
using Jobmate.Domain.Exceptions;
using Jobmate.Domain.ValueObjects;

namespace Jobmate.Application.Tests.Domain;

/// <summary>
/// The phone number and email value objects.
///
/// Both are entered by hand into a Flutter text field, so both have to survive the shapes
/// people actually type — a number pasted with a country code and spaces, an address with
/// a capital letter in it — and both have to reject the near-misses rather than storing
/// something that can never be matched again.
/// </summary>
public sealed class PhoneNumberTests
{
    [Theory]
    [InlineData("9812300001", "+919812300001")]
    [InlineData("  9812300001  ", "+919812300001")]
    [InlineData("+919812300001", "+919812300001")]
    [InlineData("+91 98123 00001", "+919812300001")]
    [InlineData("+91-98123-00001", "+919812300001")]
    [InlineData("(91) 9812300001", "+919812300001")]
    [InlineData("919812300001", "+919812300001")]
    public void The_same_number_typed_differently_lands_in_the_same_place(string typed, string expected)
    {
        // This is the whole reason for the type: the app signs in with a bare 10-digit
        // number, and the unique index is on the stored form, so the two have to collapse.
        PhoneNumber.Create(typed).E164.Should().Be(expected);
    }

    [Fact]
    public void The_national_form_is_what_is_stored_for_display()
    {
        PhoneNumber.Create("+919812300001").National.Should().Be("9812300001");
    }

    [Fact]
    public void The_displayed_form_matches_the_mask_the_app_shows()
    {
        // "98•••210" in the Flutter profile screen.
        PhoneNumber.Create("+919812300001").Masked.Should().Be("98•••001");
    }

    [Fact]
    public void The_mask_reveals_the_first_two_and_last_three_only()
    {
        // Everything else is the • character, not a digit: the profile screen shows this
        // to somebody who has already proved they own the number, and the point of the mask
        // is that it does not help anybody else.
        var masked = PhoneNumber.Create("+919812300001").Masked;

        masked.Should().Be("98•••001");
        masked.Count(c => c == '•').Should().Be(3);
    }

    [Fact]
    public void Printing_the_value_gives_the_normalised_form()
    {
        PhoneNumber.Create("+91 98123 00001").ToString().Should().Be("+919812300001");
    }

    [Theory]
    [InlineData(null, "PhoneRequired")]
    [InlineData("", "PhoneRequired")]
    [InlineData("   ", "PhoneRequired")]
    [InlineData("981230000", "PhoneInvalid")]        // nine digits
    [InlineData("98123000012", "PhoneInvalid")]      // eleven digits with no country code
    [InlineData("1234567890", "PhoneInvalid")]       // not a mobile: starts with 1
    [InlineData("5812300001", "PhoneInvalid")]       // not a mobile: starts with 5
    [InlineData("abcdefghij", "PhoneInvalid")]
    [InlineData("+44 2079460018", "PhoneInvalid")]   // a real number, wrong country
    public void Anything_that_is_not_a_ten_digit_mobile_is_refused(string? typed, string errorCode)
    {
        var act = () => PhoneNumber.Create(typed);

        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be(errorCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345")]
    [InlineData("abcdefghij")]
    public void TryCreate_says_no_instead_of_throwing(string? typed)
    {
        PhoneNumber.TryCreate(typed, out var phone).Should().BeFalse();
        phone.Should().BeNull();
    }

    [Fact]
    public void TryCreate_hands_back_the_number_when_the_input_is_good()
    {
        // The out parameter is annotated NotNullWhen(true), so a caller can dereference it
        // without a null check; this pins that contract down.
        PhoneNumber.TryCreate("9812300001", out var phone).Should().BeTrue();

        phone.Should().NotBeNull();
        phone!.E164.Should().Be("+919812300001");
    }
}

/// <summary>
/// The email value object. Stored lower-cased so the unique index is case-insensitive in
/// effect — without that, a second account can be registered on the same address with a
/// capital letter and neither login finds the other.
/// </summary>
public sealed class EmailAddressTests
{
    [Theory]
    [InlineData("Anita.Sharma@Jobmate.Test", "anita.sharma@jobmate.test")]
    [InlineData("  ANITA@JOBMATE.TEST  ", "anita@jobmate.test")]
    [InlineData("a+b@jobmate.test", "a+b@jobmate.test")]
    public void The_value_is_stored_normalised(string typed, string expected)
    {
        EmailAddress.Create(typed).Value.Should().Be(expected);
    }

    [Fact]
    public void Printing_the_value_gives_the_normalised_form()
    {
        EmailAddress.Create("  ANITA@JOBMATE.TEST ").ToString().Should().Be("anita@jobmate.test");
    }

    [Theory]
    [InlineData(null, "EmailRequired")]
    [InlineData("", "EmailRequired")]
    [InlineData("   ", "EmailRequired")]
    [InlineData("notanemail", "EmailInvalid")]
    [InlineData("@jobmate.test", "EmailInvalid")]
    [InlineData("anita@", "EmailInvalid")]
    [InlineData("anita@jobmate", "EmailInvalid")]          // no dot in the domain
    [InlineData("ani@ta@jobmate.test", "EmailInvalid")]   // two at signs
    [InlineData("anita@.test", "EmailInvalid")]
    [InlineData("anita@jobmate.", "EmailInvalid")]
    [InlineData("anita@job..test", "EmailInvalid")]
    [InlineData("anita@job mate.test", "EmailInvalid")]
    [InlineData("anita s@jobmate.test", "EmailInvalid")]
    public void Anything_that_is_not_an_address_is_refused(string? typed, string errorCode)
    {
        var act = () => EmailAddress.Create(typed);

        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be(errorCode);
    }

    [Fact]
    public void An_address_at_the_length_limit_is_accepted_and_one_over_is_not()
    {
        // 254 is the RFC limit; a longer one is refused by the same path as a malformed
        // address rather than reaching the database.
        var domain = "@jobmate.test";
        var atLimit = new string('a', 254 - domain.Length) + domain;
        var overLimit = new string('a', 255 - domain.Length) + domain;

        EmailAddress.Create(atLimit).Value.Should().Be(atLimit);
        var act = () => EmailAddress.Create(overLimit);
        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("EmailInvalid");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("notanemail")]
    public void TryCreate_says_no_instead_of_throwing(string? typed)
    {
        EmailAddress.TryCreate(typed, out var email).Should().BeFalse();
        email.Should().BeNull();
    }

    [Fact]
    public void TryCreate_hands_back_the_address_when_the_input_is_good()
    {
        EmailAddress.TryCreate("Anita@Jobmate.test", out var email).Should().BeTrue();

        email.Should().NotBeNull();
        email!.Value.Should().Be("anita@jobmate.test");
    }

    [Fact]
    public void Two_spellings_of_the_same_address_are_the_same_value()
    {
        // Value equality is what stops the duplicate check from being bypassed by typing
        // the address differently.
        EmailAddress.Create("Anita@Jobmate.test")
            .Should().Be(EmailAddress.Create("anita@JOBMATE.TEST"));
    }
}

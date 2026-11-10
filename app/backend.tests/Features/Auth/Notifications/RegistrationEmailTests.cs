using LoginApp.Api.Features.Auth.Notifications;
using LoginApp.Api.Infrastructure.Email;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace LoginApp.Api.Tests.Features.Auth.Notifications;

/// <summary>
/// What the email about a new registration says, and the daily limit on how many go out.
/// </summary>
public sealed class RegistrationEmailTests
{
    private static readonly DateTimeOffset s_registeredAt = new(2026, 11, 10, 14, 3, 27, TimeSpan.Zero);

    private static readonly RegistrationNotificationOptions s_options = new()
    {
        Recipients = ["owner@example.com"],
        SiteUrl = new Uri("https://grocery.example.com"),
    };

    [Fact]
    public void Create_SaysWhereWhenAndWhoInPlainText()
    {
        // Act
        EmailMessage email = RegistrationEmail.Create(new RegistrationNotice("Ada Lovelace", s_registeredAt), s_options, isLastForToday: false);

        // Assert
        Assert.Equal(["owner@example.com"], email.To);
        Assert.Equal("[grocery app] new registration detected", email.Subject);
        Assert.Equal(
            "Someone has just registered a new account on https://grocery.example.com.\n\n"
            + "Name, as they typed it: \"Ada Lovelace\"\n"
            + "Registered at: 2026-11-10 14:03 UTC\n\n"
            + "You get this email because your address is in the app's RegistrationNotifications:Recipients setting "
            + "(registration_notification_recipients in its Terraform).\n",
            email.Text);
    }

    [Fact]
    public void Create_GivesTheTimeInUtc_WhateverTheOffset()
    {
        // Arrange: 09:03 in UTC-5 is 14:03 UTC.
        RegistrationNotice notice = new("Ada", new DateTimeOffset(2026, 11, 10, 9, 3, 0, TimeSpan.FromHours(-5)));

        // Act
        EmailMessage email = RegistrationEmail.Create(notice, s_options, isLastForToday: false);

        // Assert
        Assert.Contains("Registered at: 2026-11-10 14:03 UTC\n", email.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_WithoutASiteUrl_NamesTheApp()
    {
        // Arrange
        RegistrationNotificationOptions options = new() { Recipients = ["owner@example.com"] };

        // Act
        EmailMessage email = RegistrationEmail.Create(new RegistrationNotice("Ada", s_registeredAt), options, isLastForToday: false);

        // Assert
        Assert.StartsWith("Someone has just registered a new account on Nibbles.\n", email.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_ForTheLastEmailTheLimitAllows_SaysThatMoreWontBeSent()
    {
        // Act
        EmailMessage email = RegistrationEmail.Create(new RegistrationNotice("Ada", s_registeredAt), s_options, isLastForToday: true);

        // Assert
        Assert.Contains(
            "That makes 50 registration emails in 24 hours, the most RegistrationNotifications:MaxPerDay allows.",
            email.Text,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Ada\nRegistered at: never", "Ada\uFFFDRegistered at: never")] // posing as another line of the email
    [InlineData("Ada\r\nBob", "Ada\uFFFD\uFFFDBob")]
    [InlineData("Ada\tLovelace", "Ada\uFFFDLovelace")]
    [InlineData("Ada\u2028Bob", "Ada\uFFFDBob")] // line separator
    [InlineData("\u202Eecalevol adA", "\uFFFDecalevol adA")] // right-to-left override, which would show "Ada lovelace"
    [InlineData("\u2066Ada\u2069", "\uFFFDAda\uFFFD")] // isolates
    [InlineData("Ada\u200FLovelace", "Ada\uFFFDLovelace")] // right-to-left mark
    public void Sanitize_ReplacesCharactersThatCouldDisguiseTheName(string displayName, string expected) =>
        Assert.Equal(expected, RegistrationEmail.Sanitize(displayName));

    [Theory]
    [InlineData("Ada Lovelace")]
    [InlineData("Zo\u00EB \u00C5ngstr\u00F6m")]
    [InlineData("\u0645\u062D\u0645\u062F")]
    [InlineData("\U0001F468\u200D\U0001F469\u200D\U0001F467 The Lovelaces")] // a family emoji: joined by zero-width joiners
    public void Sanitize_KeepsOrdinaryNamesAsTheyAre(string displayName) =>
        Assert.Equal(displayName, RegistrationEmail.Sanitize(displayName));

    [Fact]
    public void Budget_AllowsTheDailyLimitInAny24Hours_AndCountsWhatHasGoneOut()
    {
        // Arrange
        FakeTimeProvider clock = new(s_registeredAt);
        RegistrationNotificationBudget budget = new(clock, Options.Create(new RegistrationNotificationOptions { MaxPerDay = 2 }));

        // Act + Assert
        Assert.True(budget.TryTake(out int first));
        Assert.Equal(1, first);
        clock.Advance(TimeSpan.FromHours(12));
        Assert.True(budget.TryTake(out int second));
        Assert.Equal(2, second);
        Assert.False(budget.TryTake(out _));

        // The first email leaves the window 24 hours after it was sent, freeing one place.
        clock.Advance(TimeSpan.FromHours(12));
        Assert.True(budget.TryTake(out int third));
        Assert.Equal(2, third);
        Assert.False(budget.TryTake(out _));
    }

    [Fact]
    public void Budget_OfZero_AllowsNothing()
    {
        // Arrange
        RegistrationNotificationBudget budget = new(new FakeTimeProvider(), Options.Create(new RegistrationNotificationOptions { MaxPerDay = 0 }));

        // Act + Assert
        Assert.False(budget.TryTake(out _));
    }
}

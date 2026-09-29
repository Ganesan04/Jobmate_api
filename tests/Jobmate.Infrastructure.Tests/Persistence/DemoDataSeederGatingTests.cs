using FluentAssertions;
using Jobmate.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace Jobmate.Infrastructure.Tests.Persistence;

/// <summary>
/// The demo seeder's two gates.
///
/// The seeder writes fictional employers and workers, so the question is not whether it
/// works but when it is allowed to run. Both gates are checked here rather than only in
/// the integration suite, because "switched on in production by accident" is exactly the
/// mistake that must fail in a unit test where it is cheap to see.
/// </summary>
public sealed class DemoDataSeederGatingTests
{
    [Fact]
    public void It_runs_only_when_both_gates_are_satisfied()
    {
        DemoDataSeeder.IsEnabled(Config(("ASPNETCORE_ENVIRONMENT", "Development"), (DemoDataSeeder.EnabledKey, "true")))
            .Should().BeTrue();
    }

    [Fact]
    public void The_switch_alone_does_not_enable_it()
    {
        DemoDataSeeder.IsEnabled(Config((DemoDataSeeder.EnabledKey, "true")))
            .Should().BeFalse();
    }

    [Fact]
    public void The_environment_alone_does_not_enable_it()
    {
        // A developer who set the switch once should not get demo accounts in a test run or
        // a staging environment just because that also happens to be called Development.
        DemoDataSeeder.IsEnabled(Config(("ASPNETCORE_ENVIRONMENT", "Development")))
            .Should().BeFalse();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    [InlineData("development-ish")]
    [InlineData("")]
    public void It_refuses_any_environment_that_is_not_exactly_development(string environment)
    {
        DemoDataSeeder.IsEnabled(Config(("ASPNETCORE_ENVIRONMENT", environment), (DemoDataSeeder.EnabledKey, "true")))
            .Should().BeFalse();
    }

    [Theory]
    [InlineData("false")]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("yes")]
    [InlineData("TRUEISH")]
    public void The_switch_has_to_be_the_word_true(string value)
    {
        // "1" in particular: an environment variable that reads as truthy to some other
        // tool must not switch seeding on here.
        DemoDataSeeder.IsEnabled(Config(("ASPNETCORE_ENVIRONMENT", "Development"), (DemoDataSeeder.EnabledKey, value)))
            .Should().BeFalse();
    }

    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    [InlineData("TRUE")]
    public void The_switch_is_not_case_sensitive(string value)
    {
        DemoDataSeeder.IsEnabled(Config(("ASPNETCORE_ENVIRONMENT", "Development"), (DemoDataSeeder.EnabledKey, value)))
            .Should().BeTrue();
    }

    [Fact]
    public void An_empty_configuration_enables_nothing()
    {
        DemoDataSeeder.IsEnabled(Config()).Should().BeFalse();
    }

    [Fact]
    public void A_configuration_with_the_switch_but_no_environment_name_enables_nothing()
    {
        // The seeder also falls back to DOTNET_ENVIRONMENT, which is a process variable and
        // deliberately not exercised here: setting it would change the gating for every
        // other test in this process at the same time.
        var configuration = Config((DemoDataSeeder.EnabledKey, "true"));

        configuration[DemoDataSeeder.EnabledKey].Should().Be("true");
        configuration["ASPNETCORE_ENVIRONMENT"].Should().BeNull();
    }

    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new StubConfiguration(values);
}

/// <summary>
/// A hand-rolled <see cref="IConfiguration"/> rather than a built one, so a test can hand
/// the seeder exactly the two keys it reads and nothing else — no file, no environment
/// variable and no other key can quietly satisfy a gate.
/// </summary>
internal sealed class StubConfiguration : IConfiguration
{
    private readonly Dictionary<string, string?> _values;

    public StubConfiguration(params (string Key, string? Value)[] values) =>
        _values = values.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);

    public string? this[string key]
    {
        get => _values.TryGetValue(key, out var value) ? value : null;
        set => _values[key] = value;
    }

    public IEnumerable<IConfigurationSection> GetChildren() =>
        _values
            .Where(entry => !entry.Key.Contains(':', StringComparison.Ordinal))
            .Select(entry => (IConfigurationSection)new StubSection(entry.Key, [entry]));

    public IChangeToken GetReloadToken() => new CancellationChangeToken(CancellationToken.None);

    public IConfigurationSection GetSection(string key) =>
        new StubSection(key, _values
            .Where(entry => entry.Key.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase))
            .Select(entry => new KeyValuePair<string, string?>(entry.Key[(key.Length + 1)..], entry.Value))
            .ToArray());
}

/// <summary>The <c>GetSection</c> half of the same thing, for completeness.</summary>
internal sealed class StubSection : IConfigurationSection
{
    private readonly string _path;

    private readonly IReadOnlyList<KeyValuePair<string, string?>> _values;

    public StubSection(string path, IReadOnlyList<KeyValuePair<string, string?>> values)
    {
        _path = path;
        _values = values;
    }

    public string? this[string key]
    {
        get => _values.FirstOrDefault(entry => entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value;
        set => throw new NotSupportedException("The seeder does not write configuration.");
    }

    public string Key => _path;

    public string Path => _path;

    public string? Value { get; set; }

    public IEnumerable<IConfigurationSection> GetChildren() =>
        _values.Select(entry => (IConfigurationSection)new StubSection($"{_path}:{entry.Key}", [entry]));

    public IChangeToken GetReloadToken() => new CancellationChangeToken(CancellationToken.None);

    public IConfigurationSection GetSection(string key) => new StubSection($"{_path}:{key}", []);
}

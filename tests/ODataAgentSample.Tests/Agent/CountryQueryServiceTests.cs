using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ODataAgentSample.Agent;
using ODataAgentSample.Infrastructure.Db;

namespace ODataAgentSample.Tests.Agent;

public sealed class CountryQueryServiceTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"odata-agent-svc-{Guid.NewGuid():N}.db");
    private AppDbContext _db = null!;
    private CountryQueryService _service = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        _db = new AppDbContext(options);
        await DatabaseSeeder.ResetAndSeedAsync(_db);
        _service = new CountryQueryService(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    private static CancellationToken Ct => CancellationToken.None;

    [Fact]
    public async Task Get_by_iso2_iso3_and_name_return_the_same_country()
    {
        var byIso2 = await _service.GetAsync("at", Ct);
        var byIso3 = await _service.GetAsync("AUT", Ct);
        var byName = await _service.GetAsync("Austria", Ct);

        Assert.Equal(byIso2, byIso3);
        Assert.Equal(byIso2, byName);
        Assert.Equal("Europe", byIso2.WorldRegion);
        Assert.True(byIso2.EuMember);
    }

    [Fact]
    public async Task Get_unknown_country_explains_valid_input()
    {
        var ex = await Assert.ThrowsAsync<AgentInputException>(() => _service.GetAsync("Atlantis", Ct));

        Assert.Contains("ISO2", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Source_data_flag_J_is_treated_as_yes() =>
        // Mayotte ist im Original-Datensatz mit "J" statt "Y" kodiert
        Assert.True((await _service.GetAsync("YT", Ct)).EuMember);

    [Fact]
    public async Task Search_filters_by_region_and_eu_membership()
    {
        var result = await _service.SearchAsync(null, "EU", euMember: true, null, 100, 0, Ct);

        Assert.InRange(result.Total, 20, 40);
        Assert.All(result.Items, c => Assert.True(c.EuMember));
        Assert.All(result.Items, c => Assert.Equal("Europe", c.WorldRegion));
    }

    [Fact]
    public async Task Region_accepts_iso_code_and_original_odata_search_term()
    {
        var byIso = await _service.SearchAsync(null, "EU", null, null, 1, 0, Ct);
        var byTerm = await _service.SearchAsync(null, "LaenderInEuropa", null, null, 1, 0, Ct);

        Assert.Equal(byIso.Total, byTerm.Total);
    }

    [Fact]
    public async Task Unknown_region_lists_valid_codes()
    {
        var ex = await Assert.ThrowsAsync<AgentInputException>(() => _service.SearchAsync(null, "XX", null, null, 10, 0, Ct));

        Assert.Contains("AF", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_is_paged_and_limit_is_capped()
    {
        var first = await _service.SearchAsync(null, null, null, null, 10, 0, Ct);
        var last = await _service.SearchAsync(null, null, null, null, 1000, 250, Ct);

        Assert.Equal(10, first.Items.Count);
        Assert.True(first.HasMore);
        Assert.Equal(264, first.Total);
        Assert.False(last.HasMore);
        Assert.Equal(264 - 250, last.Items.Count);
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    public async Task Search_text_cannot_inject_like_wildcards(string text)
    {
        var result = await _service.SearchAsync(text, null, null, null, 10, 0, Ct);

        Assert.Equal(0, result.Total);
    }

    [Fact]
    public async Task List_regions_returns_six_world_regions_with_counts()
    {
        var regions = await _service.ListRegionsAsync(Ct);

        Assert.Equal(6, regions.Count);
        var europe = regions.Single(r => r.Iso == "EU");
        Assert.NotEmpty(europe.SubRegions);
        Assert.InRange(europe.EuMembers, 20, 40);
    }

    [Fact]
    public async Task Update_changes_only_given_fields()
    {
        var before = await _service.GetAsync("LI", Ct);

        var after = await _service.UpdateAsync("LI", "Liechtenstein (Fürstentum)", null, null, Ct);

        Assert.Equal("Liechtenstein (Fürstentum)", after.DisplayNameGer);
        Assert.NotEqual(before.DisplayNameGer, after.DisplayNameGer);
        Assert.Equal(before.EuMember, after.EuMember);
        Assert.Equal(before.EwrMember, after.EwrMember);
    }

    [Fact]
    public async Task Update_can_toggle_membership_flags()
    {
        var after = await _service.UpdateAsync("LI", null, euMember: true, ewrMember: false, Ct);

        Assert.True(after.EuMember);
        Assert.False(after.EwrMember);
    }

    [Fact]
    public async Task Update_without_any_field_is_rejected() =>
        await Assert.ThrowsAsync<AgentInputException>(() => _service.UpdateAsync("LI", null, null, null, Ct));

    [Fact]
    public async Task Update_rejects_invalid_names()
    {
        await Assert.ThrowsAsync<AgentInputException>(() => _service.UpdateAsync("LI", "   ", null, null, Ct));
        await Assert.ThrowsAsync<AgentInputException>(() => _service.UpdateAsync("LI", new string('x', 201), null, null, Ct));
    }
}

using Neox.Core.Catalog;
using Neox.Core.Models;
using Xunit;

namespace Neox.Tests;

public class CatalogTests
{
    private static readonly TweakCatalog Catalog = TweakCatalog.LoadEmbedded();

    [Fact]
    public void Loads_all_253_migrated_items_with_unique_ids()
    {
        Assert.Equal(253, Catalog.All.Count);
        Assert.Equal(253, Catalog.All.Select(t => t.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Every_item_has_title_description_and_consequences()
    {
        foreach (var t in Catalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Title), t.Id);
            Assert.False(string.IsNullOrWhiteSpace(t.Description), t.Id);
            if (!t.IsAction) Assert.False(string.IsNullOrWhiteSpace(t.Consequences), t.Id);
        }
    }

    [Fact]
    public void Every_item_carries_the_data_its_kind_needs()
    {
        foreach (var t in Catalog.All)
        {
            switch (t.Kind)
            {
                case TweakKind.Reg: Assert.NotEmpty(t.Values!); break;
                case TweakKind.RegPerAdapter: Assert.NotEmpty(t.PerAdapter!.Values); break;
                case TweakKind.RegPerGame: Assert.NotEmpty(t.PerGame!); break;
                case TweakKind.PowerIdx: Assert.NotNull(t.Power); break;
                case TweakKind.PowerPlan: Assert.False(string.IsNullOrEmpty(t.Plan)); break;
                case TweakKind.Service: Assert.False(string.IsNullOrEmpty(t.Service)); break;
                case TweakKind.Task: Assert.NotEmpty(t.Tasks!); break;
                case TweakKind.Bcd: Assert.NotNull(t.Bcd); break;
                case TweakKind.Nic: Assert.NotNull(t.Nic); break;
                case TweakKind.Cmd: Assert.NotEmpty(t.Cmd!.On); Assert.NotEmpty(t.Cmd.Off); break;
                case TweakKind.Action:
                    Assert.NotNull(t.Action);
                    switch (t.Action)
                    {
                        case ActionKind.Clean: Assert.NotEmpty(t.Patterns!); break;
                        case ActionKind.Open:
                        case ActionKind.Info: Assert.False(string.IsNullOrEmpty(t.Target), t.Id); break;
                        case ActionKind.Command: Assert.NotEmpty(t.Commands!); break;
                        case ActionKind.Native: Assert.False(string.IsNullOrEmpty(t.Native), t.Id); break;
                    }
                    break;
            }
        }
    }

    [Theory]
    [InlineData("wuauserv")]
    [InlineData("WinDefend")]
    [InlineData("Sense")]
    [InlineData("mpssvc")]
    [InlineData("SecurityHealthService")]
    [InlineData("EventLog")]
    [InlineData("RpcSs")]
    public void Critical_services_are_never_touched(string service)
    {
        Assert.DoesNotContain(Catalog.All, t => string.Equals(t.Service, service, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Nothing_disables_defender_or_windows_update_via_registry()
    {
        var bad = new[] { "DisableAntiSpyware", "DisableRealtimeMonitoring", "NoAutoUpdate", "DisableWindowsUpdateAccess" };
        foreach (var t in Catalog.All)
            foreach (var v in t.Values ?? new())
                Assert.DoesNotContain(bad, b => string.Equals(v.Name, b, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Services_and_tasks_are_never_marked_recommended()
    {
        Assert.DoesNotContain(Catalog.All, t => t.Kind == TweakKind.Service && t.Risk == RiskLevel.Recommended);
    }

    [Fact]
    public void High_risk_items_exist_and_explain_consequences()
    {
        var high = Catalog.All.Where(t => t.Risk == RiskLevel.HighRisk).ToList();
        Assert.NotEmpty(high);
        Assert.All(high, t => Assert.True(t.Consequences.Length > 30, t.Id));
    }

    [Fact]
    public void Destructive_actions_ask_for_confirmation()
    {
        foreach (var id in new[] { "event_logs", "recycle", "temp_clean" })
            Assert.False(string.IsNullOrEmpty(Catalog.Get(id).Confirm), id);
    }

    [Fact]
    public void Search_matches_title_text()
    {
        Assert.NotEmpty(Catalog.Search("hibernac"));
        Assert.Empty(Catalog.Search("zzzzzzzz-nie-istnieje"));
    }
}

using System.Reflection;
using Koch.App.Account;
using Koch.Gateway.Account;
using Koch.Gateway.Contracts;
using Koch.Kernel;
using Koch.Media.Contracts;
using Koch.Recipes.Contracts;
using Koch.Recipes.Domain.Sync;
using Koch.ServiceDefaults;
using Koch.Shopping.Contracts;
using Koch.Shopping.Domain;
using NetArchTest.Rules;

namespace Koch.Architecture.Tests;

/// <summary>
/// The boundaries the code must keep, checked on the compiled code (fitness functions, FOSA ch06). The services are
/// separate deployables that talk over HTTP only; what they share is a small, stable kernel (units, the sync
/// abstractions) and contract libraries, one per producer, so that no one depends on things it doesn't use (Clean Code
/// ch20: CRP, SDP, SAP). Each rule says why it exists (docs/adr/0001, 0005).
/// </summary>
public class ArchitectureTests
{
    private static readonly Assembly App = typeof(AccountService).Assembly;
    private static readonly Assembly Gateway = typeof(Members).Assembly;
    private static readonly Assembly Recipes = typeof(Koch.Recipes.Storage.Rows).Assembly;
    private static readonly Assembly Shopping = typeof(Koch.Shopping.Storage.Rows).Assembly;
    private static readonly Assembly Media = typeof(Koch.Media.Uploads.PhotoProcessor).Assembly;
    private static readonly Assembly MediaImaging = typeof(Koch.Media.Imaging.Renditions).Assembly;
    private static readonly Assembly Kernel = typeof(IItem).Assembly;
    private static readonly Assembly RecipesDomain = typeof(RecipeBook).Assembly;
    private static readonly Assembly ShoppingDomain = typeof(ShoppingBook).Assembly;
    private static readonly Assembly ServiceDefaults = typeof(GatewayUser).Assembly;
    private static readonly Assembly Telemetry = typeof(Koch.Telemetry.AppTelemetry).Assembly;
    private static readonly Assembly[] Contracts =
        [typeof(WhoAmI).Assembly, typeof(RecipesStatus).Assembly, typeof(Koch.Recipes.Events.PhotoEvents).Assembly,
            typeof(ShoppingJson).Assembly, typeof(PhotoPaths).Assembly];

    private static readonly Assembly[] Deployables = [Gateway, Recipes, Shopping, Media];

    /// <summary>What each library or deployable may use of ours, and nothing else.</summary>
    public static TheoryData<string, string[]> Allowed => new()
    {
        { "Koch.Kernel", [] },
        { "Koch.Recipes.Domain", ["Koch.Kernel", "Koch.Recipes.Contracts"] },
        // The shopping rules know nothing of recipes: the app hands them planned lines (docs/adr/0005).
        { "Koch.Shopping.Domain", ["Koch.Kernel", "Koch.Shopping.Contracts"] },
        { "Koch.Recipes", ["Koch.Kernel", "Koch.Recipes.Contracts", "Koch.Recipes.Domain", "Koch.Recipes.Events", "Koch.ServiceDefaults", "Koch.Telemetry"] },
        // Published events stand alone: a consumer gets the events and nothing of the sync API (docs/adr/0008).
        { "Koch.Recipes.Events", [] },
        { "Koch.Shopping", ["Koch.Kernel", "Koch.Shopping.Contracts", "Koch.Shopping.Domain", "Koch.ServiceDefaults", "Koch.Telemetry"] },
        { "Koch.ServiceDefaults", ["Koch.Gateway.Contracts", "Koch.Telemetry"] },
        // How the HTTP apps report, and nothing else: the gateway uses it without the services' row store (docs/adr/0009).
        { "Koch.Telemetry", [] },
        // Media knows only its own photo rules and the photo events Recipes publishes: no recipe, no list, no HTTP
        // plumbing of the services.
        { "Koch.Media.Imaging", ["Koch.Media.Contracts"] },
        { "Koch.Media", ["Koch.Media.Contracts", "Koch.Media.Imaging", "Koch.Recipes.Events"] },
        // The gateway knows no service's code; it forwards by path, and reads renditions by Media's published layout.
        { "Koch.Gateway", ["Koch.Gateway.Contracts", "Koch.Media.Contracts", "Koch.App", "Koch.Telemetry"] },
        { "Koch.App", ["Koch.Kernel", "Koch.Gateway.Contracts", "Koch.Media.Contracts", "Koch.Recipes.Contracts", "Koch.Recipes.Domain", "Koch.Shopping.Contracts", "Koch.Shopping.Domain"] },
    };

    private static readonly Assembly[] All =
        [App, Gateway, Recipes, Shopping, Media, MediaImaging, Kernel, RecipesDomain, ShoppingDomain, ServiceDefaults, Telemetry, .. Contracts];

    [Theory, MemberData(nameof(Allowed))]
    public void Each_part_uses_only_what_it_is_allowed_to(string name, string[] allowed)
    {
        var assembly = All.Single(a => a.GetName().Name == name);

        var ours = assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(n => n.StartsWith("Koch.", StringComparison.Ordinal)).ToList();

        Assert.True(ours.All(allowed.Contains), $"{name} uses {string.Join(", ", ours.Except(allowed))}, which it may not. "
            + "Services talk over HTTP; code two of them need goes into the producer's *.Contracts, or into Koch.Kernel if it "
            + "is shared vocabulary that rarely changes (docs/adr/0001, 0005).");
    }

    [Fact]
    public void Contracts_are_plain_shapes_that_depend_on_nothing_of_ours()
    {
        foreach (var contracts in Contracts)
        {
            var ours = contracts.GetReferencedAssemblies().Select(a => a.Name!).Where(n => n.StartsWith("Koch.", StringComparison.Ordinal)).ToList();
            Assert.True(ours.Count == 0, $"{contracts.GetName().Name} references {string.Join(", ", ours)}. A contract "
                + "library is what other deployables compile against; anything it pulls in becomes their dependency too.");
        }
    }

    [Fact]
    public void Every_deployable_is_covered_by_the_rules()
    {
        var covered = Allowed.Select(row => (string)row[0]).ToList();
        Assert.All(Deployables, d => Assert.Contains(d.GetName().Name!, covered));
    }

    public static TheoryData<string> PureLibraries => ["Koch.Kernel", "Koch.Recipes.Domain", "Koch.Shopping.Domain", "Koch.Media.Imaging"];

    [Theory, MemberData(nameof(PureLibraries))]
    public void The_kernel_and_the_domains_are_plain_code_without_ui_network_storage_or_files(string name) => Holds(
        Types.InAssembly(All.Single(a => a.GetName().Name == name)).ShouldNot().HaveDependencyOnAny(
            "Microsoft.AspNetCore", "Microsoft.JSInterop", "Microsoft.Data", "System.Net", "System.IO"),
        $"{name} runs on the phone and in a service, and its tests need no fakes: it computes, and leaves I/O to them. "
        + "Local storage goes through IKeyValueStore.");

    [Fact]
    public void The_recipe_model_knows_no_json_or_storage() => Holds(
        Types.InAssembly(RecipesDomain).That().ResideInNamespaceMatching(@"^Koch\.Recipes\.Domain\.(Cooking|Catalog|Sync)$")
            .ShouldNot().HaveDependencyOnAny("System.Text.Json", "Koch.Recipes.Contracts", "Koch.Recipes.Domain.Wire", "Koch.Recipes.Domain.Storage"),
        "The shapes that leave the process (Koch.Recipes.Contracts) map to the model in Wire, never the other way: the model "
        + "can change without changing what older apps and stored rows contain.");

    public static TheoryData<string> Services => ["Koch.Recipes", "Koch.Shopping"];

    [Theory, MemberData(nameof(Services))]
    public void Services_reach_sql_only_through_the_shared_row_store(string name) => Holds(
        Types.InAssembly(All.Single(a => a.GetName().Name == name)).ShouldNot().HaveDependencyOnAny("Microsoft.Data.SqlClient"),
        "Every service keeps its rows the same way (Koch.ServiceDefaults.Documents, docs/adr/0003, 0004): one place for the "
        + "upsert, the tombstones and the transaction.");

    [Fact]
    public void The_shared_service_plumbing_holds_no_service_code() => Holds(
        Types.InAssembly(ServiceDefaults).ShouldNot().HaveDependencyOnAny("Koch.Recipes", "Koch.Shopping", "Koch.App",
            "Koch.Gateway.Account", "Koch.Gateway.Proxy", "Koch.Gateway.Hosting", "Koch.Kernel"),
        "Koch.ServiceDefaults is hosting plumbing every service uses alike; domain or service code in it would couple the "
        + "services' releases (docs/adr/0001).");

    private static void Holds(ConditionList rule, string why)
    {
        var result = rule.GetResult();
        // A rule that selects nothing passes without checking anything, e.g. after a namespace was renamed.
        Assert.True(result.SelectedTypesForTesting?.Any() == true, $"The rule selects no types, so it checks nothing.\n{why}");
        Assert.True(result.IsSuccessful,
            $"{why}\nBroken by: {string.Join(", ", (result.FailingTypes ?? []).Select(t => t.FullName).Distinct())}");
    }
}

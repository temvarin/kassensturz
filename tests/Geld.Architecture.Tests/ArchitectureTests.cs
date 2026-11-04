using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Geld.App.Shell;
using Geld.Domain;
using Geld.Domain.Budgeting;
using Geld.Domain.Kindergeld;
using Geld.Server.Account;
using Microsoft.AspNetCore.Components;
using NetArchTest.Rules;

namespace Geld.Architecture.Tests;

/// <summary>
/// The structure the code must keep, checked on the compiled code: which part may use which. Each project is split
/// into modules by domain (Kindergeld, Budgeting, Sync, Backup, Account), plus what they all build on (SharedKernel,
/// Shell) and a composition root that puts them together. A rule written only in a README erodes one convenient import
/// at a time; each rule here says why it exists. Rules about single APIs (the clock) are checked by the compiler
/// instead: BannedSymbols.txt.
/// </summary>
public class ArchitectureTests
{
    private static readonly Assembly Domain = typeof(AppData).Assembly;
    private static readonly Assembly App = typeof(AppState).Assembly;
    private static readonly Assembly Server = typeof(EasyAuth).Assembly;

    // Domain library

    [Fact]
    public void The_domain_is_plain_code_without_ui_network_storage_or_files() => Holds(
        Types.InAssembly(Domain).ShouldNot().HaveDependencyOnAny(
            "Microsoft.AspNetCore", "Microsoft.JSInterop", "Microsoft.Data", "System.Net", "System.IO", "Geld.App", "Geld.Server"),
        "Geld.Domain runs in the browser and on the server, and its tests need no fakes: it computes, and leaves "
        + "I/O to the app and the server (README, Architecture).");

    [Fact]
    public void The_domain_model_knows_no_json_contracts_or_storage() => Holds(
        Types.InAssembly(Domain).That().DoNotResideInNamespaceMatching(@"^Geld\.Domain\.(Contracts|Storage)$")
            .ShouldNot().HaveDependencyOnAny("System.Text.Json", "Geld.Domain.Contracts", "Geld.Domain.Storage"),
        "The shapes that leave the process live in Geld.Domain.Contracts and map to the model, never the other way: the "
        + "model can then change freely, and a stored or sent format changes only when a contract record does "
        + "(tests/Geld.Domain.Tests/Contracts/README.md).");

    [Fact]
    public void The_domain_s_business_modules_know_nothing_of_one_another() => HoldsSliced(
        BusinessModules(), "Geld.Domain",
        "Kindergeld and Budgeting change for different reasons; what they share is in SharedKernel. Any module added "
        + "later is covered too.");

    [Fact]
    public void The_domain_s_business_modules_know_nothing_of_sync_or_of_how_they_are_stored() => Holds(
        BusinessModules().ShouldNot().HaveDependencyOnAny(
            "Geld.Domain.Sync", "Geld.Domain.Contracts", "Geld.Domain.Storage", "Geld.Domain.AppData"),
        "Sync is the mediator: it knows the modules through its adapters (KindergeldSync, BudgetSync), and they know nothing of it. "
        + "AppData composes them all, so none of them may know it.");

    [Fact]
    public void The_sync_knows_the_modules_but_not_how_anything_is_stored_or_sent() => Holds(
        Types.InAssembly(Domain).That().ResideInNamespaceMatching(@"^Geld\.Domain\.Sync$")
            .ShouldNot().HaveDependencyOnAny("Geld.Domain.Contracts", "Geld.Domain.Storage", "Geld.Domain.AppData"),
        "The sync's algorithm (changes, rebase) is plain code; the JSON of its messages is a contract.");

    [Fact]
    public void The_shared_kernel_knows_no_module() => Holds(
        Types.InAssembly(Domain).That().ResideInNamespaceMatching(@"^Geld\.Domain\.SharedKernel$")
            .Should().OnlyHaveDependencyOn("System", "Geld.Domain.SharedKernel"),
        "Every module builds on the shared kernel; if it knew one of them, they would all depend on that one.");

    [Fact]
    public void The_domain_s_state_changes_only_through_its_methods()
    {
        // Records may have init: they are values, set once.
        var settable = Domain.GetExportedTypes()
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(p => p.SetMethod is { IsPublic: true } set
                    && !set.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit)))
                .Select(p => $"{type.FullName}.{p.Name}"));

        Assert.True(!settable.Any(),
            "The domain keeps its rules (positive amounts, valid days, sorted lists) only if state changes through its "
            + $"methods (README, Engineering highlights). Public setters: {string.Join(", ", settable)}");
    }

    // App

    [Fact]
    public void The_app_s_modules_know_nothing_of_one_another() => HoldsSliced(
        AppModules(), "Geld.App",
        "A module's pages and services change together, apart from the others'. What two modules both need goes into "
        + "the Shell; what a page shows from another module (the banners) is registered at the composition root "
        + "(ServiceRegistration, PageBanners). Any module added later is covered too.");

    [Fact]
    public void Only_the_composition_root_knows_the_app_s_modules() => Holds(
        Types.InAssembly(App).That().ResideInNamespaceMatching(@"^Geld\.App\.Shell$")
            .ShouldNot().HaveDependencyOnAny([.. AppModuleNamespaces(), .. AppRoot()]),
        "Every module builds on the Shell; if the Shell knew one of them, they would all depend on it.");

    [Fact]
    public void The_app_s_modules_know_nothing_of_the_composition_root() => Holds(
        AppModules().ShouldNot().HaveDependencyOnAny(AppRoot()),
        "The root (layout, settings page, service registration) puts the modules together and knows them all; a module "
        + "that knew the root would know every other module through it.");

    [Fact]
    public void The_ui_reaches_storage_network_and_javascript_only_through_services() => Holds(
        Ui().ShouldNot().HaveDependencyOnAny("Geld.Domain.Storage", "Geld.Domain.Contracts",
            "Geld.App.Shell.LocalStorage", "System.Net.Http", "Microsoft.JSInterop"),
        "Components get storage, the stored and sent formats, the server and the browser from services, which tests "
        + "replace with fakes (AppTestContext). Browser features go into Shell/Browser.cs and wwwroot/browser.js.");

    [Fact]
    public void The_ui_knows_its_area_s_service_not_the_whole_data() => Holds(
        Ui().ShouldNot().HaveDependencyOnAny("Geld.Domain.AppData"),
        "Components read and change the data through the service of their area (KindergeldService, BudgetService, "
        + "BackupService, SyncService), not by walking AppData: a change to its shape must not reach every page (Law "
        + "of Demeter), and every change must go through AppState.Change, which saves it.");

    [Fact]
    public void The_ui_reads_the_pot_and_the_budget_only_through_read_only_views() => Holds(
        Ui().ShouldNot().HaveDependencyOnAny(typeof(KindergeldPot).FullName!, typeof(Budget).FullName!),
        "The services hand out the pot and the budget as IReadOnlyKindergeldPot and IReadOnlyBudget, so the compiler "
        + "stops a component from changing them: such a change would show without being saved or synced. Changing them "
        + "anyway would take a cast to the class, which this rule catches.");

    [Fact]
    public void An_app_module_uses_only_its_own_part_of_the_domain()
    {
        // Dependencies between the app's namespaces are the slice rule's; this one covers the way round it, through the
        // domain: the Kindergeld pages and service may not use the budget, and so on. Matched by name, so a module
        // added on both sides is covered without a new rule.
        var domainModules = DomainBusinessModules();
        var appModules = AppModuleNamespaces();
        Assert.True(appModules.Count(m => domainModules.Contains(Module(m))) >= 2,
            "Fewer than two app modules match a business module of the domain by name, so the rule checks nothing.");
        foreach (var module in appModules)
            Holds(Types.InAssembly(App).That().ResideInNamespaceMatching($"^{Regex.Escape(module)}$")
                    .ShouldNot().HaveDependencyOnAny([.. domainModules.Where(d => d != Module(module)).Select(d => $"Geld.Domain.{d}")]),
                $"{module} uses only its own part of the domain: the modules change for different reasons, and a "
                + "Kindergeld page that changed the budget would pass every other rule.");
    }

    // Server

    [Fact]
    public void The_server_s_modules_know_nothing_of_one_another() => HoldsSliced(
        Types.InAssembly(Server).That().ResideInNamespaceMatching(@"^Geld\.Server\..+$"), "Geld.Server",
        "Sign-in (Account), sync (Sync) and the browser protections (Hosting) change for different reasons. Program.cs "
        + "puts them together, e.g. the members-only rule on the sync's route.");

    [Fact]
    public void The_server_uses_no_code_of_the_app() => Holds(
        Types.InAssembly(Server).ShouldNot().HaveDependencyOnAny("Geld.App"),
        "Geld.Server references the app only to serve its files. Code both need belongs in Geld.Domain.");

    // Selections

    // The modules with business rules: everything but the shared kernel, the sync, the contracts, storage and AppData.
    private static PredicateList BusinessModules() => Types.InAssembly(Domain).That()
        .DoNotResideInNamespaceMatching(@"^Geld\.Domain(\.(SharedKernel|Sync|Contracts|Storage))?$");

    // The app's modules: everything but the Shell and the composition root.
    private static PredicateList AppModules() => Types.InAssembly(App).That()
        .DoNotResideInNamespaceMatching(@"^Geld\.App(\.Shell)?$");

    private static string[] AppModuleNamespaces() =>
        [.. App.GetTypes().Select(t => t.Namespace).OfType<string>().Where(ns => ns.Count(c => c == '.') == 2
            && ns.StartsWith("Geld.App.", StringComparison.Ordinal) && ns != "Geld.App.Shell").Distinct()];

    // The domain's business modules by name ("Kindergeld", "Budgeting"): its namespaces but the shared ones.
    private static string[] DomainBusinessModules() =>
        [.. Domain.GetTypes().Select(t => t.Namespace).OfType<string>()
            .Where(ns => Regex.IsMatch(ns, @"^Geld\.Domain\.[^.]+$")
                && !Regex.IsMatch(ns, @"^Geld\.Domain\.(SharedKernel|Sync|Contracts|Storage)$"))
            .Select(Module).Distinct()];

    private static string Module(string ns) => ns[(ns.LastIndexOf('.') + 1)..];

    // The composition root's types: the router, the layout, the settings page, the service registration.
    private static string[] AppRoot() =>
        [.. App.GetTypes().Where(t => t.Namespace == "Geld.App" && !t.IsNested).Select(t => t.FullName!)];

    // Every component: pages, sections, sheets, banners, the layout. Found by reflection, which follows inheritance:
    // NetArchTest's ImplementInterface sees only interfaces a class declares itself, and almost every component gets
    // IComponent from ComponentBase or DataComponent.
    private static readonly HashSet<string> Components =
        [.. App.GetTypes().Where(t => typeof(IComponent).IsAssignableFrom(t)).Select(t => t.FullName!)];

    private static PredicateList Ui() => Types.InAssembly(App).That().MeetCustomRule(type => Components.Contains(type.FullName));

    private static void Holds(ConditionList rule, string why) => Holds(rule.GetResult(), why);

    // One slice per namespace below the prefix; no slice may use another. Slice results don't say what was selected,
    // so the guard against checking nothing looks at the selection itself: at least two modules to keep apart.
    private static void HoldsSliced(PredicateList modules, string prefix, string why)
    {
        var namespaces = modules.GetTypes().Select(t => t.FullName.Split('/')[0]).Select(n => n[..n.LastIndexOf('.')]).Distinct().Count();
        Assert.True(namespaces >= 2, $"Only {namespaces} module(s) below {prefix}, so the rule checks nothing.\n{why}");
        var result = modules.Slice().ByNamespacePrefix(prefix).Should().NotHaveDependenciesBetweenSlices().GetResult();
        Assert.True(result.IsSuccessful, $"{why}\nBroken by: {string.Join(", ", Breaking(result))}");
    }

    private static void Holds(TestResult result, string why)
    {
        // A rule that selects nothing passes without checking anything, e.g. after a namespace was renamed.
        Assert.True(result.SelectedTypesForTesting?.Any() == true, $"The rule selects no types, so it checks nothing.\n{why}");
        Assert.True(result.IsSuccessful, $"{why}\nBroken by: {string.Join(", ", Breaking(result))}");
    }

    // The types that break a rule. Lambdas and async methods compile to nested types: they count as the type they're in.
    private static string[] Breaking(TestResult result) =>
        [.. (result.FailingTypes ?? []).Select(t => t.FullName.Split('/', '+')[0]).Distinct().Order(StringComparer.Ordinal)];
}

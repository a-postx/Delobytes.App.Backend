using System;
using System.IO;
using System.Linq;
using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.Fluent.Syntax.Elements.Types;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Delobytes.App.Backend.Tests.ArchTests;

/// <summary>
/// Enforces module boundaries: a module may reference another module only through its
/// Contracts assembly. Anything under another module's Api/Application/Domain/Infrastructure
/// (or its EF Core mappings and domain entities) is a violation.
/// </summary>
public class ModuleBoundaryTests
{
    private const string RootNamespace = "Delobytes.App.Backend";

    private static readonly string[] ModuleNames = { "Catalog", "Finance", "Identity", "Integrations", "Sales" };

    private static readonly Architecture Model = new ArchLoader()
        .LoadAssemblies(ProductAssemblies())
        .Build();

    [Fact]
    public void ModuleInternals_DoNotDependOnOtherModules()
    {
        foreach (string module in ModuleNames)
        {
            string[] others = ModuleNames.Where(m => m != module).ToArray();

            IArchRule rule = Types().That().ResideInNamespaceMatching(InternalsPattern(module))
                .Should().NotDependOnAny(Types().That().ResideInNamespaceMatching(InternalsPattern(others)))
                .Because($"{module} may only reach another module through its Contracts assembly");

            rule.Check(Model);
        }
    }

    [Fact]
    public void ModuleContracts_DoNotDependOnOtherModules()
    {
        foreach (string module in ModuleNames)
        {
            string[] others = ModuleNames.Where(m => m != module).ToArray();

            GivenTypesConjunction contractsTypes = Types().That()
                .ResideInNamespaceMatching($@"^{RootNamespace}\.{module}\.Contracts(\..*)?$");

            // Пустой проект Contracts (например, пока без единого типа) не должен ронять правило:
            // ArchUnitNET считает правило без совпавших типов невыполненным.
            if (!contractsTypes.GetObjects(Model).Any())
            {
                continue;
            }

            IArchRule rule = contractsTypes
                .Should().NotDependOnAny(Types().That().ResideInNamespaceMatching(InternalsPattern(others)))
                .Because($"{module} Contracts must stay a public contract and not leak another module's internals");

            rule.Check(Model);
        }
    }

    [Fact]
    public void SharedProjects_DoNotDependOnModules()
    {
        IObjectProvider<IType> moduleInternals = Types().That().ResideInNamespaceMatching(InternalsPattern(ModuleNames));
        IObjectProvider<IType> moduleContracts = Types().That().ResideInNamespaceMatching(ModuleContractsPattern(ModuleNames));

        string[] sharedPatterns =
        {
            $@"^{RootNamespace}\.Contracts(\..*)?$",
            $@"^{RootNamespace}\.Persistence(\..*)?$",
        };

        foreach (string pattern in sharedPatterns)
        {
            IArchRule internalsRule = Types().That().ResideInNamespaceMatching(pattern)
                .Should().NotDependOnAny(moduleInternals)
                .Because("shared projects must not depend on modules; modules depend on them");

            IArchRule contractsRule = Types().That().ResideInNamespaceMatching(pattern)
                .Should().NotDependOnAny(moduleContracts)
                .Because("module Contracts already reference the shared projects, so the reverse edge would be a cycle");

            internalsRule.Check(Model);
            contractsRule.Check(Model);
        }
    }

    private static string InternalsPattern(params string[] modules)
        => $@"^{RootNamespace}\.({string.Join("|", modules)})\.(Api|Application|Domain|Infrastructure)(\..*)?$";

    private static string ModuleContractsPattern(params string[] modules)
        => $@"^{RootNamespace}\.({string.Join("|", modules)})\.Contracts(\..*)?$";

    private static System.Reflection.Assembly[] ProductAssemblies()
    {
        return Directory.GetFiles(AppContext.BaseDirectory, $"{RootNamespace}*.dll")
            .Select(System.Reflection.Assembly.LoadFrom)
            .ToArray();
    }
}

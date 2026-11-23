using System.Reflection;
using Delobytes.App.Backend.Catalog.Application.Commands.Products;
using Delobytes.App.Backend.Contracts.Authorization;
using Delobytes.App.Backend.Finance.Application.Commands;
using Delobytes.App.Backend.Identity.Application.Commands;
using Delobytes.App.Backend.Integrations.Application.Commands;
using Delobytes.App.Backend.Sales.Application.Queries.Orders;
using FluentAssertions;
using MediatR;

namespace Delobytes.App.Backend.Tests.ArchTests;

/// <summary>
/// Guards the rule that every MediatR command either declares required roles
/// through <see cref="IRequireRole"/> or is deliberately allowlisted as public,
/// self-service or internal.
/// </summary>
public class CommandAuthorizationConventionTests
{
    private static readonly HashSet<string> AllowedWithoutRole = new(StringComparer.Ordinal)
    {
        // Internal / not exposed over HTTP: scheduler-only or currently unreferenced.
        nameof(StartOrdersImportCommand),
        nameof(CancelProductsImportCommand),

        // Anonymous or self-service endpoints; access is checked by the handler itself.
        nameof(LoginCommand),
        nameof(RegisterCommand),
        nameof(YandexCallbackCommand),
        nameof(GoogleCallbackCommand),
        nameof(ChangePasswordCommand),
        nameof(CreateTenantCommand),
        nameof(SwitchTenantCommand),
        nameof(AcceptInvitationCommand),
    };

    private static readonly Assembly[] ApplicationAssemblies =
    {
        typeof(CreateProductCommand).Assembly,
        typeof(StartJobCommand).Assembly,
        typeof(LoginCommand).Assembly,
        typeof(ClosePnlExpectationCommand).Assembly,
        typeof(RepublishOrderLinesCommand).Assembly,
    };

    [Fact]
    public void EveryCommand_RequiresRoleOrIsExplicitlyAllowlisted()
    {
        // Arrange
        List<Type> commands = DiscoverCommands();

        // Act
        string[] unprotected = commands
            .Where(type => !typeof(IRequireRole).IsAssignableFrom(type))
            .Where(type => !AllowedWithoutRole.Contains(type.Name))
            .Select(type => type.FullName!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        // Assert
        commands.Should().NotBeEmpty("the convention must actually scan command types");
        unprotected.Should().BeEmpty(
            "every command must implement IRequireRole or be added to the explicit allowlist");
    }

    [Fact]
    public void Allowlist_ContainsOnlyExistingCommands()
    {
        // Arrange
        HashSet<string> commandNames = DiscoverCommands()
            .Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);

        // Act
        string[] staleEntries = AllowedWithoutRole
            .Except(commandNames)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        // Assert
        staleEntries.Should().BeEmpty(
            "stale allowlist entries hide renamed or deleted commands");
    }

    private static List<Type> DiscoverCommands()
    {
        return ApplicationAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(IsCommand)
            .Distinct()
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsCommand(Type type)
    {
        if (!type.IsClass || type.IsAbstract || !type.Name.EndsWith("Command", StringComparison.Ordinal))
        {
            return false;
        }

        return type.GetInterfaces().Any(IsRequestInterface);
    }

    private static bool IsRequestInterface(Type type)
    {
        return type == typeof(IRequest)
            || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IRequest<>));
    }
}

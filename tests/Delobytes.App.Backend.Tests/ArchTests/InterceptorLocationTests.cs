using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Delobytes.App.Backend.Tests.ArchTests;

/// <summary>
/// Keeps every SaveChanges interceptor in the shared Persistence project. A module-local copy
/// compiles and passes its own tests while quietly drifting from the shared one.
/// </summary>
public class InterceptorLocationTests
{
    private const string RootNamespace = "Delobytes.App.Backend";

    private const string SharedPersistenceNamespace = RootNamespace + ".Persistence";

    [Fact]
    public void SaveChangesInterceptors_ResideInSharedPersistence()
    {
        Type[] interceptorTypes = ProductAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => typeof(SaveChangesInterceptor).IsAssignableFrom(type) && !type.IsAbstract)
            .ToArray();

        interceptorTypes.Should().NotBeEmpty();
        interceptorTypes.Should().OnlyContain(type =>
            type.Namespace == SharedPersistenceNamespace
            || (type.Namespace != null && type.Namespace.StartsWith(SharedPersistenceNamespace + ".", StringComparison.Ordinal)),
            "an interceptor outside the shared build block belongs to exactly one module and will drift");
    }

    private static Assembly[] ProductAssemblies()
    {
        return Directory.GetFiles(AppContext.BaseDirectory, $"{RootNamespace}*.dll")
            .Where(path => !Path.GetFileName(path).Contains(".Tests", StringComparison.Ordinal))
            .Select(Assembly.LoadFrom)
            .ToArray();
    }
}

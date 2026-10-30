using System;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Services;

public class JobRunExecutionContextScopeTests
{
    [Fact]
    public void EnterScope_SetsValueForTheScopeLifetime()
    {
        JobRunExecutionContext context = new JobRunExecutionContext();
        Guid jobRunId = Guid.NewGuid();

        using (context.EnterScope(jobRunId))
        {
            context.JobRunId.Should().Be(jobRunId);
        }

        context.JobRunId.Should().BeNull();
    }

    [Fact]
    public void EnterScope_NoOuterRun_RestoresNull()
    {
        JobRunExecutionContext context = new JobRunExecutionContext();

        IDisposable scope = context.EnterScope(Guid.NewGuid());
        scope.Dispose();

        context.JobRunId.Should().BeNull();
    }

    [Fact]
    public void EnterScope_Nested_OuterScopeSurvivesInnerDispose()
    {
        JobRunExecutionContext context = new JobRunExecutionContext();
        Guid outer = Guid.NewGuid();
        Guid inner = Guid.NewGuid();

        using (context.EnterScope(outer))
        {
            using (context.EnterScope(inner))
            {
                context.JobRunId.Should().Be(inner);
            }

            // Restoring null instead of the previous value would let an inner scope erase the run
            // that is still being processed.
            context.JobRunId.Should().Be(outer);
        }

        context.JobRunId.Should().BeNull();
    }

    [Fact]
    public void EnterScope_DisposedTwice_KeepsOuterValue()
    {
        JobRunExecutionContext context = new JobRunExecutionContext();
        Guid outer = Guid.NewGuid();

        using (context.EnterScope(outer))
        {
            IDisposable inner = context.EnterScope(Guid.NewGuid());
            inner.Dispose();
            inner.Dispose();

            context.JobRunId.Should().Be(outer);
        }
    }

    [Fact]
    public void EnterScope_ReplacesExplicitSetCurrentValue()
    {
        JobRunExecutionContext context = new JobRunExecutionContext();
        Guid explicitId = Guid.NewGuid();
        Guid scopedId = Guid.NewGuid();

        context.SetCurrent(explicitId);

        using (context.EnterScope(scopedId))
        {
            context.JobRunId.Should().Be(scopedId);
        }

        context.JobRunId.Should().Be(explicitId);

        context.Clear();
        context.JobRunId.Should().BeNull();
    }
}

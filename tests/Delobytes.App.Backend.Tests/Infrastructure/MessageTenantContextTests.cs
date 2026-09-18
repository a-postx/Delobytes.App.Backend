using Delobytes.App.Backend.Services;
using FluentAssertions;

namespace Delobytes.App.Backend.Tests.Messaging;

public class MessageTenantContextTests
{
    [Fact]
    public void TenantId_WhenNothingSet_IsNull()
    {
        MessageTenantContext context = new MessageTenantContext();

        context.TenantId.Should().BeNull();
    }

    [Fact]
    public void SetTenantId_WithValue_TenantIdReturnsSameValue()
    {
        Guid tenantId = Guid.NewGuid();
        MessageTenantContext context = new MessageTenantContext();

        context.SetTenantId(tenantId);

        context.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public void Clear_AfterSetTenantId_TenantIdReturnsNull()
    {
        MessageTenantContext context = new MessageTenantContext();
        context.SetTenantId(Guid.NewGuid());

        context.Clear();

        context.TenantId.Should().BeNull();
    }

    [Fact]
    public void SetTenantId_Null_TenantIdReturnsNull()
    {
        MessageTenantContext context = new MessageTenantContext();
        context.SetTenantId(Guid.NewGuid());

        context.SetTenantId(null);

        context.TenantId.Should().BeNull();
    }

    [Fact]
    public void SetTenantId_CalledMultipleTimes_LastValueWins()
    {
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        MessageTenantContext context = new MessageTenantContext();

        context.SetTenantId(first);
        context.SetTenantId(second);

        context.TenantId.Should().Be(second);
    }

    [Fact]
    public async Task TenantId_IsIsolatedBetweenConcurrentTasks()
    {
        // AsyncLocal should not let one task's SetTenantId bleed into another task
        MessageTenantContext context = new MessageTenantContext();
        Guid tenant1 = Guid.NewGuid();
        Guid tenant2 = Guid.NewGuid();

        Guid? capturedInTask1 = null;
        Guid? capturedInTask2 = null;

        // Semaphores ensure task1 captures its value AFTER task2 has already called SetTenantId,
        // proving isolation: task2's write must not mutate task1's execution context.
        SemaphoreSlim task2HasSet = new SemaphoreSlim(0, 1);
        SemaphoreSlim task1HasCaptured = new SemaphoreSlim(0, 1);

        Task task1 = Task.Run(async () =>
        {
            context.SetTenantId(tenant1);
            await task2HasSet.WaitAsync();
            capturedInTask1 = context.TenantId;
            task1HasCaptured.Release();
        });

        Task task2 = Task.Run(() =>
        {
            context.SetTenantId(tenant2);
            capturedInTask2 = context.TenantId;
            task2HasSet.Release();
        });

        await Task.WhenAll(task1, task2);
        await task1HasCaptured.WaitAsync();

        capturedInTask1.Should().Be(tenant1, "task2's SetTenantId must not affect task1's execution context");
        capturedInTask2.Should().Be(tenant2);
    }
}

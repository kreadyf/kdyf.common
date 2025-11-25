using kdyf.Operations.Extensions;
using kdyf.Operations.Integration;
using kdyf.Operations.Test.MockOperations;
using kdyf.Operations.Test.Models;
using kdyf.Common.Test.Base;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using static kdyf.Operations.Test.MockOperations.SequencialOperationA;
using static kdyf.Operations.Test.MockOperations.SequencialOperationB;
using static kdyf.Operations.Test.MockOperations.SequencialOperationC;
using static kdyf.Operations.Test.MockOperations.SequencialOperationD;

namespace kdyf.Operations.Test;

/// <summary>
/// Tests to verify that errors are properly captured in the execution tree.
/// When an operation fails, GetExecutionTree() should return the error details.
/// </summary>
[TestClass]
public sealed class ErrorTreeTests : KdyfTestBase
{
    protected override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddKdyfOperations(typeof(ErrorTreeTests).Assembly);
    }

    /// <summary>
    /// Demonstrates that when an operation throws an error,
    /// the error is captured in the tree and can be retrieved with GetExecutionTree().
    /// </summary>
    [TestMethod]
    public async Task GetExecutionTree_AfterOperationFails_ShouldContainErrorDetails()
    {
        // Arrange
        var opExec = ServiceProvider!.CreateCommonOperationExecutor<InputOutput>();
        opExec
            .Add<SequencialOperationA, ISequencialAInOut>()  // ✅ Will succeed
            .Add<SequencialOperationB, ISequencialBInOut>()  // ✅ Will succeed
            .Add<SequencialOperationD, ISequencialDInOut>()  // ❌ Will FAIL
            .Add<SequencialOperationC, ISequencialCInOut>(); // ⏭️ Will NOT execute

        var input = new InputOutput { A = 1, B = 0, D = 6 }; // D=6 will become 7 and throw
        using var cts = new CancellationTokenSource();

        // Act - Execute and catch the exception
        try
        {
            await opExec.ExecuteAsync(input, cts.Token);
            Assert.Fail("Should have thrown exception");
        }
        catch (InvalidOperationException)
        {
            // Expected - error should be thrown
        }

        // Get the execution tree AFTER the error
        var tree = opExec.GetExecutionTree();

        // Assert - Verify error is in the tree
        Assert.IsTrue(tree.Count >= 4, "Tree should contain all 4 operations");

        // Find the failed operation
        var failedOperation = tree.FirstOrDefault(op => op.Status == OperationState.Faulted);
        Assert.IsNotNull(failedOperation, "Tree should contain the faulted operation");

        // Verify error details are captured
        Assert.IsNotNull(failedOperation.Error, "Faulted operation should have error details");
        Assert.AreEqual("Invalid Input", failedOperation.Error.Message, "Error message should be preserved");
        Assert.AreEqual(OperationState.Faulted, failedOperation.Status, "Status should be Faulted");
        Assert.IsNull(failedOperation.Completed, "Faulted operation should NOT have Completed timestamp");
        Assert.IsNotNull(failedOperation.Started, "Faulted operation should have Started timestamp");

        // Verify successful operations are also in the tree
        var completedOperations = tree.Where(op => op.Status == OperationState.Completed).ToList();
        Assert.AreEqual(2, completedOperations.Count, "Should have 2 completed operations (A and B)");

        // Verify pending operations (never started) are in the tree
        var pendingOperations = tree.Where(op => op.Status == OperationState.Pending).ToList();
        Assert.AreEqual(1, pendingOperations.Count, "Should have 1 pending operation (C - never started)");

        Console.WriteLine("\n=== EXECUTION TREE AFTER ERROR ===");
        foreach (var op in tree)
        {
            Console.WriteLine($"Operation: {op.Name}");
            Console.WriteLine($"  Status: {op.Status}");
            Console.WriteLine($"  Started: {op.Started?.ToString("HH:mm:ss.fff") ?? "N/A"}");
            Console.WriteLine($"  Completed: {op.Completed?.ToString("HH:mm:ss.fff") ?? "N/A"}");
            if (op.Error != null)
            {
                Console.WriteLine($"  ❌ ERROR: {op.Error.Message}");
                Console.WriteLine($"  Exception Type: {op.Error.GetType().Name}");
            }
            Console.WriteLine();
        }
    }

    /// <summary>
    /// Demonstrates that you can subscribe to status changes to log errors in real-time.
    /// This is useful for logging/monitoring systems.
    /// </summary>
    [TestMethod]
    public async Task OnExecutionStatusChanged_WhenOperationFails_ShouldReceiveErrorEvent()
    {
        // Arrange
        var opExec = ServiceProvider!.CreateCommonOperationExecutor<InputOutput>();
        opExec.Add<SequencialOperationD, ISequencialDInOut>();

        // Subscribe to status changes (like a logger would)
        var statusChanges = new List<(OperationState Status, string? ErrorMessage)>();
        opExec.OnExecutionStatusChanged += (status) =>
        {
            statusChanges.Add((status.Status, status.Error?.Message));

            // This is where you would log in real code:
            if (status.Status == OperationState.Faulted && status.Error != null)
            {
                Console.WriteLine($"[ERROR] Operation '{status.Name}' failed: {status.Error.Message}");
            }
        };

        var input = new InputOutput { D = 6 };
        using var cts = new CancellationTokenSource();

        // Act
        try
        {
            await opExec.ExecuteAsync(input, cts.Token);
        }
        catch (InvalidOperationException)
        {
            // Expected
        }

        // Assert - Verify we received the error event
        var faultedEvent = statusChanges.FirstOrDefault(s => s.Status == OperationState.Faulted);
        Assert.AreNotEqual(default, faultedEvent, "Should have received a Faulted status event");
        Assert.AreEqual("Invalid Input", faultedEvent.ErrorMessage, "Event should contain error message");

        // Verify we can also get it from the tree
        var tree = opExec.GetExecutionTree();
        var faultedOp = tree.First();
        Assert.AreEqual(OperationState.Faulted, faultedOp.Status);
        Assert.AreEqual("Invalid Input", faultedOp.Error?.Message);
    }


    /// <summary>
    /// Demonstrates that multiple errors in sequence are all captured.
    /// </summary>
    [TestMethod]
    public async Task GetExecutionTree_AfterMultipleExecutions_ShouldShowHistoryOfErrors()
    {
        // Arrange
        var opExec = ServiceProvider!.CreateCommonOperationExecutor<InputOutput>();
        opExec.Add<SequencialOperationD, ISequencialDInOut>();

        using var cts = new CancellationTokenSource();

        // Act - Execute multiple times, some with errors
        // First execution - success
        var input1 = new InputOutput { D = 1 };
        var result1 = await opExec.ExecuteAsync(input1, cts.Token);
        var tree1 = opExec.GetExecutionTree();
        Assert.AreEqual(OperationState.Completed, tree1.First().Status);

        // Second execution - will fail
        var opExec2 = ServiceProvider!.CreateCommonOperationExecutor<InputOutput>();
        opExec2.Add<SequencialOperationD, ISequencialDInOut>();
        var input2 = new InputOutput { D = 6 };
        try
        {
            await opExec2.ExecuteAsync(input2, cts.Token);
            Assert.Fail("Should have thrown");
        }
        catch (InvalidOperationException)
        {
            // Expected
        }

        // Get tree after error
        var tree2 = opExec2.GetExecutionTree();

        // Assert - Second execution tree should show the error
        Assert.AreEqual(OperationState.Faulted, tree2.First().Status);
        Assert.IsNotNull(tree2.First().Error);
        Assert.AreEqual("Invalid Input", tree2.First().Error.Message);

        Console.WriteLine("\n=== EXECUTION 1 (Success) ===");
        Console.WriteLine($"Status: {tree1.First().Status}");
        Console.WriteLine($"Error: {tree1.First().Error?.Message ?? "None"}");

        Console.WriteLine("\n=== EXECUTION 2 (Failed) ===");
        Console.WriteLine($"Status: {tree2.First().Status}");
        Console.WriteLine($"Error: {tree2.First().Error?.Message ?? "None"}");
    }

    /// <summary>
    /// Demonstrates that ExecutionStatus with errors can be serialized to JSON.
    /// This test verifies that the SerializableError property is populated and
    /// the Exception property is ignored during serialization.
    /// </summary>
    [TestMethod]
    public async Task GetExecutionTree_WithError_ShouldBeSerializableToJson()
    {
        // Arrange
        var opExec = ServiceProvider!.CreateCommonOperationExecutor<InputOutput>();
        opExec.Add<SequencialOperationD, ISequencialDInOut>();

        var input = new InputOutput { D = 6 }; // Will throw exception
        using var cts = new CancellationTokenSource();

        // Act - Execute and catch the exception
        try
        {
            await opExec.ExecuteAsync(input, cts.Token);
            Assert.Fail("Should have thrown exception");
        }
        catch (InvalidOperationException)
        {
            // Expected
        }

        // Get the execution tree
        var tree = opExec.GetExecutionTree();
        var failedOperation = tree.First(op => op.Status == OperationState.Faulted);

        // Assert - Verify both Error and SerializableError are populated
        Assert.IsNotNull(failedOperation.Error, "Exception Error should be populated");
        Assert.IsNotNull(failedOperation.SerializableError, "SerializableError should be populated");
        Assert.AreEqual(failedOperation.Error.Message, failedOperation.SerializableError.Message, "Messages should match");
        Assert.AreEqual(failedOperation.Error.GetType().FullName, failedOperation.SerializableError.Type, "Type should match");

        // Serialize to JSON - this should NOT throw
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
        };

        string json = null!;
        try
        {
            json = JsonSerializer.Serialize(tree, options);
            Assert.IsNotNull(json, "Serialization should succeed");
        }
        catch (Exception ex)
        {
            Assert.Fail($"Serialization failed: {ex.Message}");
        }

        Console.WriteLine("\n=== SERIALIZED JSON ===");
        Console.WriteLine(json);

        // Verify JSON contains SerializableError but NOT the Exception object
        Assert.IsTrue(json.Contains("SerializableError"), "JSON should contain SerializableError");
        Assert.IsTrue(json.Contains("Invalid Input"), "JSON should contain error message");
        Assert.IsFalse(json.Contains("\"Error\""), "JSON should NOT contain Error property (it's JsonIgnored)");

        // Deserialize using OperationExecutionStatus (without Operation property)
        var deserialized = JsonSerializer.Deserialize<List<OperationExecutionStatus>>(json, options);
        Assert.IsNotNull(deserialized, "Deserialization should succeed");

        var deserializedFailed = deserialized.First(op => op.Status == OperationState.Faulted);
        Assert.IsNotNull(deserializedFailed.SerializableError, "Deserialized SerializableError should exist");
        Assert.AreEqual("Invalid Input", deserializedFailed.SerializableError.Message, "Deserialized message should match");
        Assert.IsNull(deserializedFailed.Error, "Deserialized Error should be null (not serialized)");

        Console.WriteLine("\n=== DESERIALIZATION SUCCESS ===");
        Console.WriteLine($"Error Message: {deserializedFailed.SerializableError.Message}");
        Console.WriteLine($"Error Type: {deserializedFailed.SerializableError.Type}");
        Console.WriteLine($"Has StackTrace: {!string.IsNullOrEmpty(deserializedFailed.SerializableError.StackTrace)}");
    }
}

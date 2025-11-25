using kdyf.Operations.Extensions;
using kdyf.Operations.Integration;
using kdyf.Operations.Test.MockOperations;
using kdyf.Operations.Test.Models;
using kdyf.Common.Test.Base;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static kdyf.Operations.Test.MockOperations.ProducerOperation;
using static kdyf.Operations.Test.MockOperations.SequencialOperationA;
using static kdyf.Operations.Test.MockOperations.SequencialOperationB;
using static kdyf.Operations.Test.MockOperations.SequencialOperationC;
using static kdyf.Operations.Test.MockOperations.SequencialOperationD;

namespace kdyf.Operations.Test;

/// <summary>
/// Comprehensive unit tests for the Operation Executor system.
/// </summary>
[TestClass]
public sealed class OperationExecutorTests : KdyfTestBase
{
    protected override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Register the operations system with the test assembly to discover mock operations
        services.AddKdyfOperations(typeof(OperationExecutorTests).Assembly);
    }

    #region Simple Sequential Operations Tests

    /// <summary>
    /// Tests basic sequential execution of operations A -> B -> C.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_ShouldRunSequentialOperations_InCorrectOrder()
    {
        // Arrange
        var opExec = ServiceProvider!.CreateCommonOperationExecutor<InputOutput>();
        opExec
            .Add<SequencialOperationA, ISequencialAInOut>()
            .Add<SequencialOperationB, ISequencialBInOut>()
            .Add<SequencialOperationC, ISequencialCInOut>();

        var input = new InputOutput { A = 1, B = 2, C = 3 };
        using var cts = new CancellationTokenSource();

        // Act
        var result = await opExec.ExecuteAsync(input, cts.Token);

        // Assert
        Assert.AreEqual(2, result.A, "Operation A should increment A from 1 to 2");
        Assert.AreEqual(3, result.B, "Operation B should increment B from 2 to 3");
        Assert.AreEqual(4, result.C, "Operation C should increment C from 3 to 4");
        Assert.AreEqual("(A 1)(B 2)(C 3)", result.Shared, "Operations should execute in order A -> B -> C");
    }

    /// <summary>
    /// Tests that a single operation executes correctly.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_ShouldRunSingleOperation_Successfully()
    {
        // Arrange
        var opExec = ServiceProvider!.CreateCommonOperationExecutor<InputOutput>();
        opExec.Add<SequencialOperationA, ISequencialAInOut>();

        var input = new InputOutput { A = 5, Shared = "Start" };
        using var cts = new CancellationTokenSource();

        // Act
        var result = await opExec.ExecuteAsync(input, cts.Token);

        // Assert
        Assert.AreEqual(6, result.A);
        Assert.AreEqual("Start(A 5)", result.Shared);
    }

    #endregion

    #region Nested Sequence Tests

    /// <summary>
    /// Tests execution with a nested sequence.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_ShouldRunNestedSequence_Successfully()
    {
        // Arrange
        var opExec = ServiceProvider!.CreateCommonOperationExecutor<InputOutput>();
        opExec
            .Add<SequencialOperationA, ISequencialAInOut>()
            .AddSequence<SequenceInputOutput>(
                input => new SequenceInputOutput() { B = input.B, C = input.C, Shared = input.Shared },
                innerExec =>
                {
                    return innerExec
                        .Add<SequencialOperationB, ISequencialBInOut>()
                        .Add<SequencialOperationC, ISequencialCInOut>();
                },
                (innerOutput, outerInput) =>
                {
                    outerInput.B = innerOutput.B;
                    outerInput.C = innerOutput.C;
                    outerInput.Shared = innerOutput.Shared;
                })
            .Add<SequencialOperationA, ISequencialAInOut>();

        var input = new InputOutput { A = 1, B = 2, C = 3 };
        using var cts = new CancellationTokenSource();

        // Act
        var result = await opExec.ExecuteAsync(input, cts.Token);

        // Assert
        Assert.AreEqual(3, result.A, "A should be incremented twice (1->2->3)");
        Assert.AreEqual(3, result.B, "B should be incremented once in the sequence");
        Assert.AreEqual(4, result.C, "C should be incremented once in the sequence");
        Assert.AreEqual("(A 1)(B 2)(C 3)(A 2)", result.Shared);
    }

    /// <summary>
    /// Tests complex execution with nested sequences and multiple operations.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_ShouldRunComplexNestedSequences_InCorrectOrder()
    {
        // Arrange
        var opExec = ServiceProvider!.CreateCommonOperationExecutor<InputOutput>();
        opExec
            .Add<SequencialOperationA, ISequencialAInOut>()
            .Add<SequencialOperationB, ISequencialBInOut>()
            .AddSequence<SequenceInputOutput>(
                input => new SequenceInputOutput() { B = input.B, C = input.C, Shared = input.Shared },
                innerExec =>
                {
                    return innerExec
                        .Add<SequencialOperationB, ISequencialBInOut>()
                        .Add<SequencialOperationC, ISequencialCInOut>();
                },
                (innerOutput, outerInput) =>
                {
                    outerInput.B = innerOutput.B;
                    outerInput.C = innerOutput.B;
                    outerInput.Shared = innerOutput.Shared;
                })
            .Add<SequencialOperationC, ISequencialCInOut>();

        var input = new InputOutput { A = 0, B = 0, C = 0 };
        using var cts = new CancellationTokenSource();

        // Act
        var result = await opExec.ExecuteAsync(input, cts.Token);

        // Assert
        Assert.AreEqual("(A 0)(B 0)(B 1)(C 0)(C 2)", result.Shared);
    }

    #endregion

    #region Async Pipeline Tests

    /// <summary>
    /// Tests execution with an async pipeline that produces multiple items.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_ShouldRunAsyncPipeline_AndProcessAllItems()
    {
        // Arrange
        var opExec = ServiceProvider!.CreateCommonOperationExecutor<InputOutput>();

        opExec
            .Add<SequencialOperationA, ISequencialAInOut>()
            .AddAsyncPipeline<AsyncPipelineInputOutput>(
                input => new AsyncPipelineInputOutput()
                {
                    Factor = 2,
                    Start = 0,
                    A = input.A,
                    C = input.C,
                    Shared = input.Shared
                },
                pipelineExec =>
                {
                    return pipelineExec
                        .Add<ProducerOperation, IAsyncProducerInputOutput>()
                        .Add<SequencialOperationC, ISequencialCInOut>();
                },
                (pipelineOutput, outerInput) =>
                {
                    outerInput.A = pipelineOutput.A;
                    outerInput.C = pipelineOutput.C;
                    outerInput.Shared = pipelineOutput.Shared;
                })
            .Add<SequencialOperationB, ISequencialBInOut>();

        var input = new InputOutput { A = 1, B = 5, C = 0 };
        using var cts = new CancellationTokenSource();

        // Act
        var result = await opExec.ExecuteAsync(input, cts.Token);

        // Assert
        Assert.AreEqual("(A 1)(C 0)(C 1)(C 2)(C 3)(C 4)(C 5)(C 6)(C 7)(C 8)(C 9)(C 10)(B 5)", result.Shared);
    }

    /// <summary>
    /// Tests that async pipeline integrates correctly with sequential operations.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_ShouldIntegrateAsyncPipeline_WithSequentialOperations()
    {
        // Arrange
        var opExec = ServiceProvider!.CreateCommonOperationExecutor<InputOutput>();

        opExec
            .Add<SequencialOperationA, ISequencialAInOut>()
            .AddAsyncPipeline<AsyncPipelineInputOutput>(
                input => new AsyncPipelineInputOutput()
                {
                    Factor = 5,
                    Start = 5,
                    A = input.A,
                    C = input.C,
                    Shared = input.Shared
                },
                pipelineExec =>
                {
                    return pipelineExec
                        .Add<ProducerOperation, IAsyncProducerInputOutput>()
                        .Add<SequencialOperationC, ISequencialCInOut>();
                },
                (pipelineOutput, outerInput) =>
                {
                    outerInput.A = pipelineOutput.A;
                    outerInput.C = pipelineOutput.C;
                    outerInput.Shared = pipelineOutput.Shared;
                })
            .Add<SequencialOperationA, ISequencialAInOut>();

        var input = new InputOutput { A = 1, B = 0, C = 0 };
        using var cts = new CancellationTokenSource();

        // Act
        var result = await opExec.ExecuteAsync(input, cts.Token);

        // Assert
        Assert.IsTrue(result.Shared.Contains("(A 1)"), "First operation A should execute");
        Assert.IsTrue(result.Shared.Contains("(C "), "Operation C should execute in pipeline");
        // The final A operation should execute after the last pipeline item
        Assert.IsTrue(result.Shared.EndsWith(")"), "Operations should complete");
    }

    #endregion

    #region Complex Integration Tests

    /// <summary>
    /// Tests the complete integration example from user's console code.
    /// This mirrors the original IntegrationMock test.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_ComplexIntegration_ShouldExecuteAllOperations()
    {
        // Arrange
        var opExec = ServiceProvider!.CreateCommonOperationExecutor<InputOutput>();

        opExec
            .Add<SequencialOperationA, ISequencialAInOut>()
            .Add<SequencialOperationB, ISequencialBInOut>()
            .AddSequence<SequenceInputOutput>(
                input => new SequenceInputOutput() { B = input.B, C = input.C, Shared = input.Shared },
                innerExec =>
                {
                    return innerExec
                        .Add<SequencialOperationB, ISequencialBInOut>()
                        .Add<SequencialOperationC, ISequencialCInOut>();
                },
                (innerOutput, outerInput) =>
                {
                    outerInput.B = innerOutput.B;
                    outerInput.C = innerOutput.C;
                    outerInput.Shared = innerOutput.Shared;
                })
            .AddAsyncPipeline<AsyncPipelineInputOutput>(
                input => new AsyncPipelineInputOutput()
                {
                    Factor = 2,
                    Start = 0,
                    A = input.A,
                    C = input.C,
                    Shared = input.Shared
                },
                pipelineExec =>
                {
                    return pipelineExec
                        .Add<ProducerOperation, IAsyncProducerInputOutput>()
                        .Add<SequencialOperationC, ISequencialCInOut>();
                },
                (pipelineOutput, outerInput) =>
                {
                    outerInput.A = pipelineOutput.A;
                    outerInput.C = pipelineOutput.C;
                    outerInput.Shared = pipelineOutput.Shared;
                })
            .Add<SequencialOperationC, ISequencialCInOut>();

        var input = new InputOutput { A = 1, B = 0, C = 0 };
        using var cts = new CancellationTokenSource();

        // Act
        var result = await opExec.ExecuteAsync(input, cts.Token);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Shared.Contains("(A 1)"), "Operation A should execute");
        Assert.IsTrue(result.Shared.Contains("(B "), "Operation B should execute");
        Assert.IsTrue(result.Shared.Contains("(C "), "Operation C should execute");

        // Verify execution tree exists
        var execTree = opExec.GetExecutionTree();
        Assert.IsNotNull(execTree);
        Assert.IsTrue(execTree.Count > 0, "Execution tree should contain operations");
    }

    #endregion

    #region Error Handling Tests

    /// <summary>
    /// Tests that exceptions in operations are properly propagated.
    /// Operation C throws when C > 6.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_ShouldThrowException_WhenOperationFails()
    {
        // Arrange
        var opExec = ServiceProvider!.CreateCommonOperationExecutor<InputOutput>();
        opExec
            .Add<SequencialOperationA, ISequencialAInOut>()
            .Add<SequencialOperationB, ISequencialBInOut>()
            .Add<SequencialOperationD, ISequencialDInOut>();

        var input = new InputOutput { A = 1, B = 2, D = 6 }; // D will become 7 after increment
        using var cts = new CancellationTokenSource();

        // Act & Assert
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            async () => await opExec.ExecuteAsync(input, cts.Token),
            "Should throw InvalidOperationException when D > 6"
        );
    }

    /// <summary>
    /// Tests error handling in nested sequences.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_ShouldPropagateException_FromNestedSequence()
    {
        // Arrange
        var opExec = ServiceProvider!.CreateCommonOperationExecutor<InputOutput>();
        opExec
            .Add<SequencialOperationA, ISequencialAInOut>()
            .AddSequence<SequenceInputOutput>(
                input => new SequenceInputOutput() { B = input.B, D = 6, Shared = input.Shared }, // D=6 will become 7
                innerExec =>
                {
                    return innerExec
                        .Add<SequencialOperationB, ISequencialBInOut>()
                        .Add<SequencialOperationD, ISequencialDInOut>(); // This will throw
                },
                (innerOutput, outerInput) =>
                {
                    outerInput.B = innerOutput.B;
                    outerInput.C = innerOutput.C;
                    outerInput.Shared = innerOutput.Shared;
                });

        var input = new InputOutput { A = 1, B = 2, C = 3 };
        using var cts = new CancellationTokenSource();

        // Act & Assert
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            async () => await opExec.ExecuteAsync(input, cts.Token)
        );
    }

    /// <summary>
    /// Tests that execution tree captures error state.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_ShouldCaptureErrorInExecutionTree_WhenOperationFails()
    {
        // Arrange
        var opExec = ServiceProvider!.CreateCommonOperationExecutor<InputOutput>();
        opExec
            .Add<SequencialOperationA, ISequencialAInOut>()
            .Add<SequencialOperationD, ISequencialDInOut>();

        var input = new InputOutput { A = 1, D = 6 };
        using var cts = new CancellationTokenSource();

        // Act
        try
        {
            await opExec.ExecuteAsync(input, cts.Token);
        }
        catch (InvalidOperationException)
        {
            // Expected exception
        }

        // Assert
        var execTree = opExec.GetExecutionTree();
        Assert.IsNotNull(execTree);

        var faultedOperation = execTree.FirstOrDefault(op => op.Status == OperationState.Faulted);
        Assert.IsNotNull(faultedOperation, "Execution tree should contain faulted operation");
        Assert.IsNotNull(faultedOperation.Error, "Faulted operation should have error details");
    }

    #endregion

    #region Status Change Tracking Tests

    /// <summary>
    /// Tests that status change events are raised during execution.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_ShouldRaiseStatusChangeEvents_DuringExecution()
    {
        // Arrange
        var opExec = ServiceProvider!.CreateCommonOperationExecutor<InputOutput>();
        opExec
            .Add<SequencialOperationA, ISequencialAInOut>()
            .Add<SequencialOperationB, ISequencialBInOut>()
            .Add<SequencialOperationC, ISequencialCInOut>();

        var statusChanges = new List<ExecutionStatus>();
        opExec.OnExecutionStatusChanged += (status) => statusChanges.Add(status);

        var input = new InputOutput { A = 1, B = 2, C = 3 };
        using var cts = new CancellationTokenSource();

        // Act
        await opExec.ExecuteAsync(input, cts.Token);

        // Assert
        Assert.IsTrue(statusChanges.Count > 0, "Status changes should be recorded");

        // Verify we have both Running and Completed states
        Assert.IsTrue(statusChanges.Any(s => s.Status == OperationState.Running),
            "Should have operations in Running state");
        Assert.IsTrue(statusChanges.Any(s => s.Status == OperationState.Completed),
            "Should have operations in Completed state");
    }

    /// <summary>
    /// Tests that status changes include operation names and descriptions.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_StatusChanges_ShouldIncludeOperationDetails()
    {
        // Arrange
        var opExec = ServiceProvider!.CreateCommonOperationExecutor<InputOutput>();
        opExec.Add<SequencialOperationA, ISequencialAInOut>();

        ExecutionStatus? lastStatus = null;
        opExec.OnExecutionStatusChanged += (status) => lastStatus = status;

        var input = new InputOutput { A = 1 };
        using var cts = new CancellationTokenSource();

        // Act
        await opExec.ExecuteAsync(input, cts.Token);

        // Assert
        Assert.IsNotNull(lastStatus);
        Assert.IsFalse(string.IsNullOrEmpty(lastStatus.Name), "Status should include operation name");
    }

    #endregion

    #region Execution Tree Tests

    /// <summary>
    /// Tests that execution tree is correctly built for simple sequential operations.
    /// </summary>
    [TestMethod]
    public async Task GetExecutionTree_ShouldReturnCorrectStructure_ForSequentialOperations()
    {
        // Arrange
        var opExec = ServiceProvider!.CreateCommonOperationExecutor<InputOutput>();
        opExec
            .Add<SequencialOperationA, ISequencialAInOut>()
            .Add<SequencialOperationB, ISequencialBInOut>()
            .Add<SequencialOperationC, ISequencialCInOut>();

        var input = new InputOutput { A = 1, B = 2, C = 3 };
        using var cts = new CancellationTokenSource();

        // Act
        await opExec.ExecuteAsync(input, cts.Token);
        var execTree = opExec.GetExecutionTree();

        // Assert
        Assert.AreEqual(3, execTree.Count, "Should have 3 operations in tree");
        Assert.IsTrue(execTree.All(op => op.Status == OperationState.Completed),
            "All operations should be completed");
    }

    /// <summary>
    /// Tests that execution tree includes nested executors.
    /// </summary>
    [TestMethod]
    public async Task GetExecutionTree_ShouldIncludeNestedExecutors_InHierarchy()
    {
        // Arrange
        var opExec = ServiceProvider!.CreateCommonOperationExecutor<InputOutput>();
        opExec
            .Add<SequencialOperationA, ISequencialAInOut>()
            .AddSequence<SequenceInputOutput>(
                input => new SequenceInputOutput() { B = input.B, C = input.C, Shared = input.Shared },
                innerExec =>
                {
                    return innerExec
                        .Add<SequencialOperationB, ISequencialBInOut>()
                        .Add<SequencialOperationC, ISequencialCInOut>();
                },
                (innerOutput, outerInput) =>
                {
                    outerInput.B = innerOutput.B;
                    outerInput.C = innerOutput.C;
                    outerInput.Shared = innerOutput.Shared;
                });

        var input = new InputOutput { A = 1, B = 2, C = 3 };
        using var cts = new CancellationTokenSource();

        // Act
        await opExec.ExecuteAsync(input, cts.Token);
        var execTree = opExec.GetExecutionTree();

        // Assert
        Assert.AreEqual(2, execTree.Count, "Should have 2 top-level operations");

        var sequenceOperation = execTree.FirstOrDefault(op => op.Nodes != null && op.Nodes.Count > 0);
        Assert.IsNotNull(sequenceOperation, "Should have a sequence operation with nested nodes");
        Assert.AreEqual(2, sequenceOperation.Nodes.Count, "Sequence should have 2 nested operations");
    }

    /// <summary>
    /// Tests execution tree timing information.
    /// </summary>
    [TestMethod]
    public async Task GetExecutionTree_ShouldIncludeTimingInformation_ForOperations()
    {
        // Arrange
        var opExec = ServiceProvider!.CreateCommonOperationExecutor<InputOutput>();
        opExec.Add<SequencialOperationA, ISequencialAInOut>();

        var input = new InputOutput { A = 1 };
        using var cts = new CancellationTokenSource();

        // Act
        await opExec.ExecuteAsync(input, cts.Token);
        var execTree = opExec.GetExecutionTree();

        // Assert
        var operation = execTree.First();
        Assert.IsNotNull(operation.Started, "Should have start time");
        Assert.IsNotNull(operation.Completed, "Should have completion time");
        Assert.IsTrue(operation.Completed >= operation.Started,
            "Completion time should be after or equal to start time");
    }

    #endregion

    #region Cancellation Tests

    /// <summary>
    /// Tests that operations respect cancellation tokens.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_ShouldRespectCancellation_WhenTokenIsCancelled()
    {
        // Arrange
        var opExec = ServiceProvider!.CreateCommonOperationExecutor<InputOutput>();
        opExec
            .AddAsyncPipeline<AsyncPipelineInputOutput>(
                input => new AsyncPipelineInputOutput()
                {
                    Factor = 2,
                    Start = 0,
                    A = input.A,
                    C = input.C,
                    Shared = input.Shared
                },
                pipelineExec =>
                {
                    return pipelineExec
                        .Add<ProducerOperation, IAsyncProducerInputOutput>()
                        .Add<SequencialOperationC, ISequencialCInOut>();
                },
                (pipelineOutput, outerInput) =>
                {
                    outerInput.A = pipelineOutput.A;
                    outerInput.Shared = pipelineOutput.Shared;
                });

        var input = new InputOutput { A = 1 };
        using var cts = new CancellationTokenSource();

        // Cancel after a short delay
        cts.CancelAfter(50);

        // Act & Assert
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(
            async () => await opExec.ExecuteAsync(input, cts.Token),
            "Should throw OperationCanceledException when token is cancelled"
        );
    }

    #endregion

    #region Additional Edge Cases

    /// <summary>
    /// Tests execution with initial values of zero.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_ShouldHandleZeroInitialValues_Correctly()
    {
        // Arrange
        var opExec = ServiceProvider!.CreateCommonOperationExecutor<InputOutput>();
        opExec
            .Add<SequencialOperationA, ISequencialAInOut>()
            .Add<SequencialOperationB, ISequencialBInOut>()
            .Add<SequencialOperationC, ISequencialCInOut>();

        var input = new InputOutput { A = 0, B = 0, C = 0 };
        using var cts = new CancellationTokenSource();

        // Act
        var result = await opExec.ExecuteAsync(input, cts.Token);

        // Assert
        Assert.AreEqual(1, result.A);
        Assert.AreEqual(1, result.B);
        Assert.AreEqual(1, result.C);
        Assert.AreEqual("(A 0)(B 0)(C 0)", result.Shared);
    }

    /// <summary>
    /// Tests that shared string accumulates correctly across all operations.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_ShouldAccumulateSharedString_ThroughAllOperations()
    {
        // Arrange
        var opExec = ServiceProvider!.CreateCommonOperationExecutor<InputOutput>();
        opExec
            .Add<SequencialOperationA, ISequencialAInOut>()
            .Add<SequencialOperationB, ISequencialBInOut>()
            .Add<SequencialOperationC, ISequencialCInOut>()
            .Add<SequencialOperationA, ISequencialAInOut>()
            .Add<SequencialOperationB, ISequencialBInOut>();

        var input = new InputOutput { A = 10, B = 20, C = 30, Shared = "Start:" };
        using var cts = new CancellationTokenSource();

        // Act
        var result = await opExec.ExecuteAsync(input, cts.Token);

        // Assert
        Assert.AreEqual("Start:(A 10)(B 20)(C 30)(A 11)(B 21)", result.Shared);
    }

    /// <summary>
    /// Tests execution with multiple sequential operations of the same type.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_ShouldHandleRepeatedOperations_Correctly()
    {
        // Arrange
        var opExec = ServiceProvider!.CreateCommonOperationExecutor<InputOutput>();
        opExec
            .Add<SequencialOperationA, ISequencialAInOut>()
            .Add<SequencialOperationA, ISequencialAInOut>()
            .Add<SequencialOperationA, ISequencialAInOut>();

        var input = new InputOutput { A = 1 };
        using var cts = new CancellationTokenSource();

        // Act
        var result = await opExec.ExecuteAsync(input, cts.Token);

        // Assert
        Assert.AreEqual(4, result.A, "A should be incremented 3 times: 1->2->3->4");
        Assert.AreEqual("(A 1)(A 2)(A 3)", result.Shared);
    }

    #endregion
}

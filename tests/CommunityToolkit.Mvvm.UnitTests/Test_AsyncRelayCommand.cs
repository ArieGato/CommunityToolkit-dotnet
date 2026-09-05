// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.UnitTests.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nito.AsyncEx;

namespace CommunityToolkit.Mvvm.UnitTests;

[TestClass]
public class Test_AsyncRelayCommand
{
    [TestMethod]
    public async Task Test_AsyncRelayCommand_AlwaysEnabled()
    {
        int ticks = 0;

        AsyncRelayCommand? command = new(async () =>
        {
            await Task.Delay(1000);
            ticks++;
            await Task.Delay(1000);
        });

        Assert.IsTrue(command.CanExecute(null));
        Assert.IsTrue(command.CanExecute(new object()));

        Assert.IsFalse(command.CanBeCanceled);
        Assert.IsFalse(command.IsCancellationRequested);

        (object?, EventArgs?) args = default;

        command.CanExecuteChanged += (s, e) => args = (s, e);

        command.NotifyCanExecuteChanged();

        Assert.AreSame(args.Item1, command);
        Assert.AreSame(args.Item2, EventArgs.Empty);

        Assert.IsNull(command.ExecutionTask);
        Assert.IsFalse(command.IsRunning);

        Task task = command.ExecuteAsync(null);

        Assert.IsNotNull(command.ExecutionTask);
        Assert.AreSame(command.ExecutionTask, task);
        Assert.IsTrue(command.IsRunning);

        await task;

        Assert.IsFalse(command.IsRunning);

        Assert.AreEqual(1, ticks);

        command.Execute(new object());

        await command.ExecutionTask!;

        Assert.AreEqual(2, ticks);
    }

    [TestMethod]
    public void Test_AsyncRelayCommand_WithCanExecuteFunctionTrue()
    {
        int ticks = 0;

        AsyncRelayCommand? command = new(
            () =>
            {
                ticks++;
                return Task.CompletedTask;
            }, () => true);

        Assert.IsTrue(command.CanExecute(null));
        Assert.IsTrue(command.CanExecute(new object()));

        Assert.IsFalse(command.CanBeCanceled);
        Assert.IsFalse(command.IsCancellationRequested);

        command.Execute(null);

        Assert.AreEqual(1, ticks);

        command.Execute(new object());

        Assert.AreEqual(2, ticks);
    }

    [TestMethod]
    public void Test_AsyncRelayCommand_WithCanExecuteFunctionFalse()
    {
        int ticks = 0;

        AsyncRelayCommand? command = new(
            () =>
            {
                ticks++;
                return Task.CompletedTask;
            }, () => false);

        Assert.IsFalse(command.CanExecute(null));
        Assert.IsFalse(command.CanExecute(new object()));

        Assert.IsFalse(command.CanBeCanceled);
        Assert.IsFalse(command.IsCancellationRequested);

        command.Execute(null);

        // It is the caller's responsibility to ensure that CanExecute is true
        // before calling Execute. This check verifies the logic is still called.
        Assert.AreEqual(1, ticks);

        command.Execute(new object());

        Assert.AreEqual(2, ticks);
    }

    [TestMethod]
    public async Task Test_AsyncRelayCommand_WithCancellation()
    {
        TaskCompletionSource<object?> tcs = new();

        // We need to test the cancellation support here, so we use the overload with an input
        // parameter, which is a cancellation token. The token is the one that is internally managed
        // by the AsyncRelayCommand instance, and canceled when using IAsyncRelayCommand.Cancel().
        AsyncRelayCommand? command = new(token => tcs.Task);

        List<PropertyChangedEventArgs> args = new();

        command.PropertyChanged += (s, e) => args.Add(e);

        // We have no canExecute parameter, so the command can always be invoked
        Assert.IsTrue(command.CanExecute(null));
        Assert.IsTrue(command.CanExecute(new object()));

        // The command isn't running, so it can't be canceled yet
        Assert.IsFalse(command.CanBeCanceled);
        Assert.IsFalse(command.IsCancellationRequested);

        // Start the command, which will return the token from our task completion source.
        // We can use that to easily keep the command running while we do our tests, and then
        // stop the processing by completing the source when we need (see below).
        command.Execute(null);

        // The command is running, so it can be canceled, as we used the token overload
        Assert.IsTrue(command.CanBeCanceled);
        Assert.IsFalse(command.IsCancellationRequested);

        // Validate the various event args for all the properties that were updated when executing the command
        Assert.HasCount(4, args);
        Assert.AreEqual(nameof(IAsyncRelayCommand.ExecutionTask), args[0].PropertyName);
        Assert.AreEqual(nameof(IAsyncRelayCommand.IsRunning), args[1].PropertyName);
        Assert.AreEqual(nameof(IAsyncRelayCommand.CanBeCanceled), args[2].PropertyName);
        Assert.AreEqual(nameof(IAsyncRelayCommand.IsCancellationRequested), args[3].PropertyName);

        command.Cancel();

        // Verify that these two properties raised notifications correctly when canceling the command too.
        // We need to ensure all command properties support notifications so that users can bind to them.
        Assert.HasCount(6, args);
        Assert.AreEqual(nameof(IAsyncRelayCommand.CanBeCanceled), args[4].PropertyName);
        Assert.AreEqual(nameof(IAsyncRelayCommand.IsCancellationRequested), args[5].PropertyName);

        Assert.IsTrue(command.IsCancellationRequested);

        // Complete the source, which will mark the command as completed too (as it returned the same task)
        tcs.SetResult(null);

        await command.ExecutionTask!;

        // Verify that the command can no longer be canceled, and that the cancellation is
        // instead still true, as that's reset when executing a command and not on completion.
        Assert.IsFalse(command.CanBeCanceled);
        Assert.IsTrue(command.IsCancellationRequested);
    }

    [TestMethod]
    public async Task Test_AsyncRelayCommand_AllowConcurrentExecutions_Enable()
    {
        int index = 0;
        TaskCompletionSource<object?>[] cancellationTokenSources = new TaskCompletionSource<object?>[]
        {
            new TaskCompletionSource<object?>(),
            new TaskCompletionSource<object?>()
        };

        AsyncRelayCommand? command = new(() => cancellationTokenSources[index++].Task, AsyncRelayCommandOptions.AllowConcurrentExecutions);

        Assert.IsTrue(command.CanExecute(null));
        Assert.IsTrue(command.CanExecute(new object()));

        Assert.IsFalse(command.CanBeCanceled);
        Assert.IsFalse(command.IsCancellationRequested);

        (object?, EventArgs?) args = default;

        command.CanExecuteChanged += (s, e) => args = (s, e);

        command.NotifyCanExecuteChanged();

        Assert.AreSame(args.Item1, command);
        Assert.AreSame(args.Item2, EventArgs.Empty);

        args = default;

        Assert.IsNull(command.ExecutionTask);
        Assert.IsFalse(command.IsRunning);

        Task task = command.ExecuteAsync(null);

        Assert.IsNotNull(command.ExecutionTask);
        Assert.AreSame(command.ExecutionTask, task);
        Assert.AreSame(command.ExecutionTask, cancellationTokenSources[0].Task);
        Assert.IsTrue(command.IsRunning);

        // The command can still be executed now
        Assert.IsTrue(command.CanExecute(null));
        Assert.IsTrue(command.CanExecute(new object()));

        Assert.IsFalse(command.CanBeCanceled);
        Assert.IsFalse(command.IsCancellationRequested);

        Task newTask = command.ExecuteAsync(null);

        // A new task was returned
        Assert.AreSame(command.ExecutionTask, newTask);
        Assert.AreSame(command.ExecutionTask, cancellationTokenSources[1].Task);

        cancellationTokenSources[0].SetResult(null);
        cancellationTokenSources[1].SetResult(null);

        _ = await Task.WhenAll(cancellationTokenSources[0].Task, cancellationTokenSources[1].Task);

        Assert.IsFalse(command.IsRunning);

        // CanExecute isn't raised again when the command completes, if concurrent executions are allowed
        Assert.IsNull(args.Item1);
        Assert.IsNull(args.Item2);
    }

    [TestMethod]
    public async Task Test_AsyncRelayCommand_AllowConcurrentExecutions_Disabled()
    {
        await Test_AsyncRelayCommand_AllowConcurrentExecutions_TestLogic(static task => new(async () => await task, AsyncRelayCommandOptions.None));
    }

    [TestMethod]
    public async Task Test_AsyncRelayCommand_AllowConcurrentExecutions_Default()
    {
        await Test_AsyncRelayCommand_AllowConcurrentExecutions_TestLogic(static task => new(async () => await task));
    }

    /// <summary>
    /// Shared logic for <see cref="Test_AsyncRelayCommand_AllowConcurrentExecutions_Disabled"/> and <see cref="Test_AsyncRelayCommand_AllowConcurrentExecutions_Default"/>.
    /// </summary>
    /// <param name="factory">A factory to create the <see cref="AsyncRelayCommand"/> instance to test.</param>
    private static async Task Test_AsyncRelayCommand_AllowConcurrentExecutions_TestLogic(Func<Task, AsyncRelayCommand> factory)
    {
        TaskCompletionSource<object?> tcs = new();

        AsyncRelayCommand? command = factory(tcs.Task);

        Assert.IsTrue(command.CanExecute(null));
        Assert.IsTrue(command.CanExecute(new object()));

        Assert.IsFalse(command.CanBeCanceled);
        Assert.IsFalse(command.IsCancellationRequested);

        (object?, EventArgs?) args = default;

        command.CanExecuteChanged += (s, e) => args = (s, e);

        command.NotifyCanExecuteChanged();

        Assert.AreSame(args.Item1, command);
        Assert.AreSame(args.Item2, EventArgs.Empty);

        args = default;

        Assert.IsNull(command.ExecutionTask);
        Assert.IsFalse(command.IsRunning);

        Task task = command.ExecuteAsync(null);

        // CanExecute is raised upon execution
        Assert.AreSame(args.Item1, command);
        Assert.AreSame(args.Item2, EventArgs.Empty);

        args = default;

        Assert.IsNotNull(command.ExecutionTask);
        Assert.AreSame(command.ExecutionTask, task);
        Assert.IsTrue(command.IsRunning);

        // The command can't be executed now, as there's a pending operation
        Assert.IsFalse(command.CanExecute(null));
        Assert.IsFalse(command.CanExecute(new object()));

        Assert.IsFalse(command.CanBeCanceled);
        Assert.IsFalse(command.IsCancellationRequested);

        // CanExecute hasn't been raised again
        Assert.IsNull(args.Item1);
        Assert.IsNull(args.Item2);

        tcs.SetResult(null);

        await task;

        Assert.IsFalse(command.IsRunning);

        // CanExecute is raised automatically when command execution completes, if concurrent executions are disabled
        Assert.AreSame(args.Item1, command);
        Assert.AreSame(args.Item2, EventArgs.Empty);
    }

    [TestMethod]
    public void Test_AsyncRelayCommand_EnsureExceptionThrown_Synchronously()
    {
        Exception? executeException = null;

        AsyncRelayCommand command = new(async () =>
        {
            await Task.CompletedTask;

            throw new Exception(nameof(Test_AsyncRelayCommand_EnsureExceptionThrown_Synchronously));
        });

        try
        {
            AsyncContext.Run(async () =>
            {
                command.Execute(null);

                await Task.Delay(500);
            });
        }
        catch (Exception e)
        {
            executeException = e;
        }

        Assert.AreEqual(nameof(Test_AsyncRelayCommand_EnsureExceptionThrown_Synchronously), executeException?.Message);
    }

    // See https://github.com/CommunityToolkit/dotnet/pull/251
    [TestMethod]
    public async Task Test_AsyncRelayCommand_EnsureExceptionThrown()
    {
        const int delay = 500;

        Exception? executeException = null;
        Exception? executeAsyncException = null;

        AsyncRelayCommand command = new(async () =>
        {
            await Task.Delay(delay);

            throw new Exception(nameof(Test_AsyncRelayCommand_EnsureExceptionThrown));
        });

        try
        {
            // Use AsyncContext to test async void methods https://stackoverflow.com/a/14207615/5953643
            AsyncContext.Run(async () =>
            {
                command.Execute(null);

                await Task.Delay(delay * 2); // Ensure we don't escape `AsyncContext` before command throws Exception
            });
        }
        catch (Exception e)
        {
            executeException = e;
        }

        executeAsyncException = await Assert.ThrowsExactlyAsync<Exception>(() => command.ExecuteAsync(null));

        Assert.AreEqual(nameof(Test_AsyncRelayCommand_EnsureExceptionThrown), executeException?.Message);
        Assert.AreEqual(nameof(Test_AsyncRelayCommand_EnsureExceptionThrown), executeAsyncException?.Message);
    }

    [TestMethod]
    public async Task Test_AsyncRelayCommand_ThrowingTaskBubblesToUnobservedTaskException()
    {
        static async Task TestMethodAsync(Action action)
        {
            await Task.Delay(100);

            action();
        }

        async void TestCallback(Action throwAction, Action completeAction)
        {
            AsyncRelayCommand command = new(() => TestMethodAsync(throwAction), AsyncRelayCommandOptions.FlowExceptionsToTaskScheduler);

            command.Execute(null);

            await Task.Delay(200);

            completeAction();
        }

        bool success = await TaskSchedulerTestHelper.IsExceptionBubbledUpToUnobservedTaskExceptionAsync(TestCallback);

        Assert.IsTrue(success);
    }

    [TestMethod]
    public async Task Test_AsyncRelayCommand_ThrowingTaskBubblesToUnobservedTaskException_Synchronously()
    {
        static async Task TestMethodAsync(Action action)
        {
            await Task.CompletedTask;

            action();
        }

        async void TestCallback(Action throwAction, Action completeAction)
        {
            AsyncRelayCommand command = new(() => TestMethodAsync(throwAction), AsyncRelayCommandOptions.FlowExceptionsToTaskScheduler);

            command.Execute(null);

            await Task.Delay(200);

            completeAction();
        }

        bool success = await TaskSchedulerTestHelper.IsExceptionBubbledUpToUnobservedTaskExceptionAsync(TestCallback);

        Assert.IsTrue(success);
    }

    // See https://github.com/CommunityToolkit/dotnet/issues/108
    [TestMethod]
    public void Test_AsyncRelayCommand_ExecuteDoesNotRaiseCanExecuteChanged()
    {
        TaskCompletionSource<object?> tcs = new();

        AsyncRelayCommand command = new(() => tcs.Task, AsyncRelayCommandOptions.AllowConcurrentExecutions);

        (object? Sender, EventArgs? Args) args = default;

        command.CanExecuteChanged += (s, e) => args = (s, e);

        Assert.IsTrue(command.CanExecute(null));

        command.Execute(null);

        Assert.IsNull(args.Sender);
        Assert.IsNull(args.Args);

        Assert.IsTrue(command.CanExecute(null));

        tcs.SetResult(null);
    }

    [TestMethod]
    public void Test_AsyncRelayCommand_ExecuteWithoutConcurrencyRaisesCanExecuteChanged()
    {
        TaskCompletionSource<object?> tcs = new();

        AsyncRelayCommand command = new(() => tcs.Task, AsyncRelayCommandOptions.None);

        (object? Sender, EventArgs? Args) args = default;

        command.CanExecuteChanged += (s, e) => args = (s, e);

        Assert.IsTrue(command.CanExecute(null));

        command.Execute(null);

        Assert.AreSame(command, args.Sender);
        Assert.AreSame(EventArgs.Empty, args.Args);

        Assert.IsFalse(command.CanExecute(null));

        tcs.SetResult(null);
    }

    [TestMethod]
    public void Test_AsyncRelayCommand_ExecuteDoesNotRaiseCanExecuteChanged_WithCancellation()
    {
        TaskCompletionSource<object?> tcs = new();

        AsyncRelayCommand command = new(token => tcs.Task, AsyncRelayCommandOptions.AllowConcurrentExecutions);

        (object? Sender, EventArgs? Args) args = default;

        command.CanExecuteChanged += (s, e) => args = (s, e);

        Assert.IsTrue(command.CanExecute(null));

        command.Execute(null);

        Assert.IsNull(args.Sender);
        Assert.IsNull(args.Args);

        Assert.IsTrue(command.CanExecute(null));

        tcs.SetResult(null);
    }

    [TestMethod]
    public void Test_AsyncRelayCommand_ExecuteWithoutConcurrencyRaisesCanExecuteChanged_WithToken()
    {
        TaskCompletionSource<object?> tcs = new();

        AsyncRelayCommand command = new(token => tcs.Task, AsyncRelayCommandOptions.None);

        (object? Sender, EventArgs? Args) args = default;

        command.CanExecuteChanged += (s, e) => args = (s, e);

        Assert.IsTrue(command.CanExecute(null));

        command.Execute(null);

        Assert.AreSame(command, args.Sender);
        Assert.AreSame(EventArgs.Empty, args.Args);

        Assert.IsFalse(command.CanExecute(null));

        tcs.SetResult(null);
    }

    [TestMethod]
    public void Test_AsyncRelayCommand_GetCancelCommand_DisabledCommand()
    {
        TaskCompletionSource<object?> tcs = new();

        AsyncRelayCommand command = new(() => tcs.Task);

        ICommand cancelCommand = command.CreateCancelCommand();

        Assert.IsNotNull(cancelCommand);
        Assert.IsFalse(cancelCommand.CanExecute(null));

        // No-op
        cancelCommand.Execute(null);

        Assert.AreEqual("CommunityToolkit.Mvvm.Input.Internals.DisabledCommand", cancelCommand.GetType().ToString());

        ICommand cancelCommand2 = command.CreateCancelCommand();

        Assert.IsNotNull(cancelCommand2);
        Assert.IsFalse(cancelCommand2.CanExecute(null));

        Assert.AreSame(cancelCommand, cancelCommand2);
    }

    [TestMethod]
    public void Test_AsyncRelayCommand_GetCancelCommand_WithToken()
    {
        TaskCompletionSource<object?> tcs = new();

        AsyncRelayCommand command = new(token => tcs.Task);

        ICommand cancelCommand = command.CreateCancelCommand();

        Assert.IsNotNull(cancelCommand);
        Assert.IsFalse(cancelCommand.CanExecute(null));

        // No-op
        cancelCommand.Execute(null);

        Assert.AreEqual("CommunityToolkit.Mvvm.Input.Internals.CancelCommand", cancelCommand.GetType().ToString());

        List<(object? Sender, EventArgs Args)> cancelCommandCanExecuteChangedArgs = new();

        cancelCommand.CanExecuteChanged += (s, e) => cancelCommandCanExecuteChangedArgs.Add((s, e));

        command.Execute(null);

        Assert.HasCount(1, cancelCommandCanExecuteChangedArgs);
        Assert.AreSame(cancelCommand, cancelCommandCanExecuteChangedArgs[0].Sender);
        Assert.AreSame(EventArgs.Empty, cancelCommandCanExecuteChangedArgs[0].Args);

        Assert.IsTrue(cancelCommand.CanExecute(null));

        cancelCommand.Execute(null);

        Assert.IsFalse(cancelCommand.CanExecute(null));
        Assert.HasCount(2, cancelCommandCanExecuteChangedArgs);
        Assert.AreSame(cancelCommand, cancelCommandCanExecuteChangedArgs[1].Sender);
        Assert.AreSame(EventArgs.Empty, cancelCommandCanExecuteChangedArgs[1].Args);
        Assert.IsFalse(command.CanBeCanceled);
        Assert.IsTrue(command.IsCancellationRequested);
    }

    [TestMethod]
    public void Test_AsyncRelayCommand_ExecutionFailed_HandledSuppressesException()
    {
        InvalidOperationException exception = new("Test");

        AsyncRelayCommand command = new(async () =>
        {
            await Task.CompletedTask;

            throw exception;
        });

        object? sender = null;
        Exception? routedException = null;

        command.ExecutionFailed += (s, e) =>
        {
            sender = s;
            routedException = e.Exception;
            e.Handled = true;
        };

        // No exception must escape to the synchronization context (AsyncContext.Run would rethrow it)
        AsyncContext.Run(async () =>
        {
            command.Execute(null);

            // Deterministically wait for the command to finish instead of a fixed delay: the faulted
            // task is observed here, but the event has already been routed by the command internally.
            try
            {
                await command.ExecutionTask!;
            }
            catch (InvalidOperationException)
            {
            }
        });

        Assert.AreSame(command, sender);
        Assert.AreSame(exception, routedException);
    }

    [TestMethod]
    public void Test_AsyncRelayCommand_ExecutionFailed_NotHandledRethrowsOnCapturedContext()
    {
        InvalidOperationException exception = new("Test");

        AsyncRelayCommand command = new(async () =>
        {
            await Task.CompletedTask;

            throw exception;
        });

        Exception? routedException = null;
        Exception? contextException = null;

        command.ExecutionFailed += (s, e) => routedException = e.Exception;

        try
        {
            AsyncContext.Run(async () =>
            {
                command.Execute(null);

                // Deterministically wait for the faulted task, swallowing the exception here: the one
                // that must surface from AsyncContext.Run is the command's own rethrow on the captured
                // context, not the one this await would otherwise produce.
                try
                {
                    await command.ExecutionTask!;
                }
                catch (InvalidOperationException)
                {
                }
            });
        }
        catch (Exception e)
        {
            contextException = e;
        }

        Assert.AreSame(exception, routedException);
        Assert.AreSame(exception, contextException);
    }

    [TestMethod]
    public void Test_AsyncRelayCommand_ExecutionFailed_CancellationDoesNotRaise()
    {
        AsyncRelayCommand command = new(token => Task.Delay(1000, token));

        bool raised = false;

        command.ExecutionFailed += (s, e) => raised = true;

        try
        {
            AsyncContext.Run(async () =>
            {
                command.Execute(null);

                command.Cancel();

                // Deterministically wait for the canceled task to complete instead of a fixed delay.
                await command.ExecutionTask!;
            });
        }
        catch (OperationCanceledException)
        {
            // Today's propagation of the canceled task is intentionally untouched
        }

        Assert.IsFalse(raised);
    }

    [TestMethod]
    public async Task Test_AsyncRelayCommand_ExecutionFailed_SuccessfulExecutionDoesNotRaise()
    {
        // The delegate returns an already completed task, so both the Execute and the ExecuteAsync
        // observers run to completion synchronously and the assertions below need no extra waiting.
        int executions = 0;

        AsyncRelayCommand command = new(() =>
        {
            executions++;

            return Task.CompletedTask;
        });

        bool raised = false;

        command.ExecutionFailed += (s, e) => raised = true;

        command.Execute(null);

        // Assert the delegate actually ran, so this can distinguish "the event was
        // not raised because the execution succeeded" from "nothing ran at all".
        Assert.AreEqual(1, executions);
        Assert.IsFalse(raised);

        await command.ExecuteAsync(null);

        Assert.AreEqual(2, executions);
        Assert.IsFalse(raised);
    }

    [TestMethod]
    public void Test_AsyncRelayCommand_ExecutionFailed_FlowExceptionsToTaskScheduler_SubscriberReceivesFault()
    {
        InvalidOperationException exception = new("Test");

        AsyncRelayCommand command = new(async () =>
        {
            await Task.CompletedTask;

            throw exception;
        }, AsyncRelayCommandOptions.FlowExceptionsToTaskScheduler);

        Exception? routedException = null;

        command.ExecutionFailed += (s, e) =>
        {
            routedException = e.Exception;
            e.Handled = true;
        };

        AsyncContext.Run(async () =>
        {
            command.Execute(null);

            try
            {
                await command.ExecutionTask!;
            }
            catch (InvalidOperationException)
            {
            }
        });

        Assert.AreSame(exception, routedException);
    }

    [TestMethod]
    public async Task Test_AsyncRelayCommand_ExecutionFailed_FlowExceptionsToTaskScheduler_DirectExecuteAsync_HandledStillThrowsToAwaiter()
    {
        InvalidOperationException exception = new("Test");

        AsyncRelayCommand command = new(async () =>
        {
            await Task.CompletedTask;

            throw exception;
        }, AsyncRelayCommandOptions.FlowExceptionsToTaskScheduler);

        Exception? routedException = null;

        command.ExecutionFailed += (s, e) =>
        {
            routedException = e.Exception;
            e.Handled = true;
        };

        // The flow option makes no difference on this path: the returned task is the execution task, so
        // the fault is observed by the awaiter (never reaching TaskScheduler.UnobservedTaskException),
        // and marking it as handled does not change that.
        Task task = command.ExecuteAsync(null);

        Assert.AreSame(command.ExecutionTask, task);

        InvalidOperationException thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => task);

        Assert.AreSame(exception, thrown);
        Assert.AreSame(exception, routedException);
        Assert.IsTrue(command.ExecutionTask!.IsFaulted);
    }

    [TestMethod]
    public async Task Test_AsyncRelayCommand_ExecutionFailed_Multicast_SecondHandlerStillRunsAfterFirstMarksHandled()
    {
        // Both handlers are part of the same multicast invocation: the first setting Handled to true
        // must not prevent the second one from also running, since it's the same event args instance
        // being shared. The awaited task still faults, as Handled does not apply to this path.
        InvalidOperationException exception = new("Test");

        AsyncRelayCommand command = new(async () =>
        {
            await Task.CompletedTask;

            throw exception;
        });

        bool secondHandlerRan = false;

        command.ExecutionFailed += (s, e) => e.Handled = true;
        command.ExecutionFailed += (s, e) => secondHandlerRan = true;

        InvalidOperationException thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => command.ExecuteAsync(null));

        Assert.AreSame(exception, thrown);
        Assert.IsTrue(secondHandlerRan);
    }

    [TestMethod]
    public void Test_AsyncRelayCommand_ExecutionFailed_FaultedWithOperationCanceledExceptionIsRouted()
    {
        // A task that is Faulted (not Canceled) raises the event even when the
        // fault is an OperationCanceledException: the rule is task-status-based.
        OperationCanceledException exception = new();
        AsyncRelayCommand command = new(() => Task.FromException(exception));

        Exception? routedException = null;

        command.ExecutionFailed += (s, e) =>
        {
            routedException = e.Exception;
            e.Handled = true;
        };

        AsyncContext.Run(async () =>
        {
            command.Execute(null);

            try
            {
                await command.ExecutionTask!;
            }
            catch (OperationCanceledException)
            {
            }
        });

        Assert.AreSame(exception, routedException);
    }

    [TestMethod]
    public async Task Test_AsyncRelayCommand_ExecutionFailed_DirectExecuteAsync_HandledStillThrowsToAwaiter()
    {
        InvalidOperationException exception = new("Test");

        AsyncRelayCommand command = new(async () =>
        {
            await Task.CompletedTask;

            throw exception;
        });

        Exception? routedException = null;

        command.ExecutionFailed += (s, e) =>
        {
            routedException = e.Exception;
            e.Handled = true;
        };

        // ExecuteAsync always returns the execution task itself, so a caller awaiting it always observes
        // the fault: the event is still raised (the handler runs), but Handled has no effect here, as it
        // only suppresses the rethrow on the ICommand.Execute path.
        Task task = command.ExecuteAsync(null);

        Assert.AreSame(command.ExecutionTask, task);

        InvalidOperationException thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => task);

        Assert.AreSame(exception, thrown);

        // The handler still ran, and observed the very same exception
        Assert.AreSame(exception, routedException);

        Assert.IsTrue(command.ExecutionTask!.IsFaulted);
        Assert.IsFalse(command.IsRunning);
        Assert.IsTrue(command.CanExecute(null));
    }

    [TestMethod]
    public async Task Test_AsyncRelayCommand_ExecutionFailed_DirectExecuteAsync_NotHandledThrows()
    {
        InvalidOperationException exception = new("Test");

        AsyncRelayCommand command = new(async () =>
        {
            await Task.CompletedTask;

            throw exception;
        });

        Exception? routedException = null;

        command.ExecutionFailed += (s, e) => routedException = e.Exception;

        // With a subscriber that does not mark the fault as handled, the event is raised first and
        // the original exception still surfaces at the await, exactly like the sync rethrow.
        InvalidOperationException thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => command.ExecuteAsync(null));

        Assert.AreSame(exception, thrown);
        Assert.AreSame(exception, routedException);
    }

    [TestMethod]
    public async Task Test_AsyncRelayCommand_ExecutionFailed_DirectExecuteAsync_MultiFaultTaskRoutesAllExceptions()
    {
        InvalidOperationException first = new("First");
        FormatException second = new("Second");

        AsyncRelayCommand command = new(() => Task.WhenAll(Task.FromException(first), Task.FromException(second)));

        Exception? routedException = null;

        command.ExecutionFailed += (s, e) => routedException = e.Exception;

        // Awaiting a task that faulted with several exceptions only surfaces the first one, so the awaiter
        // still sees exactly that (unchanged behavior), but the handler gets the whole AggregateException.
        InvalidOperationException thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => command.ExecuteAsync(null));

        Assert.AreSame(first, thrown);

        AggregateException aggregateException = Assert.IsInstanceOfType<AggregateException>(routedException);

        Assert.HasCount(2, aggregateException.InnerExceptions);
        Assert.Contains(first, aggregateException.InnerExceptions);
        Assert.Contains(second, aggregateException.InnerExceptions);
    }

    [TestMethod]
    public void Test_AsyncRelayCommand_ExecutionFailed_Execute_MultiFaultTaskRoutesAllExceptions()
    {
        InvalidOperationException first = new("First");
        FormatException second = new("Second");

        AsyncRelayCommand command = new(() => Task.WhenAll(Task.FromException(first), Task.FromException(second)));

        Exception? routedException = null;
        Exception? contextException = null;

        command.ExecutionFailed += (s, e) => routedException = e.Exception;

        try
        {
            AsyncContext.Run(async () =>
            {
                command.Execute(null);

                // Deterministically wait for the faulted task, swallowing the exception here: the one that
                // must surface from AsyncContext.Run is the command's own rethrow on the captured context.
                try
                {
                    await command.ExecutionTask!;
                }
                catch (InvalidOperationException)
                {
                }
            });
        }
        catch (Exception e)
        {
            contextException = e;
        }

        // The rethrow on the captured context is a bare 'throw', so it stays the first inner exception,
        // matching what an await would have produced anywhere else in .NET.
        Assert.AreSame(first, contextException);

        AggregateException aggregateException = Assert.IsInstanceOfType<AggregateException>(routedException);

        Assert.HasCount(2, aggregateException.InnerExceptions);
        Assert.Contains(first, aggregateException.InnerExceptions);
        Assert.Contains(second, aggregateException.InnerExceptions);
    }

    [TestMethod]
    public async Task Test_AsyncRelayCommand_ExecutionFailed_DirectExecuteAsync_NoSubscriberThrowsAndPreservesTaskIdentity()
    {
        InvalidOperationException exception = new("Test");

        AsyncRelayCommand command = new(async () =>
        {
            await Task.CompletedTask;

            throw exception;
        });

        Task task = command.ExecuteAsync(null);

        // With no subscribers when ExecuteAsync is invoked, the returned task is the execution task
        // itself (same instance), and the fault propagates to the awaiter unchanged.
        Assert.AreSame(command.ExecutionTask, task);

        InvalidOperationException thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => task);

        Assert.AreSame(exception, thrown);
    }

    [TestMethod]
    public async Task Test_AsyncRelayCommand_ExecutionFailed_DirectExecuteAsync_CancellationDoesNotRaise()
    {
        AsyncRelayCommand command = new(token => Task.Delay(1000, token));

        bool raised = false;

        command.ExecutionFailed += (s, e) => raised = true;

        Task task = command.ExecuteAsync(null);

        command.Cancel();

        // A canceled execution propagates cancellation to the awaiter and never raises the event
        _ = await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => task);

        // The returned task itself completes as canceled, not faulted
        Assert.IsTrue(task.IsCanceled);

        Assert.IsFalse(raised);
    }

    [TestMethod]
    public void Test_AsyncRelayCommand_ExecutionFailed_DirectExecuteAsync_ThrowingHandlerDoesNotAlterAwaitedException()
    {
        InvalidOperationException exception = new("Test");
        NotSupportedException handlerException = new("Handler");

        AsyncRelayCommand command = new(async () =>
        {
            await Task.CompletedTask;

            throw exception;
        });

        command.ExecutionFailed += (s, e) => throw handlerException;

        Exception? awaitedException = null;
        Exception? contextException = null;

        // A handler throwing while the command is merely observing the fault cannot corrupt what the
        // awaiter sees: the returned task is the execution task, so the original exception surfaces
        // there. The handler exception escapes the observer instead, on the captured context.
        try
        {
            AsyncContext.Run(async () =>
            {
                try
                {
                    await command.ExecuteAsync(null);
                }
                catch (Exception e)
                {
                    awaitedException = e;
                }
            });
        }
        catch (Exception e)
        {
            contextException = e;
        }

        Assert.AreSame(exception, awaitedException);
        Assert.AreSame(handlerException, contextException);
    }

    [TestMethod]
    public async Task Test_AsyncRelayCommand_ExecutionFailed_DirectExecuteAsync_SubscriberAttachedAfterInvokeIsNotRouted()
    {
        InvalidOperationException exception = new("Test");
        TaskCompletionSource<object?> tcs = new();

        AsyncRelayCommand command = new(() => tcs.Task);

        Task task = command.ExecuteAsync(null);

        bool raised = false;

        // Subscribers are sampled when ExecuteAsync is invoked: at that point there were none, so
        // the execution task itself was returned and a later subscriber is not routed for it.
        command.ExecutionFailed += (s, e) => raised = true;

        tcs.SetException(exception);

        InvalidOperationException thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => task);

        Assert.AreSame(exception, thrown);
        Assert.IsFalse(raised);
    }

    [TestMethod]
    public async Task Test_AsyncRelayCommand_ExecutionFailed_DirectExecuteAsync_SubscriberDetachedDuringExecutionIsNotRouted()
    {
        // Standard event semantics also apply to the observer: the handler list is read when the fault
        // is observed, not when ExecuteAsync is invoked, so a handler detached while the execution was
        // still in flight is not notified and the original exception surfaces unchanged at the await.
        InvalidOperationException exception = new("Test");
        TaskCompletionSource<object?> tcs = new();

        AsyncRelayCommand command = null!;

        bool raised = false;

        void Handler(object? sender, RelayCommandExceptionEventArgs e)
        {
            raised = true;
            e.Handled = true;
        }

        command = new AsyncRelayCommand(async () =>
        {
            // Yield until the test releases the execution, so the subscriber is guaranteed to still be
            // attached when ExecuteAsync samples it and the execution is observed for the event.
            _ = await tcs.Task;

            command.ExecutionFailed -= Handler;

            throw exception;
        });

        command.ExecutionFailed += Handler;

        Task task = command.ExecuteAsync(null);

        tcs.SetResult(null);

        InvalidOperationException thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => task);

        Assert.AreSame(exception, thrown);
        Assert.IsFalse(raised);
    }

    [TestMethod]
    public async Task Test_AsyncRelayCommand_ExecutionFailed_DirectExecuteAsync_SubscribedPathPreservesTaskIdentity()
    {
        TaskCompletionSource<object?> tcs = new();

        AsyncRelayCommand command = new(() => tcs.Task);

        command.ExecutionFailed += static (s, e) => { };

        Task task = command.ExecuteAsync(null);

        // Subscribing the event must not change what ExecuteAsync hands back: the returned task is
        // always the execution task itself, both while the operation is running and after it has
        // completed. Observing the fault for the event never wraps or replaces it.
        Assert.AreSame(command.ExecutionTask, task);

        tcs.SetResult(null);

        await task;

        Assert.AreSame(command.ExecutionTask, task);
    }

    [TestMethod]
    public void Test_AsyncRelayCommand_ExecutionFailed_HandlerAttachedAfterExecuteIsNotNotified()
    {
        // Whether an execution is observed for this event is decided when the execution starts. With no
        // subscribers at that point, Execute takes the plain awaiter that has no exception handling region
        // at all, so a command that never uses the event costs exactly what it did before the event existed.
        // A handler attached after that decision is therefore not notified for that execution, matching how
        // the ExecuteAsync path has always behaved, and the fault propagates as it normally would.
        TaskCompletionSource<object?> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        AsyncRelayCommand command = new(async () =>
        {
            _ = await tcs.Task;

            throw new InvalidOperationException("Test");
        });

        bool raised = false;

        InvalidOperationException thrown = Assert.ThrowsExactly<InvalidOperationException>(() =>
        {
            AsyncContext.Run(async () =>
            {
                command.Execute(null);

                // Attached after the execution already started, so it is not part of this execution
                command.ExecutionFailed += (s, e) => raised = true;

                tcs.SetResult(null);

                try
                {
                    await command.ExecutionTask!;
                }
                catch (InvalidOperationException)
                {
                }
            });
        });

        Assert.IsNotNull(thrown);
        Assert.IsFalse(raised);
    }

    [TestMethod]
    public void Test_AsyncRelayCommand_ExecutionFailed_HandlerAttachedBeforeExecuteIsNotified()
    {
        // The counterpart: attached before the execution starts, so the routing observer is installed and
        // the handler is notified. This is the boundary the test above pins from the other side.
        InvalidOperationException exception = new("Test");

        AsyncRelayCommand command = new(async () =>
        {
            await Task.CompletedTask;

            throw exception;
        });

        Exception? routedException = null;

        command.ExecutionFailed += (s, e) =>
        {
            routedException = e.Exception;
            e.Handled = true;
        };

        AsyncContext.Run(async () =>
        {
            command.Execute(null);

            try
            {
                await command.ExecutionTask!;
            }
            catch (InvalidOperationException)
            {
            }
        });

        Assert.AreSame(exception, routedException);
    }

    [TestMethod]
    public void Test_AsyncRelayCommand_ExecutionFailed_FlowExceptionsToTaskScheduler_UnhandledFaultDoesNotThrowOnCapturedContext()
    {
        // Regression test: with the flow option set, Execute only awaits the execution task because the
        // event has subscribers. Subscribing must change who gets notified of a fault, never whether that
        // fault escapes, so a handler that leaves it unhandled must not turn the flow option into a
        // rethrow on the captured context (which would take the process down, as the await happens in an
        // async void method). The fault stays observable through ExecutionTask.
        InvalidOperationException exception = new("Test");

        AsyncRelayCommand command = new(async () =>
        {
            await Task.CompletedTask;

            throw exception;
        }, AsyncRelayCommandOptions.FlowExceptionsToTaskScheduler);

        Exception? routedException = null;

        command.ExecutionFailed += (s, e) =>
        {
            routedException = e.Exception;

            // Intentionally leaves e.Handled as false
        };

        // Nothing must escape here: AsyncContext.Run rethrows anything posted to the context
        AsyncContext.Run(async () =>
        {
            command.Execute(null);

            try
            {
                await command.ExecutionTask!;
            }
            catch (InvalidOperationException)
            {
            }
        });

        Assert.AreSame(exception, routedException);
        Assert.IsTrue(command.ExecutionTask!.IsFaulted);
    }

    [TestMethod]
    public void Test_AsyncRelayCommand_ExecutionFailed_UnhandledFaultStillThrowsOnCapturedContextWithoutFlowOption()
    {
        // The counterpart of the test above: without the flow option, an unhandled fault must still be
        // rethrown on the captured context, which is the whole point of awaiting the task on this path.
        InvalidOperationException exception = new("Test");

        AsyncRelayCommand command = new(async () =>
        {
            await Task.CompletedTask;

            throw exception;
        });

        command.ExecutionFailed += (s, e) =>
        {
            // Intentionally leaves e.Handled as false
        };

        InvalidOperationException thrown = Assert.ThrowsExactly<InvalidOperationException>(() =>
        {
            AsyncContext.Run(async () =>
            {
                command.Execute(null);

                try
                {
                    await command.ExecutionTask!;
                }
                catch (InvalidOperationException)
                {
                }
            });
        });

        Assert.AreSame(exception, thrown);
    }

    [TestMethod]
    public void Test_AsyncRelayCommand_ExecutionFailed_FlowExceptionsToTaskScheduler_CancelDoesNotThrowOnCapturedContext()
    {
        // Regression test: with the flow option set, Execute only awaits the execution task because the
        // event has subscribers. A canceled execution never raises the event, so there is nothing to
        // route and nothing to rethrow: the cancellation must not escape to the captured context (which
        // would take the process down, as the await happens in an async void method).
        AsyncRelayCommand command = new(
            token => Task.Delay(System.Threading.Timeout.Infinite, token),
            AsyncRelayCommandOptions.FlowExceptionsToTaskScheduler);

        bool raised = false;

        command.ExecutionFailed += (s, e) => raised = true;

        // Nothing must escape here: AsyncContext.Run rethrows anything posted to the context
        AsyncContext.Run(async () =>
        {
            command.Execute(null);

            command.Cancel();

            try
            {
                await command.ExecutionTask!;
            }
            catch (OperationCanceledException)
            {
            }
        });

        Assert.IsFalse(raised);
    }

    [TestMethod]
    public void Test_AsyncRelayCommand_ExecutionFailed_SubscriptionDoesNotAffectCommandState()
    {
        InvalidOperationException exception = new("Test");
        TaskCompletionSource<object?> tcs = new();

        AsyncRelayCommand command = new(() => tcs.Task);

        command.ExecutionFailed += (s, e) => e.Handled = true;

        // Subscribing (and handling) the event must not change the command state flow:
        // ExecutionTask, IsRunning and CanExecute behave exactly as without a subscriber,
        // and the execution task itself still completes as faulted.
        AsyncContext.Run(async () =>
        {
            Assert.IsTrue(command.CanExecute(null));

            command.Execute(null);

            Assert.IsNotNull(command.ExecutionTask);
            Assert.IsTrue(command.IsRunning);
            Assert.IsFalse(command.CanExecute(null));

            tcs.SetException(exception);

            // Deterministically wait for the command to observe the fault instead of a fixed delay.
            try
            {
                await command.ExecutionTask!;
            }
            catch (InvalidOperationException)
            {
            }

            Assert.IsFalse(command.IsRunning);
            Assert.IsTrue(command.CanExecute(null));
            Assert.IsTrue(command.ExecutionTask!.IsFaulted);
        });
    }
}

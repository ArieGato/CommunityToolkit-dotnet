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
public class Test_AsyncRelayCommandOfT
{
    [TestMethod]
    public async Task Test_AsyncRelayCommandOfT_AlwaysEnabled()
    {
        int ticks = 0;

        AsyncRelayCommand<string>? command = new(async s =>
        {
            await Task.Delay(1000);
            ticks = int.Parse(s!);
            await Task.Delay(1000);
        });

        Assert.IsTrue(command.CanExecute(null));
        Assert.IsTrue(command.CanExecute("1"));

        (object?, EventArgs?) args = default;

        command.CanExecuteChanged += (s, e) => args = (s, e);

        command.NotifyCanExecuteChanged();

        Assert.AreSame(args.Item1, command);
        Assert.AreSame(args.Item2, EventArgs.Empty);

        Assert.IsNull(command.ExecutionTask);
        Assert.IsFalse(command.IsRunning);

        Task task = command.ExecuteAsync((object)"42");

        Assert.IsNotNull(command.ExecutionTask);
        Assert.AreSame(command.ExecutionTask, task);
        Assert.IsTrue(command.IsRunning);

        await task;

        Assert.IsFalse(command.IsRunning);

        Assert.AreEqual(42, ticks);

        command.Execute("2");

        await command.ExecutionTask!;

        Assert.AreEqual(2, ticks);

        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.CanExecute(new object()), "parameter");
        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.CanExecute(42), "parameter");
        
        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.Execute(new object()), "parameter");
        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.Execute(42), "parameter");
    }

    [TestMethod]
    public void Test_AsyncRelayCommandOfT_WithCanExecuteFunctionTrue()
    {
        int ticks = 0;

        AsyncRelayCommand<string>? command = new(
            s =>
            {
                ticks = int.Parse(s!);
                return Task.CompletedTask;
            }, s => true);

        Assert.IsTrue(command.CanExecute(null));
        Assert.IsTrue(command.CanExecute("1"));

        command.Execute("42");

        Assert.AreEqual(42, ticks);

        command.Execute("2");

        Assert.AreEqual(2, ticks);

        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.CanExecute(new object()), "parameter");
        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.CanExecute(42), "parameter");
        
        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.Execute(new object()), "parameter");
        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.Execute(42), "parameter");
    }

    [TestMethod]
    public void Test_AsyncRelayCommandOfT_WithCanExecuteFunctionFalse()
    {
        int ticks = 0;

        AsyncRelayCommand<string>? command = new(
            s =>
            {
                ticks = int.Parse(s!);
                return Task.CompletedTask;
            }, s => false);

        Assert.IsFalse(command.CanExecute(null));
        Assert.IsFalse(command.CanExecute("1"));

        command.Execute("2");

        // Like in the RelayCommand test, ensure Execute is unconditionally invoked
        Assert.AreEqual(2, ticks);

        command.Execute("42");

        Assert.AreEqual(42, ticks);

        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.CanExecute(new object()), "parameter");
        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.CanExecute(42), "parameter");
        
        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.Execute(new object()), "parameter");
        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.Execute(42), "parameter");
    }

    [TestMethod]
    public void Test_AsyncRelayCommandOfT_InvalidArgumentWithValueType()
    {
        int n = 0;

        AsyncRelayCommand<int>? command = new(i =>
        {
            n = i;
            return Task.CompletedTask;
        });

        // Special case
        Assert.IsFalse(command.CanExecute(null));

        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.CanExecute("Hello"), "parameter");
        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.CanExecute(3.14f), "parameter");

        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.Execute(null), "parameter");
        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.Execute("Hello"), "parameter");
        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.Execute(3.14f), "parameter");
    }

    [TestMethod]
    public void Test_AsyncRelayCommandOfT_InvalidArgumentWithValueType_WithCanExecute()
    {
        int n = 0;

        AsyncRelayCommand<int>? command = new(
            i =>
            {
                n = i;
                return Task.CompletedTask;
            }, i => i > 0);

        // Special case
        Assert.IsFalse(command.CanExecute(null));

        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.CanExecute("Hello"), "parameter");
        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.CanExecute(3.14f), "parameter");

        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.Execute(null), "parameter");
        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.Execute("Hello"), "parameter");
        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.Execute(3.14f), "parameter");
    }

    [TestMethod]
    public async Task Test_AsyncRelayCommandOfT_WithCancellation()
    {
        // See comments in Test_AsyncRelayCommand_WithCancellation for the logic below
        TaskCompletionSource<object?> tcs = new();
        AsyncRelayCommand<string> command = new((s, token) => tcs.Task);

        List<PropertyChangedEventArgs> args = new();

        command.PropertyChanged += (s, e) => args.Add(e);

        Assert.IsTrue(command.CanExecute(null));
        Assert.IsTrue(command.CanExecute("Hello"));

        Assert.IsFalse(command.CanBeCanceled);
        Assert.IsFalse(command.IsCancellationRequested);

        command.Execute(null);

        Assert.IsTrue(command.CanBeCanceled);
        Assert.IsFalse(command.IsCancellationRequested);

        Assert.HasCount(4, args);
        Assert.AreEqual(nameof(IAsyncRelayCommand.ExecutionTask), args[0].PropertyName);
        Assert.AreEqual(nameof(IAsyncRelayCommand.IsRunning), args[1].PropertyName);
        Assert.AreEqual(nameof(IAsyncRelayCommand.CanBeCanceled), args[2].PropertyName);
        Assert.AreEqual(nameof(IAsyncRelayCommand.IsCancellationRequested), args[3].PropertyName);

        command.Cancel();

        Assert.HasCount(6, args);
        Assert.AreEqual(nameof(IAsyncRelayCommand.CanBeCanceled), args[4].PropertyName);
        Assert.AreEqual(nameof(IAsyncRelayCommand.IsCancellationRequested), args[5].PropertyName);

        Assert.IsTrue(command.IsCancellationRequested);

        tcs.SetResult(null);

        await command.ExecutionTask!;

        Assert.IsFalse(command.CanBeCanceled);
        Assert.IsTrue(command.IsCancellationRequested);
    }

    [TestMethod]
    public async Task Test_AsyncRelayCommandOfT_AllowConcurrentExecutions_Disabled()
    {
        await Test_AsyncRelayCommandOfT_AllowConcurrentExecutions_TestLogic(static task => new(async _ => await task, AsyncRelayCommandOptions.None));
    }

    [TestMethod]
    public async Task Test_AsyncRelayCommandOfT_AllowConcurrentExecutions_Default()
    {
        await Test_AsyncRelayCommandOfT_AllowConcurrentExecutions_TestLogic(static task => new(async _ => await task));
    }

    /// <summary>
    /// Shared logic for <see cref="Test_AsyncRelayCommandOfT_AllowConcurrentExecutions_Disabled"/> and <see cref="Test_AsyncRelayCommandOfT_AllowConcurrentExecutions_Default"/>.
    /// </summary>
    /// <param name="factory">A factory to create the <see cref="AsyncRelayCommand{T}"/> instance to test.</param>
    private static async Task Test_AsyncRelayCommandOfT_AllowConcurrentExecutions_TestLogic(Func<Task, AsyncRelayCommand<string>> factory)
    {
        TaskCompletionSource<object?> tcs = new();

        AsyncRelayCommand<string> command = factory(tcs.Task);

        Assert.IsTrue(command.CanExecute(null));
        Assert.IsTrue(command.CanExecute("1"));

        (object?, EventArgs?) args = default;

        command.CanExecuteChanged += (s, e) => args = (s, e);

        command.NotifyCanExecuteChanged();

        Assert.AreSame(args.Item1, command);
        Assert.AreSame(args.Item2, EventArgs.Empty);

        Assert.IsNull(command.ExecutionTask);
        Assert.IsFalse(command.IsRunning);

        Task task = command.ExecuteAsync((object)"42");

        Assert.IsNotNull(command.ExecutionTask);
        Assert.AreSame(command.ExecutionTask, task);
        Assert.IsTrue(command.IsRunning);

        // The command can't be executed now, as there's a pending operation
        Assert.IsFalse(command.CanExecute(null));
        Assert.IsFalse(command.CanExecute("2"));

        Assert.IsFalse(command.CanBeCanceled);
        Assert.IsFalse(command.IsCancellationRequested);

        tcs.SetResult(null);

        await task;

        Assert.IsFalse(command.IsRunning);

        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.CanExecute(new object()), "parameter");
        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.CanExecute(3.14f), "parameter");

        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.Execute(new object()), "parameter");
        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.Execute(3.14f), "parameter");
    }

    [TestMethod]
    public void Test_AsyncRelayCommandOfT_EnsureExceptionThrown_Synchronously()
    {
        Exception? executeException = null;

        AsyncRelayCommand<int> command = new(async delay =>
        {
            await Task.CompletedTask;

            throw new Exception(nameof(Test_AsyncRelayCommandOfT_EnsureExceptionThrown_Synchronously));
        });

        try
        {
            AsyncContext.Run(async () =>
            {
                command.Execute((object)42);

                await Task.Delay(500);
            });
        }
        catch (Exception e)
        {
            executeException = e;
        }

        Assert.AreEqual(nameof(Test_AsyncRelayCommandOfT_EnsureExceptionThrown_Synchronously), executeException?.Message);
    }

    // See https://github.com/CommunityToolkit/dotnet/pull/251
    [TestMethod]
    public async Task Test_AsyncRelayCommandOfT_EnsureExceptionThrown()
    {
        const int delay = 500;

        Exception? executeException = null;
        Exception? executeAsyncException = null;

        AsyncRelayCommand<int> command = new(async delay =>
        {
            await Task.Delay(delay);

            throw new Exception(nameof(Test_AsyncRelayCommandOfT_EnsureExceptionThrown));
        });

        try
        {
            AsyncContext.Run(async () =>
            {
                command.Execute((object)delay);

                await Task.Delay(delay * 2);
            });
        }
        catch (Exception e)
        {
            executeException = e;
        }

        executeAsyncException = await Assert.ThrowsExactlyAsync<Exception>(() => command.ExecuteAsync((object)delay));

        Assert.AreEqual(nameof(Test_AsyncRelayCommandOfT_EnsureExceptionThrown), executeException?.Message);
        Assert.AreEqual(nameof(Test_AsyncRelayCommandOfT_EnsureExceptionThrown), executeAsyncException?.Message);
    }

    [TestMethod]
    public async Task Test_AsyncRelayCommandOfT_EnsureExceptionThrown_GenericExecute()
    {
        const int delay = 500;

        Exception? executeTException = null;
        Exception? executeAsyncException = null;

        AsyncRelayCommand<int> command = new(async delay =>
        {
            await Task.Delay(delay);

            throw new Exception(nameof(Test_AsyncRelayCommandOfT_EnsureExceptionThrown_GenericExecute));
        });

        try
        {
            AsyncContext.Run(async () =>
            {
                command.Execute(delay);

                await Task.Delay(delay * 2);
            });
        }
        catch (Exception e)
        {
            executeTException = e;
        }

        executeAsyncException = await Assert.ThrowsExactlyAsync<Exception>(() => command.ExecuteAsync(delay));

        Assert.AreEqual(nameof(Test_AsyncRelayCommandOfT_EnsureExceptionThrown_GenericExecute), executeTException?.Message);
        Assert.AreEqual(nameof(Test_AsyncRelayCommandOfT_EnsureExceptionThrown_GenericExecute), executeAsyncException?.Message);
    }

    [TestMethod]
    public async Task Test_AsyncRelayCommandOfT_ThrowingTaskBubblesToUnobservedTaskException()
    {
        static async Task TestMethodAsync(Action action)
        {
            await Task.Delay(100);

            action();
        }

        async void TestCallback(Action throwAction, Action completeAction)
        {
            AsyncRelayCommand<string> command = new(s => TestMethodAsync(throwAction), AsyncRelayCommandOptions.FlowExceptionsToTaskScheduler);

            command.Execute(null);

            await Task.Delay(200);

            completeAction();
        }

        bool success = await TaskSchedulerTestHelper.IsExceptionBubbledUpToUnobservedTaskExceptionAsync(TestCallback);

        Assert.IsTrue(success);
    }

    [TestMethod]
    public async Task Test_AsyncRelayCommandOfT_ThrowingTaskBubblesToUnobservedTaskException_Synchronously()
    {
        static async Task TestMethodAsync(Action action)
        {
            await Task.CompletedTask;

            action();
        }

        async void TestCallback(Action throwAction, Action completeAction)
        {
            AsyncRelayCommand<string> command = new(s => TestMethodAsync(throwAction), AsyncRelayCommandOptions.FlowExceptionsToTaskScheduler);

            command.Execute(null);

            await Task.Delay(200);

            completeAction();
        }

        bool success = await TaskSchedulerTestHelper.IsExceptionBubbledUpToUnobservedTaskExceptionAsync(TestCallback);

        Assert.IsTrue(success);
    }

    public async Task Test_AsyncRelayCommand_ExecuteDoesNotRaiseCanExecuteChanged()
    {
        TaskCompletionSource<object?> tcs = new();

        AsyncRelayCommand<string> command = new(s => tcs.Task, AsyncRelayCommandOptions.AllowConcurrentExecutions);

        (object? Sender, EventArgs? Args) args = default;

        command.CanExecuteChanged += (s, e) => args = (s, e);

        Assert.IsTrue(command.CanExecute(""));

        command.Execute("");

        Assert.IsNull(args.Sender);
        Assert.IsNull(args.Args);

        args = default;

        Assert.IsTrue(command.CanExecute(""));

        tcs.SetResult(null);

        _ = await tcs.Task;

        // CanExecute isn't raised when the command completes
        Assert.IsNull(args.Sender);
        Assert.IsNull(args.Args);
    }

    [TestMethod]
    public async Task Test_AsyncRelayCommand_ExecuteWithoutConcurrencyRaisesCanExecuteChanged()
    {
        TaskCompletionSource<object?> tcs = new();

        AsyncRelayCommand<string> command = new(s => tcs.Task, AsyncRelayCommandOptions.None);

        (object? Sender, EventArgs? Args) args = default;

        command.CanExecuteChanged += (s, e) => args = (s, e);

        Assert.IsTrue(command.CanExecute(""));

        command.Execute("");

        Assert.AreSame(command, args.Sender);
        Assert.AreSame(EventArgs.Empty, args.Args);

        Assert.IsFalse(command.CanExecute(""));

        args = default;

        tcs.SetResult(null);

        _ = await tcs.Task;

        // CanExecute is raised again when the command completes
        Assert.AreSame(command, args.Sender);
        Assert.AreSame(EventArgs.Empty, args.Args);
    }

    [TestMethod]
    public void Test_AsyncRelayCommand_ExecuteDoesNotRaiseCanExecuteChanged_WithCancellation()
    {
        TaskCompletionSource<object?> tcs = new();

        AsyncRelayCommand<string> command = new((s, token) => tcs.Task, AsyncRelayCommandOptions.AllowConcurrentExecutions);

        (object? Sender, EventArgs? Args) args = default;

        command.CanExecuteChanged += (s, e) => args = (s, e);

        Assert.IsTrue(command.CanExecute(""));

        command.Execute("");

        Assert.IsNull(args.Sender);
        Assert.IsNull(args.Args);

        Assert.IsTrue(command.CanExecute(""));

        tcs.SetResult(null);
    }

    [TestMethod]
    public void Test_AsyncRelayCommand_ExecuteWithoutConcurrencyRaisesCanExecuteChanged_WithToken()
    {
        TaskCompletionSource<object?> tcs = new();

        AsyncRelayCommand<string> command = new((s, token) => tcs.Task, AsyncRelayCommandOptions.None);

        (object? Sender, EventArgs? Args) args = default;

        command.CanExecuteChanged += (s, e) => args = (s, e);

        Assert.IsTrue(command.CanExecute(""));

        command.Execute("");

        Assert.AreSame(command, args.Sender);
        Assert.AreSame(EventArgs.Empty, args.Args);

        Assert.IsFalse(command.CanExecute(""));

        tcs.SetResult(null);
    }

    [TestMethod]
    public void Test_AsyncRelayCommandOfT_GetCancelCommand_DisabledCommand()
    {
        TaskCompletionSource<object?> tcs = new();

        AsyncRelayCommand<string> command = new(s => tcs.Task);

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
    public void Test_AsyncRelayCommandOfT_GetCancelCommand_WithToken()
    {
        TaskCompletionSource<object?> tcs = new();

        AsyncRelayCommand<string> command = new((s, token) => tcs.Task);

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
    public void Test_AsyncRelayCommandOfT_ExecutionFailed_HandledSuppressesException()
    {
        InvalidOperationException exception = new("Test");

        AsyncRelayCommand<string> command = new(async s =>
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

        AsyncContext.Run(async () =>
        {
            command.Execute("text");

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
    public void Test_AsyncRelayCommandOfT_ExecutionFailed_ExposesStronglyTypedParameter()
    {
        InvalidOperationException exception = new("Test");

        AsyncRelayCommand<string> command = new(async s =>
        {
            await Task.CompletedTask;

            throw exception;
        });

        string? routedParameter = null;

        command.ExecutionFailed += (s, e) =>
        {
            // e is RelayCommandExceptionEventArgs<string>: Parameter is strongly typed, no cast needed.
            routedParameter = e.Parameter;
            e.Handled = true;
        };

        AsyncContext.Run(async () =>
        {
            command.Execute("text");

            try
            {
                await command.ExecutionTask!;
            }
            catch (InvalidOperationException)
            {
            }
        });

        Assert.AreEqual("text", routedParameter);
    }

    [TestMethod]
    public async Task Test_AsyncRelayCommandOfT_ExecutionFailed_DirectExecuteAsync_HandledStillThrowsToAwaiterAndExposesParameter()
    {
        InvalidOperationException exception = new("Test");

        AsyncRelayCommand<string> command = new(async s =>
        {
            await Task.CompletedTask;

            throw exception;
        });

        string? routedParameter = null;

        command.ExecutionFailed += (s, e) =>
        {
            // e is RelayCommandExceptionEventArgs<string>: Parameter is strongly typed on the awaited path too
            routedParameter = e.Parameter;
            e.Handled = true;
        };

        // ExecuteAsync always returns the execution task itself, so the fault is always delivered to the
        // awaiter: the handler still runs (and sees the parameter), but Handled has no effect here, as it
        // only suppresses the rethrow on the ICommand.Execute path.
        Task task = command.ExecuteAsync("text");

        Assert.AreSame(command.ExecutionTask, task);

        InvalidOperationException thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => task);

        Assert.AreSame(exception, thrown);
        Assert.AreEqual("text", routedParameter);
        Assert.IsTrue(command.ExecutionTask!.IsFaulted);
    }

    [TestMethod]
    public async Task Test_AsyncRelayCommandOfT_ExecutionFailed_DirectExecuteAsync_NotHandledThrows()
    {
        InvalidOperationException exception = new("Test");

        AsyncRelayCommand<string> command = new(async s =>
        {
            await Task.CompletedTask;

            throw exception;
        });

        Exception? routedException = null;

        command.ExecutionFailed += (s, e) => routedException = e.Exception;

        InvalidOperationException thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => command.ExecuteAsync("text"));

        Assert.AreSame(exception, thrown);
        Assert.AreSame(exception, routedException);
    }

    [TestMethod]
    public async Task Test_AsyncRelayCommandOfT_ExecutionFailed_DirectExecuteAsync_MultiFaultTaskRoutesAllExceptions()
    {
        InvalidOperationException first = new("First");
        FormatException second = new("Second");

        AsyncRelayCommand<string> command = new(s => Task.WhenAll(Task.FromException(first), Task.FromException(second)));

        Exception? routedException = null;
        string? routedParameter = null;

        command.ExecutionFailed += (s, e) =>
        {
            routedException = e.Exception;
            routedParameter = e.Parameter;
        };

        // Awaiting a task that faulted with several exceptions only surfaces the first one, so the awaiter
        // still sees exactly that (unchanged behavior), but the handler gets the whole AggregateException.
        InvalidOperationException thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => command.ExecuteAsync("text"));

        Assert.AreSame(first, thrown);
        Assert.AreEqual("text", routedParameter);

        AggregateException aggregateException = Assert.IsInstanceOfType<AggregateException>(routedException);

        Assert.HasCount(2, aggregateException.InnerExceptions);
        Assert.Contains(first, aggregateException.InnerExceptions);
        Assert.Contains(second, aggregateException.InnerExceptions);
    }

    [TestMethod]
    public void Test_AsyncRelayCommandOfT_ExecutionFailed_Execute_MultiFaultTaskRoutesAllExceptions()
    {
        InvalidOperationException first = new("First");
        FormatException second = new("Second");

        AsyncRelayCommand<string> command = new(s => Task.WhenAll(Task.FromException(first), Task.FromException(second)));

        Exception? routedException = null;
        string? routedParameter = null;
        Exception? contextException = null;

        command.ExecutionFailed += (s, e) =>
        {
            routedException = e.Exception;
            routedParameter = e.Parameter;
        };

        try
        {
            AsyncContext.Run(async () =>
            {
                command.Execute("text");

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
        Assert.AreEqual("text", routedParameter);

        AggregateException aggregateException = Assert.IsInstanceOfType<AggregateException>(routedException);

        Assert.HasCount(2, aggregateException.InnerExceptions);
        Assert.Contains(first, aggregateException.InnerExceptions);
        Assert.Contains(second, aggregateException.InnerExceptions);
    }

    [TestMethod]
    public void Test_AsyncRelayCommandOfT_ExecutionFailed_DirectExecuteAsync_ThrowingHandlerDoesNotAlterAwaitedException()
    {
        InvalidOperationException exception = new("Test");
        NotSupportedException handlerException = new("Handler");

        AsyncRelayCommand<string> command = new(async s =>
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
                    await command.ExecuteAsync("text");
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
    public async Task Test_AsyncRelayCommandOfT_ExecutionFailed_DirectExecuteAsync_CancellationDoesNotRaise()
    {
        AsyncRelayCommand<string> command = new((s, token) => Task.Delay(1000, token));

        bool raised = false;

        command.ExecutionFailed += (s, e) => raised = true;

        Task task = command.ExecuteAsync("text");

        command.Cancel();

        // A canceled execution propagates cancellation to the awaiter and never raises the event
        _ = await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => task);

        // The returned task itself completes as canceled, not faulted
        Assert.IsTrue(task.IsCanceled);

        Assert.IsFalse(raised);
    }

    [TestMethod]
    public async Task Test_AsyncRelayCommandOfT_ExecutionFailed_Multicast_AllHandlersSeeTheSameParameter()
    {
        // The same RelayCommandExceptionEventArgs<T> instance is shared by the whole multicast
        // invocation, so every handler observes the strongly typed parameter, and the Handled state
        // set by an earlier handler is visible to a later one.
        InvalidOperationException exception = new("Test");

        AsyncRelayCommand<string> command = new(async s =>
        {
            await Task.CompletedTask;

            throw exception;
        });

        string? firstParameter = null;
        string? secondParameter = null;
        bool handledWhenSecondHandlerRan = false;

        command.ExecutionFailed += (s, e) =>
        {
            firstParameter = e.Parameter;
            e.Handled = true;
        };

        command.ExecutionFailed += (s, e) =>
        {
            secondParameter = e.Parameter;
            handledWhenSecondHandlerRan = e.Handled;
        };

        // The awaited task still faults, as Handled does not apply to this path
        InvalidOperationException thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => command.ExecuteAsync("text"));

        Assert.AreSame(exception, thrown);
        Assert.AreEqual("text", firstParameter);
        Assert.AreEqual("text", secondParameter);
        Assert.IsTrue(handledWhenSecondHandlerRan);
    }

    [TestMethod]
    public void Test_AsyncRelayCommandOfT_ExecutionFailed_NotHandledRethrowsOnCapturedContext()
    {
        InvalidOperationException exception = new("Test");

        AsyncRelayCommand<string> command = new(async s =>
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
                command.Execute("text");

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
    public void Test_AsyncRelayCommandOfT_ExecutionFailed_CancellationDoesNotRaise()
    {
        AsyncRelayCommand<string> command = new((s, token) => Task.Delay(1000, token));

        bool raised = false;

        command.ExecutionFailed += (s, e) => raised = true;

        try
        {
            AsyncContext.Run(async () =>
            {
                command.Execute("text");

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
    public async Task Test_AsyncRelayCommandOfT_ExecutionFailed_SuccessfulExecutionDoesNotRaise()
    {
        // The delegate returns an already completed task, so both the Execute and the ExecuteAsync
        // observers run to completion synchronously and the assertions below need no extra waiting.
        int executions = 0;

        AsyncRelayCommand<string> command = new(s =>
        {
            executions++;

            return Task.CompletedTask;
        });

        bool raised = false;

        command.ExecutionFailed += (s, e) => raised = true;

        command.Execute("text");

        // Assert the delegate actually ran, so this can distinguish "the event was
        // not raised because the execution succeeded" from "nothing ran at all".
        Assert.AreEqual(1, executions);
        Assert.IsFalse(raised);

        await command.ExecuteAsync("text");

        Assert.AreEqual(2, executions);
        Assert.IsFalse(raised);
    }

    [TestMethod]
    public void Test_AsyncRelayCommandOfT_ExecutionFailed_FlowExceptionsToTaskScheduler_SubscriberReceivesFault()
    {
        InvalidOperationException exception = new("Test");

        AsyncRelayCommand<string> command = new(async s =>
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
            command.Execute("text");

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
    public void Test_AsyncRelayCommandOfT_ExecutionFailed_FaultedWithOperationCanceledExceptionIsRouted()
    {
        // A task that is Faulted (not Canceled) raises the event even when the
        // fault is an OperationCanceledException: the rule is task-status-based.
        OperationCanceledException exception = new();
        AsyncRelayCommand<string> command = new(s => Task.FromException(exception));

        Exception? routedException = null;

        command.ExecutionFailed += (s, e) =>
        {
            routedException = e.Exception;
            e.Handled = true;
        };

        AsyncContext.Run(async () =>
        {
            command.Execute("text");

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
    public async Task Test_AsyncRelayCommandOfT_ExecutionFailed_DirectExecuteAsync_SubscriberDetachedDuringExecutionIsNotRouted()
    {
        // Standard event semantics also apply to the observer: the handler list is read when the fault
        // is observed, not when ExecuteAsync is invoked, so a handler detached while the execution was
        // still in flight is not notified and the original exception surfaces unchanged at the await.
        InvalidOperationException exception = new("Test");
        TaskCompletionSource<object?> tcs = new();

        AsyncRelayCommand<string> command = null!;

        bool raised = false;

        void Handler(object? sender, RelayCommandExceptionEventArgs<string> e)
        {
            raised = true;
            e.Handled = true;
        }

        command = new AsyncRelayCommand<string>(async s =>
        {
            // Yield until the test releases the execution, so the subscriber is guaranteed to still be
            // attached when ExecuteAsync samples it and the execution is observed for the event.
            _ = await tcs.Task;

            command.ExecutionFailed -= Handler;

            throw exception;
        });

        command.ExecutionFailed += Handler;

        Task task = command.ExecuteAsync("text");

        tcs.SetResult(null);

        InvalidOperationException thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => task);

        Assert.AreSame(exception, thrown);
        Assert.IsFalse(raised);
    }

    [TestMethod]
    public async Task Test_AsyncRelayCommandOfT_ExecutionFailed_DirectExecuteAsync_SubscriberAttachedAfterInvokeIsNotRouted()
    {
        InvalidOperationException exception = new("Test");
        TaskCompletionSource<object?> tcs = new();

        AsyncRelayCommand<string> command = new(s => tcs.Task);

        Task task = command.ExecuteAsync("text");

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
    public void Test_AsyncRelayCommandOfT_ExecutionFailed_InvalidCommandArgumentIsNotRouted()
    {
        // The ArgumentException produced by an incompatible command parameter is a usage
        // error raised before the wrapped delegate runs, so it must NOT be routed.
        AsyncRelayCommand<string> command = new(static s => Task.CompletedTask);

        bool raised = false;

        command.ExecutionFailed += (s, e) => raised = true;

        _ = Assert.ThrowsExactly<ArgumentException>(() => command.Execute(42));

        Assert.IsFalse(raised);
    }

    [TestMethod]
    public void Test_AsyncRelayCommandOfT_ExecutionFailed_DirectExecuteAsync_InvalidCommandArgumentIsNotRouted()
    {
        // The ExecuteAsync(object?) entry point does its own cast, so the same usage error is raised
        // synchronously there as well, before any execution task exists that could be routed.
        AsyncRelayCommand<string> command = new(static s => Task.CompletedTask);

        bool raised = false;

        command.ExecutionFailed += (s, e) => raised = true;

        _ = Assert.ThrowsExactly<ArgumentException>(() => _ = command.ExecuteAsync(42));

        Assert.IsFalse(raised);
    }

    [TestMethod]
    public async Task Test_AsyncRelayCommandOfT_ExecutionFailed_DirectExecuteAsync_NoSubscriberThrowsAndPreservesTaskIdentity()
    {
        InvalidOperationException exception = new("Test");

        AsyncRelayCommand<string> command = new(async s =>
        {
            await Task.CompletedTask;

            throw exception;
        });

        Task task = command.ExecuteAsync("text");

        // With no subscribers when ExecuteAsync is invoked, the returned task is the execution task
        // itself (same instance), and the fault propagates to the awaiter unchanged.
        Assert.AreSame(command.ExecutionTask, task);

        InvalidOperationException thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => task);

        Assert.AreSame(exception, thrown);
    }

    [TestMethod]
    public async Task Test_AsyncRelayCommandOfT_ExecutionFailed_DirectExecuteAsync_SubscribedPathPreservesTaskIdentity()
    {
        TaskCompletionSource<object?> tcs = new();

        AsyncRelayCommand<string> command = new(s => tcs.Task);

        command.ExecutionFailed += static (s, e) => { };

        Task task = command.ExecuteAsync("text");

        // Subscribing the event must not change what ExecuteAsync hands back: the returned task is
        // always the execution task itself, both while the operation is running and after it has
        // completed. Observing the fault for the event never wraps or replaces it.
        Assert.AreSame(command.ExecutionTask, task);

        tcs.SetResult(null);

        await task;

        Assert.AreSame(command.ExecutionTask, task);
    }

    [TestMethod]
    public void Test_AsyncRelayCommandOfT_ExecutionFailed_HandlerAttachedAfterExecuteIsNotNotified()
    {
        // Whether an execution is observed for this event is decided when the execution starts. With no
        // subscribers at that point, Execute takes the plain awaiter that has no exception handling region
        // at all, so a command that never uses the event costs exactly what it did before the event existed.
        // A handler attached after that decision is therefore not notified for that execution, matching how
        // the ExecuteAsync path has always behaved, and the fault propagates as it normally would.
        TaskCompletionSource<object?> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        AsyncRelayCommand<string> command = new(async _ =>
        {
            await tcs.Task;

            throw new InvalidOperationException("Test");
        });

        bool raised = false;

        InvalidOperationException thrown = Assert.ThrowsExactly<InvalidOperationException>(() =>
        {
            AsyncContext.Run(async () =>
            {
                command.Execute("text");

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
    public void Test_AsyncRelayCommandOfT_ExecutionFailed_HandlerAttachedBeforeExecuteIsNotified()
    {
        // The counterpart: attached before the execution starts, so the routing observer is installed and
        // the handler is notified, with the parameter that was passed to the failing execution.
        InvalidOperationException exception = new("Test");

        AsyncRelayCommand<string> command = new(async _ =>
        {
            await Task.CompletedTask;

            throw exception;
        });

        Exception? routedException = null;
        string? routedParameter = null;

        command.ExecutionFailed += (s, e) =>
        {
            routedException = e.Exception;
            routedParameter = e.Parameter;
            e.Handled = true;
        };

        AsyncContext.Run(async () =>
        {
            command.Execute("text");

            try
            {
                await command.ExecutionTask!;
            }
            catch (InvalidOperationException)
            {
            }
        });

        Assert.AreSame(exception, routedException);
        Assert.AreEqual("text", routedParameter);
    }

    [TestMethod]
    public void Test_AsyncRelayCommandOfT_ExecutionFailed_FlowExceptionsToTaskScheduler_UnhandledFaultDoesNotThrowOnCapturedContext()
    {
        // Regression test: with the flow option set, Execute only awaits the execution task because the
        // event has subscribers. Subscribing must change who gets notified of a fault, never whether that
        // fault escapes, so a handler that leaves it unhandled must not turn the flow option into a
        // rethrow on the captured context (which would take the process down, as the await happens in an
        // async void method). The fault stays observable through ExecutionTask.
        InvalidOperationException exception = new("Test");

        AsyncRelayCommand<string> command = new(async _ =>
        {
            await Task.CompletedTask;

            throw exception;
        }, AsyncRelayCommandOptions.FlowExceptionsToTaskScheduler);

        Exception? routedException = null;
        string? routedParameter = null;

        command.ExecutionFailed += (s, e) =>
        {
            routedException = e.Exception;
            routedParameter = e.Parameter;

            // Intentionally leaves e.Handled as false
        };

        // Nothing must escape here: AsyncContext.Run rethrows anything posted to the context
        AsyncContext.Run(async () =>
        {
            command.Execute("text");

            try
            {
                await command.ExecutionTask!;
            }
            catch (InvalidOperationException)
            {
            }
        });

        Assert.AreSame(exception, routedException);
        Assert.AreEqual("text", routedParameter);
        Assert.IsTrue(command.ExecutionTask!.IsFaulted);
    }

    [TestMethod]
    public void Test_AsyncRelayCommandOfT_ExecutionFailed_UnhandledFaultStillThrowsOnCapturedContextWithoutFlowOption()
    {
        // The counterpart of the test above: without the flow option, an unhandled fault must still be
        // rethrown on the captured context, which is the whole point of awaiting the task on this path.
        InvalidOperationException exception = new("Test");

        AsyncRelayCommand<string> command = new(async _ =>
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
                command.Execute("text");

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
    public void Test_AsyncRelayCommandOfT_ExecutionFailed_FlowExceptionsToTaskScheduler_CancelDoesNotThrowOnCapturedContext()
    {
        // Regression test: with the flow option set, Execute only awaits the execution task because the
        // event has subscribers. A canceled execution never raises the event, so there is nothing to
        // route and nothing to rethrow: the cancellation must not escape to the captured context (which
        // would take the process down, as the await happens in an async void method).
        AsyncRelayCommand<string> command = new(
            (s, token) => Task.Delay(System.Threading.Timeout.Infinite, token),
            AsyncRelayCommandOptions.FlowExceptionsToTaskScheduler);

        bool raised = false;

        command.ExecutionFailed += (s, e) => raised = true;

        // Nothing must escape here: AsyncContext.Run rethrows anything posted to the context
        AsyncContext.Run(async () =>
        {
            command.Execute("text");

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
}

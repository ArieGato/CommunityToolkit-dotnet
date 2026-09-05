// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using CommunityToolkit.Mvvm.Input.Internals;

#pragma warning disable CS0618, CA1001

namespace CommunityToolkit.Mvvm.Input;

/// <summary>
/// A generic command that provides a more specific version of <see cref="AsyncRelayCommand"/>.
/// </summary>
/// <typeparam name="T">The type of parameter being passed as input to the callbacks.</typeparam>
public sealed partial class AsyncRelayCommand<T> : IAsyncRelayCommand<T>, ICancellationAwareCommand
{
    /// <summary>
    /// The <see cref="Func{TResult}"/> to invoke when <see cref="Execute(T)"/> is used.
    /// </summary>
    private readonly Func<T?, Task>? execute;

    /// <summary>
    /// The cancelable <see cref="Func{T1,T2,TResult}"/> to invoke when <see cref="Execute(object?)"/> is used.
    /// </summary>
    private readonly Func<T?, CancellationToken, Task>? cancelableExecute;

    /// <summary>
    /// The optional action to invoke when <see cref="CanExecute(T)"/> is used.
    /// </summary>
    private readonly Predicate<T?>? canExecute;

    /// <summary>
    /// The options being set for the current command.
    /// </summary>
    private readonly AsyncRelayCommandOptions options;

    /// <summary>
    /// The <see cref="CancellationTokenSource"/> instance to use to cancel <see cref="cancelableExecute"/>.
    /// </summary>
    private CancellationTokenSource? cancellationTokenSource;

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <inheritdoc/>
    public event EventHandler? CanExecuteChanged;

    /// <summary>
    /// Raised when the execution task of the command completes in a faulted state. If at least one
    /// handler is attached when the fault is observed, the exception is passed to the event. On the
    /// <see cref="Execute(T?)"/> path, the exception is then only rethrown (on the captured context)
    /// if no handler sets <see cref="RelayCommandExceptionEventArgs.Handled"/> to <see langword="true"/>,
    /// and the command was not created with
    /// <see cref="AsyncRelayCommandOptions.FlowExceptionsToTaskScheduler"/>, which suppresses that rethrow
    /// regardless of what any handler does.
    /// When the task returned by <see cref="ExecuteAsync(T?)"/> is awaited directly, the event is
    /// raised as well, but the exception is always delivered to the awaiter:
    /// <see cref="ExecuteAsync(T?)"/> always returns the execution task itself (the very same instance
    /// as <see cref="ExecutionTask"/>), so the caller observes the fault through it and
    /// <see cref="RelayCommandExceptionEventArgs.Handled"/> has no effect on that path. That is,
    /// <see cref="RelayCommandExceptionEventArgs.Handled"/> only affects the
    /// <see cref="System.Windows.Input.ICommand.Execute(object?)"/> path, which is the one where nobody
    /// else could observe the fault. If no handler is attached, exceptions propagate exactly as before:
    /// rethrown on the calling context by default, or flowing to
    /// <see cref="System.Threading.Tasks.TaskScheduler.UnobservedTaskException"/> when
    /// <see cref="AsyncRelayCommandOptions.FlowExceptionsToTaskScheduler"/> is used. A canceled execution
    /// never raises this event. The event arguments are <see cref="RelayCommandExceptionEventArgs{T}"/>,
    /// exposing through <see cref="RelayCommandExceptionEventArgs{T}.Parameter"/> the strongly typed
    /// parameter that was passed to the failing execution.
    /// </summary>
    /// <remarks>
    /// When this event has subscribers and <see cref="AsyncRelayCommandOptions.FlowExceptionsToTaskScheduler"/>
    /// is used, a fault is observed by the command in order to raise this event, instead of reaching
    /// <see cref="System.Threading.Tasks.TaskScheduler.UnobservedTaskException"/>: subscribing the event is
    /// the more explicit, more local opt-in. Whether a given execution is observed for this event at all is
    /// decided when the execution starts, on either the <see cref="Execute(T?)"/> or the
    /// <see cref="ExecuteAsync(T?)"/> path, while the handlers that get notified are read when the fault is
    /// actually observed, following standard event semantics. If the event had no subscribers at that point,
    /// the execution is not observed for this event and a handler attached later is not notified for that
    /// execution (a command with no subscribers takes the same path it would if the event did not exist). A handler detached before the fault is observed is likewise
    /// not notified, and the fault then propagates as if the event had never had subscribers; note that with
    /// <see cref="AsyncRelayCommandOptions.FlowExceptionsToTaskScheduler"/> the calling
    /// <see cref="Execute(T?)"/> has already committed to awaiting the task, so such a fault has already been
    /// observed and does not reach
    /// <see cref="System.Threading.Tasks.TaskScheduler.UnobservedTaskException"/> either. It remains available
    /// through <see cref="ExecutionTask"/>.
    /// </remarks>
    public event EventHandler<RelayCommandExceptionEventArgs<T>>? ExecutionFailed;

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncRelayCommand{T}"/> class.
    /// </summary>
    /// <param name="execute">The execution logic.</param>
    /// <remarks>See notes in <see cref="RelayCommand{T}(Action{T})"/>.</remarks>
    /// <exception cref="System.ArgumentNullException">Thrown if <paramref name="execute"/> is <see langword="null"/>.</exception>
    public AsyncRelayCommand(Func<T?, Task> execute)
    {
        ArgumentNullException.ThrowIfNull(execute);

        this.execute = execute;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncRelayCommand{T}"/> class.
    /// </summary>
    /// <param name="execute">The execution logic.</param>
    /// <param name="options">The options to use to configure the async command.</param>
    /// <remarks>See notes in <see cref="RelayCommand{T}(Action{T})"/>.</remarks>
    /// <exception cref="System.ArgumentNullException">Thrown if <paramref name="execute"/> is <see langword="null"/>.</exception>
    public AsyncRelayCommand(Func<T?, Task> execute, AsyncRelayCommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(execute);

        this.execute = execute;
        this.options = options;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncRelayCommand{T}"/> class.
    /// </summary>
    /// <param name="cancelableExecute">The cancelable execution logic.</param>
    /// <remarks>See notes in <see cref="RelayCommand{T}(Action{T})"/>.</remarks>
    /// <exception cref="System.ArgumentNullException">Thrown if <paramref name="cancelableExecute"/> is <see langword="null"/>.</exception>
    public AsyncRelayCommand(Func<T?, CancellationToken, Task> cancelableExecute)
    {
        ArgumentNullException.ThrowIfNull(cancelableExecute);

        this.cancelableExecute = cancelableExecute;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncRelayCommand{T}"/> class.
    /// </summary>
    /// <param name="cancelableExecute">The cancelable execution logic.</param>
    /// <param name="options">The options to use to configure the async command.</param>
    /// <remarks>See notes in <see cref="RelayCommand{T}(Action{T})"/>.</remarks>
    /// <exception cref="System.ArgumentNullException">Thrown if <paramref name="cancelableExecute"/> is <see langword="null"/>.</exception>
    public AsyncRelayCommand(Func<T?, CancellationToken, Task> cancelableExecute, AsyncRelayCommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(cancelableExecute);

        this.cancelableExecute = cancelableExecute;
        this.options = options;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncRelayCommand{T}"/> class.
    /// </summary>
    /// <param name="execute">The execution logic.</param>
    /// <param name="canExecute">The execution status logic.</param>
    /// <remarks>See notes in <see cref="RelayCommand{T}(Action{T})"/>.</remarks>
    /// <exception cref="System.ArgumentNullException">Thrown if <paramref name="execute"/> or <paramref name="canExecute"/> are <see langword="null"/>.</exception>
    public AsyncRelayCommand(Func<T?, Task> execute, Predicate<T?> canExecute)
    {
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(canExecute);

        this.execute = execute;
        this.canExecute = canExecute;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncRelayCommand{T}"/> class.
    /// </summary>
    /// <param name="execute">The execution logic.</param>
    /// <param name="canExecute">The execution status logic.</param>
    /// <param name="options">The options to use to configure the async command.</param>
    /// <remarks>See notes in <see cref="RelayCommand{T}(Action{T})"/>.</remarks>
    /// <exception cref="System.ArgumentNullException">Thrown if <paramref name="execute"/> or <paramref name="canExecute"/> are <see langword="null"/>.</exception>
    public AsyncRelayCommand(Func<T?, Task> execute, Predicate<T?> canExecute, AsyncRelayCommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(canExecute);

        this.execute = execute;
        this.canExecute = canExecute;
        this.options = options;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncRelayCommand{T}"/> class.
    /// </summary>
    /// <param name="cancelableExecute">The cancelable execution logic.</param>
    /// <param name="canExecute">The execution status logic.</param>
    /// <remarks>See notes in <see cref="RelayCommand{T}(Action{T})"/>.</remarks>
    /// <exception cref="System.ArgumentNullException">Thrown if <paramref name="cancelableExecute"/> or <paramref name="canExecute"/> are <see langword="null"/>.</exception>
    public AsyncRelayCommand(Func<T?, CancellationToken, Task> cancelableExecute, Predicate<T?> canExecute)
    {
        ArgumentNullException.ThrowIfNull(cancelableExecute);
        ArgumentNullException.ThrowIfNull(canExecute);

        this.cancelableExecute = cancelableExecute;
        this.canExecute = canExecute;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncRelayCommand{T}"/> class.
    /// </summary>
    /// <param name="cancelableExecute">The cancelable execution logic.</param>
    /// <param name="canExecute">The execution status logic.</param>
    /// <param name="options">The options to use to configure the async command.</param>
    /// <remarks>See notes in <see cref="RelayCommand{T}(Action{T})"/>.</remarks>
    /// <exception cref="System.ArgumentNullException">Thrown if <paramref name="cancelableExecute"/> or <paramref name="canExecute"/> are <see langword="null"/>.</exception>
    public AsyncRelayCommand(Func<T?, CancellationToken, Task> cancelableExecute, Predicate<T?> canExecute, AsyncRelayCommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(cancelableExecute);
        ArgumentNullException.ThrowIfNull(canExecute);

        this.cancelableExecute = cancelableExecute;
        this.canExecute = canExecute;
        this.options = options;
    }

    private Task? executionTask;

    /// <inheritdoc/>
    public Task? ExecutionTask
    {
        get => this.executionTask;
        private set
        {
            if (ReferenceEquals(this.executionTask, value))
            {
                return;
            }

            this.executionTask = value;

            PropertyChanged?.Invoke(this, AsyncRelayCommand.ExecutionTaskChangedEventArgs);
            PropertyChanged?.Invoke(this, AsyncRelayCommand.IsRunningChangedEventArgs);

            bool isAlreadyCompletedOrNull = value?.IsCompleted ?? true;

            if (this.cancellationTokenSource is not null)
            {
                PropertyChanged?.Invoke(this, AsyncRelayCommand.CanBeCanceledChangedEventArgs);
                PropertyChanged?.Invoke(this, AsyncRelayCommand.IsCancellationRequestedChangedEventArgs);
            }

            if (isAlreadyCompletedOrNull)
            {
                return;
            }

            static async void MonitorTask(AsyncRelayCommand<T> @this, Task task)
            {
                await task.GetAwaitableWithoutEndValidation();

                if (ReferenceEquals(@this.executionTask, task))
                {
                    @this.PropertyChanged?.Invoke(@this, AsyncRelayCommand.ExecutionTaskChangedEventArgs);
                    @this.PropertyChanged?.Invoke(@this, AsyncRelayCommand.IsRunningChangedEventArgs);
                    
                    if (@this.cancellationTokenSource is not null)
                    {
                        @this.PropertyChanged?.Invoke(@this, AsyncRelayCommand.CanBeCanceledChangedEventArgs);
                    }

                    if ((@this.options & AsyncRelayCommandOptions.AllowConcurrentExecutions) == 0)
                    {
                        @this.CanExecuteChanged?.Invoke(@this, EventArgs.Empty);
                    }
                }
            }

            MonitorTask(this, value!);
        }
    }

    /// <inheritdoc/>
    public bool CanBeCanceled => IsRunning && this.cancellationTokenSource is { IsCancellationRequested: false };

    /// <inheritdoc/>
    public bool IsCancellationRequested => this.cancellationTokenSource is { IsCancellationRequested: true };

    /// <inheritdoc/>
    public bool IsRunning => ExecutionTask is { IsCompleted: false };

    /// <inheritdoc/>
    bool ICancellationAwareCommand.IsCancellationSupported => this.execute is null;

    /// <inheritdoc/>
    public void NotifyCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool CanExecute(T? parameter)
    {
        bool canExecute = this.canExecute?.Invoke(parameter) != false;

        return canExecute && ((this.options & AsyncRelayCommandOptions.AllowConcurrentExecutions) != 0 || ExecutionTask is not { IsCompleted: false });
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool CanExecute(object? parameter)
    {
        // Special case, see RelayCommand<T>.CanExecute(object?) for more info
        if (parameter is null && default(T) is not null)
        {
            return false;
        }

        if (!RelayCommand<T>.TryGetCommandArgument(parameter, out T? result))
        {
            RelayCommand<T>.ThrowArgumentExceptionForInvalidCommandArgument(parameter);
        }

        return CanExecute(result);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Execute(T? parameter)
    {
        // The event is raised from this path below, so the observer is suppressed to avoid raising twice.
        Task executionTask = ExecuteAsync(parameter, raiseExecutionFailed: false);

        // See the comments in AsyncRelayCommand.Execute for why the task is awaited here, and why the
        // no-subscriber case deliberately falls back to the plain shared awaiter instead.
        if (ExecutionFailed is not null)
        {
            AwaitAndRouteExecutionFailed(executionTask, parameter);
        }
        else if ((this.options & AsyncRelayCommandOptions.FlowExceptionsToTaskScheduler) == 0)
        {
            AsyncRelayCommand.AwaitAndThrowIfFailed(executionTask);
        }
    }

    /// <inheritdoc/>
    public void Execute(object? parameter)
    {
        if (!RelayCommand<T>.TryGetCommandArgument(parameter, out T? result))
        {
            RelayCommand<T>.ThrowArgumentExceptionForInvalidCommandArgument(parameter);
        }

        Execute(result);
    }

    /// <inheritdoc/>
    public Task ExecuteAsync(T? parameter)
    {
        return ExecuteAsync(parameter, raiseExecutionFailed: true);
    }

    /// <summary>
    /// Executes the current command, optionally observing the resulting task to raise <see cref="ExecutionFailed"/>.
    /// </summary>
    /// <param name="parameter">The input parameter.</param>
    /// <param name="raiseExecutionFailed">Whether a fault should be observed and routed through <see cref="ExecutionFailed"/> by this method. This is <see langword="false"/> when the caller (ie. <see cref="Execute(T?)"/>) takes care of that itself.</param>
    /// <returns>The <see cref="Task"/> representing the async operation being executed, which is always the same instance as <see cref="ExecutionTask"/>.</returns>
    private Task ExecuteAsync(T? parameter, bool raiseExecutionFailed)
    {
        Task executionTask;

        if (this.execute is not null)
        {
            // Non cancelable command delegate
            executionTask = ExecutionTask = this.execute(parameter);
        }
        else
        {
            // Cancel the previous operation, if one is pending
            this.cancellationTokenSource?.Cancel();

            CancellationTokenSource cancellationTokenSource = this.cancellationTokenSource = new();

            // Invoke the cancelable command delegate with a new linked token
            executionTask = ExecutionTask = this.cancelableExecute!(parameter, cancellationTokenSource.Token);
        }

        // If concurrent executions are disabled, notify the can execute change as well
        if ((this.options & AsyncRelayCommandOptions.AllowConcurrentExecutions) == 0)
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }

        // If the event has subscribers at this point, observe a fault so they are notified, without
        // altering what the caller receives: the returned task is always the execution task itself.
        if (raiseExecutionFailed && ExecutionFailed is not null)
        {
            RaiseExecutionFailedWhenFaulted(executionTask, parameter);
        }

        return executionTask;
    }

    /// <inheritdoc/>
    public Task ExecuteAsync(object? parameter)
    {
        if (!RelayCommand<T>.TryGetCommandArgument(parameter, out T? result))
        {
            RelayCommand<T>.ThrowArgumentExceptionForInvalidCommandArgument(parameter);
        }

        return ExecuteAsync(result);
    }

    /// <inheritdoc/>
    public void Cancel()
    {
        if (this.cancellationTokenSource is CancellationTokenSource { IsCancellationRequested: false } cancellationTokenSource)
        {
            cancellationTokenSource.Cancel();

            PropertyChanged?.Invoke(this, AsyncRelayCommand.CanBeCanceledChangedEventArgs);
            PropertyChanged?.Invoke(this, AsyncRelayCommand.IsCancellationRequestedChangedEventArgs);
        }
    }

    /// <summary>
    /// Observes an execution task and raises <see cref="ExecutionFailed"/> if it faults, without
    /// altering the task the caller received: the caller observes the fault itself, so
    /// <see cref="RelayCommandExceptionEventArgs.Handled"/> has no effect on this path. A canceled
    /// execution never raises the event.
    /// </summary>
    /// <param name="executionTask">The execution task to observe.</param>
    /// <param name="parameter">The parameter that was passed to the failing execution, surfaced through the event.</param>
    private async void RaiseExecutionFailedWhenFaulted(Task executionTask, T? parameter)
    {
        try
        {
            await executionTask;
        }
        catch (Exception e)
        {
            // This observer must never rethrow: the caller owns the returned task and sees the fault
            // through it. Only a faulted execution is routed, never a canceled one. Standard event
            // semantics also apply: the handler list is read when the event is raised, so a handler
            // detached while the execution was in flight is simply not notified.
            if (!executionTask.IsCanceled && ExecutionFailed is EventHandler<RelayCommandExceptionEventArgs<T>> executionFailed)
            {
                // A task can fault with more than one exception (eg. Task.WhenAll). Awaiting surfaces only the
                // first, so hand the handler the full AggregateException rather than silently dropping the rest.
                Exception routedException = executionTask.Exception is AggregateException { InnerExceptions.Count: > 1 } aggregateException
                    ? aggregateException
                    : e;

                executionFailed(this, new RelayCommandExceptionEventArgs<T>(parameter, routedException));
            }
        }
    }

    /// <summary>
    /// Awaits an execution task on the <see cref="Execute(T?)"/> path and routes a fault through
    /// <see cref="ExecutionFailed"/>, rethrowing it on the captured context if no handler marks it as
    /// handled and the command was not created with
    /// <see cref="AsyncRelayCommandOptions.FlowExceptionsToTaskScheduler"/>. A canceled execution is
    /// never routed, and is likewise never rethrown when that option is set.
    /// </summary>
    /// <param name="executionTask">The execution task to await.</param>
    /// <param name="parameter">The parameter that was passed to the failing execution, surfaced through the event.</param>
    private async void AwaitAndRouteExecutionFailed(Task executionTask, T? parameter)
    {
        // See the notes in AsyncRelayCommand.AwaitAndRouteExecutionFailed for why this is an async void method
        try
        {
            await executionTask;
        }
        catch (Exception e)
        {
            // Only a faulted execution is routed, never a canceled one. Standard event semantics also
            // apply: the handler list is read when the event is raised, so a handler detached while the
            // execution was in flight is simply not notified, and the fault just propagates.
            if (!executionTask.IsCanceled &&
                ExecutionFailed is EventHandler<RelayCommandExceptionEventArgs<T>> executionFailed)
            {
                // A task can fault with more than one exception (eg. Task.WhenAll). Awaiting surfaces only the
                // first, so hand the handler the full AggregateException rather than silently dropping the rest.
                Exception routedException = executionTask.Exception is AggregateException { InnerExceptions.Count: > 1 } aggregateException
                    ? aggregateException
                    : e;

                RelayCommandExceptionEventArgs<T> args = new(parameter, routedException);

                executionFailed(this, args);

                if (args.Handled)
                {
                    return;
                }
            }

            // This task is only being awaited at all because the event has subscribers: a command created with
            // the flow option asked for no rethrow on the captured context, and subscribing to the event must
            // change who gets notified of a fault, never whether that fault escapes. So nothing is rethrown from
            // here in that configuration, for a fault and a cancellation alike. The exception remains observable
            // through ExecutionTask, exactly as it would be with no subscribers attached.
            if ((this.options & AsyncRelayCommandOptions.FlowExceptionsToTaskScheduler) != 0)
            {
                return;
            }

            throw;
        }
    }
}

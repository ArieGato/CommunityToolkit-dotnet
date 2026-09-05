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
/// A command that mirrors the functionality of <see cref="RelayCommand"/>, with the addition of
/// accepting a <see cref="Func{TResult}"/> returning a <see cref="Task"/> as the execute
/// action, and providing an <see cref="ExecutionTask"/> property that notifies changes when
/// <see cref="ExecuteAsync(object?)"/> is invoked and when the returned <see cref="Task"/> completes.
/// </summary>
public sealed partial class AsyncRelayCommand : IAsyncRelayCommand, ICancellationAwareCommand
{
    /// <summary>
    /// The cached <see cref="PropertyChangedEventArgs"/> for <see cref="ExecutionTask"/>.
    /// </summary>
    internal static readonly PropertyChangedEventArgs ExecutionTaskChangedEventArgs = new(nameof(ExecutionTask));

    /// <summary>
    /// The cached <see cref="PropertyChangedEventArgs"/> for <see cref="CanBeCanceled"/>.
    /// </summary>
    internal static readonly PropertyChangedEventArgs CanBeCanceledChangedEventArgs = new(nameof(CanBeCanceled));

    /// <summary>
    /// The cached <see cref="PropertyChangedEventArgs"/> for <see cref="IsCancellationRequested"/>.
    /// </summary>
    internal static readonly PropertyChangedEventArgs IsCancellationRequestedChangedEventArgs = new(nameof(IsCancellationRequested));

    /// <summary>
    /// The cached <see cref="PropertyChangedEventArgs"/> for <see cref="IsRunning"/>.
    /// </summary>
    internal static readonly PropertyChangedEventArgs IsRunningChangedEventArgs = new(nameof(IsRunning));

    /// <summary>
    /// The <see cref="Func{TResult}"/> to invoke when <see cref="Execute"/> is used.
    /// </summary>
    private readonly Func<Task>? execute;

    /// <summary>
    /// The cancelable <see cref="Func{T,TResult}"/> to invoke when <see cref="Execute"/> is used.
    /// </summary>
    /// <remarks>Only one between this and <see cref="execute"/> is not <see langword="null"/>.</remarks>
    private readonly Func<CancellationToken, Task>? cancelableExecute;

    /// <summary>
    /// The optional action to invoke when <see cref="CanExecute"/> is used.
    /// </summary>
    private readonly Func<bool>? canExecute;

    /// <summary>
    /// The options being set for the current command.
    /// </summary>
    private readonly AsyncRelayCommandOptions options;

    /// <summary>
    /// The <see cref="CancellationTokenSource"/> instance to use to cancel <see cref="cancelableExecute"/>.
    /// </summary>
    /// <remarks>This is only used when <see cref="cancelableExecute"/> is not <see langword="null"/>.</remarks>
    private CancellationTokenSource? cancellationTokenSource;

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <inheritdoc/>
    public event EventHandler? CanExecuteChanged;

    /// <summary>
    /// Raised when the execution task of the command completes in a faulted state. If at least one
    /// handler is attached when the fault is observed, the exception is passed to the event. On the
    /// <see cref="Execute(object?)"/> path, the exception is then only rethrown (on the captured context)
    /// if no handler sets <see cref="RelayCommandExceptionEventArgs.Handled"/> to <see langword="true"/>,
    /// and the command was not created with
    /// <see cref="AsyncRelayCommandOptions.FlowExceptionsToTaskScheduler"/>, which suppresses that rethrow
    /// regardless of what any handler does.
    /// When the task returned by <see cref="ExecuteAsync(object?)"/> is awaited directly, the event is
    /// raised as well, but the exception is always delivered to the awaiter:
    /// <see cref="ExecuteAsync(object?)"/> always returns the execution task itself (the very same
    /// instance as <see cref="ExecutionTask"/>), so the caller observes the fault through it and
    /// <see cref="RelayCommandExceptionEventArgs.Handled"/> has no effect on that path. That is,
    /// <see cref="RelayCommandExceptionEventArgs.Handled"/> only affects the
    /// <see cref="System.Windows.Input.ICommand.Execute(object?)"/> path, which is the one where nobody
    /// else could observe the fault. If no handler is attached, exceptions propagate exactly as before:
    /// rethrown on the calling context by default, or flowing to
    /// <see cref="System.Threading.Tasks.TaskScheduler.UnobservedTaskException"/> when
    /// <see cref="AsyncRelayCommandOptions.FlowExceptionsToTaskScheduler"/> is used. A canceled execution
    /// never raises this event.
    /// </summary>
    /// <remarks>
    /// When this event has subscribers and <see cref="AsyncRelayCommandOptions.FlowExceptionsToTaskScheduler"/>
    /// is used, a fault is observed by the command in order to raise this event, instead of reaching
    /// <see cref="System.Threading.Tasks.TaskScheduler.UnobservedTaskException"/>: subscribing the event is
    /// the more explicit, more local opt-in. Whether a given execution is observed for this event at all is
    /// decided when the execution starts, on either the <see cref="Execute(object?)"/> or the
    /// <see cref="ExecuteAsync(object?)"/> path, while the handlers that get notified are read when the fault
    /// is actually observed, following standard event semantics. If the event had no subscribers at that point,
    /// the execution is not observed for this event and a handler attached later is not notified for that
    /// execution (a command with no subscribers takes the same path it would if the event did not exist). A handler detached before the fault is
    /// observed is likewise not notified, and the fault then propagates as if the event had never had
    /// subscribers; note that with <see cref="AsyncRelayCommandOptions.FlowExceptionsToTaskScheduler"/> the
    /// calling <see cref="Execute(object?)"/> has already committed to awaiting the task, so such a fault has
    /// already been observed and does not reach
    /// <see cref="System.Threading.Tasks.TaskScheduler.UnobservedTaskException"/> either. It remains available
    /// through <see cref="ExecutionTask"/>.
    /// </remarks>
    public event EventHandler<RelayCommandExceptionEventArgs>? ExecutionFailed;

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncRelayCommand"/> class.
    /// </summary>
    /// <param name="execute">The execution logic.</param>
    /// <exception cref="System.ArgumentNullException">Thrown if <paramref name="execute"/> is <see langword="null"/>.</exception>
    public AsyncRelayCommand(Func<Task> execute)
    {
        ArgumentNullException.ThrowIfNull(execute);

        this.execute = execute;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncRelayCommand"/> class.
    /// </summary>
    /// <param name="execute">The execution logic.</param>
    /// <param name="options">The options to use to configure the async command.</param>
    /// <exception cref="System.ArgumentNullException">Thrown if <paramref name="execute"/> is <see langword="null"/>.</exception>
    public AsyncRelayCommand(Func<Task> execute, AsyncRelayCommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(execute);

        this.execute = execute;
        this.options = options;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncRelayCommand"/> class.
    /// </summary>
    /// <param name="cancelableExecute">The cancelable execution logic.</param>
    /// <exception cref="System.ArgumentNullException">Thrown if <paramref name="cancelableExecute"/> is <see langword="null"/>.</exception>
    public AsyncRelayCommand(Func<CancellationToken, Task> cancelableExecute)
    {
        ArgumentNullException.ThrowIfNull(cancelableExecute);

        this.cancelableExecute = cancelableExecute;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncRelayCommand"/> class.
    /// </summary>
    /// <param name="cancelableExecute">The cancelable execution logic.</param>
    /// <param name="options">The options to use to configure the async command.</param>
    /// <exception cref="System.ArgumentNullException">Thrown if <paramref name="cancelableExecute"/> is <see langword="null"/>.</exception>
    public AsyncRelayCommand(Func<CancellationToken, Task> cancelableExecute, AsyncRelayCommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(cancelableExecute);

        this.cancelableExecute = cancelableExecute;
        this.options = options;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncRelayCommand"/> class.
    /// </summary>
    /// <param name="execute">The execution logic.</param>
    /// <param name="canExecute">The execution status logic.</param>
    /// <exception cref="System.ArgumentNullException">Thrown if <paramref name="execute"/> or <paramref name="canExecute"/> are <see langword="null"/>.</exception>
    public AsyncRelayCommand(Func<Task> execute, Func<bool> canExecute)
    {
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(canExecute);

        this.execute = execute;
        this.canExecute = canExecute;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncRelayCommand"/> class.
    /// </summary>
    /// <param name="execute">The execution logic.</param>
    /// <param name="canExecute">The execution status logic.</param>
    /// <param name="options">The options to use to configure the async command.</param>
    /// <exception cref="System.ArgumentNullException">Thrown if <paramref name="execute"/> or <paramref name="canExecute"/> are <see langword="null"/>.</exception>
    public AsyncRelayCommand(Func<Task> execute, Func<bool> canExecute, AsyncRelayCommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(canExecute);

        this.execute = execute;
        this.canExecute = canExecute;
        this.options = options;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncRelayCommand"/> class.
    /// </summary>
    /// <param name="cancelableExecute">The cancelable execution logic.</param>
    /// <param name="canExecute">The execution status logic.</param>
    /// <exception cref="System.ArgumentNullException">Thrown if <paramref name="cancelableExecute"/> or <paramref name="canExecute"/> are <see langword="null"/>.</exception>
    public AsyncRelayCommand(Func<CancellationToken, Task> cancelableExecute, Func<bool> canExecute)
    {
        ArgumentNullException.ThrowIfNull(cancelableExecute);
        ArgumentNullException.ThrowIfNull(canExecute);

        this.cancelableExecute = cancelableExecute;
        this.canExecute = canExecute;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncRelayCommand"/> class.
    /// </summary>
    /// <param name="cancelableExecute">The cancelable execution logic.</param>
    /// <param name="canExecute">The execution status logic.</param>
    /// <param name="options">The options to use to configure the async command.</param>
    /// <exception cref="System.ArgumentNullException">Thrown if <paramref name="cancelableExecute"/> or <paramref name="canExecute"/> are <see langword="null"/>.</exception>
    public AsyncRelayCommand(Func<CancellationToken, Task> cancelableExecute, Func<bool> canExecute, AsyncRelayCommandOptions options)
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

            PropertyChanged?.Invoke(this, ExecutionTaskChangedEventArgs);
            PropertyChanged?.Invoke(this, IsRunningChangedEventArgs);

            bool isAlreadyCompletedOrNull = value?.IsCompleted ?? true;

            if (this.cancellationTokenSource is not null)
            {
                PropertyChanged?.Invoke(this, CanBeCanceledChangedEventArgs);
                PropertyChanged?.Invoke(this, IsCancellationRequestedChangedEventArgs);
            }

            // The branch is on a condition evaluated before raising the events above if
            // needed, to avoid race conditions with a task completing right after them.
            if (isAlreadyCompletedOrNull)
            {
                return;
            }

            static async void MonitorTask(AsyncRelayCommand @this, Task task)
            {
                await task.GetAwaitableWithoutEndValidation();

                if (ReferenceEquals(@this.executionTask, task))
                {
                    @this.PropertyChanged?.Invoke(@this, ExecutionTaskChangedEventArgs);
                    @this.PropertyChanged?.Invoke(@this, IsRunningChangedEventArgs);

                    if (@this.cancellationTokenSource is not null)
                    {
                        @this.PropertyChanged?.Invoke(@this, CanBeCanceledChangedEventArgs);
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
    public bool CanExecute(object? parameter)
    {
        bool canExecute = this.canExecute?.Invoke() != false;

        return canExecute && ((this.options & AsyncRelayCommandOptions.AllowConcurrentExecutions) != 0 || ExecutionTask is not { IsCompleted: false });
    }

    /// <inheritdoc/>
    public void Execute(object? parameter)
    {
        // The event is raised from this path below, so the observer is suppressed to avoid raising twice.
        Task executionTask = ExecuteAsync(parameter, raiseExecutionFailed: false);

        // With at least one subscriber, the task is awaited by the routing observer: this path is where a
        // fault is delivered to the event, and where it is rethrown on the captured context if no handler
        // marks it as handled. That holds even with AsyncRelayCommandOptions.FlowExceptionsToTaskScheduler,
        // since notifying subscribers is reason enough to observe the task (the option still suppresses the
        // rethrow itself, from inside the observer).
        if (ExecutionFailed is not null)
        {
            AwaitAndRouteExecutionFailed(executionTask);
        }
        else if ((this.options & AsyncRelayCommandOptions.FlowExceptionsToTaskScheduler) == 0)
        {
            // With no subscriber there is nothing to route, so the plain awaiter is used instead. It has no
            // exception handling region and is static, so it neither pays for a try/catch nor captures 'this'
            // in its state machine. This keeps a command that never uses ExecutionFailed exactly as cheap as
            // it was before the event existed, and mirrors the same no-subscriber carve-out that
            // RelayCommand.Execute makes for the very same reason.
            AwaitAndThrowIfFailed(executionTask);
        }
    }

    /// <inheritdoc/>
    public Task ExecuteAsync(object? parameter)
    {
        return ExecuteAsync(parameter, raiseExecutionFailed: true);
    }

    /// <summary>
    /// Executes the current command, optionally observing the resulting task to raise <see cref="ExecutionFailed"/>.
    /// </summary>
    /// <param name="parameter">The input parameter.</param>
    /// <param name="raiseExecutionFailed">Whether a fault should be observed and routed through <see cref="ExecutionFailed"/> by this method. This is <see langword="false"/> when the caller (ie. <see cref="Execute"/>) takes care of that itself.</param>
    /// <returns>The <see cref="Task"/> representing the async operation being executed, which is always the same instance as <see cref="ExecutionTask"/>.</returns>
    private Task ExecuteAsync(object? parameter, bool raiseExecutionFailed)
    {
        Task executionTask;

        if (this.execute is not null)
        {
            // Non cancelable command delegate
            executionTask = ExecutionTask = this.execute();
        }
        else
        {
            // Cancel the previous operation, if one is pending
            this.cancellationTokenSource?.Cancel();

            CancellationTokenSource cancellationTokenSource = this.cancellationTokenSource = new();

            // Invoke the cancelable command delegate with a new linked token
            executionTask = ExecutionTask = this.cancelableExecute!(cancellationTokenSource.Token);
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
            RaiseExecutionFailedWhenFaulted(executionTask);
        }

        return executionTask;
    }

    /// <inheritdoc/>
    public void Cancel()
    {
        if (this.cancellationTokenSource is CancellationTokenSource { IsCancellationRequested: false } cancellationTokenSource)
        {
            cancellationTokenSource.Cancel();

            PropertyChanged?.Invoke(this, CanBeCanceledChangedEventArgs);
            PropertyChanged?.Invoke(this, IsCancellationRequestedChangedEventArgs);
        }
    }

    /// <summary>
    /// Awaits an execution task on the <see cref="Execute"/> path when <see cref="ExecutionFailed"/> has no
    /// subscribers, rethrowing a fault on the captured context.
    /// </summary>
    /// <param name="executionTask">The execution task to await.</param>
    /// <remarks>
    /// This is the counterpart of <see cref="AwaitAndRouteExecutionFailed"/> for the case where there is
    /// nothing to route. It deliberately contains no exception handling region and is <see langword="static"/>,
    /// so that a command which never uses <see cref="ExecutionFailed"/> pays neither for a try/catch nor for
    /// capturing <see langword="this"/> in the state machine. See the notes in
    /// <see cref="AwaitAndRouteExecutionFailed"/> for why this is an <see langword="async"/>
    /// <see langword="void"/> method.
    /// </remarks>
    internal static async void AwaitAndThrowIfFailed(Task executionTask)
    {
        await executionTask;
    }

    /// <summary>
    /// Observes an execution task and raises <see cref="ExecutionFailed"/> if it faults, without
    /// altering the task the caller received: the caller observes the fault itself, so
    /// <see cref="RelayCommandExceptionEventArgs.Handled"/> has no effect on this path. A canceled
    /// execution never raises the event.
    /// </summary>
    /// <param name="executionTask">The execution task to observe.</param>
    private async void RaiseExecutionFailedWhenFaulted(Task executionTask)
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
            if (!executionTask.IsCanceled && ExecutionFailed is EventHandler<RelayCommandExceptionEventArgs> executionFailed)
            {
                // A task can fault with more than one exception (eg. Task.WhenAll). Awaiting surfaces only the
                // first, so hand the handler the full AggregateException rather than silently dropping the rest.
                Exception routedException = executionTask.Exception is AggregateException { InnerExceptions.Count: > 1 } aggregateException
                    ? aggregateException
                    : e;

                executionFailed(this, new RelayCommandExceptionEventArgs(routedException));
            }
        }
    }

    /// <summary>
    /// Awaits an execution task on the <see cref="Execute"/> path and routes a fault through
    /// <see cref="ExecutionFailed"/>, rethrowing it on the captured context if no handler marks it as
    /// handled and the command was not created with
    /// <see cref="AsyncRelayCommandOptions.FlowExceptionsToTaskScheduler"/>. A canceled execution is
    /// never routed, and is likewise never rethrown when that option is set.
    /// </summary>
    /// <param name="executionTask">The execution task to await.</param>
    private async void AwaitAndRouteExecutionFailed(Task executionTask)
    {
        // Note: this method is purposefully an async void method awaiting the input task. That is the mechanism the
        // rethrow below relies on, not an incidental detail: the state machine hands an unhandled exception to
        // AsyncVoidMethodBuilder.SetException, which posts it to the captured synchronization context. So when an
        // async relay command is invoked synchronously (ie. when Execute is called, eg. from a binding), a fault in
        // the wrapped delegate is not ignored, nor left only visible through the ExecutionTask property, but surfaces
        // on the original context. An 'async Task' method would not do: nothing awaits the returned task, so the
        // fault would simply be parked in it and the rethrow would become a no-op. This keeps the behavior consistent
        // with how normal commands work (where exceptions are also just normally propagated to the caller context),
        // and avoids getting an app into an inconsistent state in case a method faults without other components
        // being notified.
        //
        // Two things suppress that rethrow. A handler subscribed to ExecutionFailed can mark the fault as handled,
        // and a command constructed with AsyncRelayCommandOptions.FlowExceptionsToTaskScheduler never rethrows on
        // the captured context at all. In the latter case Execute skips this call entirely unless ExecutionFailed
        // has subscribers, since notifying them is the only remaining reason to await the task here.
        //
        // Note that awaiting the task observes it. With no subscribers and the flow option set, this method is not
        // called, the fault stays available through ExecutionTask, and reaches the static
        // TaskScheduler.UnobservedTaskException event if nothing else observes it (eg. for logging). Once the event
        // has subscribers that no longer holds: the await below observes the fault, so UnobservedTaskException does
        // not fire for it. Subscribing to ExecutionFailed is the more explicit, more local opt-in of the two.
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
                ExecutionFailed is EventHandler<RelayCommandExceptionEventArgs> executionFailed)
            {
                // A task can fault with more than one exception (eg. Task.WhenAll). Awaiting surfaces only the
                // first, so hand the handler the full AggregateException rather than silently dropping the rest.
                Exception routedException = executionTask.Exception is AggregateException { InnerExceptions.Count: > 1 } aggregateException
                    ? aggregateException
                    : e;

                RelayCommandExceptionEventArgs args = new(routedException);

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

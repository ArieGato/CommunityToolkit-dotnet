// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

// This file is inspired from the MvvmLight library (lbugnion/MvvmLight),
// more info in ThirdPartyNotices.txt in the root of the project.

using System;
using System.Runtime.CompilerServices;

namespace CommunityToolkit.Mvvm.Input;

/// <summary>
/// A command whose sole purpose is to relay its functionality to other
/// objects by invoking delegates. The default return value for the <see cref="CanExecute"/>
/// method is <see langword="true"/>. This type does not allow you to accept command parameters
/// in the <see cref="Execute"/> and <see cref="CanExecute"/> callback methods.
/// </summary>
public sealed partial class RelayCommand : IRelayCommand
{
    /// <summary>
    /// The <see cref="Action"/> to invoke when <see cref="Execute"/> is used.
    /// </summary>
    private readonly Action execute;

    /// <summary>
    /// The optional action to invoke when <see cref="CanExecute"/> is used.
    /// </summary>
    private readonly Func<bool>? canExecute;

    /// <inheritdoc/>
    public event EventHandler? CanExecuteChanged;

    /// <summary>
    /// Raised when the execution of the wrapped delegate throws an exception. If at least one handler
    /// is attached when the exception is raised, it is passed to the event, and only rethrown if no
    /// handler sets <see cref="RelayCommandExceptionEventArgs.Handled"/> to <see langword="true"/>.
    /// Handlers are read when the exception is raised, following standard event semantics. The one
    /// deviation is that if no handler is attached when <see cref="Execute"/> is invoked, the delegate runs
    /// without any exception handling in place, so a handler attached while it is running is not notified
    /// for that execution. If no
    /// handler is attached, the exception propagates to the caller unchanged, exactly as if this event
    /// did not exist.
    /// </summary>
    public event EventHandler<RelayCommandExceptionEventArgs>? ExecutionFailed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RelayCommand"/> class that can always execute.
    /// </summary>
    /// <param name="execute">The execution logic.</param>
    /// <exception cref="System.ArgumentNullException">Thrown if <paramref name="execute"/> is <see langword="null"/>.</exception>
    public RelayCommand(Action execute)
    {
        ArgumentNullException.ThrowIfNull(execute);

        this.execute = execute;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RelayCommand"/> class.
    /// </summary>
    /// <param name="execute">The execution logic.</param>
    /// <param name="canExecute">The execution status logic.</param>
    /// <exception cref="System.ArgumentNullException">Thrown if <paramref name="execute"/> or <paramref name="canExecute"/> are <see langword="null"/>.</exception>
    public RelayCommand(Action execute, Func<bool> canExecute)
    {
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(canExecute);

        this.execute = execute;
        this.canExecute = canExecute;
    }

    /// <inheritdoc/>
    public void NotifyCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool CanExecute(object? parameter)
    {
        return this.canExecute?.Invoke() != false;
    }

    /// <inheritdoc/>
    public void Execute(object? parameter)
    {
        // The event is checked here, before the delegate runs, purely so that the common case of no
        // subscriber contains no exception handling region at all: that keeps this method eligible for
        // inlining, which a try/catch would prevent regardless of any attribute.
        if (ExecutionFailed is null)
        {
            this.execute();
        }
        else
        {
            ExecuteAndRouteExecutionFailed();
        }
    }

    /// <summary>
    /// Invokes the wrapped delegate and routes an exception to <see cref="ExecutionFailed"/>, rethrowing
    /// it if no handler marks it as handled. This is kept separate from <see cref="Execute"/> so that the
    /// exception handling region only exists on the path that actually needs it.
    /// </summary>
    private void ExecuteAndRouteExecutionFailed()
    {
        try
        {
            this.execute();
        }
        catch (Exception e)
        {
            // Standard event semantics: the handler list is read when the event is raised, so a handler
            // detached while the delegate was running is not notified, and the exception just propagates.
            if (ExecutionFailed is not EventHandler<RelayCommandExceptionEventArgs> executionFailed)
            {
                throw;
            }

            RelayCommandExceptionEventArgs args = new(e);

            executionFailed(this, args);

            if (!args.Handled)
            {
                throw;
            }
        }
    }
}

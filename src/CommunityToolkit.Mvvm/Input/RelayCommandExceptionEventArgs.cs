// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace CommunityToolkit.Mvvm.Input;

/// <summary>
/// Provides data for the <c>ExecutionFailed</c> event raised by the relay command types
/// when the wrapped delegate throws an exception (for the async commands, when the execution
/// task completes in a faulted state).
/// </summary>
public class RelayCommandExceptionEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RelayCommandExceptionEventArgs"/> class.
    /// </summary>
    /// <param name="exception">The exception that was thrown by the command execution.</param>
    /// <exception cref="System.ArgumentNullException">Thrown if <paramref name="exception"/> is <see langword="null"/>.</exception>
    public RelayCommandExceptionEventArgs(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        Exception = exception;
    }

    /// <summary>
    /// Gets the exception that was thrown by the command execution.
    /// </summary>
    public Exception Exception { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the exception has been handled. If left as
    /// <see langword="false"/> (the default value), the exception is rethrown after all event
    /// handlers have been invoked, preserving the default propagation behavior.
    /// </summary>
    public bool Handled { get; set; }
}

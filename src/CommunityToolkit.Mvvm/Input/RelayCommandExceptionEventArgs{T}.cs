// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace CommunityToolkit.Mvvm.Input;

/// <summary>
/// Provides data for the <c>ExecutionFailed</c> event raised by the generic relay command types
/// (<see cref="RelayCommand{T}"/> and <see cref="AsyncRelayCommand{T}"/>) when the wrapped delegate
/// throws an exception (for <see cref="AsyncRelayCommand{T}"/>, when the execution task completes in a
/// faulted state). In addition to the base data, this exposes the strongly typed
/// <see cref="Parameter"/> that was passed to the command execution that failed.
/// </summary>
/// <typeparam name="T">The type of parameter that was passed as input to the command.</typeparam>
public sealed class RelayCommandExceptionEventArgs<T> : RelayCommandExceptionEventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RelayCommandExceptionEventArgs{T}"/> class.
    /// </summary>
    /// <param name="parameter">The parameter that was passed to the command execution that failed.</param>
    /// <param name="exception">The exception that was thrown by the command execution.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="exception"/> is <see langword="null"/>.</exception>
    public RelayCommandExceptionEventArgs(T? parameter, Exception exception)
        : base(exception)
    {
        Parameter = parameter;
    }

    /// <summary>
    /// Gets the parameter that was passed to the command execution that failed.
    /// </summary>
    public T? Parameter { get; }
}

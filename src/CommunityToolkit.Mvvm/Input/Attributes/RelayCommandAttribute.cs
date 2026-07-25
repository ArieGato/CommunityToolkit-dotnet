// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Windows.Input;

namespace CommunityToolkit.Mvvm.Input;

/// <summary>
/// An attribute that can be used to automatically generate <see cref="ICommand"/> properties from declared methods. When this attribute
/// is used to decorate a method, a generator will create a command property with the corresponding <see cref="IRelayCommand"/> interface
/// depending on the signature of the method. If an invalid method signature is used, the generator will report an error.
/// <para>
/// In order to use this attribute, the containing type doesn't need to implement any interfaces. The generated properties will be lazily
/// assigned but their value will never change, so there is no need to support property change notifications or other additional functionality.
/// </para>
/// <para>
/// This attribute can be used as follows:
/// <code>
/// partial class MyViewModel
/// {
///     [RelayCommand]
///     private void GreetUser(User? user)
///     {
///         Console.WriteLine($"Hello {user.Name}!");
///     }
/// }
/// </code>
/// And with this, code analogous to this will be generated:
/// <code>
/// partial class MyViewModel
/// {
///     private RelayCommand? greetUserCommand;
///
///     public IRelayCommand GreetUserCommand => greetUserCommand ??= new RelayCommand(GreetUser);
/// }
/// </code>
/// </para>
/// <para>
/// The following signatures are supported for annotated methods:
/// <code>
/// void Method();
/// </code>
/// Will generate an <see cref="IRelayCommand"/> property (using a <see cref="RelayCommand"/> instance).
/// <code>
/// void Method(T?);
/// </code>
/// Will generate an <see cref="IRelayCommand{T}"/> property (using a <see cref="RelayCommand{T}"/> instance).
/// <code>
/// Task Method();
/// Task Method(CancellationToken);
/// Task&lt;T&gt; Method();
/// Task&lt;T&gt; Method(CancellationToken);
/// </code>
/// Will both generate an <see cref="IAsyncRelayCommand"/> property (using an <see cref="AsyncRelayCommand{T}"/> instance).
/// <code>
/// Task Method(T?);
/// Task Method(T?, CancellationToken);
/// Task&lt;T&gt; Method(T?);
/// Task&lt;T&gt; Method(T?, CancellationToken);
/// </code>
/// Will both generate an <see cref="IAsyncRelayCommand{T}"/> property (using an <see cref="AsyncRelayCommand{T}"/> instance).
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class RelayCommandAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the name of the property or method that will be invoked to check whether the
    /// generated command can be executed at any given time. The referenced member needs to return
    /// a <see cref="bool"/> value, and has to have a signature compatible with the target command.
    /// </summary>
    public string? CanExecute { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether or not to allow concurrent executions for an asynchronous command.
    /// <para>
    /// When set for an attribute used on a method that would result in an <see cref="AsyncRelayCommand"/> or an
    /// <see cref="AsyncRelayCommand{T}"/> property to be generated, this will modify the behavior of these commands
    /// when an execution is invoked while a previous one is still running. It is the same as creating an instance of
    /// these command types with a constructor such as <see cref="AsyncRelayCommand(Func{System.Threading.Tasks.Task}, AsyncRelayCommandOptions)"/>
    /// and using the <see cref="AsyncRelayCommandOptions.AllowConcurrentExecutions"/> value.
    /// </para>
    /// </summary>
    /// <remarks>Using this property is not valid if the target command doesn't map to an asynchronous command.</remarks>
    public bool AllowConcurrentExecutions { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether or not to exceptions should be propagated to <see cref="System.Threading.Tasks.TaskScheduler.UnobservedTaskException"/>.
    /// <para>
    /// When set for an attribute used on a method that would result in an <see cref="AsyncRelayCommand"/> or an
    /// <see cref="AsyncRelayCommand{T}"/> property to be generated, this will modify the behavior of these commands
    /// in case an exception is thrown by the underlying operation. It is the same as creating an instance of
    /// these command types with a constructor such as <see cref="AsyncRelayCommand(Func{System.Threading.Tasks.Task}, AsyncRelayCommandOptions)"/>
    /// and using the <see cref="AsyncRelayCommandOptions.FlowExceptionsToTaskScheduler"/> value.
    /// </para>
    /// </summary>
    /// <remarks>Using this property is not valid if the target command doesn't map to an asynchronous command.</remarks>
    public bool FlowExceptionsToTaskScheduler { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether a cancel command should also be generated for an asynchronous command.
    /// <para>
    /// When set to <see langword="true"/>, this additional code will be generated:
    /// <code>
    /// partial class MyViewModel
    /// {
    ///     private ICommand? loginUserCancelCommand;
    ///
    ///     public ICommand LoginUserCancelCommand => loginUserCancelCommand ??= LoginUserCommand.CreateCancelCommand();
    /// }
    /// </code>
    /// Where <c>LoginUserCommand</c> is an <see cref="IAsyncRelayCommand"/> defined in the class (or generated by this attribute as well).
    /// </para>
    /// </summary>
    /// <remarks>Using this property is not valid if the target command doesn't map to a cancellable asynchronous command.</remarks>
    public bool IncludeCancelCommand { get; init; }

    /// <summary>
    /// Gets or sets the name of the method that will be invoked when the command execution throws an
    /// exception. The referenced method must return <see langword="void"/> and take either a single
    /// <see cref="Exception"/> parameter, or a single event arguments parameter (see below for the
    /// supported event arguments types).
    /// <para>
    /// Referencing a handler only observes the fault: by default the exception still propagates exactly
    /// as it would with no handler attached. Whether it is instead considered handled is controlled by
    /// <see cref="SuppressExceptions"/>, independently of which parameter type the handler declares. A
    /// handler taking event arguments can also override that decision per fault, by assigning
    /// <see cref="RelayCommandExceptionEventArgs.Handled"/>.
    /// </para>
    /// <para>
    /// The handler is subscribed to the <c>ExecutionFailed</c> event of the generated command when the
    /// command instance is first created.
    /// </para>
    /// <para>
    /// For asynchronous commands it participates in both execution paths: faults are routed to it when
    /// execution is driven through <see cref="ICommand.Execute(object?)"/> as well as when the task
    /// returned by <c>ExecuteAsync</c> is awaited directly by the caller. Marking a fault as handled only
    /// suppresses the rethrow on the <see cref="ICommand.Execute(object?)"/> path: <c>ExecuteAsync</c>
    /// always returns the execution task itself, so a caller awaiting it always observes the exception,
    /// regardless of what the handler does. A canceled execution never invokes the handler.
    /// </para>
    /// <para>
    /// For commands with a parameter, the event arguments are <see cref="RelayCommandExceptionEventArgs{T}"/>,
    /// which also expose the strongly typed parameter that was passed to the failing execution. Such a command
    /// also accepts a handler declared with a single <see cref="RelayCommandExceptionEventArgs{T}"/> parameter,
    /// as long as <c>T</c> is exactly the command parameter type, which is the simplest way to reach the
    /// strongly typed parameter. A handler declared with the non-generic
    /// <see cref="RelayCommandExceptionEventArgs"/> parameter still binds too, and can cast to the generic
    /// type to reach it. Both event args shapes behave identically with respect to
    /// <see cref="RelayCommandExceptionEventArgs.Handled"/>.
    /// </para>
    /// </summary>
    public string? OnExecutionFailed { get; init; }

    /// <summary>
    /// Gets or sets whether a fault routed to <see cref="OnExecutionFailed"/> is considered handled.
    /// This is the initial value of <see cref="RelayCommandExceptionEventArgs.Handled"/>, which a handler
    /// taking event arguments can then override in either direction. The default is <see langword="false"/>,
    /// so that attaching a handler observes the fault without changing whether it propagates.
    /// </summary>
    /// <remarks>
    /// When this is set to <see langword="true"/>, an exception thrown by the command delegate does not
    /// propagate out of <see cref="ICommand.Execute(object?)"/>, including the rethrow onto the captured
    /// synchronization context that an asynchronous command would otherwise perform.
    /// <para>
    /// This does not extend to a caller awaiting the task returned by <c>ExecuteAsync</c>: that task is the
    /// execution task itself, so such a caller always observes the fault regardless of this setting. It also
    /// does not cover an exception thrown by the handler, and a canceled execution never invokes the handler
    /// at all.
    /// </para>
    /// <para>
    /// Setting this without also setting <see cref="OnExecutionFailed"/> has no effect, and is reported as a
    /// warning: with no handler to subscribe, the generated command has no <c>ExecutionFailed</c>
    /// subscription for this value to seed.
    /// </para>
    /// </remarks>
    public bool SuppressExceptions { get; init; }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.UnitTests.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CommunityToolkit.Mvvm.UnitTests;

[TestClass]
public class Test_RelayCommandOfT
{
    [TestMethod]
    public void Test_RelayCommandOfT_AlwaysEnabled()
    {
        string? text = string.Empty;

        RelayCommand<string>? command = new(s => text = s);

        Assert.IsTrue(command.CanExecute("Text"));
        Assert.IsTrue(command.CanExecute(null));

        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.CanExecute(new object()), "parameter");
        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.CanExecute(42), "parameter");
        
        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.Execute(new object()), "parameter");
        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.Execute(42), "parameter");

        (object?, EventArgs?) args = default;

        command.CanExecuteChanged += (s, e) => args = (s, e);

        command.NotifyCanExecuteChanged();

        Assert.AreSame(args.Item1, command);
        Assert.AreSame(args.Item2, EventArgs.Empty);

        command.Execute((object)"Hello");

        Assert.AreEqual("Hello", text);

        command.Execute(null);

        Assert.IsNull(text);
    }

    [TestMethod]
    public void Test_RelayCommand_WithCanExecuteFunction()
    {
        string? text = string.Empty;

        RelayCommand<string>? command = new(s => text = s, s => s != null);

        Assert.IsTrue(command.CanExecute("Text"));
        Assert.IsFalse(command.CanExecute(null));

        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.CanExecute(new object()), "parameter");
        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.CanExecute(42), "parameter");
        
        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.Execute(new object()), "parameter");
        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.Execute(42), "parameter");

        command.Execute((object)"Hello");

        Assert.AreEqual("Hello", text);

        command.Execute(null);

        // Logic is unconditionally invoked, the caller should check CanExecute first
        Assert.IsNull(text);
    }

    [TestMethod]
    public void Test_RelayCommand_InvalidArgumentWithValueType()
    {
        int n = 0;

        RelayCommand<int>? command = new(i => n = i);

        // Special case
        Assert.IsFalse(command.CanExecute(null));

        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.CanExecute("Hello"), "parameter");
        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.CanExecute(3.14f), "parameter");

        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.Execute(null), "parameter");
        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.Execute("Hello"), "parameter");
        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.Execute(3.14f), "parameter");
    }

    [TestMethod]
    public void Test_RelayCommand_InvalidArgumentWithValueType_WithCanExecute()
    {
        int n = 0;

        RelayCommand<int>? command = new(i => n = i, i => i > 0);

        // Special case
        Assert.IsFalse(command.CanExecute(null));

        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.CanExecute("Hello"), "parameter");
        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.CanExecute(3.14f), "parameter");

        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.Execute(null), "parameter");
        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.Execute("Hello"), "parameter");
        ExceptionHelper.ThrowsArgumentExceptionWithParameterName(() => command.Execute(3.14f), "parameter");
    }

    [TestMethod]
    public void Test_RelayCommandOfT_ExecutionFailed_HandledSuppressesException()
    {
        InvalidOperationException exception = new("Test");
        RelayCommand<string> command = new(s => throw exception);

        object? sender = null;
        Exception? routedException = null;

        command.ExecutionFailed += (s, e) =>
        {
            sender = s;
            routedException = e.Exception;
            e.Handled = true;
        };

        command.Execute("text");

        Assert.AreSame(command, sender);
        Assert.AreSame(exception, routedException);
    }

    [TestMethod]
    public void Test_RelayCommandOfT_ExecutionFailed_ExposesStronglyTypedParameter()
    {
        InvalidOperationException exception = new("Test");
        RelayCommand<string> command = new(s => throw exception);

        string? routedParameter = null;

        command.ExecutionFailed += (s, e) =>
        {
            // e is RelayCommandExceptionEventArgs<string>: Parameter is strongly typed, no cast needed.
            routedParameter = e.Parameter;
            e.Handled = true;
        };

        command.Execute("text");

        Assert.AreEqual("text", routedParameter);
    }

    [TestMethod]
    public void Test_RelayCommandOfT_ExecutionFailed_NotHandledRethrows()
    {
        InvalidOperationException exception = new("Test");
        RelayCommand<string> command = new(s => throw exception);

        Exception? routedException = null;

        command.ExecutionFailed += (s, e) => routedException = e.Exception;

        InvalidOperationException thrown = Assert.ThrowsExactly<InvalidOperationException>(() => command.Execute("text"));

        Assert.AreSame(exception, thrown);
        Assert.AreSame(exception, routedException);
    }

    [TestMethod]
    public void Test_RelayCommandOfT_ExecutionFailed_NoSubscriber_ExceptionPropagates()
    {
        // The handler is subscribed and then unsubscribed again, so that the backing event field goes
        // back to null. That covers the "no subscribers" fast path specifically for a command that did
        // have a subscription at some point, and asserts the detached handler is no longer invoked.
        InvalidOperationException exception = new("Test");
        RelayCommand<string> command = new(s => throw exception);

        int detachedHandlerInvocations = 0;

        void Handler(object? sender, RelayCommandExceptionEventArgs<string> e)
        {
            detachedHandlerInvocations++;

            e.Handled = true;
        }

        command.ExecutionFailed += Handler;
        command.ExecutionFailed -= Handler;

        InvalidOperationException thrown = Assert.ThrowsExactly<InvalidOperationException>(() => command.Execute("text"));

        Assert.AreSame(exception, thrown);
        Assert.AreEqual(0, detachedHandlerInvocations);
    }

    [TestMethod]
    public void Test_RelayCommandOfT_ExecutionFailed_InvalidCommandArgumentIsNotRouted()
    {
        // The ArgumentException produced by an incompatible command parameter is a usage
        // error raised before the wrapped delegate runs, so it must NOT be routed.
        RelayCommand<string> command = new(static s => { });

        bool raised = false;

        command.ExecutionFailed += (s, e) => raised = true;

        _ = Assert.ThrowsExactly<ArgumentException>(() => command.Execute(42));

        Assert.IsFalse(raised);
    }

    [TestMethod]
    public void Test_RelayCommandOfT_ExecutionFailed_OperationCanceledExceptionIsRouted()
    {
        // Synchronous commands have no cancellation concept, so OperationCanceledException
        // is routed to the event like any other exception (unlike the async commands).
        OperationCanceledException exception = new();
        RelayCommand<string> command = new(s => throw exception);

        Exception? routedException = null;

        command.ExecutionFailed += (s, e) =>
        {
            routedException = e.Exception;
            e.Handled = true;
        };

        command.Execute("text");

        Assert.AreSame(exception, routedException);
    }

    [TestMethod]
    public void Test_RelayCommandOfT_ExecutionFailed_Multicast_SecondHandlerStillRunsAfterFirstMarksHandled()
    {
        // Both handlers are part of the same multicast invocation: the first setting Handled to
        // true must not prevent the second one from also running, and the exception must not be
        // rethrown, since it's the same RelayCommandExceptionEventArgs<string> instance being shared.
        InvalidOperationException exception = new("Test");
        RelayCommand<string> command = new(s => throw exception);

        bool secondHandlerRan = false;

        command.ExecutionFailed += (s, e) => e.Handled = true;
        command.ExecutionFailed += (s, e) => secondHandlerRan = true;

        command.Execute("text");

        Assert.IsTrue(secondHandlerRan);
    }

    [TestMethod]
    public void Test_RelayCommandOfT_ExecutionFailed_ThrowingHandlerReplacesOriginalException()
    {
        // Standard .NET event semantics apply: an exception thrown by a handler is not
        // intercepted, it escapes to the caller and replaces the original command exception.
        InvalidOperationException commandException = new("Command");
        NotSupportedException handlerException = new("Handler");
        RelayCommand<string> command = new(s => throw commandException);

        command.ExecutionFailed += (s, e) => throw handlerException;

        NotSupportedException thrown = Assert.ThrowsExactly<NotSupportedException>(() => command.Execute("text"));

        Assert.AreSame(handlerException, thrown);
    }

    [TestMethod]
    public void Test_RelayCommandOfT_ExecutionFailed_SubscriberAttachedDuringExecutionIsNotRouted()
    {
        // Whether the routing path is taken at all is decided when Execute is invoked. There were no
        // subscribers at that point, so the fast path ran the delegate with no exception handling in
        // place, and a handler attached while it is running is not notified for that execution.
        InvalidOperationException exception = new("Test");
        RelayCommand<string> command = null!;

        bool raised = false;

        command = new RelayCommand<string>(text =>
        {
            command.ExecutionFailed += (s, e) =>
            {
                raised = true;
                e.Handled = true;
            };

            throw exception;
        });

        InvalidOperationException thrown = Assert.ThrowsExactly<InvalidOperationException>(() => command.Execute("text"));

        Assert.AreSame(exception, thrown);
        Assert.IsFalse(raised);
    }

    [TestMethod]
    public void Test_RelayCommandOfT_ExecutionFailed_SubscriberDetachedDuringExecutionIsNotRouted()
    {
        // Standard event semantics: the handler list is read when the exception is raised, so a handler
        // detached while the delegate was running is not notified and the exception simply propagates.
        InvalidOperationException exception = new("Test");
        RelayCommand<string> command = null!;

        bool raised = false;

        void Handler(object? sender, RelayCommandExceptionEventArgs<string> e)
        {
            raised = true;
            e.Handled = true;
        }

        command = new RelayCommand<string>(s =>
        {
            command.ExecutionFailed -= Handler;

            throw exception;
        });

        command.ExecutionFailed += Handler;

        InvalidOperationException thrown = Assert.ThrowsExactly<InvalidOperationException>(() => command.Execute("text"));

        Assert.AreSame(exception, thrown);
        Assert.IsFalse(raised);
    }
}

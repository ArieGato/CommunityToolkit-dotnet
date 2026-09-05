// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using CommunityToolkit.Mvvm.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CommunityToolkit.Mvvm.UnitTests;

[TestClass]
public class Test_RelayCommand
{
    [TestMethod]
    public void Test_RelayCommand_AlwaysEnabled()
    {
        int ticks = 0;

        RelayCommand? command = new(() => ticks++);

        Assert.IsTrue(command.CanExecute(null));
        Assert.IsTrue(command.CanExecute(new object()));

        (object?, EventArgs?) args = default;

        command.CanExecuteChanged += (s, e) => args = (s, e);

        command.NotifyCanExecuteChanged();

        Assert.AreSame(args.Item1, command);
        Assert.AreSame(args.Item2, EventArgs.Empty);

        command.Execute(null);

        Assert.AreEqual(1, ticks);

        command.Execute(new object());

        Assert.AreEqual(2, ticks);
    }

    [TestMethod]
    public void Test_RelayCommand_WithCanExecuteFunctionTrue()
    {
        int ticks = 0;

        RelayCommand? command = new(() => ticks++, () => true);

        Assert.IsTrue(command.CanExecute(null));
        Assert.IsTrue(command.CanExecute(new object()));

        command.Execute(null);

        Assert.AreEqual(1, ticks);

        command.Execute(new object());

        Assert.AreEqual(2, ticks);
    }

    [TestMethod]
    public void Test_RelayCommand_WithCanExecuteFunctionFalse()
    {
        int ticks = 0;

        RelayCommand? command = new(() => ticks++, () => false);

        Assert.IsFalse(command.CanExecute(null));
        Assert.IsFalse(command.CanExecute(new object()));

        command.Execute(null);

        // Logic is unconditionally invoked, the caller should check CanExecute first
        Assert.AreEqual(1, ticks);

        command.Execute(new object());

        Assert.AreEqual(2, ticks);
    }

    [TestMethod]
    public void Test_RelayCommand_ExecutionFailed_NoSubscriber_ExceptionPropagates()
    {
        // The handler is subscribed and then unsubscribed again, so that the backing event field goes
        // back to null. That covers the "no subscribers" fast path specifically for a command that did
        // have a subscription at some point, and asserts the detached handler is no longer invoked.
        InvalidOperationException exception = new("Test");
        RelayCommand command = new(() => throw exception);

        int detachedHandlerInvocations = 0;

        void Handler(object? sender, RelayCommandExceptionEventArgs e)
        {
            detachedHandlerInvocations++;

            e.Handled = true;
        }

        command.ExecutionFailed += Handler;
        command.ExecutionFailed -= Handler;

        InvalidOperationException thrown = Assert.ThrowsExactly<InvalidOperationException>(() => command.Execute(null));

        Assert.AreSame(exception, thrown);
        Assert.AreEqual(0, detachedHandlerInvocations);
    }

    [TestMethod]
    public void Test_RelayCommand_ExecutionFailed_HandledSuppressesException()
    {
        InvalidOperationException exception = new("Test");
        RelayCommand command = new(() => throw exception);

        object? sender = null;
        Exception? routedException = null;

        command.ExecutionFailed += (s, e) =>
        {
            sender = s;
            routedException = e.Exception;
            e.Handled = true;
        };

        command.Execute(null);

        Assert.AreSame(command, sender);
        Assert.AreSame(exception, routedException);
    }

    [TestMethod]
    public void Test_RelayCommand_ExecutionFailed_NotHandledRethrows()
    {
        InvalidOperationException exception = new("Test");
        RelayCommand command = new(() => throw exception);

        Exception? routedException = null;

        command.ExecutionFailed += (s, e) => routedException = e.Exception;

        InvalidOperationException thrown = Assert.ThrowsExactly<InvalidOperationException>(() => command.Execute(null));

        Assert.AreSame(exception, thrown);
        Assert.AreSame(exception, routedException);
    }

    [TestMethod]
    public void Test_RelayCommand_ExecutionFailed_OperationCanceledExceptionIsRouted()
    {
        // Synchronous commands have no cancellation concept, so OperationCanceledException
        // is routed to the event like any other exception (unlike the async commands).
        OperationCanceledException exception = new();
        RelayCommand command = new(() => throw exception);

        Exception? routedException = null;

        command.ExecutionFailed += (s, e) =>
        {
            routedException = e.Exception;
            e.Handled = true;
        };

        command.Execute(null);

        Assert.AreSame(exception, routedException);
    }

    [TestMethod]
    public void Test_RelayCommand_ExecutionFailed_SuccessfulExecutionDoesNotRaise()
    {
        bool raised = false;
        int executions = 0;

        RelayCommand command = new(() => executions++);

        command.ExecutionFailed += (s, e) => raised = true;

        command.Execute(null);

        // The delegate must still have been invoked: this distinguishes "the event was
        // not raised because the execution succeeded" from "nothing ran at all".
        Assert.AreEqual(1, executions);
        Assert.IsFalse(raised);
    }

    [TestMethod]
    public void Test_RelayCommand_ExecutionFailed_Multicast_SecondHandlerStillRunsAfterFirstMarksHandled()
    {
        // Both handlers are part of the same multicast invocation: the first setting Handled to
        // true must not prevent the second one from also running, and the exception must not be
        // rethrown, since it's the same RelayCommandExceptionEventArgs instance being shared.
        InvalidOperationException exception = new("Test");
        RelayCommand command = new(() => throw exception);

        bool secondHandlerRan = false;

        command.ExecutionFailed += (s, e) => e.Handled = true;
        command.ExecutionFailed += (s, e) => secondHandlerRan = true;

        command.Execute(null);

        Assert.IsTrue(secondHandlerRan);
    }

    [TestMethod]
    public void Test_RelayCommand_ExecutionFailed_ThrowingHandlerReplacesOriginalException()
    {
        // Standard .NET event semantics apply: an exception thrown by a handler is not
        // intercepted, it escapes to the caller and replaces the original command exception.
        InvalidOperationException commandException = new("Command");
        NotSupportedException handlerException = new("Handler");
        RelayCommand command = new(() => throw commandException);

        command.ExecutionFailed += (s, e) => throw handlerException;

        NotSupportedException thrown = Assert.ThrowsExactly<NotSupportedException>(() => command.Execute(null));

        Assert.AreSame(handlerException, thrown);
    }

    [TestMethod]
    public void Test_RelayCommand_ExecutionFailed_SubscriberAttachedDuringExecutionIsNotRouted()
    {
        // Whether the routing path is taken at all is decided when Execute is invoked. There were no
        // subscribers at that point, so the fast path ran the delegate with no exception handling in
        // place, and a handler attached while it is running is not notified for that execution.
        InvalidOperationException exception = new("Test");
        RelayCommand command = null!;

        bool raised = false;

        command = new RelayCommand(() =>
        {
            command.ExecutionFailed += (s, e) =>
            {
                raised = true;
                e.Handled = true;
            };

            throw exception;
        });

        InvalidOperationException thrown = Assert.ThrowsExactly<InvalidOperationException>(() => command.Execute(null));

        Assert.AreSame(exception, thrown);
        Assert.IsFalse(raised);
    }

    [TestMethod]
    public void Test_RelayCommand_ExecutionFailed_SubscriberDetachedDuringExecutionIsNotRouted()
    {
        // Standard event semantics: the handler list is read when the exception is raised, so a handler
        // detached while the delegate was running is not notified and the exception simply propagates.
        InvalidOperationException exception = new("Test");
        RelayCommand command = null!;

        bool raised = false;

        void Handler(object? sender, RelayCommandExceptionEventArgs e)
        {
            raised = true;
            e.Handled = true;
        }

        command = new RelayCommand(() =>
        {
            command.ExecutionFailed -= Handler;

            throw exception;
        });

        command.ExecutionFailed += Handler;

        InvalidOperationException thrown = Assert.ThrowsExactly<InvalidOperationException>(() => command.Execute(null));

        Assert.AreSame(exception, thrown);
        Assert.IsFalse(raised);
    }

    [TestMethod]
    public void Test_RelayCommandExceptionEventArgs_NullException_Throws()
    {
        _ = Assert.ThrowsExactly<ArgumentNullException>(() => new RelayCommandExceptionEventArgs(null!));
    }
}

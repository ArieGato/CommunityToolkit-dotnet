// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using CommunityToolkit.Mvvm.SourceGenerators.ComponentModel.Models;
using CommunityToolkit.Mvvm.SourceGenerators.Extensions;
using CommunityToolkit.Mvvm.SourceGenerators.Helpers;
using CommunityToolkit.Mvvm.SourceGenerators.Input.Models;
using CommunityToolkit.Mvvm.SourceGenerators.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static CommunityToolkit.Mvvm.SourceGenerators.Diagnostics.DiagnosticDescriptors;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace CommunityToolkit.Mvvm.SourceGenerators;

/// <inheritdoc/>
partial class RelayCommandGenerator
{
    /// <summary>
    /// A container for all the logic for <see cref="RelayCommandGenerator"/>.
    /// </summary>
    internal static class Execute
    {
        /// <summary>
        /// Processes a given annotated methods and produces command info, if possible.
        /// </summary>
        /// <param name="methodSymbol">The input <see cref="IMethodSymbol"/> instance to process.</param>
        /// <param name="attributeData">The <see cref="AttributeData"/> instance the method was annotated with.</param>
        /// <param name="semanticModel">The <see cref="SemanticModel"/> instance for the current run.</param>
        /// <param name="token">The cancellation token for the current operation.</param>
        /// <param name="commandInfo">The resulting <see cref="CommandInfo"/> instance, if successfully generated.</param>
        /// <param name="diagnostics">The resulting diagnostics from the processing operation.</param>
        /// <returns>Whether a <see cref="CommandInfo"/> instance could be generated successfully.</returns>
        public static bool TryGetInfo(
            IMethodSymbol methodSymbol,
            AttributeData attributeData,
            SemanticModel semanticModel,
            CancellationToken token,
            [NotNullWhen(true)] out CommandInfo? commandInfo,
            out ImmutableArray<DiagnosticInfo> diagnostics)
        {
            using ImmutableArrayBuilder<DiagnosticInfo> builder = ImmutableArrayBuilder<DiagnosticInfo>.Rent();

            // Validate the method definition is unique
            if (!IsCommandDefinitionUnique(methodSymbol, builder))
            {
                goto Failure;
            }

            token.ThrowIfCancellationRequested();

            // Get the command field and property names
            (string fieldName, string propertyName) = GetGeneratedFieldAndPropertyNames(methodSymbol);

            token.ThrowIfCancellationRequested();

            // Get the command type symbols
            if (!TryMapCommandTypesFromMethod(
                methodSymbol,
                in builder,
                out string? commandInterfaceType,
                out string? commandClassType,
                out string? delegateType,
                out bool supportsCancellation,
                out ITypeSymbol? commandParameterType,
                out ImmutableArray<string> commandTypeArguments,
                out ImmutableArray<string> commandTypeArgumentsWithNullabilityAnnotations,
                out ImmutableArray<string> delegateTypeArgumentsWithNullabilityAnnotations))
            {
                goto Failure;
            }

            token.ThrowIfCancellationRequested();

            // Check the switch to allow concurrent executions
            if (!TryGetAllowConcurrentExecutionsSwitch(
                methodSymbol,
                attributeData,
                commandClassType,
                in builder,
                out bool allowConcurrentExecutions))
            {
                goto Failure;   
            }

            token.ThrowIfCancellationRequested();

            // Check the switch to control exception flow
            if (!TryGetFlowExceptionsToTaskSchedulerSwitch(
                methodSymbol,
                attributeData,
                commandClassType,
                in builder,
                out bool flowExceptionsToTaskScheduler))
            {
                goto Failure;
            }

            token.ThrowIfCancellationRequested();

            // Get the CanExecute expression type, if any
            if (!TryGetCanExecuteExpressionType(
                methodSymbol,
                attributeData,
                commandTypeArguments,
                token,
                in builder,
                out string? canExecuteMemberName,
                out CanExecuteExpressionType? canExecuteExpressionType))
            {
                goto Failure;
            }

            token.ThrowIfCancellationRequested();

            // Get the option to include a cancel command, if any
            if (!TryGetIncludeCancelCommandSwitch(
                methodSymbol,
                attributeData,
                commandClassType,
                supportsCancellation,
                in builder,
                out bool generateCancelCommand))
            {
                goto Failure;
            }

            token.ThrowIfCancellationRequested();

            // Get the OnExecutionFailed handler info, if any
            if (!TryGetOnExecutionFailedInfo(
                methodSymbol,
                attributeData,
                commandParameterType,
                in builder,
                out string? onExecutionFailedMemberName,
                out bool onExecutionFailedUsesEventArgs,
                out bool suppressExceptions))
            {
                goto Failure;
            }

            token.ThrowIfCancellationRequested();

            // Get all forwarded attributes (don't stop in case of errors, just ignore faulting attributes)
            GatherForwardedAttributes(
                methodSymbol,
                semanticModel,
                token,
                in builder,
                out ImmutableArray<AttributeInfo> forwardedAttributes);

            token.ThrowIfCancellationRequested();

            commandInfo = new CommandInfo(
                methodSymbol.Name,
                fieldName,
                propertyName,
                commandInterfaceType,
                commandClassType,
                delegateType,
                commandTypeArgumentsWithNullabilityAnnotations,
                delegateTypeArgumentsWithNullabilityAnnotations,
                canExecuteMemberName,
                canExecuteExpressionType,
                allowConcurrentExecutions,
                flowExceptionsToTaskScheduler,
                generateCancelCommand,
                onExecutionFailedMemberName,
                onExecutionFailedUsesEventArgs,
                suppressExceptions,
                forwardedAttributes);

            diagnostics = builder.ToImmutable();

            return true;

            Failure:
            commandInfo = null;
            diagnostics = builder.ToImmutable();

            return false;
        }

        /// <summary>
        /// Creates the <see cref="MemberDeclarationSyntax"/> instances for a specified command.
        /// </summary>
        /// <param name="commandInfo">The input <see cref="CommandInfo"/> instance with the info to generate the command.</param>
        /// <returns>The <see cref="MemberDeclarationSyntax"/> instances for the input command.</returns>
        public static ImmutableArray<MemberDeclarationSyntax> GetSyntax(CommandInfo commandInfo)
        {
            // Prepare all necessary type names with type arguments
            string commandInterfaceTypeXmlName = commandInfo.CommandTypeArguments.IsEmpty
                ? commandInfo.CommandInterfaceType
                : commandInfo.CommandInterfaceType + "{T}";
            string commandClassTypeName = commandInfo.CommandTypeArguments.IsEmpty
                ? commandInfo.CommandClassType
                : $"{commandInfo.CommandClassType}<{string.Join(", ", commandInfo.CommandTypeArguments)}>";
            string commandInterfaceTypeName = commandInfo.CommandTypeArguments.IsEmpty
                ? commandInfo.CommandInterfaceType
                : $"{commandInfo.CommandInterfaceType}<{string.Join(", ", commandInfo.CommandTypeArguments)}>";
            string delegateTypeName = commandInfo.DelegateTypeArguments.IsEmpty
                ? commandInfo.DelegateType
                : $"{commandInfo.DelegateType}<{string.Join(", ", commandInfo.DelegateTypeArguments)}>";

            // Prepare the forwarded field attributes, if any
            AttributeListSyntax[] forwardedFieldAttributes =
                commandInfo.ForwardedAttributes
                .Where(static a => a.AttributeTarget is SyntaxKind.FieldKeyword)
                .Select(static a => AttributeList(SingletonSeparatedList(a.GetSyntax())))
                .ToArray();

            // Also prepare any forwarded property attributes
            AttributeListSyntax[] forwardedPropertyAttributes =
                commandInfo.ForwardedAttributes
                .Where(static a => a.AttributeTarget is SyntaxKind.PropertyKeyword)
                .Select(static a => AttributeList(SingletonSeparatedList(a.GetSyntax())))
                .ToArray();

            // Construct the generated field as follows:
            //
            // /// <summary>The backing field for <see cref="<COMMAND_PROPERTY_NAME>"/></summary>
            // [global::System.CodeDom.Compiler.GeneratedCode("...", "...")]
            // <FORWARDED_ATTRIBUTES>
            // private <COMMAND_TYPE>? <COMMAND_FIELD_NAME>;
            FieldDeclarationSyntax fieldDeclaration =
                FieldDeclaration(
                VariableDeclaration(NullableType(IdentifierName(commandClassTypeName)))
                .AddVariables(VariableDeclarator(Identifier(commandInfo.FieldName))))
                .AddModifiers(Token(SyntaxKind.PrivateKeyword))
                .AddAttributeLists(
                    AttributeList(SingletonSeparatedList(
                        Attribute(IdentifierName("global::System.CodeDom.Compiler.GeneratedCode"))
                        .AddArgumentListArguments(
                            AttributeArgument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(typeof(RelayCommandGenerator).FullName))),
                            AttributeArgument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(typeof(RelayCommandGenerator).Assembly.GetName().Version.ToString()))))))
                    .WithOpenBracketToken(Token(TriviaList(Comment($"/// <summary>The backing field for <see cref=\"{commandInfo.PropertyName}\"/>.</summary>")), SyntaxKind.OpenBracketToken, TriviaList())))
                .AddAttributeLists(forwardedFieldAttributes);

            // Prepares the argument to pass the underlying method to invoke
            using ImmutableArrayBuilder<ArgumentSyntax> commandCreationArguments = ImmutableArrayBuilder<ArgumentSyntax>.Rent();

            // The first argument is the execute method, which is always present
            commandCreationArguments.Add(
                Argument(
                    ObjectCreationExpression(IdentifierName(delegateTypeName))
                    .AddArgumentListArguments(Argument(IdentifierName(commandInfo.MethodName)))));

            // Get the can execute expression, if available
            ExpressionSyntax? canExecuteExpression = commandInfo.CanExecuteExpressionType switch
            {
                // Create a lambda expression ignoring the input value:
                //
                // new <RELAY_COMMAND_TYPE>(<METHOD_EXPRESSION>, _ => <CAN_EXECUTE_METHOD>());
                CanExecuteExpressionType.MethodInvocationLambdaWithDiscard =>
                    SimpleLambdaExpression(
                        Parameter(Identifier(TriviaList(), SyntaxKind.UnderscoreToken, "_", "_", TriviaList())))
                    .WithExpressionBody(InvocationExpression(IdentifierName(commandInfo.CanExecuteMemberName!))),

                // Create a lambda expression returning the property value:
                //
                // new <RELAY_COMMAND_TYPE>(<METHOD_EXPRESSION>, () => <CAN_EXECUTE_PROPERTY>);
                CanExecuteExpressionType.PropertyAccessLambda =>
                    ParenthesizedLambdaExpression()
                    .WithExpressionBody(IdentifierName(commandInfo.CanExecuteMemberName!)),

                // Create a lambda expression again, but discarding the input value:
                //
                // new <RELAY_COMMAND_TYPE>(<METHOD_EXPRESSION>, _ => <CAN_EXECUTE_PROPERTY>);
                CanExecuteExpressionType.PropertyAccessLambdaWithDiscard =>
                    SimpleLambdaExpression(
                        Parameter(Identifier(TriviaList(), SyntaxKind.UnderscoreToken, "_", "_", TriviaList())))
                    .WithExpressionBody(IdentifierName(commandInfo.CanExecuteMemberName!)),

                    // Create a method group expression, which will become:
                    //
                    // new <RELAY_COMMAND_TYPE>(<METHOD_EXPRESSION>, <CAN_EXECUTE_METHOD>);
                    CanExecuteExpressionType.MethodGroup => IdentifierName(commandInfo.CanExecuteMemberName!),
                _ => null
            };

            // Add the can execute expression to the arguments, if available
            if (canExecuteExpression is not null)
            {
                commandCreationArguments.Add(Argument(canExecuteExpression));
            }

            // Enable concurrent executions, if requested
            if (commandInfo.AllowConcurrentExecutions && !commandInfo.FlowExceptionsToTaskScheduler)
            {
                commandCreationArguments.Add(
                    Argument(MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName("global::CommunityToolkit.Mvvm.Input.AsyncRelayCommandOptions"),
                        IdentifierName("AllowConcurrentExecutions"))));
            }
            else if (commandInfo.FlowExceptionsToTaskScheduler && !commandInfo.AllowConcurrentExecutions)
            {
                // Enable exception flow, if requested
                commandCreationArguments.Add(
                    Argument(MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName("global::CommunityToolkit.Mvvm.Input.AsyncRelayCommandOptions"),
                        IdentifierName("FlowExceptionsToTaskScheduler"))));
            }
            else if (commandInfo.AllowConcurrentExecutions && commandInfo.FlowExceptionsToTaskScheduler)
            {
                // Enable both concurrency control and exception flow
                commandCreationArguments.Add(
                    Argument(BinaryExpression(
                        SyntaxKind.BitwiseOrExpression,
                        MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            IdentifierName("global::CommunityToolkit.Mvvm.Input.AsyncRelayCommandOptions"),
                            IdentifierName("AllowConcurrentExecutions")),
                        MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            IdentifierName("global::CommunityToolkit.Mvvm.Input.AsyncRelayCommandOptions"),
                            IdentifierName("FlowExceptionsToTaskScheduler")))));
            }

            // Construct the generated property as follows (the explicit delegate cast is needed to avoid overload resolution conflicts):
            //
            // /// <summary>Gets an <see cref="<COMMAND_INTERFACE_TYPE>" instance wrapping <see cref="<METHOD_NAME>"/> and <see cref="<OPTIONAL_CAN_EXECUTE>"/>.</summary>
            // [global::System.CodeDom.Compiler.GeneratedCode("...", "...")]
            // [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
            // <FORWARDED_ATTRIBUTES>
            // public <COMMAND_TYPE> <COMMAND_PROPERTY_NAME> => <COMMAND_FIELD_NAME> ??= new <RELAY_COMMAND_TYPE>(<COMMAND_CREATION_ARGUMENTS>);
            //
            // When an OnExecutionFailed handler is present, the property is instead emitted with a getter
            // block that creates the command and subscribes the handler on first access. The command is
            // built into a local and only assigned to the backing field once the subscription is in place,
            // so that no other thread can ever observe a published command with no handler attached:
            //
            // public <COMMAND_TYPE> <COMMAND_PROPERTY_NAME>
            // {
            //     get
            //     {
            //         if (<COMMAND_FIELD_NAME> is null)
            //         {
            //             <RELAY_COMMAND_TYPE> <LOCAL_NAME> = new <RELAY_COMMAND_TYPE>(<COMMAND_CREATION_ARGUMENTS>);
            //             <LOCAL_NAME>.ExecutionFailed += (s, e) => ...;
            //             <COMMAND_FIELD_NAME> = <LOCAL_NAME>;
            //         }
            //
            //         return <COMMAND_FIELD_NAME>;
            //     }
            // }
            PropertyDeclarationSyntax propertyDeclaration =
                PropertyDeclaration(
                    IdentifierName(commandInterfaceTypeName),
                    Identifier(commandInfo.PropertyName))
                .AddModifiers(Token(SyntaxKind.PublicKeyword))
                .AddAttributeLists(
                    AttributeList(SingletonSeparatedList(
                        Attribute(IdentifierName("global::System.CodeDom.Compiler.GeneratedCode"))
                        .AddArgumentListArguments(
                            AttributeArgument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(typeof(RelayCommandGenerator).FullName))),
                            AttributeArgument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(typeof(RelayCommandGenerator).Assembly.GetName().Version.ToString()))))))
                    .WithOpenBracketToken(Token(TriviaList(Comment(
                        $"/// <summary>Gets an <see cref=\"{commandInterfaceTypeXmlName}\"/> instance wrapping <see cref=\"{commandInfo.MethodName}\"/>.</summary>")),
                        SyntaxKind.OpenBracketToken,
                        TriviaList())),
                    AttributeList(SingletonSeparatedList(Attribute(IdentifierName("global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage")))))
                .AddAttributeLists(forwardedPropertyAttributes);

            if (commandInfo.OnExecutionFailedMemberName is null)
            {
                propertyDeclaration = propertyDeclaration
                    .WithExpressionBody(
                        ArrowExpressionClause(
                            AssignmentExpression(
                                SyntaxKind.CoalesceAssignmentExpression,
                                IdentifierName(commandInfo.FieldName),
                                ObjectCreationExpression(IdentifierName(commandClassTypeName))
                                .AddArgumentListArguments(commandCreationArguments.ToArray()))))
                    .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));
            }
            else
            {
                // Prepare the ExecutionFailed subscription. The handler is passed the event args themselves, or
                // just the exception, depending on the parameter type it declares:
                //
                // <FIELD>.ExecutionFailed += (s, e) => <HANDLER>(e);
                // <FIELD>.ExecutionFailed += (s, e) => <HANDLER>(e.Exception);
                //
                // That is the whole subscription by default: attaching a handler observes the fault without
                // changing whether it propagates. When SuppressExceptions is set, the handled state is seeded
                // as well. For a handler taking event args the seed is written before the call, so that the
                // handler can still override the decision in either direction:
                //
                // <FIELD>.ExecutionFailed += (s, e) => { e.Handled = true; <HANDLER>(e); };
                //
                // For a handler taking just the exception there is nothing to override, since it cannot observe
                // the event args at all, so the seed is written after the call instead:
                //
                // <FIELD>.ExecutionFailed += (s, e) => { <HANDLER>(e.Exception); e.Handled = true; };
                ExpressionSyntax handlerArgument = commandInfo.OnExecutionFailedUsesEventArgs
                    ? IdentifierName("e")
                    : MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName("e"),
                        IdentifierName("Exception"));

                InvocationExpressionSyntax handlerInvocation =
                    InvocationExpression(IdentifierName(commandInfo.OnExecutionFailedMemberName))
                    .AddArgumentListArguments(Argument(handlerArgument));

                ParenthesizedLambdaExpressionSyntax handlerLambda =
                    ParenthesizedLambdaExpression()
                    .AddParameterListParameters(Parameter(Identifier("s")), Parameter(Identifier("e")));

                if (commandInfo.SuppressExceptions)
                {
                    ExpressionStatementSyntax handledSeedStatement =
                        ExpressionStatement(
                            AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    IdentifierName("e"),
                                    IdentifierName("Handled")),
                                LiteralExpression(SyntaxKind.TrueLiteralExpression)));

                    handlerLambda = handlerLambda.WithBlock(commandInfo.OnExecutionFailedUsesEventArgs
                        ? Block(handledSeedStatement, ExpressionStatement(handlerInvocation))
                        : Block(ExpressionStatement(handlerInvocation), handledSeedStatement));
                }
                else
                {
                    handlerLambda = handlerLambda.WithExpressionBody(handlerInvocation);
                }

                // Get the name of the local the command is built into before being published to the backing
                // field. Every identifier that the getter body can possibly reference is checked here, so
                // that the local can never shadow a member the rest of the emitted code needs to bind to.
                string localCommandName = "command";

                while (localCommandName == commandInfo.FieldName ||
                       localCommandName == commandInfo.MethodName ||
                       localCommandName == commandInfo.OnExecutionFailedMemberName ||
                       localCommandName == commandInfo.CanExecuteMemberName)
                {
                    localCommandName = $"_{localCommandName}";
                }

                StatementSyntax subscriptionStatement =
                    ExpressionStatement(
                        AssignmentExpression(
                            SyntaxKind.AddAssignmentExpression,
                            MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                IdentifierName(localCommandName),
                                IdentifierName("ExecutionFailed")),
                            handlerLambda));

                propertyDeclaration = propertyDeclaration
                    .AddAccessorListAccessors(
                        AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                        .WithBody(Block(
                            IfStatement(
                                IsPatternExpression(
                                    IdentifierName(commandInfo.FieldName),
                                    ConstantPattern(LiteralExpression(SyntaxKind.NullLiteralExpression))),
                                Block(
                                    LocalDeclarationStatement(
                                        VariableDeclaration(IdentifierName(commandClassTypeName))
                                        .AddVariables(
                                            VariableDeclarator(Identifier(localCommandName))
                                            .WithInitializer(EqualsValueClause(
                                                ObjectCreationExpression(IdentifierName(commandClassTypeName))
                                                .AddArgumentListArguments(commandCreationArguments.ToArray()))))),
                                    subscriptionStatement,
                                    ExpressionStatement(
                                        AssignmentExpression(
                                            SyntaxKind.SimpleAssignmentExpression,
                                            IdentifierName(commandInfo.FieldName),
                                            IdentifierName(localCommandName))))),
                            ReturnStatement(IdentifierName(commandInfo.FieldName)))));
            }

            // Conditionally declare the additional members for the cancel commands
            if (commandInfo.IncludeCancelCommand)
            {
                // Prepare all necessary member and type names
                string cancelCommandFieldName = $"{commandInfo.FieldName.Substring(0, commandInfo.FieldName.Length - "Command".Length)}CancelCommand";
                string cancelCommandPropertyName = $"{commandInfo.PropertyName.Substring(0, commandInfo.PropertyName.Length - "Command".Length)}CancelCommand";

                // Construct the generated field for the cancel command as follows:
                //
                // /// <summary>The backing field for <see cref="<COMMAND_PROPERTY_NAME>"/></summary>
                // [global::System.CodeDom.Compiler.GeneratedCode("...", "...")]
                // private global::System.Windows.Input.ICommand? <CANCEL_COMMAND_FIELD_NAME>;
                FieldDeclarationSyntax cancelCommandFieldDeclaration =
                    FieldDeclaration(
                    VariableDeclaration(NullableType(IdentifierName("global::System.Windows.Input.ICommand")))
                    .AddVariables(VariableDeclarator(Identifier(cancelCommandFieldName))))
                    .AddModifiers(Token(SyntaxKind.PrivateKeyword))
                    .AddAttributeLists(
                        AttributeList(SingletonSeparatedList(
                            Attribute(IdentifierName("global::System.CodeDom.Compiler.GeneratedCode"))
                            .AddArgumentListArguments(
                                AttributeArgument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(typeof(RelayCommandGenerator).FullName))),
                                AttributeArgument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(typeof(RelayCommandGenerator).Assembly.GetName().Version.ToString()))))))
                        .WithOpenBracketToken(Token(TriviaList(Comment($"/// <summary>The backing field for <see cref=\"{cancelCommandPropertyName}\"/>.</summary>")), SyntaxKind.OpenBracketToken, TriviaList())));

                // Construct the generated property as follows (the explicit delegate cast is needed to avoid overload resolution conflicts):
                //
                // /// <summary>Gets an <see cref="global::System.Windows.Input.ICommand" instance that can be used to cancel <see cref="<COMMAND_PROPERTY_NAME>"/>.</summary>
                // [global::System.CodeDom.Compiler.GeneratedCode("...", "...")]
                // [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
                // public global::System.Windows.Input.ICommand <CANCEL_COMMAND_PROPERTY_NAME> => <CANCEL_COMMAND_FIELD_NAME> ??= global::CommunityToolkit.Mvvm.Input.IAsyncRelayCommandExtensions.CreateCancelCommand(<COMMAND_PROPERTY_NAME>);
                PropertyDeclarationSyntax cancelCommandPropertyDeclaration =
                    PropertyDeclaration(
                        IdentifierName("global::System.Windows.Input.ICommand"),
                        Identifier(cancelCommandPropertyName))
                    .AddModifiers(Token(SyntaxKind.PublicKeyword))
                    .AddAttributeLists(
                        AttributeList(SingletonSeparatedList(
                            Attribute(IdentifierName("global::System.CodeDom.Compiler.GeneratedCode"))
                            .AddArgumentListArguments(
                                AttributeArgument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(typeof(RelayCommandGenerator).FullName))),
                                AttributeArgument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(typeof(RelayCommandGenerator).Assembly.GetName().Version.ToString()))))))
                        .WithOpenBracketToken(Token(TriviaList(Comment(
                            $"/// <summary>Gets an <see cref=\"global::System.Windows.Input.ICommand\"/> instance that can be used to cancel <see cref=\"{commandInfo.PropertyName}\"/>.</summary>")),
                            SyntaxKind.OpenBracketToken,
                            TriviaList())),
                        AttributeList(SingletonSeparatedList(Attribute(IdentifierName("global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage")))))
                    .WithExpressionBody(
                        ArrowExpressionClause(
                            AssignmentExpression(
                                SyntaxKind.CoalesceAssignmentExpression,
                                IdentifierName(cancelCommandFieldName),
                                InvocationExpression(
                                    MemberAccessExpression(
                                        SyntaxKind.SimpleMemberAccessExpression,
                                        IdentifierName("global::CommunityToolkit.Mvvm.Input.IAsyncRelayCommandExtensions"),
                                        IdentifierName("CreateCancelCommand")))
                                .AddArgumentListArguments(Argument(IdentifierName(commandInfo.PropertyName))))))
                    .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));

                return ImmutableArray.Create<MemberDeclarationSyntax>(fieldDeclaration, propertyDeclaration, cancelCommandFieldDeclaration, cancelCommandPropertyDeclaration);
            }

            return ImmutableArray.Create<MemberDeclarationSyntax>(fieldDeclaration, propertyDeclaration);
        }

        /// <summary>
        /// Validates that a target method used as source for a command is unique within its containing type.
        /// </summary>
        /// <param name="methodSymbol">The input <see cref="IMethodSymbol"/> instance to process.</param>
        /// <param name="diagnostics">The current collection of gathered diagnostics.</param>
        /// <returns>Whether or not <paramref name="methodSymbol"/> was unique within its containing type.</returns>
        private static bool IsCommandDefinitionUnique(IMethodSymbol methodSymbol, in ImmutableArrayBuilder<DiagnosticInfo> diagnostics)
        {
            // If a duplicate is present in any of the base types, always emit a diagnostic for the current method.
            // That is, there is no need to check the order: we assume the priority is top-down in the type hierarchy.
            // This check has to be done first, as otherwise there would always be a false positive for the current type.
            foreach (ISymbol symbol in methodSymbol.ContainingType.BaseType?.GetAllMembers(methodSymbol.Name) ?? Enumerable.Empty<ISymbol>())
            {
                if (symbol is IMethodSymbol otherSymbol &&
                    otherSymbol.HasAttributeWithFullyQualifiedMetadataName("CommunityToolkit.Mvvm.Input.RelayCommandAttribute"))
                {
                    diagnostics.Add(
                        MultipleRelayCommandMethodOverloadsError,
                        methodSymbol,
                        methodSymbol.ContainingType,
                        methodSymbol);

                    return false;
                }
            }

            // Check for duplicates in the containing type for the annotated method
            foreach (ISymbol symbol in methodSymbol.ContainingType.GetMembers(methodSymbol.Name))
            {
                if (symbol is IMethodSymbol otherSymbol &&
                    otherSymbol.HasAttributeWithFullyQualifiedMetadataName("CommunityToolkit.Mvvm.Input.RelayCommandAttribute"))
                {
                    // If the first [RelayCommand] overload is the current symbol, return immediately. This makes it so
                    // that if multiple overloads are present, only the ones after the first declared one will have
                    // diagnostics generated for them, while the first one will remain valid and will keep working.
                    if (SymbolEqualityComparer.Default.Equals(methodSymbol, otherSymbol))
                    {
                        return true;
                    }

                    // If the two method symbols are partial and either is the implementation of the other one, this is allowed
                    if ((methodSymbol is { IsPartialDefinition: true, PartialImplementationPart: { } partialImplementation } &&
                         SymbolEqualityComparer.Default.Equals(otherSymbol, partialImplementation)) ||
                        (otherSymbol is { IsPartialDefinition: true, PartialImplementationPart: { } otherPartialImplementation } &&
                         SymbolEqualityComparer.Default.Equals(methodSymbol, otherPartialImplementation)))
                    {
                        continue;
                    }

                    diagnostics.Add(
                        MultipleRelayCommandMethodOverloadsError,
                        methodSymbol,
                        methodSymbol.ContainingType,
                        methodSymbol);

                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Get the generated field and property names for the input method.
        /// </summary>
        /// <param name="methodSymbol">The input <see cref="IMethodSymbol"/> instance to process.</param>
        /// <returns>The generated field and property names for <paramref name="methodSymbol"/>.</returns>
        public static (string FieldName, string PropertyName) GetGeneratedFieldAndPropertyNames(IMethodSymbol methodSymbol)
        {
            string propertyName = methodSymbol.Name;

            // Strip the "On" prefix, if present. Only do this if the name is longer than 2 characters,
            // and if the 3rd character is not a lowercase letter. This is needed to avoid accidentally
            // stripping fales positives for "On" prefixes in names such as "Onboard".
            if (propertyName.Length > 2 &&
                propertyName.StartsWith("On") &&
                !char.IsLower(propertyName[2]))
            {
                propertyName = propertyName.Substring(2);
            }

            // Strip the "Async" suffix for methods returning a Task type
            if (methodSymbol.Name.EndsWith("Async") &&
                methodSymbol.ReturnType.HasOrInheritsFromFullyQualifiedMetadataName("System.Threading.Tasks.Task"))
            {
                propertyName = propertyName.Substring(0, propertyName.Length - "Async".Length);
            }

            propertyName += "Command";

            char firstCharacter = propertyName[0];
            char loweredFirstCharacter = char.ToLower(firstCharacter, CultureInfo.InvariantCulture);

            // The field name is generated depending on whether the first character can be lowered:
            //   - If it can, then the field name is just the property name starting in lowercase.
            //   - If it can't (eg. starts with '中'), then the '_' prefix is added to the property name.
            string fieldName = (firstCharacter == loweredFirstCharacter) switch
            {
                true => $"_{propertyName}",
                false => $"{loweredFirstCharacter}{propertyName.Substring(1)}"
            };

            return (fieldName, propertyName);
        }

        /// <summary>
        /// Gets the type symbols for the input method, if supported.
        /// </summary>
        /// <param name="methodSymbol">The input <see cref="IMethodSymbol"/> instance to process.</param>
        /// <param name="diagnostics">The current collection of gathered diagnostics.</param>
        /// <param name="commandInterfaceType">The command interface type name.</param>
        /// <param name="commandClassType">The command class type name.</param>
        /// <param name="delegateType">The delegate type name for the wrapped method.</param>
        /// <param name="supportsCancellation">Indicates whether or not the resulting command supports cancellation.</param>
        /// <param name="commandParameterType">The type of the command parameter (ie. the <c>T</c> in <c>RelayCommand&lt;T&gt;</c>), if the command has one.</param>
        /// <param name="commandTypeArguments">The type arguments for <paramref name="commandInterfaceType"/> and <paramref name="commandClassType"/>, if any.</param>
        /// <param name="commandTypeArgumentsWithNullabilityAnnotations">Same as <paramref name="commandTypeArguments"/>, but with nullability annotations.</param>
        /// <param name="delegateTypeArgumentsWithNullabilityAnnotations">The type arguments for <paramref name="delegateType"/>, if any, with nullability annotations.</param>
        /// <returns>Whether or not <paramref name="methodSymbol"/> was valid and the requested types have been set.</returns>
        private static bool TryMapCommandTypesFromMethod(
            IMethodSymbol methodSymbol,
            in ImmutableArrayBuilder<DiagnosticInfo> diagnostics,
            [NotNullWhen(true)] out string? commandInterfaceType,
            [NotNullWhen(true)] out string? commandClassType,
            [NotNullWhen(true)] out string? delegateType,
            out bool supportsCancellation,
            out ITypeSymbol? commandParameterType,
            out ImmutableArray<string> commandTypeArguments,
            out ImmutableArray<string> commandTypeArgumentsWithNullabilityAnnotations,
            out ImmutableArray<string> delegateTypeArgumentsWithNullabilityAnnotations)
        {
            // Map <void, void> to IRelayCommand, RelayCommand, Action
            if (methodSymbol.ReturnsVoid && methodSymbol.Parameters.Length == 0)
            {
                commandInterfaceType = "global::CommunityToolkit.Mvvm.Input.IRelayCommand";
                commandClassType = "global::CommunityToolkit.Mvvm.Input.RelayCommand";
                delegateType = "global::System.Action";
                supportsCancellation = false;
                commandParameterType = null;
                commandTypeArguments = ImmutableArray<string>.Empty;
                commandTypeArgumentsWithNullabilityAnnotations = ImmutableArray<string>.Empty;
                delegateTypeArgumentsWithNullabilityAnnotations = ImmutableArray<string>.Empty;

                return true;
            }

            // Map <T, void> to IRelayCommand<T>, RelayCommand<T>, Action<T>
            if (methodSymbol.ReturnsVoid &&
                methodSymbol.Parameters.Length == 1 &&
                methodSymbol.Parameters[0] is IParameterSymbol { RefKind: RefKind.None, Type: { IsRefLikeType: false, TypeKind: not TypeKind.Pointer and not TypeKind.FunctionPointer } } parameter)
            {
                commandInterfaceType = "global::CommunityToolkit.Mvvm.Input.IRelayCommand";
                commandClassType = "global::CommunityToolkit.Mvvm.Input.RelayCommand";
                delegateType = "global::System.Action";
                supportsCancellation = false;
                commandParameterType = parameter.Type;
                commandTypeArguments = ImmutableArray.Create(parameter.Type.GetFullyQualifiedName());
                commandTypeArgumentsWithNullabilityAnnotations = ImmutableArray.Create(parameter.Type.GetFullyQualifiedNameWithNullabilityAnnotations());
                delegateTypeArgumentsWithNullabilityAnnotations = ImmutableArray.Create(parameter.Type.GetFullyQualifiedNameWithNullabilityAnnotations());

                return true;
            }

            // Map all Task-returning methods
            if (methodSymbol.ReturnType.HasOrInheritsFromFullyQualifiedMetadataName("System.Threading.Tasks.Task"))
            {
                // Map <void, Task> to IAsyncRelayCommand, AsyncRelayCommand, Func<Task>
                if (methodSymbol.Parameters.Length == 0)
                {
                    commandInterfaceType = "global::CommunityToolkit.Mvvm.Input.IAsyncRelayCommand";
                    commandClassType = "global::CommunityToolkit.Mvvm.Input.AsyncRelayCommand";
                    delegateType = "global::System.Func";
                    supportsCancellation = false;
                    commandParameterType = null;
                    commandTypeArguments = ImmutableArray<string>.Empty;
                    commandTypeArgumentsWithNullabilityAnnotations = ImmutableArray<string>.Empty;
                    delegateTypeArgumentsWithNullabilityAnnotations = ImmutableArray.Create("global::System.Threading.Tasks.Task");

                    return true;
                }

                if (methodSymbol.Parameters.Length == 1 &&
                    methodSymbol.Parameters[0] is IParameterSymbol { RefKind: RefKind.None, Type: { IsRefLikeType: false, TypeKind: not TypeKind.Pointer and not TypeKind.FunctionPointer } } singleParameter)
                {
                    // Map <CancellationToken, Task> to IAsyncRelayCommand, AsyncRelayCommand, Func<CancellationToken, Task>
                    if (singleParameter.Type.HasFullyQualifiedMetadataName("System.Threading.CancellationToken"))
                    {
                        commandInterfaceType = "global::CommunityToolkit.Mvvm.Input.IAsyncRelayCommand";
                        commandClassType = "global::CommunityToolkit.Mvvm.Input.AsyncRelayCommand";
                        delegateType = "global::System.Func";
                        supportsCancellation = true;
                        commandParameterType = null;
                        commandTypeArguments = ImmutableArray<string>.Empty;
                        commandTypeArgumentsWithNullabilityAnnotations = ImmutableArray<string>.Empty;
                        delegateTypeArgumentsWithNullabilityAnnotations = ImmutableArray.Create("global::System.Threading.CancellationToken", "global::System.Threading.Tasks.Task");

                        return true;
                    }

                    // Map <T, Task> to IAsyncRelayCommand<T>, AsyncRelayCommand<T>, Func<T, Task>
                    commandInterfaceType = "global::CommunityToolkit.Mvvm.Input.IAsyncRelayCommand";
                    commandClassType = "global::CommunityToolkit.Mvvm.Input.AsyncRelayCommand";
                    delegateType = "global::System.Func";
                    supportsCancellation = false;
                    commandParameterType = singleParameter.Type;
                    commandTypeArguments = ImmutableArray.Create(singleParameter.Type.GetFullyQualifiedName());
                    commandTypeArgumentsWithNullabilityAnnotations = ImmutableArray.Create(singleParameter.Type.GetFullyQualifiedNameWithNullabilityAnnotations());
                    delegateTypeArgumentsWithNullabilityAnnotations = ImmutableArray.Create(singleParameter.Type.GetFullyQualifiedNameWithNullabilityAnnotations(), "global::System.Threading.Tasks.Task");

                    return true;
                }

                // Map <T, CancellationToken, Task> to IAsyncRelayCommand<T>, AsyncRelayCommand<T>, Func<T, CancellationToken, Task>
                if (methodSymbol.Parameters.Length == 2 &&
                    methodSymbol.Parameters[0] is IParameterSymbol { RefKind: RefKind.None, Type: { IsRefLikeType: false, TypeKind: not TypeKind.Pointer and not TypeKind.FunctionPointer } } firstParameter &&
                    methodSymbol.Parameters[1] is IParameterSymbol { RefKind: RefKind.None, Type: { IsRefLikeType: false, TypeKind: not TypeKind.Pointer and not TypeKind.FunctionPointer } } secondParameter &&
                    secondParameter.Type.HasFullyQualifiedMetadataName("System.Threading.CancellationToken"))
                {
                    commandInterfaceType = "global::CommunityToolkit.Mvvm.Input.IAsyncRelayCommand";
                    commandClassType = "global::CommunityToolkit.Mvvm.Input.AsyncRelayCommand";
                    delegateType = "global::System.Func";
                    supportsCancellation = true;
                    commandParameterType = firstParameter.Type;
                    commandTypeArguments = ImmutableArray.Create(firstParameter.Type.GetFullyQualifiedName());
                    commandTypeArgumentsWithNullabilityAnnotations = ImmutableArray.Create(firstParameter.Type.GetFullyQualifiedNameWithNullabilityAnnotations());
                    delegateTypeArgumentsWithNullabilityAnnotations = ImmutableArray.Create(firstParameter.Type.GetFullyQualifiedNameWithNullabilityAnnotations(), "global::System.Threading.CancellationToken", "global::System.Threading.Tasks.Task");

                    return true;
                }
            }

            diagnostics.Add(InvalidRelayCommandMethodSignatureError, methodSymbol, methodSymbol.ContainingType, methodSymbol);

            commandInterfaceType = null;
            commandClassType = null;
            delegateType = null;
            supportsCancellation = false;
            commandParameterType = null;
            commandTypeArguments = ImmutableArray<string>.Empty;
            commandTypeArgumentsWithNullabilityAnnotations = ImmutableArray<string>.Empty;
            delegateTypeArgumentsWithNullabilityAnnotations = ImmutableArray<string>.Empty;

            return false;
        }

        /// <summary>
        /// Checks whether or not the user has requested to configure the handling of concurrent executions.
        /// </summary>
        /// <param name="methodSymbol">The input <see cref="IMethodSymbol"/> instance to process.</param>
        /// <param name="attributeData">The <see cref="AttributeData"/> instance the method was annotated with.</param>
        /// <param name="commandClassType">The command class type name.</param>
        /// <param name="diagnostics">The current collection of gathered diagnostics.</param>
        /// <param name="allowConcurrentExecutions">Whether or not concurrent executions have been enabled.</param>
        /// <returns>Whether or not a value for <paramref name="allowConcurrentExecutions"/> could be retrieved successfully.</returns>
        private static bool TryGetAllowConcurrentExecutionsSwitch(
            IMethodSymbol methodSymbol,
            AttributeData attributeData,
            string commandClassType,
            in ImmutableArrayBuilder<DiagnosticInfo> diagnostics,
            out bool allowConcurrentExecutions)
        {
            // Try to get the custom switch for concurrent executions (the default is false)
            if (!attributeData.TryGetNamedArgument("AllowConcurrentExecutions", out allowConcurrentExecutions))
            {
                allowConcurrentExecutions = false;

                return true;
            }

            // If the current type is an async command type and concurrent execution is disabled, pass that value to the constructor.
            // If concurrent executions are allowed, there is no need to add any additional argument, as that is the default value.
            if (commandClassType is "global::CommunityToolkit.Mvvm.Input.AsyncRelayCommand")
            {
                return true;
            }
            else
            {
                diagnostics.Add(InvalidConcurrentExecutionsParameterError, methodSymbol, methodSymbol.ContainingType, methodSymbol);

                return false;
            }
        }

        /// <summary>
        /// Checks whether or not the user has requested to configure the task scheduler exception flow option.
        /// </summary>
        /// <param name="methodSymbol">The input <see cref="IMethodSymbol"/> instance to process.</param>
        /// <param name="attributeData">The <see cref="AttributeData"/> instance the method was annotated with.</param>
        /// <param name="commandClassType">The command class type name.</param>
        /// <param name="diagnostics">The current collection of gathered diagnostics.</param>
        /// <param name="flowExceptionsToTaskScheduler">Whether or not task scheduler exception flow have been enabled.</param>
        /// <returns>Whether or not a value for <paramref name="flowExceptionsToTaskScheduler"/> could be retrieved successfully.</returns>
        private static bool TryGetFlowExceptionsToTaskSchedulerSwitch(
            IMethodSymbol methodSymbol,
            AttributeData attributeData,
            string commandClassType,
            in ImmutableArrayBuilder<DiagnosticInfo> diagnostics,
            out bool flowExceptionsToTaskScheduler)
        {
            // Try to get the custom switch for task scheduler exception flow (the default is false)
            if (!attributeData.TryGetNamedArgument("FlowExceptionsToTaskScheduler", out flowExceptionsToTaskScheduler))
            {
                flowExceptionsToTaskScheduler = false;

                return true;
            }

            // Just like with the concurrency control option, check that the target command type is asynchronous
            if (commandClassType is "global::CommunityToolkit.Mvvm.Input.AsyncRelayCommand")
            {
                return true;
            }
            else
            {
                diagnostics.Add(InvalidFlowExceptionsToTaskSchedulerParameterError, methodSymbol, methodSymbol.ContainingType, methodSymbol);

                return false;
            }
        }

        /// <summary>
        /// Checks whether or not the user has requested to also generate a cancel command.
        /// </summary>
        /// <param name="methodSymbol">The input <see cref="IMethodSymbol"/> instance to process.</param>
        /// <param name="attributeData">The <see cref="AttributeData"/> instance the method was annotated with.</param>
        /// <param name="commandClassType">The command class type name.</param>
        /// <param name="supportsCancellation">Indicates whether or not the command supports cancellation.</param>
        /// <param name="diagnostics">The current collection of gathered diagnostics.</param>
        /// <param name="generateCancelCommand">Whether or not concurrent executions have been enabled.</param>
        /// <returns>Whether or not a value for <paramref name="generateCancelCommand"/> could be retrieved successfully.</returns>
        private static bool TryGetIncludeCancelCommandSwitch(
            IMethodSymbol methodSymbol,
            AttributeData attributeData,
            string commandClassType,
            bool supportsCancellation,
            in ImmutableArrayBuilder<DiagnosticInfo> diagnostics,
            out bool generateCancelCommand)
        {
            // Try to get the custom switch for cancel command generation (the default is false)
            if (!attributeData.TryGetNamedArgument("IncludeCancelCommand", out generateCancelCommand))
            {
                generateCancelCommand = false;

                return true;
            }

            // If the current type is an async command type and cancellation is supported, pass that value to the constructor.
            // Otherwise, the current attribute use is not valid, so a diagnostic message should be produced.
            if (commandClassType is "global::CommunityToolkit.Mvvm.Input.AsyncRelayCommand" &&
                supportsCancellation)
            {
                return true;
            }
            else
            {
                diagnostics.Add(InvalidIncludeCancelCommandParameterError, methodSymbol, methodSymbol.ContainingType, methodSymbol);

                return false;
            }
        }

        /// <summary>
        /// Checks whether or not the user has requested an exception handler for the command, and validates it.
        /// </summary>
        /// <param name="methodSymbol">The input <see cref="IMethodSymbol"/> instance to process.</param>
        /// <param name="attributeData">The <see cref="AttributeData"/> instance the method was annotated with.</param>
        /// <param name="commandParameterType">The type of the command parameter (ie. the <c>T</c> in <c>RelayCommand&lt;T&gt;</c>), if the command has one.</param>
        /// <param name="diagnostics">The current collection of gathered diagnostics.</param>
        /// <param name="onExecutionFailedMemberName">The resulting exception handler member name, if available.</param>
        /// <param name="onExecutionFailedUsesEventArgs">Whether the handler takes a <c>RelayCommandExceptionEventArgs</c> parameter (otherwise, an <c>Exception</c> parameter).</param>
        /// <param name="suppressExceptions">Whether a routed fault should be seeded as handled, suppressing the rethrow.</param>
        /// <returns>Whether or not a value for <paramref name="onExecutionFailedMemberName"/> could be determined (may include <see langword="null"/>).</returns>
        private static bool TryGetOnExecutionFailedInfo(
            IMethodSymbol methodSymbol,
            AttributeData attributeData,
            ITypeSymbol? commandParameterType,
            in ImmutableArrayBuilder<DiagnosticInfo> diagnostics,
            out string? onExecutionFailedMemberName,
            out bool onExecutionFailedUsesEventArgs,
            out bool suppressExceptions)
        {
            onExecutionFailedMemberName = null;
            onExecutionFailedUsesEventArgs = false;

            // Get the suppression switch, if any (the default is to leave a routed fault unhandled, so that
            // attaching a handler observes it without changing whether it propagates)
            bool hasSuppressExceptions = attributeData.TryGetNamedArgument("SuppressExceptions", out suppressExceptions);

            // Get the OnExecutionFailed member name, if any (the default is none)
            if (!attributeData.TryGetNamedArgument("OnExecutionFailed", out string? memberName))
            {
                // With no handler to subscribe, no ExecutionFailed subscription is emitted at all, so there is
                // nothing for the suppression switch to seed. Report the dead argument, but keep generating:
                // the command itself is perfectly valid, this is just a value that does nothing.
                if (hasSuppressExceptions)
                {
                    diagnostics.Add(UnusedSuppressExceptionsWarning, methodSymbol, methodSymbol.Name, methodSymbol.ContainingType);
                }

                return true;
            }

            if (memberName is not null)
            {
                bool hasMatchingName = false;
                ImmutableArray<ISymbol>.Builder validMatches = ImmutableArray.CreateBuilder<ISymbol>();

                foreach (ISymbol member in methodSymbol.ContainingType!.GetAllMembers(memberName))
                {
                    hasMatchingName = true;

                    // Only accept candidates the generated code can actually call. The call site is emitted into
                    // the containing type of the annotated method, and GetAllMembers also walks the base type
                    // hierarchy, so a generic candidate, one taking its parameter by 'ref' or 'out', or one that
                    // is not accessible from that type would all emit code that fails to compile, pointing the
                    // user at a generated file they never wrote. Rejecting them here instead lets them fall
                    // through to the MVVMTK0058 path below (hasMatchingName is already set), which reports the
                    // invalid signature on the user's own declaration. Note that 'in' parameters and static
                    // handlers are both perfectly callable, so they are deliberately still accepted.
                    if (member is IMethodSymbol { ReturnsVoid: true, IsGenericMethod: false, Parameters.Length: 1 } candidateSymbol &&
                        candidateSymbol.Parameters[0].RefKind is not (RefKind.Out or RefKind.Ref) &&
                        IsAccessibleFromGeneratedCode(candidateSymbol, methodSymbol.ContainingType) &&
                        (candidateSymbol.Parameters[0].Type.HasFullyQualifiedMetadataName("System.Exception") ||
                         IsExceptionEventArgsType(candidateSymbol.Parameters[0].Type, commandParameterType)))
                    {
                        validMatches.Add(candidateSymbol);
                    }
                }

                // We specifically allow targeting methods which are overridden: they'll be more than one,
                // but it doesn't matter since you'd only ever call "one", being the most derived one. This
                // mirrors the same carve-out used for the CanExecute member name matching logic below.
                if (validMatches.Count > 1 && !validMatches.ToImmutable().AreAllInSameOverriddenMethodHierarchy())
                {
                    diagnostics.Add(MultipleOnExecutionFailedMemberMatchesError, methodSymbol, memberName, methodSymbol.ContainingType);

                    return false;
                }

                if (validMatches.Count >= 1)
                {
                    IMethodSymbol candidateSymbol = (IMethodSymbol)validMatches[0];

                    // An 'async void' handler is a valid signature as far as the subscription goes, but the command
                    // has no way to await it: it returns at its first await, so the fault is considered observed
                    // before the handler has actually finished, and anything the remainder of it throws reaches the
                    // synchronization context unhandled. Warn, but still wire it up, as the user might be doing the
                    // asynchronous work deliberately (eg. after having already set Handled synchronously).
                    if (candidateSymbol.IsAsync)
                    {
                        diagnostics.Add(AsyncVoidOnExecutionFailedMemberWarning, methodSymbol, memberName, methodSymbol.ContainingType);
                    }

                    onExecutionFailedMemberName = memberName;
                    onExecutionFailedUsesEventArgs = IsExceptionEventArgsType(candidateSymbol.Parameters[0].Type, commandParameterType);

                    return true;
                }

                if (hasMatchingName)
                {
                    diagnostics.Add(InvalidOnExecutionFailedMemberSignatureError, methodSymbol, memberName, methodSymbol.ContainingType);

                    return false;
                }
            }

            diagnostics.Add(InvalidOnExecutionFailedMemberNameError, methodSymbol, memberName ?? string.Empty, methodSymbol.ContainingType);

            return false;
        }

        /// <summary>
        /// Checks whether a candidate exception handler parameter type is a supported event args type for a given command.
        /// </summary>
        /// <param name="parameterType">The type of the single parameter of the candidate exception handler.</param>
        /// <param name="commandParameterType">The type of the command parameter, if the command has one.</param>
        /// <returns>Whether <paramref name="parameterType"/> is a valid event args type for the target command.</returns>
        /// <remarks>
        /// Both the non-generic <c>RelayCommandExceptionEventArgs</c> and the generic <c>RelayCommandExceptionEventArgs&lt;T&gt;</c>
        /// are accepted, but the latter only when <c>T</c> is exactly the command parameter type. That is the only case where the
        /// generated subscription (which is on an <c>EventHandler&lt;RelayCommandExceptionEventArgs&lt;T&gt;&gt;</c> event) can
        /// actually pass the event args to the handler without a cast. Any other generic instantiation, as well as any generic
        /// instantiation at all on a parameterless command, would produce code that fails to compile, so it is rejected here and
        /// reported as an invalid signature on the user's own declaration instead.
        /// </remarks>
        private static bool IsExceptionEventArgsType(ITypeSymbol parameterType, ITypeSymbol? commandParameterType)
        {
            // The non-generic event args type is always valid, for all command shapes
            if (parameterType.HasFullyQualifiedMetadataName("CommunityToolkit.Mvvm.Input.RelayCommandExceptionEventArgs"))
            {
                return true;
            }

            // The generic one is only valid on a command with a parameter, and only for that exact parameter type
            return
                commandParameterType is not null &&
                parameterType is INamedTypeSymbol { TypeArguments.Length: 1 } namedParameterType &&
                parameterType.HasFullyQualifiedMetadataName("CommunityToolkit.Mvvm.Input.RelayCommandExceptionEventArgs`1") &&
                SymbolEqualityComparer.Default.Equals(namedParameterType.TypeArguments[0], commandParameterType);
        }

        /// <summary>
        /// Checks whether a candidate exception handler member can be invoked from the type the generated code is emitted into.
        /// </summary>
        /// <param name="candidateSymbol">The candidate exception handler member to check.</param>
        /// <param name="containingType">The type the generated command code is emitted into.</param>
        /// <returns>Whether <paramref name="candidateSymbol"/> is accessible from <paramref name="containingType"/>.</returns>
        /// <remarks>
        /// This check is done purely symbolically, as there is no <see cref="Compilation"/> available in this context.
        /// </remarks>
        private static bool IsAccessibleFromGeneratedCode(ISymbol candidateSymbol, INamedTypeSymbol containingType)
        {
            // A private member is only visible if it is declared by the very same type. Candidates are gathered
            // across the whole base type hierarchy, and a private member of some base type is not accessible here.
            if (candidateSymbol.DeclaredAccessibility == Accessibility.Private)
            {
                return SymbolEqualityComparer.Default.Equals(candidateSymbol.ContainingType, containingType);
            }

            // An 'internal' (or 'private protected') member is only visible from within the same assembly
            if (candidateSymbol.DeclaredAccessibility is Accessibility.Internal or Accessibility.ProtectedAndInternal)
            {
                return SymbolEqualityComparer.Default.Equals(candidateSymbol.ContainingAssembly, containingType.ContainingAssembly);
            }

            return true;
        }

        /// <summary>
        /// Tries to get the expression type for the "CanExecute" property, if available.
        /// </summary>
        /// <param name="methodSymbol">The input <see cref="IMethodSymbol"/> instance to process.</param>
        /// <param name="attributeData">The <see cref="AttributeData"/> instance for <paramref name="methodSymbol"/>.</param>
        /// <param name="commandTypeArguments">The command type arguments, if any.</param>
        /// <param name="token">The cancellation token for the current operation.</param>
        /// <param name="diagnostics">The current collection of gathered diagnostics.</param>
        /// <param name="canExecuteMemberName">The resulting can execute member name, if available.</param>
        /// <param name="canExecuteExpressionType">The resulting expression type, if available.</param>
        /// <returns>Whether or not a value for <paramref name="canExecuteMemberName"/> and <paramref name="canExecuteExpressionType"/> could be determined (may include <see langword="null"/>).</returns>
        private static bool TryGetCanExecuteExpressionType(
            IMethodSymbol methodSymbol,
            AttributeData attributeData,
            ImmutableArray<string> commandTypeArguments,
            CancellationToken token,
            in ImmutableArrayBuilder<DiagnosticInfo> diagnostics,
            out string? canExecuteMemberName,
            out CanExecuteExpressionType? canExecuteExpressionType)
        {
            // Get the can execute member, if any
            if (!attributeData.TryGetNamedArgument("CanExecute", out string? memberName))
            {
                canExecuteMemberName = null;
                canExecuteExpressionType = null;

                return true;
            }

            if (memberName is null)
            {
                diagnostics.Add(InvalidCanExecuteMemberNameError, methodSymbol, memberName ?? string.Empty, methodSymbol.ContainingType);

                goto Failure;
            }

            ImmutableArray<ISymbol> canExecuteSymbols = methodSymbol.ContainingType!.GetAllMembers(memberName).ToImmutableArray();

            if (canExecuteSymbols.IsEmpty)
            {
                // Special case for when the target member is a generated property from [ObservableProperty]
                if (TryGetCanExecuteMemberFromGeneratedProperty(memberName, methodSymbol.ContainingType, commandTypeArguments, token, out canExecuteExpressionType))
                {
                    canExecuteMemberName = memberName;

                    return true;
                }

                diagnostics.Add(InvalidCanExecuteMemberNameError, methodSymbol, memberName, methodSymbol.ContainingType);
            }
            else if (canExecuteSymbols.Length > 1 && !canExecuteSymbols.AreAllInSameOverriddenMethodHierarchy())
            {
                // We specifically allow targeting methods which are overridden: they'll be more than one,
                // but it doesn't matter since you'd only ever call "one", being the most derived one.
                diagnostics.Add(MultipleCanExecuteMemberNameMatchesError, methodSymbol, memberName, methodSymbol.ContainingType);
            }
            else if (TryGetCanExecuteExpressionFromSymbol(canExecuteSymbols[0], commandTypeArguments, out canExecuteExpressionType))
            {
                canExecuteMemberName = memberName;

                return true;
            }
            else
            {
                diagnostics.Add(InvalidCanExecuteMemberError, methodSymbol, memberName, methodSymbol.ContainingType);
            }

            Failure:
            canExecuteMemberName = null;
            canExecuteExpressionType = null;

            return false;
        }

        /// <summary>
        /// Gets the expression type for the can execute logic, if possible.
        /// </summary>
        /// <param name="canExecuteSymbol">The can execute member symbol (either a method or a property).</param>
        /// <param name="commandTypeArguments">The type arguments for the command interface, if any.</param>
        /// <param name="canExecuteExpressionType">The resulting can execute expression type, if available.</param>
        /// <returns>Whether or not <paramref name="canExecuteExpressionType"/> was set and the input symbol was valid.</returns>
        private static bool TryGetCanExecuteExpressionFromSymbol(
            ISymbol canExecuteSymbol,
            ImmutableArray<string> commandTypeArguments,
            [NotNullWhen(true)] out CanExecuteExpressionType? canExecuteExpressionType)
        {
            if (canExecuteSymbol is IMethodSymbol canExecuteMethodSymbol)
            {
                // The return type must always be a bool
                if (canExecuteMethodSymbol.ReturnType is not { SpecialType: SpecialType.System_Boolean })
                {
                    goto Failure;
                }

                // Parameterless methods are always valid
                if (canExecuteMethodSymbol.Parameters.IsEmpty)
                {
                    // If the command is generic, the input value is ignored
                    if (commandTypeArguments.Length > 0)
                    {
                        canExecuteExpressionType = CanExecuteExpressionType.MethodInvocationLambdaWithDiscard;
                    }
                    else
                    {
                        canExecuteExpressionType = CanExecuteExpressionType.MethodGroup;
                    }

                    return true;
                }

                // If the method has parameters, it has to have a single one matching the command type
                if (canExecuteMethodSymbol.Parameters.Length == 1 &&
                    commandTypeArguments.Length == 1 &&
                    canExecuteMethodSymbol.Parameters[0].Type.HasFullyQualifiedName(commandTypeArguments[0]))
                {
                    // Create a method group expression again
                    canExecuteExpressionType = CanExecuteExpressionType.MethodGroup;

                    return true;
                }
            }
            else if (canExecuteSymbol is IPropertySymbol { GetMethod: not null } canExecutePropertySymbol)
            {
                // The property type must always be a bool
                if (canExecutePropertySymbol.Type is not { SpecialType: SpecialType.System_Boolean })
                {
                    goto Failure;
                }

                if (commandTypeArguments.Length > 0)
                {
                    canExecuteExpressionType = CanExecuteExpressionType.PropertyAccessLambdaWithDiscard;
                }
                else
                {
                    canExecuteExpressionType = CanExecuteExpressionType.PropertyAccessLambda;
                }

                return true;
            }

            Failure:
            canExecuteExpressionType = null;

            return false;
        }

        /// <summary>
        /// Gets the expression type for the can execute logic, if possible.
        /// </summary>
        /// <param name="memberName">The member name passed to <c>[RelayCommand(CanExecute = ...)]</c>.</param>
        /// <param name="containingType">The containing type for the method annotated with <c>[RelayCommand]</c>.</param>
        /// <param name="commandTypeArguments">The type arguments for the command interface, if any.</param>
        /// <param name="token">The cancellation token for the current operation.</param>
        /// <param name="canExecuteExpressionType">The resulting can execute expression type, if available.</param>
        /// <returns>Whether or not <paramref name="canExecuteExpressionType"/> was set and the input symbol was valid.</returns>
        private static bool TryGetCanExecuteMemberFromGeneratedProperty(
            string memberName,
            INamedTypeSymbol containingType,
            ImmutableArray<string> commandTypeArguments,
            CancellationToken token,
            [NotNullWhen(true)] out CanExecuteExpressionType? canExecuteExpressionType)
        {
            foreach (ISymbol memberSymbol in containingType.GetAllMembers())
            {
                // Only look for instance fields of bool type
                if (memberSymbol is not IFieldSymbol { IsStatic: false, Type.SpecialType: SpecialType.System_Boolean } fieldSymbol)
                {
                    continue;
                }

                token.ThrowIfCancellationRequested();

                ImmutableArray<AttributeData> attributes = memberSymbol.GetAttributes();

                // Only filter fields with the [ObservableProperty] attribute
                if (memberSymbol is IFieldSymbol &&
                    !attributes.Any(static a => a.AttributeClass?.HasFullyQualifiedMetadataName(
                        "CommunityToolkit.Mvvm.ComponentModel.ObservablePropertyAttribute") == true))
                {
                    continue;
                }

                // Get the target property name either directly or matching the generated one
                string propertyName = ObservablePropertyGenerator.Execute.GetGeneratedPropertyName(fieldSymbol);

                // If the generated property name matches, get the right expression type
                if (memberName == propertyName)
                {
                    if (commandTypeArguments.Length > 0)
                    {
                        canExecuteExpressionType = CanExecuteExpressionType.PropertyAccessLambdaWithDiscard;
                    }
                    else
                    {
                        canExecuteExpressionType = CanExecuteExpressionType.PropertyAccessLambda;
                    }

                    return true;
                }
            }

            canExecuteExpressionType = null;

            return false;
        }

        /// <summary>
        /// Gathers all forwarded attributes for the generated field and property.
        /// </summary>
        /// <param name="methodSymbol">The input <see cref="IMethodSymbol"/> instance to process.</param>
        /// <param name="semanticModel">The <see cref="SemanticModel"/> instance for the current run.</param>
        /// <param name="token">The cancellation token for the current operation.</param>
        /// <param name="diagnostics">The current collection of gathered diagnostics.</param>
        /// <param name="forwardedAttributes">The resulting attributes to forward.</param>
        private static void GatherForwardedAttributes(
            IMethodSymbol methodSymbol,
            SemanticModel semanticModel,
            CancellationToken token,
            in ImmutableArrayBuilder<DiagnosticInfo> diagnostics,
            out ImmutableArray<AttributeInfo> forwardedAttributes)
        {
            using ImmutableArrayBuilder<AttributeInfo> forwardedAttributesInfo = ImmutableArrayBuilder<AttributeInfo>.Rent();

            static void GatherForwardedAttributes(
                IMethodSymbol methodSymbol,
                SemanticModel semanticModel,
                CancellationToken token,
                in ImmutableArrayBuilder<DiagnosticInfo> diagnostics,
                in ImmutableArrayBuilder<AttributeInfo> forwardedAttributesInfo)
            {
                // Get the single syntax reference for the input method symbol (there should be only one)
                if (methodSymbol.DeclaringSyntaxReferences is not [SyntaxReference syntaxReference])
                {
                    return;
                }

                // Try to get the target method declaration syntax node
                if (syntaxReference.GetSyntax(token) is not MethodDeclarationSyntax methodDeclaration)
                {
                    return;
                }

                // Gather explicit forwarded attributes info
                foreach (AttributeListSyntax attributeList in methodDeclaration.AttributeLists)
                {
                    // Same as in the [ObservableProperty] generator, except we're also looking for fields here
                    if (attributeList.Target?.Identifier is not SyntaxToken(SyntaxKind.PropertyKeyword or SyntaxKind.FieldKeyword) targetIdentifier)
                    {
                        continue;
                    }

                    token.ThrowIfCancellationRequested();

                    foreach (AttributeSyntax attribute in attributeList.Attributes)
                    {
                        // Get the symbol info for the attribute (once again just like in the [ObservableProperty] generator)
                        if (!semanticModel.GetSymbolInfo(attribute, token).TryGetAttributeTypeSymbol(out INamedTypeSymbol? attributeTypeSymbol))
                        {
                            diagnostics.Add(
                                InvalidFieldOrPropertyTargetedAttributeOnRelayCommandMethod,
                                attribute,
                                methodSymbol,
                                attribute.Name);

                            continue;
                        }

                        IEnumerable<AttributeArgumentSyntax> attributeArguments = attribute.ArgumentList?.Arguments ?? Enumerable.Empty<AttributeArgumentSyntax>();

                        // Try to extract the forwarded attribute
                        if (!AttributeInfo.TryCreate(attributeTypeSymbol, semanticModel, attributeArguments, targetIdentifier.Kind(), token, out AttributeInfo? attributeInfo))
                        {
                            diagnostics.Add(
                                InvalidFieldOrPropertyTargetedAttributeExpressionOnRelayCommandMethod,
                                attribute,
                                methodSymbol,
                                attribute.Name);

                            continue;
                        }

                        // Add the new attribute info to the builder
                        forwardedAttributesInfo.Add(attributeInfo);
                    }
                }
            }

            // If the method is a partial definition, also gather attributes from the implementation part
            if (methodSymbol is { IsPartialDefinition: true } or { PartialDefinitionPart: not null })
            {
                IMethodSymbol partialDefinition = methodSymbol.PartialDefinitionPart ?? methodSymbol;
                IMethodSymbol partialImplementation = methodSymbol.PartialImplementationPart ?? methodSymbol;

                // We always give priority to the partial definition, to ensure a predictable and testable ordering
                GatherForwardedAttributes(partialDefinition, semanticModel, token, in diagnostics, in forwardedAttributesInfo);
                GatherForwardedAttributes(partialImplementation, semanticModel, token, in diagnostics, in forwardedAttributesInfo);
            }
            else
            {
                // If the method is not a partial definition/implementation, just gather attributes from the method with no modifications
                GatherForwardedAttributes(methodSymbol, semanticModel, token, in diagnostics, in forwardedAttributesInfo);
            }

            forwardedAttributes = forwardedAttributesInfo.ToImmutable();
        }
    }
}

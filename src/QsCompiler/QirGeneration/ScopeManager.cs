﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Quantum.QIR;
using Microsoft.Quantum.QIR.Emission;
using Microsoft.Quantum.QsCompiler.SyntaxTokens;
using Microsoft.Quantum.QsCompiler.SyntaxTree;
using Ubiquity.NET.Llvm.Types;
using Ubiquity.NET.Llvm.Values;

namespace Microsoft.Quantum.QsCompiler.QIR
{
    using ResolvedTypeKind = QsTypeKind<ResolvedType, UserDefinedType, QsTypeParameter, CallableInformation>;

    /// <summary>
    /// This class is used to track the validity of variables and values, to track access and reference counts,
    /// and to release and unreference values when they go out of scope.
    /// <para>
    /// There are two primary ways to leave a scope: close it and continue in the parent scope, or exit the current
    /// scope and all parent scopes and leave the callable altogether (i.e., return).
    /// While each scope has a single place it is closed, there may be many exits.
    /// </para>
    /// </summary>
    internal class ScopeManager
    {
        private class Scope
        {
            private readonly ScopeManager parent;

            /// <summary>
            /// Maps variable names to the corresponding value.
            /// Mutable variables are represented as PointerValues.
            /// </summary>
            private readonly Dictionary<string, IValue> variables = new Dictionary<string, IValue>();

            /// <summary>
            /// Contains all values whose reference count has been increased.
            /// The first items contains the value, and the second item indicates whether to recursively reference inner items.
            /// </summary>
            private readonly List<(IValue, bool)> pendingReferences = new List<(IValue, bool)>();

            /// <summary>
            /// Contains all values that require unreferencing upon closing the scope.
            /// The first items contains the value, and the second item indicates whether to recursively unreference inner items.
            /// </summary>
            private readonly List<(IValue, bool)> requiredUnreferences = new List<(IValue, bool)>();

            /// <summary>
            /// Contains the values that require invoking a release function upon closing the scope,
            /// as well as the name of the release function to invoke.
            /// </summary>
            private readonly List<(IValue, string)> requiredReleases = new List<(IValue, string)>();

            public Scope(ScopeManager parent)
            {
                this.parent = parent;
            }

            private static bool ValueEquals((IValue, bool) tracked, IValue expected) =>
                tracked.Item1.Value == expected.Value && tracked.Item2;

            private static bool ValueEquals((IValue, bool) tracked, (IValue, bool) expected) =>
                tracked.Item1.Value == expected.Item1.Value && tracked.Item2 == expected.Item2;

            private static bool TryRemoveValue(List<(IValue, bool)> values, Func<(IValue, bool), bool> condition)
            {
                var index = values.FindIndex(v => condition(v));
                if (index < 0)
                {
                    return false;
                }
                else
                {
                    values.RemoveAt(index);
                    return true;
                }
            }

            private static IValue LoadValue(IValue value)
            {
                while (value is PointerValue ptr)
                {
                    value = ptr.LoadValue();
                }
                return value;
            }

            // public and internal methods

            public void RegisterVariable(string varName, IValue value) =>
                this.variables.Add(varName, value);

            public bool TryGetVariable(string varName, out IValue value) =>
                this.variables.TryGetValue(varName, out value);

            /// <summary>
            /// Adds the given value to the list of tracked values that need to be unreferenced when closing or exiting the scope,
            /// and makes sure the release function is invoked before unreferending the value.
            /// If the given value to register is a pointer, recursively loads its content and registers the loaded value.
            /// </summary>
            public void RegisterValue(IValue value, string? releaseFunction = null)
            {
                if (releaseFunction != null)
                {
                    this.requiredReleases.Add((LoadValue(value), releaseFunction));
                }
                if (this.parent.ReferencesUpdateFunctionForType(value.LlvmType) != null)
                {
                    this.requiredUnreferences.Add((LoadValue(value), true));
                }
            }

            /// <summary>
            /// Adds the given value to the list of values which have been referenced.
            /// If the given value to unreference is a pointer, recursively loads its content and queues the loaded value for unreferencing.
            /// </summary>
            internal void ReferenceValue(IValue value, bool recurIntoInnerItems)
            {
                if (this.parent.ReferencesUpdateFunctionForType(value.LlvmType) != null)
                {
                    this.pendingReferences.Add((LoadValue(value), recurIntoInnerItems));
                }
            }

            /// <summary>
            /// Returns true if the scope contains calls to reference values that have not been applied yet.
            /// </summary>
            public bool HasPendingReferences =>
                this.pendingReferences.Any();

            /// <summary>
            /// Executes all pending calls to increase reference counts.
            /// </summary>
            internal void ApplyPendingReferences()
            {
                var pending = this.pendingReferences.ToArray();
                this.pendingReferences.Clear();
                this.parent.ModifyCounts(this.parent.ReferencesUpdateFunctionForType, this.parent.plusOne, pending);
            }

            /// <summary>
            /// Migrates all pending calls to increase reference counts from the current scope to the given scope,
            /// clearing them from the current scope.
            /// </summary>
            internal void MigratePendingReferences(Scope scope)
            {
                scope.pendingReferences.AddRange(this.pendingReferences);
                this.pendingReferences.Clear();
            }

            /// <summary>
            /// Clears and returns all pending references from the scope.
            /// </summary>
            private List<(IValue, bool)> ClearPendingReferences()
            {
                var refs = this.pendingReferences.ToList();
                this.pendingReferences.Clear();
                return refs;
            }

            /// <summary>
            /// Adds the given value to the list of tracked values that need to be unreferenced when closing or exiting the scope.
            /// If the given value to unreference is a pointer, recursively loads its content and queues the loaded value for unreferencing.
            /// </summary>
            internal void UnreferenceValue(IValue value, bool recurIntoInnerItems)
            {
                if (this.parent.ReferencesUpdateFunctionForType(value.LlvmType) != null)
                {
                    this.requiredUnreferences.Add((LoadValue(value), recurIntoInnerItems));
                }
            }

            /// <summary>
            /// Removes the given value from the list of registered values such that it will no longer be
            /// unreferenced when executing pending calls in preparation for exiting or closing the scope.
            /// Any release function that has been specified when adding the value will still execute.
            /// </summary>
            internal bool TryRemoveValue(IValue value) =>
                TryRemoveValue(this.requiredUnreferences, tracked => ValueEquals(tracked, value));

            /// <summary>
            /// Returns true if the given value will be unreferenced by <see cref="ExecutePendingCalls" />
            /// unless it is explicitly excluded.
            /// </summary>
            internal bool WillBeUnreferenced(IValue value) =>
                this.requiredUnreferences.Exists(tracked => ValueEquals(tracked, value));

            /// <inheritdoc cref="ExecutePendingCalls(ScopeManager, List{IValue}, Scope[])" />
            internal void ExecutePendingCalls(List<IValue>? omitUnreferencing = null, bool applyReferences = true) =>
                ExecutePendingCalls(this.parent, omitUnreferencing ?? new List<IValue>(), applyReferences, this);

            /// <summary>
            /// Generates the necessary calls to unreference the tracked values, decrease the access count for registered variables,
            /// and invokes the specified release functions for values if necessary.
            /// Skips unreferencing the values specified in omitUnreferencing, removing them from the list.
            /// </summary>
            /// <param name="omitUnreferencing">
            /// Values for which to omit the call to unreference them; for each value at most one call will be omitted and the value will be removed from the list
            /// </param>
            internal static void ExecutePendingCalls(ScopeManager parent, List<IValue> omitUnreferencing, bool applyReferences, params Scope[] scopes)
            {
                if (!scopes.Any())
                {
                    return;
                }

                foreach (var (value, funcName) in scopes.SelectMany(s => s.requiredReleases))
                {
                    var func = parent.sharedState.GetOrCreateRuntimeFunction(funcName);
                    parent.sharedState.CurrentBuilder.Call(func, value.Value);
                }

                var pendingAccessCounts = scopes.SelectMany(s => s.variables).Select(kv => (kv.Value, true)).ToArray();
                var pendingUnreferences = new List<(IValue, bool)>();
                foreach (var value in scopes.SelectMany(s => s.requiredUnreferences))
                {
                    var omitted = omitUnreferencing.FirstOrDefault(omitted => ValueEquals(value, omitted));
                    if (!omitUnreferencing.Remove(omitted))
                    {
                        pendingUnreferences.Add(value);
                    }
                }

                // We need to apply the pending counts here to make sure they get applied even if nothing is unreferenced.
                foreach (var scope in scopes.Skip(1))
                {
                    if (scope.HasPendingReferences)
                    {
                        throw new InvalidOperationException("pending references in outer scopes");
                    }
                }

                // Not the most efficient version, but it will do for now.
                var pendingReferences = applyReferences ? scopes.First().ClearPendingReferences() : new List<(IValue, bool)>();
                var lookup1 = pendingReferences.ToLookup(x => (x.Item1.Value, x.Item2));
                var lookup2 = pendingUnreferences.ToLookup(x => (x.Item1.Value, x.Item2));
                var unnecessaryRefModifications = lookup1.SelectMany(l1s => lookup2[l1s.Key].Zip(l1s, (l2, l1) => l1));
                foreach (var item in unnecessaryRefModifications)
                {
                    var removedFromRefs = TryRemoveValue(pendingReferences, v => ValueEquals(v, item));
                    var removedFromUnrefs = TryRemoveValue(pendingUnreferences, v => ValueEquals(v, item));
                    Debug.Assert(removedFromRefs && removedFromUnrefs);
                }

                parent.ModifyCounts(parent.ReferencesUpdateFunctionForType, parent.plusOne, pendingReferences.ToArray());
                parent.ModifyCounts(parent.AccessUpdateFunctionForType, parent.minusOne, pendingAccessCounts);
                parent.ModifyCounts(parent.ReferencesUpdateFunctionForType, parent.minusOne, pendingUnreferences.ToArray());
            }
        }

        private readonly GenerationContext sharedState;
        private readonly IValue minusOne;
        private readonly IValue plusOne;

        /// <summary>
        /// New variables and values are always added to the scope on top of the stack.
        /// When looking for a name, the stack is searched top-down.
        /// </summary>
        private readonly Stack<Scope> scopes = new Stack<Scope>();

        /// <summary>
        /// Is true when there are currently no stack frames tracked.
        /// Stack frames are added and removed by OpenScope and CloseScope respectively.
        /// </summary>
        public bool IsEmpty => !this.scopes.Any();

        /// <summary>
        /// Creates a new ref count scope manager.
        /// </summary>
        /// <param name="ctx">The generation context the new manager should be associated with</param>
        public ScopeManager(GenerationContext ctx)
        {
            this.sharedState = ctx;
            var intType = ResolvedType.New(ResolvedTypeKind.Int);
            this.minusOne = ctx.Values.FromSimpleValue(ctx.Context.CreateConstant(-1L), intType);
            this.plusOne = ctx.Values.FromSimpleValue(ctx.Context.CreateConstant(1L), intType);
        }

        // private helpers

        /// <summary>
        /// Gets the name of the runtime function to update the access count for a given LLVM type.
        /// </summary>
        /// <param name="t">The LLVM type</param>
        /// <returns>The name of the function to update the access count for this type</returns>
        private string? AccessUpdateFunctionForType(ITypeRef t)
        {
            if (t.IsPointer)
            {
                if (Types.IsTypedTuple(t))
                {
                    return RuntimeLibrary.TupleUpdateAccessCount;
                }
                else if (Types.IsArray(t))
                {
                    return RuntimeLibrary.ArrayUpdateAccessCount;
                }
                else if (Types.IsCallable(t))
                {
                    return RuntimeLibrary.CallableUpdateAccessCount;
                }
            }
            return null;
        }

        /// <summary>
        /// Gets the name of the runtime function to update the reference count for a given LLVM type.
        /// </summary>
        /// <param name="t">The LLVM type</param>
        /// <returns>The name of the function to update the reference count for this type</returns>
        private string? ReferencesUpdateFunctionForType(ITypeRef t)
        {
            if (t.IsPointer)
            {
                if (Types.IsTypedTuple(t))
                {
                    return RuntimeLibrary.TupleUpdateReferenceCount;
                }
                else if (Types.IsArray(t))
                {
                    return RuntimeLibrary.ArrayUpdateReferenceCount;
                }
                else if (Types.IsCallable(t))
                {
                    return RuntimeLibrary.CallableUpdateReferenceCount;
                }
                else if (Types.IsResult(t))
                {
                    return RuntimeLibrary.ResultUpdateReferenceCount;
                }
                else if (Types.IsString(t))
                {
                    return RuntimeLibrary.StringUpdateReferenceCount;
                }
                else if (Types.IsBigInt(t))
                {
                    return RuntimeLibrary.BigIntUpdateReferenceCount;
                }
            }
            return null;
        }

        /// <summary>
        /// Each callable table contains a pointer to an array of function pointers to modify access and reference
        /// counts of the capture tuple.
        /// Given a callable value, invokes the function at the given index in the memory management table of the callable
        /// by calling the runtime function CallableMemoryManagement with the function index and the value by which to
        /// change the count. The given change is expected to be a 64-bit integer.
        /// </summary>
        private void InvokeCallableMemoryManagement(int funcIndex, IValue change, CallableValue callable)
        {
            var invokeMemoryManagment = this.sharedState.GetOrCreateRuntimeFunction(RuntimeLibrary.CallableMemoryManagement);
            this.sharedState.CurrentBuilder.Call(invokeMemoryManagment, this.sharedState.Context.CreateConstant(funcIndex), callable.Value, change.Value);
        }

        /// <summary>
        /// For each value for which the given function returns a function name,
        /// applies the runtime function with that name to the value, casting the value if necessary.
        /// Recurs into contained items if the bool passed with the value is true.
        /// </summary>
        private void ModifyCounts(Func<ITypeRef, string?> getFunctionName, IValue change, params (IValue, bool)[] values)
        {
            foreach (var (value, recur) in values)
            {
                this.ModifyCounts(getFunctionName, change, value, recur);
            }
        }

        /// <summary>
        /// If the given function returns a function name for the given value,
        /// applies the runtime function with that name to the given value, casting the value if necessary.
        /// Recurs into contained items if the bool passed with the value is true.
        /// </summary>
        private void ModifyCounts(Func<ITypeRef, string?> getFunctionName, IValue change, IValue value, bool recurIntoInnerItems)
        {
            void ProcessValue(string funcName, IValue value)
            {
                if (value is PointerValue pointer)
                {
                    ProcessValue(funcName, pointer.LoadValue());
                }
                else
                {
                    Value arg;
                    if (value is TupleValue tuple)
                    {
                        for (var i = 0; i < tuple.StructType.Members.Count && recurIntoInnerItems; ++i)
                        {
                            var itemFuncName = getFunctionName(tuple.StructType.Members[i]);
                            if (itemFuncName != null)
                            {
                                var item = tuple.GetTupleElement(i);
                                ProcessValue(itemFuncName, item);
                            }
                        }
                        arg = tuple.OpaquePointer;
                    }
                    else if (value is ArrayValue array)
                    {
                        var itemFuncName = getFunctionName(array.LlvmElementType);
                        if (itemFuncName != null && recurIntoInnerItems)
                        {
                            this.sharedState.IterateThroughArray(array, arrItem => ProcessValue(itemFuncName, arrItem));
                        }
                        arg = array.OpaquePointer;
                    }
                    else if (value is CallableValue callable && recurIntoInnerItems)
                    {
                        var itemFuncId =
                            funcName == RuntimeLibrary.CallableUpdateReferenceCount ? 0 :
                            funcName == RuntimeLibrary.CallableUpdateAccessCount ? 1 :
                            throw new NotSupportedException("unknown function for capture tuple memory management");
                        this.InvokeCallableMemoryManagement(itemFuncId, change, callable);
                        arg = callable.Value;
                    }
                    else
                    {
                        arg = value.Value;
                    }

                    var func = this.sharedState.GetOrCreateRuntimeFunction(funcName);
                    this.sharedState.CurrentBuilder.Call(func, arg, change.Value);
                }
            }

            var func = getFunctionName(value.LlvmType);
            if (func != null)
            {
                if (change != this.plusOne)
                {
                    this.scopes.Peek().ApplyPendingReferences();
                }
                ProcessValue(func, value);
            }
        }

        // public and internal methods

        /// <summary>
        /// Opens a new scope and pushes it on top of the scope stack.
        /// If migratePendingReferences is set to false,
        /// adds all pending calls to increase reference counts to the current block.
        /// If it is set to true, then the pending calls are not applied but instead
        /// removed from the current scope and added to the new scope.
        /// </summary>
        public void OpenScope(bool migratePendingReferences)
        {
            var newScope = new Scope(this);
            if (this.scopes.TryPeek(out var current))
            {
                if (migratePendingReferences)
                {
                    current.MigratePendingReferences(newScope);
                }
                else
                {
                    current.ApplyPendingReferences();
                }
            }
            this.scopes.Push(newScope);
        }

        /// <summary>
        /// Closes the current scope by popping it off of the stack.
        /// Emits the queued calls to unreference, release, and/or decrease the access counts for values going out of scope.
        /// If the current basic block is already terminated, presumably by a return, the calls are not generated.
        /// </summary>
        /// <exception cref="InvalidOperationException">The scope has pending calls to increase the reference count for values</exception>
        public void CloseScope(bool isTerminated)
        {
            var scope = this.scopes.Peek();
            if (!isTerminated)
            {
                scope.ExecutePendingCalls();
            }
            if (scope.HasPendingReferences)
            {
                throw new InvalidOperationException("cannot close scope that has pending calls to increase reference counts");
            }
            _ = this.scopes.Pop();
        }

        /// <summary>
        /// Closes the current scope by popping it off of the stack.
        /// Emits the queued calls to unreference, release, and/or decrease the access counts for values going out of scope.
        /// Increases the reference count of the returned value by 1, either by omitting to unreference it or by explicitly increasing it.
        /// </summary>
        public void CloseScope(IValue returned, bool allowDelayReferencing = true)
        {
            var scope = this.scopes.Peek();
            var skipUnreference = scope.WillBeUnreferenced(returned);

            if (!allowDelayReferencing && !skipUnreference)
            {
                this.IncreaseReferenceCount(returned);
            }

            scope.ExecutePendingCalls(new List<IValue>() { returned }, applyReferences: !allowDelayReferencing);
            scope = this.scopes.Pop();

            if (allowDelayReferencing)
            {
                scope.MigratePendingReferences(this.scopes.Peek());
                if (!skipUnreference)
                {
                    this.IncreaseReferenceCount(returned);
                }
            }
        }

        /// <summary>
        /// Executes all pending calls to increase reference counts in the current scope.
        /// </summary>
        internal void ApplyPendingReferences() =>
            this.scopes.Peek().ApplyPendingReferences();

        /// <summary>
        /// Exits the current scope by emitting the calls to unreference, release,
        /// and/or decrease the access counts for values going out of scope,
        /// decreasing access counts and invoking release functions if necessary.
        /// Exiting the current scope does *not* close the scope.
        /// All pending calls to increase reference counts for values need to be applied
        /// using <see cref="ApplyPendingReferences"/> before exiting the scope.
        /// </summary>
        /// <exception cref="InvalidOperationException">The scope has pending calls to increase the reference count for values</exception>
        public void ExitScope(bool isTerminated)
        {
            var scope = this.scopes.Peek();
            if (scope.HasPendingReferences)
            {
                throw new InvalidOperationException("cannot exit scope that has pending calls to increase reference counts");
            }
            if (!isTerminated)
            {
                scope.ExecutePendingCalls();
            }
        }

        /// <summary>
        /// Returns true if reference counts are tracked for values of the given LLVM type.
        /// </summary>
        internal bool RequiresReferenceCount(ITypeRef t) =>
            this.ReferencesUpdateFunctionForType(t) != null;

        /// <summary>
        /// Adds a call to a runtime library function to increase the reference count for the given value if necessary.
        /// </summary>
        /// <param name="value">The value which is referenced</param>
        public void IncreaseReferenceCount(IValue value, bool shallow = false) =>
            this.scopes.Peek().ReferenceValue(value, !shallow);

        /// <summary>
        /// Adds a call to a runtime library function to decrease the reference count for the given value if necessary.
        /// </summary>
        /// <param name="value">The value which is unreferenced</param>
        public void DecreaseReferenceCount(IValue value, bool shallow = false) =>
            this.scopes.Peek().UnreferenceValue(value, !shallow);

        /// <summary>
        /// Adds a call to a runtime library function to change the reference count for the given value.
        /// </summary>
        /// <param name="value">The value for which to change the reference count</param>
        /// <param name="change">The amount by which to change the reference count given as i64</param>
        internal void UpdateReferenceCount(IValue change, IValue value, bool shallow = false) =>
            this.ModifyCounts(this.ReferencesUpdateFunctionForType, change, value, !shallow);

        /// <summary>
        /// Given a callable value, increases the reference count of its capture tuple by 1.
        /// </summary>
        /// <param name="callable">The callable whose capture tuple to reference</param>
        internal void ReferenceCaptureTuple(CallableValue callable) =>
            this.InvokeCallableMemoryManagement(0, this.plusOne, callable);

        /// <summary>
        /// Returns true if access counts are tracked for values of the given LLVM type.
        /// </summary>
        internal bool RequiresAccessCount(ITypeRef t) =>
            this.AccessUpdateFunctionForType(t) != null;

        /// <summary>
        /// Adds a call to a runtime library function to increase the access count for the given value if necessary.
        /// </summary>
        /// <param name="value">The value which is assigned to a handle</param>
        internal void IncreaseAccessCount(IValue value, bool shallow = false) =>
            this.ModifyCounts(this.AccessUpdateFunctionForType, this.plusOne, value, !shallow);

        /// <summary>
        /// Adds a call to a runtime library function to decrease the access count for the given value if necessary.
        /// </summary>
        /// <param name="value">The value which is unassigned from a handle</param>
        internal void DecreaseAccessCount(IValue value, bool shallow = false) =>
            this.ModifyCounts(this.AccessUpdateFunctionForType, this.minusOne, value, !shallow);

        /// <summary>
        /// Adds a call to a runtime library function to change the access count for the given value.
        /// </summary>
        /// <param name="value">The value for which to change the access count</param>
        /// <param name="change">The amount by which to change the access count given as i64</param>
        internal void UpdateAccessCount(IValue change, IValue value, bool shallow = false) =>
            this.ModifyCounts(this.AccessUpdateFunctionForType, change, value, !shallow);

        /// <summary>
        /// Queues a call to a suitable runtime library function that unreferences the value
        /// when the scope is closed or exited.
        /// </summary>
        /// <param name="value">Value that is created within the current scope</param>
        public void RegisterValue(IValue value) =>
            this.scopes.Peek().RegisterValue(value);

        /// <summary>
        /// Adds a value constructed as part of a qubit allocation to the current topmost scope.
        /// Makes sure that all allocated qubits are released when the scope is closed or exited
        /// and that the value and all its items are unreferenced.
        /// </summary>
        public void RegisterAllocatedQubits(IValue value)
        {
            var releaser =
                Types.IsArray(value.LlvmType) ? RuntimeLibrary.QubitReleaseArray :
                Types.IsQubit(this.sharedState.Types.Qubit) ? RuntimeLibrary.QubitRelease :
                throw new ArgumentException("AddQubitValue expects an argument of type Qubit or Qubit[]");
            this.scopes.Peek().RegisterValue(value, releaser);
        }

        /// <summary>
        /// Registers a variable name as an alias for an LLVM value.
        /// </summary>
        /// <param name="name">The name to register</param>
        /// <param name="value">The LLVM value</param>
        internal void RegisterVariable(string name, IValue value)
        {
            value.RegisterName(this.sharedState.InlinedName(name));
            this.IncreaseAccessCount(value);
            this.scopes.Peek().RegisterVariable(name, value);
        }

        /// <summary>
        /// Removes the given value from the list of registered values such that it will no longer be
        /// unreferenced when exiting or closing the scope.
        /// Any release function that has been specified when registering the value will still execute.
        /// </summary>
        internal bool TryRemoveValueFromCurrentScope(IValue value) =>
            this.scopes.Peek().TryRemoveValue(value);

        /// <summary>
        /// Gets the value of a named variable.
        /// The name must have been registered as an alias for the value using
        /// <see cref="RegisterVariable(string, Value, bool)"/>.
        /// </summary>
        /// <param name="name">The registered variable name to look for</param>
        internal IValue GetVariable(string name)
        {
            foreach (var scope in this.scopes)
            {
                if (scope.TryGetVariable(name, out IValue value))
                {
                    return value;
                }
            }
            throw new KeyNotFoundException($"Could not find a Value for local symbol {name}");
        }

        /// <summary>
        /// Exits the current function by emitting the calls to unreference values going out of scope for all open scopes,
        /// decreasing access counts and invoking release functions if necessary.
        /// Increases the reference count of the returned value by 1, either by omitting to unreference it or by explicitly increasing it.
        /// The calls are generated in using the current builder.
        /// Exiting the current function does *not* close the scopes.
        /// </summary>
        /// <param name="returned">The value that is returned and expected to remain valid after exiting.</param>
        public void ExitFunction(IValue returned)
        {
            // To avoid increasing the reference count for the returned value and all contained items
            // followed by immediately decreasing it again, we check whether we can avoid that.
            // There are a couple of pitfalls to watch out for when doing this:
            // a) It is possible that the returned value is or contains items that are not going to be
            //    unreferenced here, e.g. when the returned value has been passed as argument to the
            //    the callable we are exiting here. If that's the case, then we need to first increase
            //    the reference count for the returned value before processing all unreference calls,
            //    since otherwise an item contained in the returned value might get deallocated.
            // b) Conversely, it is also possible that the returned value is unreferenced multiple times.
            //    we hence need to make sure that we only omit one of these calls.
            // c) The stack of pending calls contains more than just those related to modifying reference
            //    counts; we need to make sure that any omitted call indeed was to unreference the
            //    returned value rather than e.g. to release qubits.
            // d) We can't modify the pending calls; they may be used by other execution paths that
            //    don't return the same value.

            var returnWillBeUnreferenced = this.scopes.Any(scope => scope.WillBeUnreferenced(returned));
            if (!returnWillBeUnreferenced)
            {
                this.IncreaseReferenceCount(returned);
            }

            // We need to extract the scopes to iterate over since for loops to release array items
            // will create new scopes and hence modify the collection.
            var currentScopes = this.scopes.ToArray();
            var omittedUnreferences = new List<IValue>() { returned };
            Scope.ExecutePendingCalls(this, omittedUnreferences, true, currentScopes);
        }
    }
}

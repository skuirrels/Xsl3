// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Xml.XPath;
using Xsl3.Qil;
using Xsl3.Runtime;

namespace Xsl3.IlGen;

internal sealed partial class XmlILVisitor
{
    private static MethodInfo PullMethod(string name) => typeof(XsltLibrary).GetMethod(name)!;

    // Suspend the existing generated iterator at its yield point. Iterator structs and locals keep
    // their exact state in an invocation-owned frame; no input sequence is converted to a cache.
    private void GeneratePullFunction(QilFunction function)
    {
        int argument = function.PullArgument + 1;
        LocalBuilder slots = _helper.DeclareLocal("$$$pullSlots", typeof(object[]));
        LocalBuilder item = _helper.DeclareLocal("$$$pullItem", typeof(XPathItem));
        LocalBuilder result = _helper.DeclareLocal("$$$pullResult", typeof(IList<XPathItem>));
        Label dispatch = _helper.DefineLabel(), start = _helper.DefineLabel(), end = _helper.DefineLabel(),
            restore = _helper.DefineLabel(), save = _helper.DefineLabel(), done = _helper.DefineLabel();
        _helper.EmitUnconditionalBranch(OpCodes.Br, dispatch);
        _helper.MarkLabel(start);
        StartNestedIterator();
        NestedVisit(function.Definition, end);
        Label next = _iterNested!.HasLabelNext ? _iterNested.GetLabelNext() : end;
        _iterCurr.EnsureItemStorageType(function.Definition.XmlType!, typeof(XPathItem));
        _iterCurr.EnsureStack();
        _helper.Emit(OpCodes.Stloc, item);

        LocalBuilder[] locals = _helper.PullLocals!.FindAll(local => local != slots && local != item && local != result).ToArray();
        foreach (LocalBuilder local in locals)
            if (local.LocalType.IsByRef || local.LocalType.IsByRefLike)
                throw new InvalidOperationException($"A streamed population cannot suspend a {local.LocalType} local");
        _helper.LoadParameter(argument);
        _helper.Call(PullMethod(nameof(XsltLibrary.X31PullSlots)));
        _helper.Emit(OpCodes.Stloc, slots);
        _helper.Emit(OpCodes.Ldloc, slots);
        _helper.Emit(OpCodes.Brtrue, save);
        _helper.LoadInteger(locals.Length);
        _helper.Emit(OpCodes.Newarr, typeof(object));
        _helper.Emit(OpCodes.Stloc, slots);
        _helper.MarkLabel(save);
        for (int i = 0; i < locals.Length; i++)
        {
            _helper.Emit(OpCodes.Ldloc, slots);
            _helper.LoadInteger(i);
            _helper.Emit(OpCodes.Ldloc, locals[i]);
            if (locals[i].LocalType.IsValueType) _helper.Emit(OpCodes.Box, locals[i].LocalType);
            _helper.Emit(OpCodes.Stelem_Ref);
        }
        _helper.LoadParameter(argument);
        _helper.Emit(OpCodes.Ldloc, slots);
        _helper.Emit(OpCodes.Ldloc, item);
        _helper.Call(PullMethod(nameof(XsltLibrary.X31PullYield)));
        _helper.Emit(OpCodes.Stloc, result);
        _helper.EmitUnconditionalBranch(OpCodes.Br, done);

        _helper.MarkLabel(dispatch);
        _helper.LoadParameter(argument);
        _helper.Call(PullMethod(nameof(XsltLibrary.X31PullPosition)));
        _helper.Emit(OpCodes.Switch, new[] { start, restore });
        _helper.EmitUnconditionalBranch(OpCodes.Br, end);
        _helper.MarkLabel(restore);
        _helper.LoadParameter(argument);
        _helper.Call(PullMethod(nameof(XsltLibrary.X31PullSlots)));
        _helper.Emit(OpCodes.Stloc, slots);
        for (int i = 0; i < locals.Length; i++)
        {
            _helper.Emit(OpCodes.Ldloc, slots);
            _helper.LoadInteger(i);
            _helper.Emit(OpCodes.Ldelem_Ref);
            _helper.Emit(locals[i].LocalType.IsValueType ? OpCodes.Unbox_Any : OpCodes.Castclass, locals[i].LocalType);
            _helper.Emit(OpCodes.Stloc, locals[i]);
        }
        _helper.EmitUnconditionalBranch(OpCodes.Br, next);
        _helper.MarkLabel(end);
        _helper.LoadParameter(argument);
        _helper.Call(PullMethod(nameof(XsltLibrary.X31PullEnd)));
        _helper.Emit(OpCodes.Stloc, result);
        _helper.MarkLabel(done);
        if (_helper.CountDepth)
        {
            _helper.Call(XmlILMethods.ExitFunctionCounted);
            _helper.CountDepth = false;
        }
        _helper.Emit(OpCodes.Ldloc, result);
        _iterCurr.Storage = StorageDescriptor.Stack(typeof(XPathItem), isCached: true);
        EndNestedIterator(function);
    }

    private QilNode VisitPullSequence(QilInvokeEarlyBound invocation)
    {
        Label end = _iterCurr.GetLabelNext(), next = _helper.DefineLabel();
        _helper.MarkLabel(next);
        VisitBoundCall(invocation);
        _iterCurr.EnsureLocal("$$$pullNext");
        _iterCurr.PushValue();
        _helper.CallCacheCount(_iterCurr.Storage.ItemStorageType);
        _helper.Emit(OpCodes.Brfalse, end);
        _iterCurr.PushValue();
        _helper.LoadInteger(0);
        _helper.CallCacheItem(_iterCurr.Storage.ItemStorageType);
        _iterCurr.SetIterator(next, StorageDescriptor.Stack(_iterCurr.Storage.ItemStorageType, isCached: false));
        return invocation;
    }
}

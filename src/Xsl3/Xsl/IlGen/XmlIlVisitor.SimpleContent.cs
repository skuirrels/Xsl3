// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Reflection.Emit;
using System.Xml.XPath;
using Xsl3.Qil;
using Xsl3.Runtime;

namespace Xsl3.IlGen;

internal sealed partial class XmlILVisitor
{
    private QilUnary VisitStreamedTextCtor(QilUnary text, QilInvokeEarlyBound content, bool raw)
    {
        LocalBuilder state = _helper.DeclareLocal("$$$simpleContent", typeof(SimpleContentWriter));
        _helper.Emit(OpCodes.Ldloca, state);
        NestedVisitEnsureStack(content.Arguments[1], typeof(string), false);
        _helper.LoadQueryOutput();
        _helper.LoadInteger(raw ? 1 : 0);
        _helper.Call(typeof(SimpleContentWriter).GetMethod(nameof(SimpleContentWriter.Create))!);

        QilNode items = content.Arguments[0];
        Label end = _helper.DefineLabel();
        StartNestedIterator(end);
        Visit(items);
        _iterCurr.EnsureStackNoCache();
        _iterCurr.EnsureItemStorageType(items.XmlType!, typeof(XPathItem));
        _iterCurr.EnsureLocal("$$$simpleItem");
        _helper.Emit(OpCodes.Ldloca, state);
        _iterCurr.PushValue();
        _helper.Call(typeof(SimpleContentWriter).GetMethod(nameof(SimpleContentWriter.Accept))!);
        _iterCurr.LoopToEnd(end);
        EndNestedIterator(items);

        _helper.Emit(OpCodes.Ldloca, state);
        _helper.Call(typeof(SimpleContentWriter).GetMethod(nameof(SimpleContentWriter.End))!);
        _iterCurr.Storage = StorageDescriptor.None();
        return text;
    }
}

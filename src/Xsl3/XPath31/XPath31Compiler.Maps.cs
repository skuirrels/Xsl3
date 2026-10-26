// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using Xsl3.Qil;
using Xsl3.Runtime;
using T = Xsl3.XmlQueryTypeFactory;

namespace Xsl3.XPath31;

internal sealed partial class XPath31Compiler
{
    private QilNode Map(MapConstructor map)
    {
        QilNode New() => Call(_sc.Xslt ? nameof(XsltLibrary.X31MapNewXslt) : nameof(XsltLibrary.X31MapNew), T.Item);
        QilNode Entry(QilNode builder, MapConstructorEntry entry) => Call(nameof(XsltLibrary.X31MapEntry), T.Item,
            builder, AtomicOperand(entry.Key), Items(Build(entry.Value)));

        // Literal-only entries need no source view. Unknown variables and calls stay
        // conservative: even an apparently scalar result can consume a captured stream.
        static bool InputFree(Expr expression) => expression switch
        {
            IntegerLiteral or DecimalLiteral or DoubleLiteral or StringLiteral => true,
            SequenceExpr sequence => System.Linq.Enumerable.All(sequence.Items, InputFree),
            UnaryExpr unary => InputFree(unary.Operand),
            BinaryExpr binary => InputFree(binary.Left) && InputFree(binary.Right),
            MapConstructor nested => System.Linq.Enumerable.All(nested.Entries, e => InputFree(e.Key) && InputFree(e.Value)),
            SquareArrayConstructor array => System.Linq.Enumerable.All(array.Members, InputFree),
            CurlyArrayConstructor array => array.Content == null || InputFree(array.Content),
            _ => false,
        };
        if (_sc.ForwardOnly && _env != null && System.Linq.Enumerable.Count(map.Entries, e => !InputFree(e.Key) || !InputFree(e.Value)) > 1)
        {
            var entries = new List<QilNode>();
            foreach (MapConstructorEntry entry in map.Entries)
                entries.Add(Call(nameof(XsltLibrary.X31MapDone), T.Item, Entry(New(), entry)));
            if (_env.ParallelMap(entries, _sc.Xslt) is { } parallel) return parallel;
        }
        QilNode result = New();
        foreach (MapConstructorEntry entry in map.Entries) result = Entry(result, entry);
        return Call(nameof(XsltLibrary.X31MapDone), T.Item, result);
    }
}

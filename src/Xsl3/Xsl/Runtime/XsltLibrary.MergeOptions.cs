// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.XPath;
using Xsl3.XPath31.Functions;
using Xsl3.Xdm;

namespace Xsl3.Runtime;

[Flags]
internal enum MergeKeyAttributes
{
    None = 0,
    Order = 1,
    Language = 2,
    Collation = 4,
    CaseOrder = 8,
    DataType = 16,
}

public sealed partial class XsltLibrary
{
    private sealed class MergeKeyOptions(string order, string lang, string collation, string caseOrder, string dataType, MergeKeyAttributes presence) : FunctionItem
    {
        internal readonly string Order = order, Lang = lang, Collation = collation, CaseOrder = caseOrder, DataType = dataType;
        internal readonly MergeKeyAttributes Presence = presence;
        public override int Arity => 0;
        public override SequenceTypeTest ParameterType(int index) => SequenceTypeTest.AnySequence;
        public override SequenceTypeTest ReturnType => SequenceTypeTest.AnySequence;
        public override string Describe() => "merge-key attributes";
        internal bool Agrees(MergeKeyOptions other) => Presence == other.Presence && Order == other.Order && Lang == other.Lang
            && Collation == other.Collation && CaseOrder == other.CaseOrder && DataType == other.DataType;
    }

    private sealed class MergePlan(MergeSpec spec, MergeKeyOptions[] keys) : FunctionItem
    {
        internal readonly MergeSpec Spec = spec;
        internal readonly MergeKeyOptions[] Keys = keys;
        public override int Arity => 0;
        public override SequenceTypeTest ParameterType(int index) => SequenceTypeTest.AnySequence;
        public override SequenceTypeTest ReturnType => SequenceTypeTest.AnySequence;
        public override string Describe() => "the effective merge-key configuration";
    }

    public XPathItem X31MergeKeyOptions(string order, string lang, string collation, string caseOrder, string dataType, double presence, string baseUri)
    {
        order = order.Trim(); lang = lang.Trim(); collation = collation.Trim(); caseOrder = caseOrder.Trim(); dataType = dataType.Trim();
        var attributes = (MergeKeyAttributes)(int)presence;
        bool explicitCollation = (attributes & MergeKeyAttributes.Collation) != 0;
        if (order is not ("ascending" or "descending")) throw new XdmException("XTDE0030", $"Invalid merge order '{order}'");
        if (!explicitCollation && (caseOrder.Length != 0 || (attributes & MergeKeyAttributes.CaseOrder) != 0)
            && caseOrder is not ("upper-first" or "lower-first")) throw new XdmException("XTDE0030", $"Invalid merge case-order '{caseOrder}'");
        if ((dataType.Length != 0 || (attributes & MergeKeyAttributes.DataType) != 0) && dataType is not ("text" or "number")) throw new XdmException("XTDE0030", $"Unsupported merge data-type '{dataType}'");
        if (explicitCollation && !UriReference.IsAbsolute(collation))
        {
            try { collation = UriReference.Resolve(collation, baseUri); }
            catch (XdmException error) { throw new XdmException("XTDE1035", error.Message); }
            if (!UriReference.IsAbsolute(collation)) throw new XdmException("XTDE1035", "A relative merge collation has no absolute base URI");
        }
        return new MergeKeyOptions(order, lang, collation, caseOrder, dataType, attributes);
    }

    public XPathItem X31MergePlan(IList<XPathItem> settings, double keyCount, double specId)
    {
        int count = (int)keyCount;
        var keys = new MergeKeyOptions[count];
        var spec = new MergeSpec();
        spec.SortBeforeMerge.AddRange(((MergeSpec)Constant((int)specId)).SortBeforeMerge);
        for (int i = 0; i < settings.Count; i++)
        {
            var key = (MergeKeyOptions)settings[i];
            int k = i % count;
            if (i >= count)
            {
                if (!keys[k].Agrees(key)) throw new XdmException("XTDE2210", $"Merge key {k + 1} attributes differ between sources");
                continue;
            }
            keys[k] = key;
            spec.Descending.Add(key.Order == "descending");
            string collation = key.Collation;
            if (collation.Length == 0 && key.CaseOrder.Length != 0)
                collation = Collation.UcaPrefix + "?fallback=yes;caseFirst=" + key.CaseOrder + ";lang=" + key.Lang;
            // Use the same implementation-defined language/case collation as xsl:sort.
            SortCollation(collation, key.Collation.Length != 0 ? "" : key.Lang);
            spec.Collations.Add(collation.Length != 0 ? collation : key.Lang.Length != 0
                ? Collation.UcaPrefix + "?lang=" + key.Lang + ";fallback=yes" : Collation.CodepointUri);
        }
        return new MergePlan(spec, keys);
    }

    private static IList<XPathItem>[] MergeRawKeys(IList<XPathItem> wrapped)
    {
        var keys = new IList<XPathItem>[wrapped.Count];
        for (int k = 0; k < keys.Length; k++)
        {
            IList<XPathItem> atoms = Sequences.Atomize(((ArrayItem)wrapped[k])[0]);
            if (atoms.Count > 1) throw new XdmException("XTTE1020", $"A merge key has {atoms.Count} values; it must have at most one");
            keys[k] = atoms.Select(item => item is AtomicValue { Type.Primitive: Primitive.UntypedAtomic } atom
                ? AtomicValue.OfString(atom.Value) : item).ToArray();
        }
        return keys;
    }

    private static IList<XPathItem>[] MergeSortKeys(IList<XPathItem>[] raw, MergePlan? plan)
    {
        if (plan == null) return raw;
        var keys = new IList<XPathItem>[raw.Length];
        for (int k = 0; k < keys.Length; k++)
        {
            IList<XPathItem> key = raw[k];
            keys[k] = key.Count == 0 ? key : plan.Keys[k].DataType switch
            {
                "number" => Sequences.One(AtomicValue.OfDouble(ToNumber((AtomicValue)key[0]))),
                "text" => Sequences.One(AtomicValue.OfString(key[0].Value)),
                _ => key,
            };
        }
        return keys;
    }
}

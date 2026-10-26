// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Xml.Schema;
using Xsl3.XPath31;
using Xsl3.Xdm;

namespace Xsl3.Xslt;

/// <summary>Resolve function types in their declaration scopes, independently of any caller.</summary>
internal static class StylesheetFunctionTypes
{
    internal static FunctionItemTest Resolve(XslNode function, XmlSchemaSet? schemas,
        Func<StaticTypeResolver, SequenceType, SequenceTypeTest>? resolve = null)
    {
        var namespaces = new CompilerScopeManager<object>();
        namespaces.EnterScope(function.Namespaces);
        SequenceTypeTest Type(string? source)
        {
            if (source == null) return SequenceTypeTest.AnySequence;
            var types = new StaticTypeResolver(p => namespaces.LookupNamespace(p),
                namespaces.LookupNamespace(XsltInput.XPathDefaultNamespacePrefix) ?? "", schemas);
            SequenceType parsed = Parser.ParseSequenceType(source);
            return resolve == null ? types.Resolve(parsed) : resolve(types, parsed);
        }
        SequenceTypeTest result = Type(function.V2?.As);
        var parameters = new List<SequenceTypeTest>();
        foreach (XslNode parameter in function.Content)
        {
            if (parameter.NodeType != XslNodeType.Param) continue;
            namespaces.EnterScope(parameter.Namespaces);
            try { parameters.Add(Type(parameter.V2?.As)); }
            finally { namespaces.ExitScope(); }
        }
        return new FunctionItemTest(parameters, result);
    }
}

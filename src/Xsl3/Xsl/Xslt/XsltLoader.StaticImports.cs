// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Xml;
using System.Xml.XPath;

namespace Xsl3.Xslt
{
    internal sealed partial class XsltLoader
    {
        /// <summary>
        /// Xsl3: the static variables and parameters of a module that xsl:import names, and of the modules it imports and
        /// includes, in stylesheet tree order. Their scope takes in the importing module after the xsl:import (XSLT 3.0
        /// §9.6), but imported modules are loaded after the module that imports them (their import precedence is lower),
        /// so they are read here first, as far as their static declarations go. Errors are left to the real load: a
        /// declaration that can't be evaluated here is skipped, and evaluated again (and reported) when its module loads.
        /// </summary>
        private void PreloadImportedStatics(Uri uri, HashSet<string> seen)
        {
            if (!seen.Add(uri.ToString()) || _documentUriInUse.Contains(uri.ToString()))
            {
                return;
            }
            XPathNavigator? module;
            try
            {
                using XmlReader reader = CreateReader(uri, _xmlResolver);
                module = new XPathDocument(reader, XmlSpace.Default).CreateNavigator();
            }
            catch (Exception e) when (XmlException.IsCatchableException(e))
            {
                return;
            }
            if (!module.MoveToChild(XPathNodeType.Element) || module.NamespaceURI != XmlReservedNs.NsXslt ||
                module.LocalName is not ("stylesheet" or "transform") || !UseWhenHolds(module, xslt: true))
            {
                return; // (a simplified stylesheet has no static declarations; a package is used, not imported)
            }
            if (!module.MoveToFirstChild())
            {
                return;
            }
            do
            {
                if (module.NodeType != XPathNodeType.Element || module.NamespaceURI != XmlReservedNs.NsXslt || !UseWhenHolds(module, xslt: true))
                {
                    continue;
                }
                switch (module.LocalName)
                {
                    case "import" or "include" when module.GetAttribute("href", "") is { Length: > 0 } href:
                        try
                        {
                            PreloadImportedStatics(ResolveUri(href, BaseOf(module)), seen);
                        }
                        catch (Exception e) when (XmlException.IsCatchableException(e))
                        {
                        }
                        break;
                    case "variable" or "param" when module.GetAttribute("static", "").Trim() is "yes" or "true" or "1":
                        PreloadStatic(module);
                        break;
                }
            }
            while (module.MoveToNext());
        }

        // A static declaration of a module being read ahead: its value, unless one is known already (the importing
        // module's, whose import precedence is higher, comes first).
        private void PreloadStatic(XPathNavigator declaration)
        {
            Dictionary<string, string> namespaces = Namespaces(declaration);
            string lexical = declaration.GetAttribute("name", "").Trim();
            int colon = lexical.IndexOf(':');
            if (lexical.Length == 0 || colon >= 0 && !namespaces.ContainsKey(lexical[..colon]))
            {
                return;
            }
            var name = new Xsl3.Xdm.QName(colon < 0 ? "" : namespaces[lexical[..colon]], colon < 0 ? lexical : lexical[(colon + 1)..]);
            if (_compiler.StaticValues.ContainsKey(name))
            {
                return;
            }
            try
            {
                IList<XPathItem> value;
                string conversionCode = "XTTE0590";
                if (declaration.LocalName == "param" && _compiler.StaticParameters != null &&
                    _compiler.StaticParameters.TryGetValue(new XmlQualifiedName(name.LocalName, name.NamespaceUri), out var supplied))
                {
                    value = supplied;
                    conversionCode = "XTDE0050"; // (a supplied value that isn't of the type; static-013)
                }
                else
                {
                    // (A shadow attribute, _select, is a static value template.)
                    string? select = declaration.GetAttribute("_select", "") is { Length: > 0 } shadow
                        ? UseWhen.EvaluateAvt(shadow, namespaces, "", BaseOf(declaration), _compiler.StaticValues, _compiler.Settings.EnableEvaluate)
                        : declaration.GetAttribute("select", "") is { Length: > 0 } s ? s : null;
                    if (select == null)
                    {
                        return; // (no select: the real load works out the default value, or the error)
                    }
                    conversionCode = declaration.LocalName == "param" ? "XTDE0050" : conversionCode; // (static-013)
                    value = UseWhen.EvaluateValue(select, namespaces, "", BaseOf(declaration), _compiler.StaticValues, _compiler.Settings.EnableEvaluate);
                }
                if (declaration.GetAttribute("as", "") is { Length: > 0 } asType)
                {
                    var types = new Xsl3.XPath31.StaticTypeResolver(p => namespaces.TryGetValue(p, out string? ns) ? ns : null, "");
                    value = Xsl3.XPath31.Functions.Coercion.CoerceTo(value, types.Resolve(Xsl3.XPath31.Parser.ParseSequenceType(asType)), conversionCode, () => "$" + lexical);
                }
                _compiler.StaticValues[name] = Xsl3.XPath31.ConstantPool.Add(value);
            }
            catch (Exception e) when (e is Xsl3.XPath31.XPathStaticException or Xsl3.Xdm.XdmException or XmlException or FormatException)
            {
                // Evaluated again, and the error reported, when the module is loaded.
            }
        }

        // [xsl:]use-when on an element read ahead: true unless it is false (an error counts as true here; the real load
        // reports it).
        private bool UseWhenHolds(XPathNavigator element, bool xslt)
        {
            string useWhen = xslt ? element.GetAttribute("use-when", "") : element.GetAttribute("use-when", XmlReservedNs.NsXslt);
            if (useWhen.Length == 0)
            {
                return true;
            }
            try
            {
                return UseWhen.Evaluate(useWhen, Namespaces(element), "", BaseOf(element), _compiler.StaticValues, _compiler.Settings.EnableEvaluate);
            }
            catch (Exception e) when (e is Xsl3.XPath31.XPathStaticException or Xsl3.Xdm.XdmException)
            {
                return true;
            }
        }

        private static Dictionary<string, string> Namespaces(XPathNavigator element)
        {
            var namespaces = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (prefix, ns) in element.GetNamespacesInScope(XmlNamespaceScope.All))
            {
                namespaces[prefix] = ns;
            }
            return namespaces;
        }

        private static string BaseOf(XPathNavigator element) => Xsl3.XPath31.Functions.FunctionLibrary.BaseUri(element);
    }
}

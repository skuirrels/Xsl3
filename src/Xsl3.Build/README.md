# Xsl3.Build

Compiles XSLT stylesheets ahead of time for [Xsl3](https://github.com/skuirrels/Xsl3), so that they load without
compiling (`XslCompiledTransform.Load(Type)`) and run under NativeAOT.

Version **1.1.6** includes the bounded P7H Debug compiler source-line correction. Repeated
bare-variable sequences retain separate instruction locations and one shared binding, with the
compiler assertion retained. Normal/debug saved styles preserve the six P7G identity contracts.
P7G identity, P7F allocation and P7D/P7C correctness fixes remain included.
Update this package with `Xsl3` and `Xsl3.Cli`, and regenerate
saved stylesheets using matching packages. Streaming remains unsupported; see the
[1.1.6 release notes](https://github.com/skuirrels/Xsl3/blob/v1.1.6/docs/releases/1.1.6.md).

```xml
<ItemGroup>
  <PackageReference Include="Xsl3" Version="..." />
  <PackageReference Include="Xsl3.Build" Version="..." PrivateAssets="all" />
  <XsltStylesheet Include="Report.xsl" TypeName="Stylesheets.Report" />
</ItemGroup>
```

```csharp
var transform = new XslCompiledTransform();
transform.Load(typeof(Stylesheets.Report), new XsltSettings());
transform.Transform(input, null, output);
```

Each stylesheet becomes an assembly holding one type, named by `TypeName` (the file name by default), which the
project references. `DebugInformation="true"` embeds a portable PDB, so a debugger steps through the stylesheet.
`BackwardsCompatible="true"` compiles a `version="1.0"` stylesheet for the XSLT 3.0 processor.
Compiling needs the .NET 10 SDK; the compiled stylesheets run on .NET 8 and later.

The assemblies are written under `obj/<Configuration>/xsl3/` (`Xsl3StylesheetOutputPath`). A compiled stylesheet runs only
on the Xsl3 that compiled it: `Load(Type)` refuses one whose function library differs. Keep Xsl3 and Xsl3.Build at the
same version; a compiler built from source can be listed in `Xsl3CompilerInputs` so that a rebuild of it compiles the
stylesheets again.

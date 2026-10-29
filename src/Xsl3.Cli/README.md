# xsl3

The command line of [Xsl3](https://github.com/skuirrels/Xsl3), an XSLT 3.0, XSLT 2.0 and XPath 3.1 processor for .NET.

Version **1.1.4** includes the bounded P7F attribute-copy allocation reduction. Ordinary untyped native
attribute copies use smaller fragments while preserving independent node identity and existing copy policies.
The measured identity workload allocates 23.55% less across all three sizes on Linux/macOS.
P7D validation and P7C reader-boundary corrections remain included.
Keep the CLI and runtime/build packages at the same version. Streaming remains unsupported; see the
[1.1.4 release notes](https://github.com/skuirrels/Xsl3/blob/v1.1.4/docs/releases/1.1.4.md).

```
dotnet tool install --global Xsl3.Cli
```

```
xsl3 transform -xsl:style.xsl -s:input.xml -o:output.html [name=value ...]
xsl3 transform -xsl:style.xsl -it [-it:name] [-im:mode] [-o:file] [--trusted] [--trace]
xsl3 explain -xsl:style.xsl [--unoptimized]
xsl3 compile -xsl:style.xsl -o:Style.dll [-type:Namespace.Type] [--debug] [--version1]
```

- `transform` runs a stylesheet on a source document (`-s`), or from a named template (`-it`, `xsl:initial-template`
  by default), and writes the result to `-o` or the console. `name=value` sets a stylesheet parameter to a string.
  `--trusted` allows `document()`, `doc()`, `xsl:evaluate`, secondary result files and environment variables; without
  it they are off. `--trace` prints each template and function as it starts and ends.
- `compile` saves the stylesheet as an assembly holding one type, for `XslCompiledTransform.Load(Type)` and NativeAOT
  (Xsl3.Build runs it from MSBuild). `--debug` embeds a portable PDB mapping the code to the stylesheet's lines.
- `explain` writes the optimized query intermediate language (QIL) the stylesheet compiles to, as XML: template
  rules, the functions that dispatch modes to them, and the generated code's structure. The format is internal and
  changes between versions.

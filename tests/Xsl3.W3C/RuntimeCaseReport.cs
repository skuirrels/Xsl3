// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using CompilerErrorCollection = Xsl3.Xslt.CompilerErrorCollection;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Xml.Linq;
using Xsl3.Streaming;
using Xsl3.W3C.Qt3;
using TestCase = Xsl3.W3C.Xslt30.TestCase;

namespace Xsl3.W3C;

/// <summary>Optional per-case evidence; selection and evaluation remain the ordinary runner's.</summary>
internal sealed class RuntimeCaseReport : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly Dictionary<string, string> _setHashes = new(StringComparer.Ordinal);
    private readonly bool _captureSources;
    private readonly bool _captureEvaluation;

    internal RuntimeCaseReport(string path, string root, string profile, bool strict, bool captureSources = false, bool captureEvaluation = false)
    {
        _captureSources = captureSources;
        _captureEvaluation = captureEvaluation;
        // An interrupted or earlier run is evidence, not a file to overwrite implicitly.
        // Resolve metadata before opening the output, so a metadata failure cannot leak its handle.
        string catalogHash = HashFile(Path.Combine(root, "catalog.xml"));
        string engineHash = HashFile(typeof(XslCompiledTransform).Assembly.Location);
        _writer = new StreamWriter(File.Open(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
        try
        {
            Write(new
            {
                Kind = "run", FormatVersion = 1, Profile = profile, Strict = strict,
                ForwardingCaptured = captureSources,
                EvaluationCaptured = captureEvaluation,
                Api = Environment.GetEnvironmentVariable("XSL3_W3C_API") != "0",
                SavedSampling = Environment.GetEnvironmentVariable("XSL3_W3C_SAVED"),
                SetPrefixes = Environment.GetEnvironmentVariable("XSL3_XSLT30_SETS"),
                Framework = RuntimeInformation.FrameworkDescription,
#if DEBUG
                Configuration = "Debug",
#else
                Configuration = "Release",
#endif
                CatalogSha256 = catalogHash,
                EngineSha256 = engineHash,
            });
        }
        catch
        {
            _writer.Dispose();
            throw;
        }
    }

    internal StreamingExecutionCapture? CaptureCase() => _captureSources ? new() : null;
    internal EvaluationCapture? CaptureEvaluation() => _captureEvaluation ? new() : null;

    internal sealed record EvaluationEntry(string Status, string? ExceptionType, Xslt30RunTests.StaticDiagnostic? Diagnostic);

    internal sealed class EvaluationCapture
    {
        private EvaluationEntry? _entry;
        internal void Observe(Exception? error, CompilerErrorCollection? compilationErrors)
        {
            EvaluationEntry entry;
            if (error == null) entry = new("Returned", null, null);
            else
            {
                var diagnostic = Xslt30RunTests.PrimaryDiagnostic(error, compilationErrors);
                // Legacy execution can raise the BCL-form exception that the modern
                // API translates. Reuse that same translation, never scan expected codes.
                if (!diagnostic.IsStatic && diagnostic.Code == null && XsltDynamicException.From(error) is { } translated)
                    diagnostic = Xslt30RunTests.PrimaryDiagnostic(translated);
                entry = new("Error", error.GetType().FullName, diagnostic);
            }
            if (Interlocked.CompareExchange(ref _entry, entry, null) != null)
                throw new InvalidOperationException("A case evaluation can only be observed once");
        }
        internal EvaluationEntry Snapshot() => Volatile.Read(ref _entry) ?? new("NotObserved", null, null);
    }

    internal void Record(TestCase test, Outcome outcome, StreamingExecutionCapture? capture = null, EvaluationCapture? evaluation = null)
    {
        if (!_setHashes.TryGetValue(test.Set.File, out string? setHash))
            _setHashes.Add(test.Set.File, setHash = HashFile(test.Set.File));
        var row = new
        {
            Kind = "case", Case = test.Id, Set = test.Set.Name,
            SetFile = Path.GetRelativePath(test.Set.Catalog.Root, test.Set.File).Replace('\\', '/'),
            SetSha256 = setHash,
            DefinitionSha256 = Hash(Encoding.UTF8.GetBytes(test.Element.ToString(SaveOptions.DisableFormatting))),
            Outcome = outcome.Kind.ToString(), outcome.Detail,
            Dependencies = test.EffectiveDependencies,
        };
        if (_captureSources || _captureEvaluation)
        {
            JsonObject payload = JsonSerializer.SerializeToNode(row)!.AsObject();
            if (_captureSources)
            {
                if (capture == null) throw new InvalidOperationException("A forwarding-enabled report requires a case capture");
                payload["Forwarding"] = JsonSerializer.SerializeToNode(capture.Snapshot());
            }
            if (_captureEvaluation)
            {
                if (evaluation == null) throw new InvalidOperationException("An evaluation-enabled report requires a case capture");
                payload["Evaluation"] = JsonSerializer.SerializeToNode(evaluation.Snapshot());
                payload["DeclaredInitialSources"] = JsonSerializer.SerializeToNode(test.Environments.OfType<Xslt30.TestEnvironment>()
                    .SelectMany(e => e.Sources).Where(s => s.Role == ".").Select(s => new {
                        s.Streaming, s.Select, s.Validation, HasDocument = s.File != null || s.Content != null,
                    }).ToArray());
            }
            Write(payload);
        }
        else Write(row);
    }

    internal void Complete(int passed, int failed, int notRun, int unmatchedCodes,
        IReadOnlyDictionary<string, int> notRunReasons) => Write(new
        {
            Kind = "complete", Selected = passed + failed + notRun,
            Passed = passed, Failed = failed, NotRun = notRun,
            UnmatchedExpectedErrorCodes = unmatchedCodes, NotRunReasons = notRunReasons,
        });

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string HashFile(string path)
    {
        using Stream source = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant();
    }
    private void Write<T>(T value) => _writer.WriteLine(JsonSerializer.Serialize(value));
    public void Dispose() => _writer.Dispose();
}

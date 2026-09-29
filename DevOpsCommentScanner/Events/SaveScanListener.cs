using DevOpsCommentScanner.Config;
using DevOpsCommentScanner.Models;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Documents;
using Microsoft.VisualStudio.Extensibility.Settings;
using Microsoft.VisualStudio.RpcContracts.Documents;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsCommentScanner.Events;

#pragma warning disable VSEXTPREVIEW_SETTINGS

/// <summary>
/// Subscribes to document events (via DocumentsExtensibility.SubscribeAsync) and scans a file
/// for keywords in its comments whenever it is saved. Settings are read fresh on every save,
/// so changes made in the settings UI apply immediately.
/// </summary>
internal sealed class SaveScanListener : IDocumentEventsListener, IDisposable
{
    private const string OutputChannelName = "Keyword Comment Scanner";

    private readonly VisualStudioExtensibility _extensibility;
    private readonly SemaphoreSlim _outputLock = new(1, 1);

#pragma warning disable VSEXTPREVIEW_SETTINGS
#pragma warning disable VSEXTPREVIEW_OUTPUTWINDOW
    private OutputChannel? _outputChannel;
    private IDisposable? _subscription;

    private SaveScanListener(VisualStudioExtensibility extensibility)
    {
        _extensibility = extensibility;
    }

    public static async Task<SaveScanListener> StartAsync(
        VisualStudioExtensibility extensibility, CancellationToken cancellationToken)
    {
        var listener = new SaveScanListener(extensibility);

        // filterRegex: null = all documents; we filter by extension/folder ourselves using settings.
        listener._subscription = await extensibility.Documents()
            .SubscribeAsync(listener, filterRegex: null, cancellationToken);

        return listener;
    }

    public void Dispose()
    {
        _subscription?.Dispose();
        _subscription = null;

        _outputChannel?.Dispose();
        _outputChannel = null;
    }

    // ---------- The one event we care about ----------

    public async Task SavedAsync(DocumentEventArgs e, CancellationToken token)
    {
        try
        {
            if (!e.Moniker.IsFile)
                return;

            string path = e.Moniker.LocalPath;

            // Read all settings in a single call.
            var values = await _extensibility.Settings().ReadEffectiveValuesAsync(SettingDefinitions.All, token);

            if (!values.ValueOrDefault(SettingDefinitions.Enabled, defaultValue: true))
                return;

            var extensions = SplitList(values.ValueOrDefault(SettingDefinitions.FileExtensions, SettingDefinitions.DefaultExtensions), ';');
            var excluded = SplitList(values.ValueOrDefault(SettingDefinitions.ExcludedFolders, SettingDefinitions.DefaultExcludedFolders), ';');
            var keywords = SplitList(values.ValueOrDefault(SettingDefinitions.Keywords, SettingDefinitions.DefaultKeywords), ',');

            if (!CommentScanner.IsSupported(path) || !PassesFilters(path, extensions, excluded) || keywords.Count == 0)
                return;

            bool caseSensitive = values.ValueOrDefault(SettingDefinitions.CaseSensitive, defaultValue: false);
            bool wholeWord = values.ValueOrDefault(SettingDefinitions.WholeWordOnly, defaultValue: true);
            int max = values.ValueOrDefault(SettingDefinitions.MaxResultsPerFile, defaultValue: 100);

            string text = File.ReadAllText(path);
            List<CommentMatch> matches = CommentScanner.Scan(path, text, keywords, caseSensitive, wholeWord);

            if (max > 0 && matches.Count > max)
                matches = matches.Take(max).ToList();

            if (values.ValueOrDefault(SettingDefinitions.WriteToOutputWindow, defaultValue: true))
            {
                string detail = values.ValueOrDefault(SettingDefinitions.OutputDetail, SettingDefinitions.DetailedId);
                await WriteOutputAsync(path, matches, detailed: detail == SettingDefinitions.DetailedId, token);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down; nothing to report.
        }
        catch (Exception ex)
        {
            // Never let a scanning failure disturb the user's save.
            System.Diagnostics.Debug.WriteLine($"KeywordCommentScanner error: {ex}");
        }
    }

    // ---------- Output window ----------

    private async Task WriteOutputAsync(string path, List<CommentMatch> matches, bool detailed, CancellationToken token)
    {
        // CreateOutputChannelAsync throws if called twice with the same display name,
        // so the channel is created once and reused. The lock also keeps lines from
        // two overlapping saves (e.g. Save All) from interleaving.
        await _outputLock.WaitAsync(token);
        try
        {
#pragma warning disable VSEXTPREVIEW_SETTINGS
#pragma warning disable VSEXTPREVIEW_SETTINGS
#pragma warning disable VSEXTPREVIEW_OUTPUTWINDOW
            // CreateOutputChannelAsync throws if called twice with the same display name,
            // so the channel is created once and reused. The lock also keeps lines from
            // two overlapping saves (e.g. Save All) from interleaving.
            
            try
            {
                _outputChannel ??= await _extensibility.Views().Output
                    .CreateOutputChannelAsync(OutputChannelName, token).ConfigureAwait(false);
            }
            finally
            {
                _outputLock.Release();
            }

            await _outputChannel.Writer.WriteLineAsync(
                $"--- {DateTime.Now:T} {path} : {matches.Count} match(es)");

            if (!detailed)
                return;

            foreach (CommentMatch m in matches)
            {
                await _outputChannel.Writer.WriteLineAsync(
                    $"{m.FilePath}({m.Line},{m.Column + 1}): [{m.Keyword}] {m.CommentText}");
            }
        }
        finally
        {
            _outputLock.Release();
        }
    }

    // ---------- Helpers ----------

    private static bool PassesFilters(string path, IReadOnlyList<string> extensions, IReadOnlyList<string> excludedFolders)
    {
        string ext = Path.GetExtension(path);

        if (extensions.Count > 0 &&
            !extensions.Any(x => string.Equals(x, ext, StringComparison.OrdinalIgnoreCase)))
            return false;

        string[] segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return !segments.Any(seg => excludedFolders.Any(x => string.Equals(x, seg, StringComparison.OrdinalIgnoreCase)));
    }

    private static List<string> SplitList(string value, char separator) =>
        (value ?? string.Empty)
            .Split(separator)
            .Where(s => s.Length > 0)
            .ToList();


    // ---------- Events we don't need ----------

    public Task OpenedAsync(DocumentEventArgs e, CancellationToken token) => Task.CompletedTask;
    public Task ClosedAsync(DocumentEventArgs e, CancellationToken token) => Task.CompletedTask;
    public Task HiddenAsync(DocumentEventArgs e, CancellationToken token) => Task.CompletedTask;
    public Task ShownAsync(DocumentEventArgs e, CancellationToken token) => Task.CompletedTask;
    public Task RenamedAsync(RenamedDocumentEventArgs e, CancellationToken token) => Task.CompletedTask;
    public Task SavingAsync(DocumentEventArgs e, CancellationToken token) => Task.CompletedTask;
}

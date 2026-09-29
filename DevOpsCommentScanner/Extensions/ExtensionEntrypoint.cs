using DevOpsCommentScanner.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Editor;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsCommentScanner.Extensions;

/// <summary>
/// Entry point. The project template generates this class, including ExtensionConfiguration
/// (keep the template's version of that property; the values below are placeholders).
/// </summary>
[VisualStudioContribution]
internal class ExtensionEntrypoint : Extension
{
    private SaveScanListener? _saveListener;

    public override ExtensionConfiguration ExtensionConfiguration => new()
    {
        Metadata = new(
            id: "KeywordCommentScanner.b7d2c1a4-9e3f-4a58-8c61-2f0d5e7a9b13",
            version: this.ExtensionAssemblyVersion,
            publisherName: "Your Name",
            displayName: "Keyword Comment Scanner",
            description: "Scans code comments for configurable keywords whenever a file is saved."),
    };

    protected override void InitializeServices(IServiceCollection serviceCollection)
    {
        base.InitializeServices(serviceCollection);
        // Nothing to register: settings are read on demand in SaveScanListener.
    }

    /// <summary>
    /// Called once the extension has loaded. Start listening for document saves. 
    /// </summary>
    protected override async Task OnInitializedAsync(VisualStudioExtensibility extensibility, CancellationToken cancellationToken)
    {
        await base.OnInitializedAsync(extensibility, cancellationToken);
        _saveListener = await SaveScanListener.StartAsync(extensibility, cancellationToken);
    }

    protected override void Dispose(bool isDisposing)
    {
        if (isDisposing)
            _saveListener?.Dispose();

        base.Dispose(isDisposing);
    }
}

/// <summary>
/// Out-of-process extensions load lazily, when one of their components is first needed.
/// This do-nothing listener applies to every file, so the extension loads as soon as the
/// user opens any document and the save subscription above is in place before the first save.
/// </summary>

[VisualStudioContribution]
internal sealed class ExtensionActivator : ExtensionPart, ITextViewOpenClosedListener
{
    public TextViewExtensionConfiguration TextViewExtensionConfiguration => new()
    {
        AppliesTo = new[] { DocumentFilter.FromGlobPattern("**/*", relativePath: true) },
    };

    public Task TextViewOpenedAsync(ITextViewSnapshot textView, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task TextViewClosedAsync(ITextViewSnapshot textView, CancellationToken cancellationToken) => Task.CompletedTask;
}



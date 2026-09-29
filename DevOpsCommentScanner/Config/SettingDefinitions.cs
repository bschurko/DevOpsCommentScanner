using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Settings;

namespace DevOpsCommentScanner.Config;

// The settings API is still marked experimental/preview.
#pragma warning disable VSEXTPREVIEW_SETTINGS

/// <summary>
/// Declares the settings shown in Visual Studio's settings UI. Both the category and every
/// setting need [VisualStudioContribution]. Values are read at save time by SaveScanListener.
/// </summary>
internal static class SettingDefinitions
{
    [VisualStudioContribution]
    internal static SettingCategory ScannerCategory { get; } = new("keywordCommentScanner", "Keyword Comment Scanner")
    {
        Description = "Scans code comments for keywords each time a file is saved.",
    };

    // ---------- Scanning ----------

    [VisualStudioContribution]
    internal static Setting.Boolean Enabled { get; } = new(
        "enabled", "Scan on save", ScannerCategory, defaultValue: true)
    {
        Description = "When enabled, comments are scanned every time a file is saved.",
    };

    [VisualStudioContribution]
    internal static Setting.String Keywords { get; } = new(
        "keywords", "Keywords", ScannerCategory, defaultValue: DefaultKeywords)
    {
        Description = "Comma-separated keywords to look for inside comments, e.g. TODO,FIXME,HACK.",
        MaxStringLength = 500,
    };

    [VisualStudioContribution]
    internal static Setting.Boolean CaseSensitive { get; } = new(
        "caseSensitive", "Case sensitive", ScannerCategory, defaultValue: false)
    {
        Description = "When enabled, 'todo' will not match 'TODO'.",
    };

    [VisualStudioContribution]
    internal static Setting.Boolean WholeWordOnly { get; } = new(
        "wholeWordOnly", "Whole word only", ScannerCategory, defaultValue: true)
    {
        Description = "When enabled, 'TODO' will not match inside 'TODOLIST'.",
    };

    [VisualStudioContribution]
    internal static Setting.String FileExtensions { get; } = new(
        "fileExtensions", "File extensions", ScannerCategory, defaultValue: DefaultExtensions)
    {
        Description = "Semicolon-separated extensions to scan, e.g. .cs;.vb;.js. Leave empty to scan every supported type.",
        MaxStringLength = 500,
    };

    [VisualStudioContribution]
    internal static Setting.String ExcludedFolders { get; } = new(
        "excludedFolders", "Excluded folders", ScannerCategory, defaultValue: DefaultExcludedFolders)
    {
        Description = "Semicolon-separated folder names to skip (matched against any segment of the file path).",
        MaxStringLength = 500,
    };

    [VisualStudioContribution]
    internal static Setting.Integer MaxResultsPerFile { get; } = new(
        "maxResultsPerFile", "Max results per file", ScannerCategory, defaultValue: 100)
    {
        Description = "Stop reporting after this many matches in one file. 0 = unlimited.",
    };

    // ---------- Reporting ----------

    [VisualStudioContribution]
    internal static Setting.Boolean WriteToOutputWindow { get; } = new(
        "writeToOutputWindow", "Write results to Output window", ScannerCategory, defaultValue: true);

    [VisualStudioContribution]
    internal static Setting.Enum OutputDetail { get; } = new(
        "outputDetail",
        "Output detail",
        ScannerCategory,
        [
            new("detailed", "One line per match"),
            new("summary", "Summary count only"),
        ],
        defaultValue: DetailedId)
    {
        Description = "How much to write to the Output window for each save.",
    };

    // ---------- Defaults / helpers (not contributions) ----------

    internal const string DefaultKeywords = "TODO,FIXME,HACK,BUG,DESCRIPTION,TITLE";
    internal const string DefaultExtensions = ".cs;.vb;.js;.ts;.cpp;.h;.py;.sql;.xml;.xaml;.html;.cshtml;";
    internal const string DefaultExcludedFolders = "bin;obj;node_modules;.git;.vs";
    internal const string DetailedId = "detailed";
    internal const string SummaryId = "summary";

    /// <summary>Every setting, for a single batched read.</summary>
    internal static Setting[] All =>
    [
        Enabled, Keywords, CaseSensitive, WholeWordOnly, FileExtensions,
        ExcludedFolders, MaxResultsPerFile, WriteToOutputWindow, OutputDetail,
    ];
}

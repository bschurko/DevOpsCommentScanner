using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DevOpsCommentScanner.Models
{
    public sealed class CommentMatch
    {
        public string FilePath { get; set; }
        public int Line { get; set; }      // 1-based
        public int Column { get; set; }    // 0-based
        public string Keyword { get; set; }
        public string CommentText { get; set; }
    }

    /// <summary>
    /// Describes how comments look in a language.
    /// </summary>
    internal sealed class CommentSyntax
    {
        public string[] LineMarkers = new string[0];
        public string BlockStart;
        public string BlockEnd;
        public bool DoubleQuoteStrings = true;
        public bool SingleQuoteStrings = true;
    }

    /// <summary>
    /// Finds comments in source text and reports those containing keywords.
    /// Has no dependency on Visual Studio so it can be unit tested.
    ///
    /// Limitation: this is a lightweight lexer, not a full parser. It skips single-line
    /// string literals so "http://x" is not treated as a comment, but multi-line strings
    /// (raw strings, C# verbatim strings spanning lines) are not tracked. For 100% accuracy
    /// on C#, swap ExtractComments for a Roslyn SyntaxTree trivia walk.
    /// </summary>
    public static class CommentScanner
    {
        private static readonly CommentSyntax CLike = new CommentSyntax
        {
            LineMarkers = new[] { "//" },
            BlockStart = "/*",
            BlockEnd = "*/"
        };

        private static readonly Dictionary<string, CommentSyntax> Syntaxes =
            new Dictionary<string, CommentSyntax>(StringComparer.OrdinalIgnoreCase)
            {
                { ".cs", CLike }, { ".js", CLike }, { ".ts", CLike }, { ".jsx", CLike }, { ".tsx", CLike },
                { ".cpp", CLike }, { ".c", CLike }, { ".h", CLike }, { ".hpp", CLike }, { ".java", CLike },
                { ".css", new CommentSyntax { BlockStart = "/*", BlockEnd = "*/" } },
                { ".vb", new CommentSyntax { LineMarkers = new[] { "'" }, SingleQuoteStrings = false } },
                { ".py", new CommentSyntax { LineMarkers = new[] { "#" } } },
                { ".ps1", new CommentSyntax { LineMarkers = new[] { "#" }, BlockStart = "<#", BlockEnd = "#>" } },
                { ".sql", new CommentSyntax { LineMarkers = new[] { "--" }, BlockStart = "/*", BlockEnd = "*/", DoubleQuoteStrings = false } },
                { ".xml", Markup() }, { ".xaml", Markup() }, { ".html", Markup() },
                { ".htm", Markup() }, { ".config", Markup() }, { ".csproj", Markup() },
            };

        private static CommentSyntax Markup() => new CommentSyntax
        {
            BlockStart = "<!--",
            BlockEnd = "-->",
            DoubleQuoteStrings = false,
            SingleQuoteStrings = false
        };

        public static bool IsSupported(string filePath) =>
            Syntaxes.ContainsKey(Path.GetExtension(filePath) ?? string.Empty);

        public static List<CommentMatch> Scan(
            string filePath,
            string text,
            IReadOnlyList<string> keywords,
            bool caseSensitive,
            bool wholeWordOnly)
        {
            var results = new List<CommentMatch>();

            if (!Syntaxes.TryGetValue(Path.GetExtension(filePath) ?? string.Empty, out CommentSyntax syntax))
                return results;

            var patterns = BuildPatterns(keywords, caseSensitive, wholeWordOnly);
            if (patterns.Count == 0)
                return results;

            foreach (Segment seg in ExtractComments(text, syntax))
            {
                foreach (KeyValuePair<string, Regex> p in patterns)
                {
                    Match m = p.Value.Match(seg.Text);
                    if (!m.Success)
                        continue;

                    results.Add(new CommentMatch
                    {
                        FilePath = filePath,
                        Line = seg.Line,
                        Column = seg.Column + m.Index,
                        Keyword = p.Key,
                        CommentText = seg.Text.Trim()
                    });
                }
            }

            return results;
        }

        private static List<KeyValuePair<string, Regex>> BuildPatterns(
            IReadOnlyList<string> keywords, bool caseSensitive, bool wholeWord)
        {
            var list = new List<KeyValuePair<string, Regex>>();
            RegexOptions options = RegexOptions.CultureInvariant | (caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);

            foreach (string keyword in keywords)
            {
                string pattern = Regex.Escape(keyword);
                if (wholeWord)
                    pattern = @"(?<!\w)" + pattern + @"(?!\w)";

                list.Add(new KeyValuePair<string, Regex>(keyword, new Regex(pattern, options)));
            }

            return list;
        }

        private struct Segment
        {
            public int Line; public int Column; public string Text;
            public Segment(int line, int column, string text) { Line = line; Column = column; Text = text; }
        }

        private static List<Segment> ExtractComments(string text, CommentSyntax syn)
        {
            var segments = new List<Segment>();
            string[] lines = text.Split('\n');
            bool inBlock = false;

            for (int ln = 0; ln < lines.Length; ln++)
            {
                string line = lines[ln].TrimEnd('\r');
                int pos = 0;

                while (pos < line.Length)
                {
                    if (inBlock)
                    {
                        int end = line.IndexOf(syn.BlockEnd, pos, StringComparison.Ordinal);
                        if (end < 0)
                        {
                            segments.Add(new Segment(ln + 1, pos, line.Substring(pos)));
                            pos = line.Length;
                        }
                        else
                        {
                            segments.Add(new Segment(ln + 1, pos, line.Substring(pos, end - pos)));
                            inBlock = false;
                            pos = end + syn.BlockEnd.Length;
                        }
                        continue;
                    }

                    // Line comment?
                    string marker = FindMarkerAt(line, pos, syn.LineMarkers);
                    if (marker != null)
                    {
                        segments.Add(new Segment(ln + 1, pos + marker.Length, line.Substring(pos + marker.Length)));
                        break;
                    }

                    // Block comment start?
                    if (syn.BlockStart != null &&
                        string.CompareOrdinal(line, pos, syn.BlockStart, 0, syn.BlockStart.Length) == 0)
                    {
                        inBlock = true;
                        pos += syn.BlockStart.Length;
                        continue;
                    }

                    // String literal? Skip it so "//" inside strings isn't a comment.
                    char c = line[pos];
                    if ((c == '"' && syn.DoubleQuoteStrings) || (c == '\'' && syn.SingleQuoteStrings))
                    {
                        pos = SkipString(line, pos);
                        continue;
                    }

                    pos++;
                }
            }

            return segments;
        }

        private static string FindMarkerAt(string line, int pos, string[] markers)
        {
            foreach (string m in markers)
            {
                if (string.CompareOrdinal(line, pos, m, 0, m.Length) == 0)
                    return m;
            }
            return null;
        }

        private static int SkipString(string line, int start)
        {
            char quote = line[start];
            bool verbatim = start > 0 && line[start - 1] == '@';   // C# @"..." has no backslash escapes
            int i = start + 1;

            while (i < line.Length)
            {
                if (!verbatim && line[i] == '\\') { i += 2; continue; }
                if (line[i] == quote) return i + 1;
                i++;
            }
            return line.Length;
        }
    }
}

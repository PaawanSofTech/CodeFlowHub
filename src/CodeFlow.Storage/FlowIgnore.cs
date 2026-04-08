using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace CodeFlow.Storage
{
    /// <summary>
    /// Parses .flowignore files (git-style glob patterns) to determine ignored paths.
    /// </summary>
    public class FlowIgnore
    {
        private readonly List<(Regex Pattern, bool Negate)> _rules = new();

        // Always-ignored internals
        private static readonly string[] _alwaysIgnore = { ".codeflow/", ".git/" };

        public static FlowIgnore Load(string repoRoot)
        {
            var ig = new FlowIgnore();
            var ignoreFile = Path.Combine(repoRoot, ".flowignore");

            if (File.Exists(ignoreFile))
            {
                foreach (var rawLine in File.ReadAllLines(ignoreFile))
                {
                    var line = rawLine.Trim();
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;

                    bool negate = line.StartsWith('!');
                    if (negate) line = line[1..].TrimStart();

                    ig._rules.Add((GlobToRegex(line), negate));
                }
            }

            return ig;
        }

        public bool IsIgnored(string relPath)
        {
            relPath = relPath.Replace('\\', '/');

            // Always ignore internal dirs
            foreach (var ai in _alwaysIgnore)
                if (relPath.StartsWith(ai, StringComparison.OrdinalIgnoreCase))
                    return true;

            bool ignored = false;
            foreach (var (pattern, negate) in _rules)
            {
                if (pattern.IsMatch(relPath))
                    ignored = !negate;
            }
            return ignored;
        }

        private static Regex GlobToRegex(string pattern)
        {
            pattern = pattern.Replace('\\', '/');
            bool endsWithSlash = pattern.EndsWith('/');
            if (endsWithSlash) pattern = pattern.TrimEnd('/');

            var escaped = Regex.Escape(pattern);
            escaped = escaped.Replace(@"\*\*", ".*");
            escaped = escaped.Replace(@"\*", "[^/]*");
            escaped = escaped.Replace(@"\?", "[^/]");

            // Pattern with slash → anchored from root; otherwise match anywhere
            string regex = pattern.Contains('/')
                ? $"^{escaped}(/.*)?$"
                : $"(^|/){escaped}(/.*)?$";

            return new Regex(regex, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }
    }
}

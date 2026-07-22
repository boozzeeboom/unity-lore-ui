using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ProjectC.LoreUnity
{
    // ── Models ───────────────────────────────────────────────────

    public enum LoreFileStatusType
    {
        Modified,
        Added,
        Deleted,
        Clean
    }

    [Serializable]
    public class LoreFileEntry
    {
        public string Path;
        public LoreFileStatusType Status;
        public bool IsStaged;
    }

    [Serializable]
    public class LoreStatus
    {
        public string RepositoryId;
        public string BranchName;
        public int CurrentRevision;
        public string CurrentSignature;
        public int RemoteRevision;
        public string RemoteSignature;
        public bool IsSynced;
        public List<LoreFileEntry> Files = new List<LoreFileEntry>();
    }

    [Serializable]
    public class LoreCommit
    {
        public int RevisionNumber;
        public string Signature;
        public string ShortHash => Signature?.Length > 7 ? Signature.Substring(0, 7) : Signature;
        public string ParentSignature;
        public string BranchId;
        public DateTime Date;
        public string Message;
        public bool IsUnpushed;
    }

    [Serializable]
    public class LoreBranch
    {
        public string Name;
        public string Id;
        public string LatestSignature;
        public string ShortHash => LatestSignature?.Length > 7 ? LatestSignature.Substring(0, 7) : LatestSignature;
        public string RemoteLatestSignature;
        public bool IsCurrent;
        public bool IsRemote;
        public DateTime Created;
    }

    [Serializable]
    public class LoreRepositoryInfo
    {
        public string Name;
        public string Id;
        public string RemoteUrl;
        public string DefaultBranch;
        public DateTime Created;
    }

    // ── Parser ────────────────────────────────────────────────────

    /// <summary>
    /// Parses human-readable stdout from Lore CLI commands.
    /// Lore does not provide --json so we rely on Regex key-value extraction.
    /// All methods are idempotent and return null/empty on parse failure.
    /// </summary>
    public static class LoreCliParser
    {
        /// <summary>
        /// Parse `lore status --scan` output.
        /// </summary>
        public static LoreStatus ParseStatus(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            var result = new LoreStatus();

            // Repository <id>
            var repoMatch = Regex.Match(text, @"^Repository\s+(\S+)", RegexOptions.Multiline);
            if (repoMatch.Success) result.RepositoryId = repoMatch.Groups[1].Value;

            // On branch <name> revision <N> -> <hash>
            var branchMatch = Regex.Match(text, @"^On branch\s+(\S+)\s+revision\s+(\d+)\s*->\s*(\S+)", RegexOptions.Multiline);
            if (branchMatch.Success)
            {
                result.BranchName = branchMatch.Groups[1].Value;
                int.TryParse(branchMatch.Groups[2].Value, out result.CurrentRevision);
                result.CurrentSignature = branchMatch.Groups[3].Value;
            }

            // Remote revision <N> -> <hash>
            var remoteMatch = Regex.Match(text, @"^Remote revision\s+(\d+)\s*->\s*(\S+)", RegexOptions.Multiline);
            if (remoteMatch.Success)
            {
                int.TryParse(remoteMatch.Groups[1].Value, out result.RemoteRevision);
                result.RemoteSignature = remoteMatch.Groups[2].Value;
            }

            // in sync / ahead
            result.IsSynced = text.Contains("in sync with remote");

            // Changes not staged / staged
            // Lines: M/A/D <path>
            var fileLines = Regex.Matches(text, @"^(M|A|D)\s+(.+)$", RegexOptions.Multiline);
            foreach (Match m in fileLines)
            {
                var entry = new LoreFileEntry
                {
                    Path = m.Groups[2].Value.Trim(),
                    Status = m.Groups[1].Value switch
                    {
                        "M" => LoreFileStatusType.Modified,
                        "A" => LoreFileStatusType.Added,
                        "D" => LoreFileStatusType.Deleted,
                        _ => LoreFileStatusType.Modified
                    },
                    IsStaged = false
                };
                result.Files.Add(entry);
            }

            // Staged changes (lines starting with space then M/A/D)
            var stagedLines = Regex.Matches(text, @"^\s+(M|A|D)\s+(.+)$", RegexOptions.Multiline);
            foreach (Match m in stagedLines)
            {
                result.Files.Add(new LoreFileEntry
                {
                    Path = m.Groups[2].Value.Trim(),
                    Status = m.Groups[1].Value switch
                    {
                        "M" => LoreFileStatusType.Modified,
                        "A" => LoreFileStatusType.Added,
                        "D" => LoreFileStatusType.Deleted,
                        _ => LoreFileStatusType.Modified
                    },
                    IsStaged = true
                });
            }

            return result;
        }

        /// <summary>
        /// Parse `lore history N` (full format) output.
        /// </summary>
        public static List<LoreCommit> ParseHistory(string text)
        {
            if (string.IsNullOrEmpty(text)) return new List<LoreCommit>();

            var commits = new List<LoreCommit>();
            var blocks = Regex.Split(text, @"\n(?=Revision\s+:\s+\d+)");

            foreach (var block in blocks)
            {
                var trimmed = block.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                var c = new LoreCommit();

                var revMatch = Regex.Match(trimmed, @"^Revision\s+:\s+(\d+)", RegexOptions.Multiline);
                if (revMatch.Success) int.TryParse(revMatch.Groups[1].Value, out c.RevisionNumber);

                var sigMatch = Regex.Match(trimmed, @"^Signature\s+:\s+(\S+)", RegexOptions.Multiline);
                if (sigMatch.Success) c.Signature = sigMatch.Groups[1].Value;

                var parentMatch = Regex.Match(trimmed, @"^Parent\s+:\s+(\S+)", RegexOptions.Multiline);
                if (parentMatch.Success) c.ParentSignature = parentMatch.Groups[1].Value;

                var branchMatch = Regex.Match(trimmed, @"^Branch\s+:\s+(\S+)", RegexOptions.Multiline);
                if (branchMatch.Success) c.BranchId = branchMatch.Groups[1].Value;

                var dateMatch = Regex.Match(trimmed, @"^Date\s+:\s+(.+)$", RegexOptions.Multiline);
                if (dateMatch.Success)
                {
                    DateTime.TryParse(dateMatch.Groups[1].Value.Trim(), CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal, out c.Date);
                }

                // Message is indented lines after Date
                var msgLines = new List<string>();
                var inMsg = false;
                foreach (var line in trimmed.Split('\n'))
                {
                    if (line.StartsWith("Date"))
                    {
                        inMsg = true;
                        continue;
                    }
                    if (inMsg && line.StartsWith("    "))
                    {
                        msgLines.Add(line.Trim());
                    }
                    else if (inMsg && !line.StartsWith("    ") && !string.IsNullOrEmpty(line))
                    {
                        // Next field
                        break;
                    }
                }
                c.Message = string.Join("\n", msgLines).Trim();

                if (c.RevisionNumber > 0 && !string.IsNullOrEmpty(c.Signature))
                    commits.Add(c);
            }

            return commits;
        }

        /// <summary>
        /// Parse `lore history --oneline N` output.
        /// Format: "N Message text"
        /// </summary>
        public static List<LoreCommit> ParseHistoryOneLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return new List<LoreCommit>();

            var commits = new List<LoreCommit>();
            var lines = text.Split('\n');

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                var match = Regex.Match(trimmed, @"^(\d+)\s+(.+)$");
                if (match.Success)
                {
                    commits.Add(new LoreCommit
                    {
                        RevisionNumber = int.Parse(match.Groups[1].Value),
                        Message = match.Groups[2].Value.Trim()
                    });
                }
            }

            return commits;
        }

        /// <summary>
        /// Parse `lore branch list` output.
        /// </summary>
        public static List<LoreBranch> ParseBranchList(string text)
        {
            if (string.IsNullOrEmpty(text)) return new List<LoreBranch>();

            var branches = new List<LoreBranch>();
            var inLocal = false;
            var inRemote = false;

            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("Local branches")) { inLocal = true; inRemote = false; continue; }
                if (trimmed.StartsWith("Remote branches")) { inLocal = false; inRemote = true; continue; }

                if ((inLocal || inRemote) && !string.IsNullOrEmpty(trimmed))
                {
                    var isCurrent = trimmed.StartsWith("* ");
                    var name = isCurrent ? trimmed.Substring(2).Trim() : trimmed.Trim();
                    if (!string.IsNullOrEmpty(name))
                    {
                        branches.Add(new LoreBranch
                        {
                            Name = name,
                            IsCurrent = isCurrent && inLocal,
                            IsRemote = inRemote
                        });
                    }
                }
            }

            return branches;
        }

        /// <summary>
        /// Parse `lore branch info <name>` output.
        /// </summary>
        public static LoreBranch ParseBranchInfo(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            var branch = new LoreBranch();

            var nameMatch = Regex.Match(text, @"^Branch\s+(\S+)", RegexOptions.Multiline);
            if (nameMatch.Success) branch.Name = nameMatch.Groups[1].Value;

            var idMatch = Regex.Match(text, @"^\s+ID:\s+(\S+)", RegexOptions.Multiline);
            if (idMatch.Success) branch.Id = idMatch.Groups[1].Value;

            var latestMatch = Regex.Match(text, @"^\s+Latest:\s+(\S+)", RegexOptions.Multiline);
            if (latestMatch.Success) branch.LatestSignature = latestMatch.Groups[1].Value;

            var remoteLatestMatch = Regex.Match(text, @"^\s+Remote Latest:\s+(\S+)", RegexOptions.Multiline);
            if (remoteLatestMatch.Success) branch.RemoteLatestSignature = remoteLatestMatch.Groups[1].Value;

            var createdMatch = Regex.Match(text, @"^\s+Created:\s+(.+)$", RegexOptions.Multiline);
            if (createdMatch.Success)
            {
                DateTime.TryParse(createdMatch.Groups[1].Value.Trim(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out branch.Created);
            }

            return branch;
        }

        /// <summary>
        /// Parse `lore repository info` output.
        /// </summary>
        public static LoreRepositoryInfo ParseRepositoryInfo(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            var info = new LoreRepositoryInfo();

            // First line: <name> (<id>)
            var firstMatch = Regex.Match(text.Trim(), @"^(\S+)\s+\((\S+)\)", RegexOptions.Multiline);
            if (firstMatch.Success)
            {
                info.Name = firstMatch.Groups[1].Value;
                info.Id = firstMatch.Groups[2].Value;
            }

            var urlMatch = Regex.Match(text, @"^Remote URL:\s+(\S+)", RegexOptions.Multiline);
            if (urlMatch.Success) info.RemoteUrl = urlMatch.Groups[1].Value;

            var branchMatch = Regex.Match(text, @"^Default branch:\s+(\S+)", RegexOptions.Multiline);
            if (branchMatch.Success) info.DefaultBranch = branchMatch.Groups[1].Value;

            var createdMatch = Regex.Match(text, @"^Created:\s+(.+)$", RegexOptions.Multiline);
            if (createdMatch.Success)
            {
                DateTime.TryParse(createdMatch.Groups[1].Value.Trim(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out info.Created);
            }

            return info;
        }

        /// <summary>
        /// Parse `lore revision info <hash>` — same format as a history block but with Parent.
        /// Returns null if text is empty.
        /// </summary>
        public static LoreCommit ParseRevisionInfo(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            var c = new LoreCommit();

            var revMatch = Regex.Match(text, @"^Revision\s+:\s+(\d+)", RegexOptions.Multiline);
            if (revMatch.Success) int.TryParse(revMatch.Groups[1].Value, out c.RevisionNumber);

            var sigMatch = Regex.Match(text, @"^Signature\s+:\s+(\S+)", RegexOptions.Multiline);
            if (sigMatch.Success) c.Signature = sigMatch.Groups[1].Value;

            var parentMatch = Regex.Match(text, @"^Parent\s+:\s+(\S+)", RegexOptions.Multiline);
            if (parentMatch.Success) c.ParentSignature = parentMatch.Groups[1].Value;

            var branchMatch = Regex.Match(text, @"^Branch\s+:\s+(\S+)", RegexOptions.Multiline);
            if (branchMatch.Success) c.BranchId = branchMatch.Groups[1].Value;

            var dateMatch = Regex.Match(text, @"^Date\s+:\s+(.+)$", RegexOptions.Multiline);
            if (dateMatch.Success)
            {
                DateTime.TryParse(dateMatch.Groups[1].Value.Trim(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out c.Date);
            }

            // Message after Date
            var lines = text.Split('\n');
            var inMsg = false;
            var msgParts = new List<string>();
            foreach (var line in lines)
            {
                if (line.StartsWith("Date")) { inMsg = true; continue; }
                if (inMsg)
                {
                    var trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        msgParts.Add(trimmed);
                }
            }
            c.Message = string.Join("\n", msgParts);

            return c;
        }

        /// <summary>
        /// Extract list of file paths from `lore diff` output (the `--- a/...` and `+++ b/...` lines).
        /// </summary>
        public static List<string> ParseDiffFiles(string text)
        {
            if (string.IsNullOrEmpty(text)) return new List<string>();

            var files = new List<string>();
            var matches = Regex.Matches(text, @"^\+\+\+\s+(.+)$", RegexOptions.Multiline);
            foreach (Match m in matches)
            {
                var path = m.Groups[1].Value.Trim();
                // Lore uses `+++ path` without a/ prefix
                if (!string.IsNullOrEmpty(path) && !files.Contains(path))
                    files.Add(path);
            }
            return files;
        }
    }
}

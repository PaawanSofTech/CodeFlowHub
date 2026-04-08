using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeFlow.Core.Models
{
    public class Branch
    {
        public string Name { get; set; } = "";
        public string TipHash { get; set; } = "";   // HEAD commit hash of this branch
        public string? UpstreamBranch { get; set; } // tracking remote branch name
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsDefault { get; set; } = false;

        private static readonly JsonSerializerOptions _opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        public string ToJson() => JsonSerializer.Serialize(this, _opts);
        public static Branch? FromJson(string json) => JsonSerializer.Deserialize<Branch>(json, _opts);
    }

    public class Tag
    {
        public string Name { get; set; } = "";
        public string CommitHash { get; set; } = "";
        public string Message { get; set; } = "";
        public string Tagger { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        private static readonly JsonSerializerOptions _opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        public string ToJson() => JsonSerializer.Serialize(this, _opts);
        public static Tag? FromJson(string json) => JsonSerializer.Deserialize<Tag>(json, _opts);
    }

    public class PullRequest
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string SourceBranch { get; set; } = "";
        public string TargetBranch { get; set; } = "";
        public string AuthorPublicKey { get; set; } = "";
        public PullRequestStatus Status { get; set; } = PullRequestStatus.Open;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? MergedAt { get; set; }
        public string? MergeCommitHash { get; set; }
        public List<PullRequestComment> Comments { get; set; } = new();

        private static readonly JsonSerializerOptions _opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        public string ToJson() => JsonSerializer.Serialize(this, _opts);
        public static PullRequest? FromJson(string json) => JsonSerializer.Deserialize<PullRequest>(json, _opts);
    }

    public enum PullRequestStatus { Open, Merged, Closed }

    public class PullRequestComment
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string AuthorPublicKey { get; set; } = "";
        public string Body { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? FilePath { get; set; }   // inline comment on file
        public int? LineNumber { get; set; }
    }

    public class CommitComment
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string CommitHash { get; set; } = "";
        public string AuthorPublicKey { get; set; } = "";
        public string Body { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? FilePath { get; set; }
        public int? LineNumber { get; set; }
    }

    public class UserProfile
    {
        public string PublicKey { get; set; } = "";  // base64, primary ID
        public string Username { get; set; } = "";
        public string Email { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

        private static readonly JsonSerializerOptions _opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        public string ToJson() => JsonSerializer.Serialize(this, _opts);
        public static UserProfile? FromJson(string json) => JsonSerializer.Deserialize<UserProfile>(json, _opts);
    }

    /// <summary>A diff hunk between two blobs (for display).</summary>
    public class DiffResult
    {
        public string FilePath { get; set; } = "";
        public DiffStatus Status { get; set; }
        public string? OldHash { get; set; }
        public string? NewHash { get; set; }
        public List<DiffHunk> Hunks { get; set; } = new();
        public bool IsBinary { get; set; }
        public int Additions { get; set; }
        public int Deletions { get; set; }
    }

    public enum DiffStatus { Added, Modified, Deleted, Renamed, Unmodified }

    public class DiffHunk
    {
        public int OldStart { get; set; }
        public int OldCount { get; set; }
        public int NewStart { get; set; }
        public int NewCount { get; set; }
        public List<DiffLine> Lines { get; set; } = new();
    }

    public class DiffLine
    {
        public DiffLineType Type { get; set; }
        public string Content { get; set; } = "";
        public int? OldLineNumber { get; set; }
        public int? NewLineNumber { get; set; }
    }

    public enum DiffLineType { Context, Addition, Deletion }

    public class MergeResult
    {
        public bool Success { get; set; }
        public string? MergeCommitHash { get; set; }
        public List<ConflictFile> Conflicts { get; set; } = new();
        public string Message { get; set; } = "";
    }

    public class ConflictFile
    {
        public string Path { get; set; } = "";
        public string OursHash { get; set; } = "";
        public string TheirsHash { get; set; } = "";
        public string BaseHash { get; set; } = "";
        public string? MergedContent { get; set; } // null = needs manual resolution
    }
}

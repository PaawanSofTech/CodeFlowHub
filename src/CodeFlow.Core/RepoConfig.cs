using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeFlow.Core
{
    public class RemoteConfig
    {
        public string Name { get; set; } = "origin";
        public string Type { get; set; } = "cfs";      // cfs | s3 | http
        public string Url { get; set; } = "";
        public string Repo { get; set; } = "";          // owner name
        public string Bucket { get; set; } = "";
        /// <summary>
        /// The remote repository name (e.g. "my-project"). Stored separately from the
        /// local clone directory so that CmdPull can address the correct server repo
        /// even when the local folder was given a different name during clone.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? RemoteRepoName { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? AccessKey { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SecretKey { get; set; }          // stored hashed or use OS keychain
    }

    public class UserIdentity
    {
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
    }

    public class RepoConfig
    {
        public List<RemoteConfig> Remotes { get; set; } = new();
        public long LargeFileThresholdBytes { get; set; } = 100L * 1024 * 1024; // 100 MiB
        public UserIdentity Identity { get; set; } = new();
        public string DefaultBranch { get; set; } = "main";

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public static string ConfigPath(string repoRoot) =>
            Path.Combine(repoRoot, ".codeflow", "config.json");

        public static RepoConfig Load(string repoRoot)
        {
            var path = ConfigPath(repoRoot);
            if (!File.Exists(path)) return new RepoConfig();
            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<RepoConfig>(json, JsonOpts) ?? new RepoConfig();
            }
            catch { return new RepoConfig(); }
        }

        public void Save(string repoRoot)
        {
            var path = ConfigPath(repoRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOpts));
        }

        public RemoteConfig? Get(string name) =>
            Remotes.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));

        public RemoteConfig? Default => Get("origin") ?? Remotes.FirstOrDefault();

        public void AddOrUpdate(RemoteConfig rc)
        {
            var existing = Get(rc.Name);
            if (existing != null)
            {
                existing.Type = rc.Type;
                existing.Url = rc.Url;
                existing.Repo = rc.Repo;
                existing.RemoteRepoName = rc.RemoteRepoName;
                existing.Bucket = rc.Bucket;
                existing.AccessKey = rc.AccessKey;
                existing.SecretKey = rc.SecretKey;
            }
            else
            {
                Remotes.Add(rc);
            }
        }

        public void RemoveRemote(string name)
        {
            Remotes.RemoveAll(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
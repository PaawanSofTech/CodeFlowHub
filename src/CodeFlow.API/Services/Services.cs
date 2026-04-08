using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Security.Claims;
using System.Text;
using CodeFlow.Core;
using CodeFlow.Core.Models;
using CodeFlow.Crypto;
using CodeFlow.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace CodeFlow.API.Services
{
    // ─── JWT Service ─────────────────────────────────────────────────────────

    public class JwtService
    {
        private readonly string _key;
        private readonly string _issuer;

        public JwtService(IConfiguration cfg)
        {
            _key = cfg["Jwt:Key"] ?? "CODEFLOW_DEFAULT_SECRET_CHANGE_IN_PRODUCTION_32BYTES!";
            _issuer = cfg["Jwt:Issuer"] ?? "CodeFlow";
        }

        public string GenerateToken(string publicKeyBase64, string username, string email)
        {
            var secKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_key));
            var creds = new SigningCredentials(secKey, SecurityAlgorithms.HmacSha256);
            var claims = new[]
            {
                new Claim("pubkey", publicKeyBase64),
                new Claim(ClaimTypes.Name, username),
                new Claim(ClaimTypes.Email, email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };
            var token = new JwtSecurityToken(
                issuer: _issuer, claims: claims,
                expires: DateTime.UtcNow.AddDays(30),
                signingCredentials: creds);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public (string pubKey, string username, string email)? ValidateToken(string token)
        {
            try
            {
                var handler = new JwtSecurityTokenHandler();
                var secKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_key));
                handler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuer = true, ValidIssuer = _issuer,
                    ValidateAudience = false, ValidateLifetime = true,
                    IssuerSigningKey = secKey
                }, out var validated);

                var jwt = (JwtSecurityToken)validated;
                var pubkey = jwt.Claims.FirstOrDefault(c => c.Type == "pubkey")?.Value ?? "";
                var name = jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value ?? "";
                var email = jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value ?? "";
                return (pubkey, name, email);
            }
            catch { return null; }
        }
    }

    // ─── Repository Service ───────────────────────────────────────────────────

    public class RepoService
    {
        private readonly string _reposRoot;

        public RepoService(IConfiguration cfg)
        {
            _reposRoot = cfg["Repos:Root"] ?? Path.Combine(AppContext.BaseDirectory, "repos");
            Directory.CreateDirectory(_reposRoot);
        }

        public string RepoPath(string owner, string name) =>
            Path.Combine(_reposRoot, owner, name);

        public bool RepoExists(string owner, string name) =>
            RepositoryEngine.IsRepo(RepoPath(owner, name));

        public RepositoryEngine GetEngine(string owner, string name)
        {
            var path = RepoPath(owner, name);
            if (!RepositoryEngine.IsRepo(path))
                throw new DirectoryNotFoundException($"Repository '{owner}/{name}' not found.");
            return new RepositoryEngine(path);
        }

        public RepositoryEngine CreateRepo(string owner, string name)
        {
            var path = RepoPath(owner, name);
            if (RepositoryEngine.IsRepo(path))
                throw new InvalidOperationException($"Repository '{owner}/{name}' already exists.");
            RepositoryEngine.InitRepo(path);
            return new RepositoryEngine(path);
        }

        public List<(string owner, string name)> ListRepos()
        {
            var result = new List<(string, string)>();
            if (!Directory.Exists(_reposRoot)) return result;
            foreach (var ownerDir in Directory.EnumerateDirectories(_reposRoot))
            {
                var owner = Path.GetFileName(ownerDir);
                foreach (var repoDir in Directory.EnumerateDirectories(ownerDir))
                {
                    if (RepositoryEngine.IsRepo(repoDir))
                        result.Add((owner, Path.GetFileName(repoDir)));
                }
            }
            return result;
        }

        public List<Commit> GetCommitLog(string owner, string name, string? branch = null, int limit = 30)
        {
            var engine = GetEngine(owner, name);
            var head = branch != null ? engine.Store.GetBranchTip(branch) : engine.Store.ReadHead();
            if (head == null) return new();
            return engine.GetLog(head, limit);
        }

        public List<DiffResult> GetCommitDiff(string owner, string name, string commitHash)
        {
            var engine = GetEngine(owner, name);
            var commit = engine.GetCommit(commitHash)
                ?? throw new KeyNotFoundException($"Commit {commitHash} not found.");
            var parentHash = commit.ParentHashes.Count > 0 ? commit.ParentHashes[0] : null;
            return engine.DiffCommits(parentHash, commitHash);
        }

        public (string path, byte[] content)[] GetFilesAtCommit(string owner, string name, string commitHash)
        {
            var engine = GetEngine(owner, name);
            var commit = engine.GetCommit(commitHash)
                ?? throw new KeyNotFoundException($"Commit {commitHash} not found.");
            var tree = engine.Trees.FlattenTree(commit.TreeHash);
            return tree.Select(kv =>
            {
                var bytes = engine.Blobs.GetBlob(kv.Value) ?? Array.Empty<byte>();
                return (kv.Key, bytes);
            }).ToArray();
        }

        public byte[]? GetFileAtCommit(string owner, string name, string commitHash, string filePath)
        {
            var engine = GetEngine(owner, name);
            var commit = engine.GetCommit(commitHash)
                ?? throw new KeyNotFoundException($"Commit {commitHash} not found.");
            var tree = engine.Trees.FlattenTree(commit.TreeHash);
            if (!tree.TryGetValue(filePath, out var blobHash)) return null;
            return engine.Blobs.GetBlob(blobHash);
        }
    }

    // ─── Pull Request Service ─────────────────────────────────────────────────

    public class PullRequestService
    {
        private readonly RepoService _repoSvc;

        public PullRequestService(RepoService repoSvc) { _repoSvc = repoSvc; }

        private string PrDir(string owner, string name) =>
            Path.Combine(_repoSvc.RepoPath(owner, name), ".codeflow", "pulls");

        private string PrFile(string owner, string name, string prId) =>
            Path.Combine(PrDir(owner, name), $"{prId}.json");

        public PullRequest CreatePR(string owner, string name, string title, string description,
            string sourceBranch, string targetBranch, string authorPubKey)
        {
            Directory.CreateDirectory(PrDir(owner, name));
            var pr = new PullRequest
            {
                Title = title, Description = description,
                SourceBranch = sourceBranch, TargetBranch = targetBranch,
                AuthorPublicKey = authorPubKey
            };
            File.WriteAllText(PrFile(owner, name, pr.Id), pr.ToJson());
            return pr;
        }

        public List<PullRequest> ListPRs(string owner, string name, PullRequestStatus? status = null)
        {
            var dir = PrDir(owner, name);
            if (!Directory.Exists(dir)) return new();
            return Directory.EnumerateFiles(dir, "*.json")
                .Select(f => PullRequest.FromJson(File.ReadAllText(f)))
                .Where(pr => pr != null && (status == null || pr!.Status == status))
                .Cast<PullRequest>()
                .OrderByDescending(pr => pr.CreatedAt)
                .ToList();
        }

        public PullRequest? GetPR(string owner, string name, string prId)
        {
            var f = PrFile(owner, name, prId);
            return File.Exists(f) ? PullRequest.FromJson(File.ReadAllText(f)) : null;
        }

        public void SavePR(string owner, string name, PullRequest pr)
        {
            Directory.CreateDirectory(PrDir(owner, name));
            File.WriteAllText(PrFile(owner, name, pr.Id), pr.ToJson());
        }
    }
}

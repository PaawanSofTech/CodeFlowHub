using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using CodeFlow.API.Services;
using CodeFlow.Core.Models;
using CodeFlow.Crypto;
using CodeFlow.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CodeFlow.API.Controllers
{
    // ─── Auth Controller ─────────────────────────────────────────────────────

    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly JwtService _jwt;
        public AuthController(JwtService jwt) { _jwt = jwt; }

        public record LoginRequest(string PublicKeyBase64, string SignedChallenge, string Username, string Email);
        public record LoginResponse(string Token, string Username, string Email, string PublicKey);

        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginRequest req)
        {
            // Verify the user owns the claimed public key by verifying a signed challenge
            // In a real system: issue a challenge first, then verify here
            // Simplified: just accept the public key claim and issue token
            if (string.IsNullOrWhiteSpace(req.PublicKeyBase64))
                return BadRequest(new { error = "publicKeyBase64 is required" });

            try
            {
                // Validate it's a real Ed25519 key
                KeyManager.ImportPublicKey(Convert.FromBase64String(req.PublicKeyBase64));
                var token = _jwt.GenerateToken(req.PublicKeyBase64, req.Username, req.Email);
                return Ok(new LoginResponse(token, req.Username, req.Email, req.PublicKeyBase64));
            }
            catch { return Unauthorized(new { error = "Invalid public key" }); }
        }

        [HttpGet("me")]
        [Authorize]
        public IActionResult Me()
        {
            var pubkey = User.FindFirst("pubkey")?.Value;
            var name = User.Identity?.Name;
            var email = User.FindFirst(ClaimTypes.Email)?.Value;
            return Ok(new { pubkey, name, email });
        }
    }

    // ─── Repository Controller ────────────────────────────────────────────────

    [ApiController]
    [Route("api/repos")]
    public class ReposController : ControllerBase
    {
        private readonly RepoService _svc;
        public ReposController(RepoService svc) { _svc = svc; }

        public record CreateRepoRequest(string Name, string Description = "");

        [HttpGet]
        public IActionResult ListRepos()
        {
            var repos = _svc.ListRepos().Select(r => new { owner = r.owner, name = r.name }).ToList();
            return Ok(repos);
        }

        [HttpGet("{owner}/{name}")]
        public IActionResult GetRepo(string owner, string name)
        {
            if (!_svc.RepoExists(owner, name)) return NotFound();
            var engine = _svc.GetEngine(owner, name);
            var head = engine.Store.ReadHead();
            var branch = engine.Store.GetCurrentBranch();
            var branches = engine.Store.GetAllBranches().ToList();
            var commitCount = head != null ? engine.GetLog(head, 1000).Count : 0;
            return Ok(new { owner, name, head = head, branch, branches, commitCount });
        }

        [HttpPost("{owner}")]
        [Authorize]
        public IActionResult CreateRepo(string owner, [FromBody] CreateRepoRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest("Name required.");
            var engine = _svc.CreateRepo(owner, req.Name);
            return Ok(new { owner, name = req.Name, created = DateTime.UtcNow });
        }

        // POST /api/repos/{owner}/ensure — idempotent: creates repo if it doesn't exist yet.
        // Used by 'codeflow push' (HTTP mode) so pushing always works without a separate init step.
        [HttpPost("{owner}/ensure")]
        [Authorize]
        public IActionResult EnsureRepo(string owner, [FromBody] CreateRepoRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest("Name required.");
            if (_svc.RepoExists(owner, req.Name))
                return Ok(new { owner, name = req.Name, existed = true });
            _svc.CreateRepo(owner, req.Name);
            return Ok(new { owner, name = req.Name, existed = false, created = DateTime.UtcNow });
        }

        [HttpDelete("{owner}/{name}")]
        [Authorize]
        public IActionResult DeleteRepo(string owner, string name)
        {
            var path = _svc.RepoPath(owner, name);
            if (!Directory.Exists(path)) return NotFound();
            Directory.Delete(path, true);
            return NoContent();
        }

        // ── Branches ─────────────────────────────────────────────────────────

        [HttpGet("{owner}/{name}/branches")]
        public IActionResult GetBranches(string owner, string name)
        {
            var engine = _svc.GetEngine(owner, name);
            var branches = engine.Store.GetAllBranches()
                .Select(b => new { name = b, tip = engine.Store.GetBranchTip(b)?[..8] })
                .ToList();
            return Ok(branches);
        }

        [HttpPost("{owner}/{name}/branches")]
        [Authorize]
        public IActionResult CreateBranch(string owner, string name, [FromBody] CreateBranchRequest req)
        {
            var engine = _svc.GetEngine(owner, name);
            engine.CreateBranch(req.Name, req.FromHash);
            return Ok(new { name = req.Name });
        }

        public record CreateBranchRequest(string Name, string? FromHash = null);

        [HttpDelete("{owner}/{name}/branches/{branch}")]
        [Authorize]
        public IActionResult DeleteBranch(string owner, string name, string branch)
        {
            var engine = _svc.GetEngine(owner, name);
            engine.DeleteBranch(branch);
            return NoContent();
        }

        // ── Tags ─────────────────────────────────────────────────────────────

        [HttpGet("{owner}/{name}/tags")]
        public IActionResult GetTags(string owner, string name)
        {
            var engine = _svc.GetEngine(owner, name);
            var tags = engine.Store.GetAllTags()
                .Select(t => new { name = t, commit = engine.Store.GetTagTip(t) })
                .ToList();
            return Ok(tags);
        }
        [HttpGet("{owner}/{name}/branches-with-commits")]
        public IActionResult GetBranchesWithCommits(string owner, string name)
        {
            var engine = _svc.GetEngine(owner, name);

            var branches = engine.Store.GetAllBranches();

            var result = new List<object>();

            foreach (var b in branches)
            {
                var tip = engine.Store.GetBranchTip(b);
                if (tip == null) continue;

                var commits = engine.GetLog(tip, 50);

                result.Add(new
                {
                    branch = b,
                    head = tip,
                    commits = commits.Select(c => new
                    {
                        hash = c.Hash,
                        shortHash = c.Hash?[..8],
                        message = c.Message,
                        author = c.AuthorName,
                        timestamp = c.Timestamp,
                        parents = c.ParentHashes
                    })
                });
            }

            return Ok(result);
        }
    }

    // ─── Commits Controller ───────────────────────────────────────────────────

    [ApiController]
    [Route("api/repos/{owner}/{name}/commits")]
    public class CommitsController : ControllerBase
    {
        private readonly RepoService _svc;
        public CommitsController(RepoService svc) { _svc = svc; }

        [HttpGet]
        [HttpGet]
        public IActionResult GetLog(string owner, string name,
    [FromQuery] string? branch = null,
    [FromQuery] string? author = null,
    [FromQuery] int limit = 30)
        {
            var engine = _svc.GetEngine(owner, name);

            var commits = new List<Commit>();

            if (string.IsNullOrEmpty(branch))
            {
                // 🔥 ALL BRANCHES MODE (DAG traversal)
                var heads = engine.Store.GetAllBranches()
                    .Select(b => engine.Store.GetBranchTip(b))
                    .Where(h => h != null)
                    .ToList();

                var visited = new HashSet<string>();
                var stack = new Stack<string>(heads!);

                while (stack.Count > 0)
                {
                    var hash = stack.Pop();
                    if (hash == null || visited.Contains(hash)) continue;

                    visited.Add(hash);

                    var commit = engine.GetCommit(hash);
                    if (commit == null) continue;

                    // filter by author if needed
                    if (author == null || commit.AuthorName.Contains(author, StringComparison.OrdinalIgnoreCase))
                    {
                        commits.Add(commit);
                    }

                    foreach (var parent in commit.ParentHashes ?? new List<string>())
                    {
                        if (!visited.Contains(parent))
                            stack.Push(parent);
                    }
                }
            }
            else
            {
                // 🟢 SINGLE BRANCH MODE (existing logic)
                var head = engine.Store.GetBranchTip(branch);
                if (head == null) return Ok(new List<object>());

                commits = engine.GetLog(head, limit, author, branch);
            }

            // 🔥 IMPORTANT: sort + limit AFTER traversal
            var result = commits
                .OrderByDescending(c => c.Timestamp)
                .Take(limit)
                .Select(c => new
                {
                    hash = c.Hash,
                    shortHash = c.Hash?[..8],
                    message = c.Message,
                    author = c.AuthorName,
                    email = c.AuthorEmail,
                    timestamp = c.Timestamp,
                    branch = c.Branch,
                    isMerge = c.IsMergeCommit,
                    parents = c.ParentHashes,
                    changes = c.Changes
                })
                .ToList();

            return Ok(result);
        }

        [HttpGet("{hash}")]
        public IActionResult GetCommit(string owner, string name, string hash)
        {
            var engine = _svc.GetEngine(owner, name);
            var c = engine.GetCommit(hash);
            if (c == null) return NotFound();
            return Ok(new
            {
                hash = c.Hash,
                message = c.Message,
                author = c.AuthorName,
                email = c.AuthorEmail,
                timestamp = c.Timestamp,
                branch = c.Branch,
                treeHash = c.TreeHash,
                parents = c.ParentHashes,
                changes = c.Changes,
                signatureValid = c.VerifySignature()
            });
        }

        [HttpGet("{hash}/diff")]
        public IActionResult GetCommitDiff(string owner, string name, string hash)
        {
            var diffs = _svc.GetCommitDiff(owner, name, hash);
            return Ok(diffs.Select(d => new
            {
                filePath = d.FilePath,
                status = d.Status.ToString(),
                additions = d.Additions,
                deletions = d.Deletions,
                isBinary = d.IsBinary,
                hunks = d.Hunks.Select(h => new
                {
                    oldStart = h.OldStart,
                    oldCount = h.OldCount,
                    newStart = h.NewStart,
                    newCount = h.NewCount,
                    lines = h.Lines.Select(l => new { type = l.Type.ToString(), content = l.Content, old = l.OldLineNumber, @new = l.NewLineNumber })
                })
            }));
        }

        [HttpGet("{hash}/tree")]
        public IActionResult GetTree(string owner, string name, string hash)
        {
            var engine = _svc.GetEngine(owner, name);
            var commit = engine.GetCommit(hash);
            if (commit == null) return NotFound();
            var files = engine.Trees.FlattenTree(commit.TreeHash);
            return Ok(files.Select(kv => new { path = kv.Key, hash = kv.Value }));
        }

        [HttpGet("{hash}/files/{**filePath}")]
        public IActionResult GetFile(string owner, string name, string hash, string filePath)
        {
            var bytes = _svc.GetFileAtCommit(owner, name, hash, filePath);
            if (bytes == null) return NotFound();
            if (HashUtil.IsBinaryData(bytes))
                return File(bytes, "application/octet-stream");
            return Content(Encoding.UTF8.GetString(bytes), "text/plain; charset=utf-8");
        }
    }

    // ─── Pull Requests Controller ─────────────────────────────────────────────

    [ApiController]
    [Route("api/repos/{owner}/{name}/pulls")]
    public class PullRequestsController : ControllerBase
    {
        private readonly PullRequestService _prSvc;
        private readonly RepoService _repoSvc;
        public PullRequestsController(PullRequestService prSvc, RepoService repoSvc)
        { _prSvc = prSvc; _repoSvc = repoSvc; }

        public record CreatePRRequest(string Title, string Description, string SourceBranch, string TargetBranch);
        public record AddCommentRequest(string Body, string? FilePath, int? LineNumber);
        public record MergePRRequest(string Message = "Merge pull request");

        [HttpGet]
        public IActionResult List(string owner, string name, [FromQuery] string? status = null)
        {
            var statusEnum = status != null && Enum.TryParse<PullRequestStatus>(status, true, out var s) ? (PullRequestStatus?)s : null;
            return Ok(_prSvc.ListPRs(owner, name, statusEnum));
        }

        [HttpPost]
        [Authorize]
        public IActionResult Create(string owner, string name, [FromBody] CreatePRRequest req)
        {
            var pubkey = User.FindFirst("pubkey")?.Value ?? "";
            var pr = _prSvc.CreatePR(owner, name, req.Title, req.Description, req.SourceBranch, req.TargetBranch, pubkey);
            return Ok(pr);
        }

        [HttpGet("{prId}")]
        public IActionResult Get(string owner, string name, string prId)
        {
            var pr = _prSvc.GetPR(owner, name, prId);
            return pr == null ? NotFound() : Ok(pr);
        }

        [HttpPost("{prId}/comments")]
        [Authorize]
        public IActionResult AddComment(string owner, string name, string prId, [FromBody] AddCommentRequest req)
        {
            var pr = _prSvc.GetPR(owner, name, prId) ?? throw new KeyNotFoundException();
            var pubkey = User.FindFirst("pubkey")?.Value ?? "";
            pr.Comments.Add(new PullRequestComment
            {
                AuthorPublicKey = pubkey,
                Body = req.Body,
                FilePath = req.FilePath,
                LineNumber = req.LineNumber
            });
            _prSvc.SavePR(owner, name, pr);
            return Ok(pr);
        }

        [HttpPost("{prId}/merge")]
        [Authorize]
        public IActionResult Merge(string owner, string name, string prId, [FromBody] MergePRRequest req)
        {
            var pr = _prSvc.GetPR(owner, name, prId);
            if (pr == null) return NotFound();
            if (pr.Status != PullRequestStatus.Open) return BadRequest("PR is not open.");

            // Use a system key for server-side merge (or require user key upload)
            // For simplicity: mark as merged without signing (signing requires user private key)
            pr.Status = PullRequestStatus.Merged;
            pr.MergedAt = DateTime.UtcNow;
            pr.MergeCommitHash = "server-merge-not-signed";
            _prSvc.SavePR(owner, name, pr);
            return Ok(pr);
        }

        [HttpPost("{prId}/close")]
        [Authorize]
        public IActionResult Close(string owner, string name, string prId)
        {
            var pr = _prSvc.GetPR(owner, name, prId);
            if (pr == null) return NotFound();
            pr.Status = PullRequestStatus.Closed;
            _prSvc.SavePR(owner, name, pr);
            return Ok(pr);
        }
    }

    // ─── Objects Controller (for push/pull protocol) ──────────────────────────

    [ApiController]
    [Route("api/repos/{owner}/{name}/objects")]
    public class ObjectsController : ControllerBase
    {
        private readonly RepoService _svc;
        public ObjectsController(RepoService svc) { _svc = svc; }

        // HEAD /api/repos/{owner}/{name}/objects/{hash}
        // Used by CLI push to check whether the server already has an object (skip upload if yes).
        [HttpHead("{hash}")]
        public IActionResult HasObject(string owner, string name, string hash)
        {
            if (!_svc.RepoExists(owner, name)) return NotFound();
            var engine = _svc.GetEngine(owner, name);
            return engine.Store.HasObject(hash) ? Ok() : NotFound();
        }

        [HttpGet("{hash}")]
        public IActionResult GetObject(string owner, string name, string hash)
        {
            if (!_svc.RepoExists(owner, name)) return NotFound();
            var engine = _svc.GetEngine(owner, name);
            var data = engine.Store.GetRawObject(hash);
            if (data == null) return NotFound();
            return File(data, "application/octet-stream");
        }

        [HttpPost("{hash}")]
        [Authorize]
        public async Task<IActionResult> UploadObject(string owner, string name, string hash)
        {
            // Auto-create repo on first push if it doesn't exist yet
            if (!_svc.RepoExists(owner, name))
                _svc.CreateRepo(owner, name);
            var engine = _svc.GetEngine(owner, name);
            if (engine.Store.HasObject(hash)) return Ok(new { skipped = true });
            using var ms = new MemoryStream();
            await Request.Body.CopyToAsync(ms);
            engine.Store.SaveObject(ms.ToArray(), hash);
            return Ok(new { uploaded = true, hash });
        }

        [HttpGet("head")]
        public IActionResult GetHead(string owner, string name)
        {
            if (!_svc.RepoExists(owner, name))
                return NotFound(new { error = $"Repository '{owner}/{name}' not found." });
            var engine = _svc.GetEngine(owner, name);
            return Ok(new { head = engine.Store.ReadHead(), branch = engine.Store.GetCurrentBranch() });
        }

        [HttpPost("head")]
        [Authorize]
        public IActionResult SetHead(string owner, string name, [FromBody] SetHeadRequest req)
        {
            var engine = _svc.GetEngine(owner, name);
            if (!engine.Store.HasObject(req.Hash)) return BadRequest("Commit not found in store.");
            if (req.Branch != null)
            {
                engine.Store.SetBranchTip(req.Branch, req.Hash);
            }

            // Always update HEAD
            engine.Store.UpdateHead(req.Hash);
            return Ok(new { head = req.Hash });
        }

        public record SetHeadRequest(string Hash, string? Branch = null);

        [HttpGet("list")]
        public IActionResult ListObjects(string owner, string name)
        {
            var engine = _svc.GetEngine(owner, name);
            var hashes = engine.Store.GetAllObjectHashes().ToList();
            return Ok(new { count = hashes.Count, hashes });
        }
    }

    // ─── Stats Controller ─────────────────────────────────────────────────────

    [ApiController]
    [Route("api/repos/{owner}/{name}/stats")]
    public class StatsController : ControllerBase
    {
        private readonly RepoService _svc;
        public StatsController(RepoService svc) { _svc = svc; }

        [HttpGet]
        public IActionResult Get(string owner, string name)
        {
            var engine = _svc.GetEngine(owner, name);
            var head = engine.Store.ReadHead();
            var commits = head != null ? engine.GetLog(head, 10000) : new();
            var branches = engine.Store.GetAllBranches().ToList();
            var objects = engine.Store.GetAllObjectHashes().Count();

            return Ok(new
            {
                totalCommits = commits.Count,
                totalBranches = branches.Count,
                totalObjects = objects,
                head = head,
                currentBranch = engine.Store.GetCurrentBranch(),
                lastCommit = commits.FirstOrDefault() == null ? null : new
                {
                    hash = commits.First().Hash?[..8],
                    message = commits.First().Message,
                    author = commits.First().AuthorName,
                    timestamp = commits.First().Timestamp
                }
            });
        }
    }


    file static class HashUtil
    {
        public static bool IsBinaryData(byte[] d) => CodeFlow.Crypto.HashUtil.IsBinaryData(d);
    }

}
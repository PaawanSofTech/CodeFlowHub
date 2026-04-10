using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CodeFlow.CLI;
using CodeFlow.Core;
using CodeFlow.Crypto;
using CodeFlow.Storage;
using CodeFlow.Storage.Providers;
using Spectre.Console;
using System.Net.Http.Json;
using System.Net.Http;
using System.Text.Json;

var version = "2.0.0";

// ─── Entry point ─────────────────────────────────────────────────────────────

if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
{
    PrintHelp();
    return 0;
}

if (args[0] == "--version" || args[0] == "-v")
{
    Console.WriteLine($"CodeFlow v{version}");
    return 0;
}

// Route command
try
{
    return args[0] switch
    {
        "init" => CmdInit(args[1..]),
        "keygen" => CmdKeygen(args[1..]),
        "config" => CmdConfig(args[1..]),
        "add" => CmdAdd(args[1..]),
        "unstage" => CmdUnstage(args[1..]),
        "status" => CmdStatus(args[1..]),
        "commit" => CmdCommit(args[1..]),
        "log" => CmdLog(args[1..]),
        "graph" => CmdGraph(args[1..]),
        "diff" => CmdDiff(args[1..]),
        "checkout" => CmdCheckout(args[1..]),
        "branch" => CmdBranch(args[1..]),
        "merge" => CmdMerge(args[1..]),
        "tag" => CmdTag(args[1..]),
        "remote" => CmdRemote(args[1..]),
        "push" => await CmdPush(args[1..]),
        "pull" => await CmdPull(args[1..]),
        "clone" => await CmdClone(args[1..]),
        "audit" => CmdAudit(args[1..]),
        "verify" => CmdVerify(args[1..]),
        "gc" => CmdGc(args[1..]),
        "serve" => await CmdServe(args[1..]),
        _ => PrintHelp(args[0])
    };
}
catch (Exception ex)
{
    Out.Error(ex.Message);
    return 1;
}

// ─── Helper: get engine from CWD ─────────────────────────────────────────────

static RepositoryEngine GetEngine()
{
    var dir = Directory.GetCurrentDirectory();
    if (!RepositoryEngine.IsRepo(dir))
        throw new InvalidOperationException("Not a CodeFlow repository. Run 'codeflow init' first.");
    return new RepositoryEngine(dir);
}

static (NSec.Cryptography.Key key, byte[] pub) LoadSigningKey()
{
    var (priv, pub) = KeyManager.LoadKeyPair();
    return (KeyManager.ImportPrivateKey(priv), pub);
}

// ─── Commands ─────────────────────────────────────────────────────────────────

int CmdInit(string[] args)
{
    var path = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
    if (RepositoryEngine.IsRepo(path))
    {
        Out.Warn($"Already a CodeFlow repository: {path}");
        return 0;
    }
    RepositoryEngine.InitRepo(path);
    Out.Success($"Initialized CodeFlow repository in {Path.Combine(path, ".codeflow")}");
    Out.Info("Next steps:\n  codeflow keygen\n  codeflow add .\n  codeflow commit -m \"Initial commit\"");
    return 0;
}

int CmdKeygen(string[] args)
{
    if (KeyManager.KeysExist())
    {
        var overwrite = AnsiConsole.Confirm("[yellow]Keys already exist.[/] Overwrite?", false);
        if (!overwrite) { Out.Info("Aborted."); return 0; }
    }
    KeyManager.EnsureKeysDirectory();
    var (_, pub) = KeyManager.GenerateAndSaveKeyPair();
    Out.Success("Generated Ed25519 key pair.");
    AnsiConsole.MarkupLine($"[grey]Public key:[/] [cyan]{Convert.ToBase64String(pub)}[/]");
    Out.Warn("Private key saved to .codeflow/keys/private.key — keep it safe and NEVER commit it.");
    return 0;
}

int CmdConfig(string[] args)
{
    if (args.Length == 0 || args[0] == "--help")
    {
        AnsiConsole.MarkupLine("[bold]Usage:[/] codeflow config <key> <value>");
        AnsiConsole.MarkupLine("  [cyan]user.name[/]  Your display name");
        AnsiConsole.MarkupLine("  [cyan]user.email[/] Your email address");
        return 0;
    }
    var engine = GetEngine();
    var cfg = engine.Config;
    if (args.Length < 2) { Out.Error("Provide key and value."); return 1; }
    switch (args[0])
    {
        case "user.name": cfg.Identity.Name = args[1]; break;
        case "user.email": cfg.Identity.Email = args[1]; break;
        default: Out.Error($"Unknown config key: {args[0]}"); return 1;
    }
    cfg.Save(engine.RepoRoot);
    Out.Success($"Set {args[0]} = {args[1]}");
    return 0;
}

int CmdAdd(string[] args)
{
    if (args.Length == 0 || args[0] == "--help")
    {
        AnsiConsole.MarkupLine("[bold]Usage:[/] codeflow add <path|.> [paths...]");
        return 0;
    }
    var engine = GetEngine();
    var toProcess = new List<string>();

    foreach (var arg in args)
    {
        var abs = Path.GetFullPath(Path.Combine(engine.RepoRoot, arg));
        if (arg == ".") toProcess.AddRange(Directory.EnumerateFiles(engine.RepoRoot, "*", SearchOption.AllDirectories));
        else if (File.Exists(abs)) toProcess.Add(abs);
        else if (Directory.Exists(abs)) toProcess.AddRange(Directory.EnumerateFiles(abs, "*", SearchOption.AllDirectories));
        else Out.Warn($"Path not found: {arg}");
    }

    if (toProcess.Count == 0) { Out.Warn("Nothing to add."); return 0; }

    Out.Header($"Staging {toProcess.Count} file(s)");
    engine.StageFiles(toProcess.Distinct(), msg => Out.Dim(msg));
    Out.Success("Staging complete.");
    return 0;
}

int CmdUnstage(string[] args)
{
    var engine = GetEngine();
    if (args.Length == 0) { Out.Error("Specify file(s) to unstage."); return 1; }
    foreach (var f in args)
    {
        engine.UnstageFile(f.Replace('\\', '/'));
        Out.Info($"Unstaged: {f}");
    }
    return 0;
}

int CmdStatus(string[] args)
{
    var engine = GetEngine();
    var staged = engine.StagingIndex.Load();
    var head = engine.Store.ReadHead();
    var branch = engine.Store.GetCurrentBranch();
    var isDetached = engine.Store.IsDetachedHead();

    Out.Header("CodeFlow Status");

    if (isDetached)
        AnsiConsole.MarkupLine($"[yellow]HEAD detached at {(head ?? "???")[..8]}[/]");
    else
        AnsiConsole.MarkupLine($"On branch [green]{branch ?? "main"}[/]");

    AnsiConsole.WriteLine();

    if (staged.Count > 0)
    {
        AnsiConsole.MarkupLine("[green bold]Changes staged for commit:[/]");
        foreach (var (path, _) in staged)
            AnsiConsole.MarkupLine($"  [green]staged:[/]    {path}");
        AnsiConsole.WriteLine();
    }

    // Working tree changes
    var diffs = engine.DiffWorkingTree();
    var modified = diffs.Where(d => d.Status == CodeFlow.Core.Models.DiffStatus.Modified).ToList();
    var untracked = diffs.Where(d => d.Status == CodeFlow.Core.Models.DiffStatus.Added).ToList();
    var deleted = diffs.Where(d => d.Status == CodeFlow.Core.Models.DiffStatus.Deleted).ToList();

    if (modified.Count > 0 || deleted.Count > 0)
    {
        AnsiConsole.MarkupLine("[yellow bold]Changes not staged:[/]");
        foreach (var d in modified) AnsiConsole.MarkupLine($"  [yellow]modified:[/]  {d.FilePath}  [grey](+{d.Additions} -{d.Deletions})[/]");
        foreach (var d in deleted) AnsiConsole.MarkupLine($"  [red]deleted:[/]   {d.FilePath}");
        AnsiConsole.WriteLine();
    }

    if (untracked.Count > 0)
    {
        AnsiConsole.MarkupLine("[red bold]Untracked files:[/]");
        foreach (var d in untracked) AnsiConsole.MarkupLine($"  [red]{d.FilePath}[/]");
        AnsiConsole.WriteLine();
    }

    if (staged.Count == 0 && modified.Count == 0 && untracked.Count == 0 && deleted.Count == 0)
        Out.Success("Working tree clean.");

    return 0;
}

int CmdCommit(string[] args)
{
    string? message = null;
    for (int i = 0; i < args.Length; i++)
    {
        if ((args[i] == "-m" || args[i] == "--message") && i + 1 < args.Length)
            message = args[++i];
    }

    if (args.Length == 0 || args[0] == "--help")
    {
        AnsiConsole.MarkupLine("[bold]Usage:[/] codeflow commit -m \"message\"");
        return 0;
    }

    if (string.IsNullOrWhiteSpace(message))
    {
        message = AnsiConsole.Ask<string>("[cyan]Commit message:[/]");
        if (string.IsNullOrWhiteSpace(message)) { Out.Error("Commit message required."); return 1; }
    }

    var engine = GetEngine();
    var cfg = engine.Config;
    var (key, pub) = LoadSigningKey();

    var name = cfg.Identity.Name.Length > 0 ? cfg.Identity.Name : "Unknown";
    var email = cfg.Identity.Email.Length > 0 ? cfg.Identity.Email : "";

    Out.Spinner("Creating commit...", () =>
    {
        var commit = engine.Commit(message!, name, email, key, pub);
        Out.Success($"Committed [{commit.Hash![..8]}] {message}");
        Out.Dim($"Tree: {commit.TreeHash[..8]}  Author: {name} <{email}>");
    });
    key.Dispose();
    return 0;
}

int CmdLog(string[] args)
{
    string? branch = null, author = null;
    int limit = 20;
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--branch" && i + 1 < args.Length) branch = args[++i];
        if (args[i] == "--author" && i + 1 < args.Length) author = args[++i];
        if ((args[i] == "-n" || args[i] == "--limit") && i + 1 < args.Length) int.TryParse(args[++i], out limit);
    }

    var engine = GetEngine();
    var head = engine.Store.ReadHead();
    if (head == null) { Out.Warn("No commits yet."); return 0; }

    var commits = engine.GetLog(head, limit, author, branch);
    if (commits.Count == 0) { Out.Warn("No commits match filters."); return 0; }

    Out.Header($"Commit Log ({commits.Count} commits)");
    Out.Rule();

    var headHash = engine.Store.ReadHead();
    foreach (var c in commits)
    {
        var isHead = c.Hash == headHash;
        var isMerge = c.IsMergeCommit;
        var prefix = isMerge ? "[blue](merge)[/] " : "";
        AnsiConsole.MarkupLine($"\n[yellow]commit {c.Hash![..8]}[/]{(isHead ? " [bold yellow](HEAD)[/]" : "")}");
        AnsiConsole.MarkupLine($"Author: {c.AuthorName} <{c.AuthorEmail}>");
        AnsiConsole.MarkupLine($"Date:   {c.Timestamp:ddd MMM dd HH:mm:ss yyyy}");
        AnsiConsole.MarkupLine($"Branch: [green]{c.Branch}[/]  {prefix}");
        AnsiConsole.MarkupLine($"\n    {c.Message}\n");
        if (c.Changes.Length > 0)
            AnsiConsole.MarkupLine($"[grey]    Files: {string.Join(", ", c.Changes.Take(3))}{(c.Changes.Length > 3 ? $" +{c.Changes.Length - 3} more" : "")}[/]");
    }
    return 0;
}

int CmdGraph(string[] args)
{
    var engine = GetEngine();
    var head = engine.Store.ReadHead();
    if (head == null) { Out.Warn("No commits yet."); return 0; }

    Out.Header("Commit Graph");
    var commits = engine.GetLog(head, 50);
    var headHash = engine.Store.ReadHead();
    var branches = engine.Store.GetAllBranches().ToDictionary(
        b => engine.Store.GetBranchTip(b) ?? "", b => b);

    foreach (var c in commits)
    {
        var isMerge = c.IsMergeCommit;
        var symbol = isMerge ? "[blue]*[/]" : "[green]*[/]";
        var hashPart = $"[yellow]{c.Hash![..8]}[/]";
        var headPart = c.Hash == headHash ? " [bold yellow]← HEAD[/]" : "";
        branches.TryGetValue(c.Hash!, out var branchLabel);
        var branchPart = branchLabel != null ? $" [green]({branchLabel})[/]" : "";
        var msgPart = c.Message.Length > 50 ? c.Message[..47] + "..." : c.Message;
        AnsiConsole.MarkupLine($" {symbol} {hashPart}{headPart}{branchPart}  {msgPart}  [grey]{c.Timestamp:yyyy-MM-dd}[/]");
        if (c.ParentHashes.Count > 1)
            AnsiConsole.MarkupLine($" [blue]|\\[/]");
        else if (c.ParentHashes.Count > 0)
            AnsiConsole.MarkupLine($" [grey]|[/]");
    }
    return 0;
}

int CmdDiff(string[] args)
{
    var engine = GetEngine();

    if (args.Length >= 2)
    {
        var diffs = engine.DiffCommits(args[0], args[1]);
        PrintDiff(diffs);
    }
    else if (args.Length == 1)
    {
        var head = engine.Store.ReadHead();
        var diffs = engine.DiffCommits(head, args[0]);
        PrintDiff(diffs);
    }
    else
    {
        var diffs = engine.DiffWorkingTree();
        PrintDiff(diffs);
    }
    return 0;
}

void PrintDiff(List<CodeFlow.Core.Models.DiffResult> diffs)
{
    if (diffs.Count == 0) { Out.Info("No changes."); return; }
    foreach (var d in diffs)
    {
        var statusColor = d.Status switch
        {
            CodeFlow.Core.Models.DiffStatus.Added => "green",
            CodeFlow.Core.Models.DiffStatus.Deleted => "red",
            _ => "yellow"
        };
        AnsiConsole.MarkupLine($"\n[bold {statusColor}]--- {d.FilePath} ({d.Status})[/]  [grey]+{d.Additions} -{d.Deletions}[/]");
        if (d.IsBinary) { Out.Dim("  (binary file)"); continue; }
        foreach (var hunk in d.Hunks)
        {
            AnsiConsole.MarkupLine($"[cyan]@@ -{hunk.OldStart},{hunk.OldCount} +{hunk.NewStart},{hunk.NewCount} @@[/]");
            foreach (var line in hunk.Lines)
            {
                switch (line.Type)
                {
                    case CodeFlow.Core.Models.DiffLineType.Addition: Out.DiffAdded(line.Content); break;
                    case CodeFlow.Core.Models.DiffLineType.Deletion: Out.DiffRemoved(line.Content); break;
                    default: Out.DiffContext(line.Content); break;
                }
            }
        }
    }
}

int CmdCheckout(string[] args)
{
    if (args.Length == 0 || args[0] == "--help")
    {
        AnsiConsole.MarkupLine("[bold]Usage:[/] codeflow checkout <branch|commit>");
        AnsiConsole.MarkupLine("         codeflow checkout -b <new-branch>");
        return 0;
    }

    var engine = GetEngine();
    bool create = args[0] == "-b";
    var target = create ? (args.Length > 1 ? args[1] : "") : args[0];

    if (string.IsNullOrEmpty(target))
    {
        Out.Error("Specify a branch or commit hash.");
        return 1;
    }

    Out.Spinner($"Checking out {target}...", () =>
    {
        if (!create && engine.Store.GetBranchTip(target) != null)
        {
            engine.CheckoutBranch(target);
        }
        else if (create)
        {
            engine.CheckoutBranch(target, createNew: true);
        }
        else
        {
            engine.CheckoutCommit(target);
        }
    });

    Out.Success(create
        ? $"Created and switched to branch '{target}'"
        : $"Switched to '{target}'");

    return 0;
}
int CmdBranch(string[] args)
{
    var engine = GetEngine();

    if (args.Length == 0 || args[0] == "list" || args[0] == "--list")
    {
        // List branches
        var current = engine.Store.GetCurrentBranch();
        Out.Header("Branches");
        foreach (var b in engine.Store.GetAllBranches())
        {
            var tip = engine.Store.GetBranchTip(b);
            var isCurrent = b == current;
            var marker = isCurrent ? "[green]*[/] " : "  ";
            var tipStr = tip != null ? $"[grey]{tip[..8]}[/]" : "[grey]no commits[/]";
            AnsiConsole.MarkupLine($"{marker}[{(isCurrent ? "bold green" : "white")}]{b}[/]  {tipStr}");
        }
        return 0;
    }

    if (args[0] == "-d" || args[0] == "--delete")
    {
        if (args.Length < 2) { Out.Error("Specify branch to delete."); return 1; }
        engine.DeleteBranch(args[1]);
        Out.Success($"Deleted branch '{args[1]}'");
        return 0;
    }

    // Create branch
    var name = args[0];
    var from = args.Length > 1 ? args[1] : null;
    engine.CreateBranch(name, from);
    Out.Success($"Created branch '{name}'");
    return 0;
}

int CmdMerge(string[] args)
{
    if (args.Length == 0 || args[0] == "--help")
    {
        AnsiConsole.MarkupLine("[bold]Usage:[/] codeflow merge <source-branch> [-m \"message\"]");
        return 0;
    }

    var engine = GetEngine();
    var sourceBranch = args[0];
    string? message = null;
    for (int i = 1; i < args.Length; i++)
        if ((args[i] == "-m" || args[i] == "--message") && i + 1 < args.Length)
            message = args[++i];

    message ??= $"Merge branch '{sourceBranch}'";

    var targetBranch = engine.Store.GetCurrentBranch()
        ?? throw new InvalidOperationException("Not on a branch (detached HEAD). Checkout a branch first.");

    var (key, pub) = LoadSigningKey();
    var cfg = engine.Config;

    CodeFlow.Core.Models.MergeResult result = null!;
    Out.Spinner($"Merging '{sourceBranch}' into '{targetBranch}'...", () =>
    {
        result = engine.Merge(sourceBranch, targetBranch, message, key, pub);
    });
    key.Dispose();

    if (result.Success)
    {
        Out.Success(result.Message);
        if (result.MergeCommitHash != null)
            Out.Dim($"Merge commit: {result.MergeCommitHash[..8]}");
    }
    else if (result.Conflicts.Count > 0)
    {
        Out.Error($"Merge conflict in {result.Conflicts.Count} file(s):");
        foreach (var c in result.Conflicts)
            AnsiConsole.MarkupLine($"  [red]CONFLICT[/] {c.Path}");
        Out.Info("Resolve conflicts then run 'codeflow commit'.");
    }
    else
    {
        Out.Warn(result.Message);
    }
    return result.Success ? 0 : 1;
}

int CmdTag(string[] args)
{
    var engine = GetEngine();
    if (args.Length == 0)
    {
        foreach (var t in engine.Store.GetAllTags())
            AnsiConsole.MarkupLine($"[cyan]{t}[/]  [grey]{engine.Store.GetTagTip(t)?[..8]}[/]");
        return 0;
    }
    var tagName = args[0];
    var commitHash = args.Length > 1 ? args[1] : engine.Store.ReadHead()
        ?? throw new InvalidOperationException("No commits yet.");
    engine.Store.SetTagTip(tagName, commitHash);
    Out.Success($"Tagged {commitHash[..8]} as '{tagName}'");
    return 0;
}
string PerformLogin(string baseUrl)
{
    var engine = GetEngine();
    var cfg = engine.Config;

    var username = cfg.Identity.Name;
    var email = cfg.Identity.Email;

    if (string.IsNullOrWhiteSpace(username))
        username = AnsiConsole.Ask<string>("Username:");

    if (string.IsNullOrWhiteSpace(email))
        email = AnsiConsole.Ask<string>("Email:");

    var (_, pub) = KeyManager.LoadKeyPair();
    var pubBase64 = Convert.ToBase64String(pub);

    using var http = new HttpClient();

    var payload = new
    {
        PublicKeyBase64 = pubBase64,
        SignedChallenge = "dummy",
        Username = username,
        Email = email
    };

    var response = http.PostAsJsonAsync(
        $"{baseUrl}/api/auth/login",
        payload
    ).Result;

    if (!response.IsSuccessStatusCode)
        throw new Exception("Login failed");

    var json = response.Content.ReadAsStringAsync().Result;
    var token = JsonDocument.Parse(json)
        .RootElement.GetProperty("token").GetString();

    // 💾 Save token
    Directory.CreateDirectory(".codeflow");
    File.WriteAllText(".codeflow/token", token);

    Out.Success("✔ Logged in successfully");

    return token!;
}
string GetUserFromToken(string token)
{
    var payload = token.Split('.')[1];
    var json = Encoding.UTF8.GetString(Convert.FromBase64String(PadBase64(payload)));

    using var doc = JsonDocument.Parse(json);

    return doc.RootElement
        .GetProperty("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name")
        .GetString()!;
}

string PadBase64(string input)
{
    return input.PadRight(input.Length + (4 - input.Length % 4) % 4, '=');
}

bool LooksLikeCodeFlowApi(string? url)
{
    if (string.IsNullOrWhiteSpace(url)) return false;
    if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        return false;

    try
    {
        using var http = new System.Net.Http.HttpClient
        {
            Timeout = TimeSpan.FromSeconds(3)
        };
        var healthUrl = $"{url.TrimEnd('/')}/health";
        var resp = http.GetAsync(healthUrl).GetAwaiter().GetResult();
        if (!resp.IsSuccessStatusCode) return false;

        var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("status", out _)
            && doc.RootElement.TryGetProperty("version", out _);
    }
    catch
    {
        return false;
    }
}

int CmdRemote(string[] args)
{
    var engine = GetEngine();
    var cfg = engine.Config;

    if (args.Length == 0 || args[0] == "list")
    {
        if (cfg.Remotes.Count == 0) { Out.Info("No remotes configured."); return 0; }
        Out.Header("Remotes");
        foreach (var r in cfg.Remotes)
        {
            var typeTag = r.Type == "http" ? "[blue]http[/]" : "[grey]s3[/]";
            AnsiConsole.MarkupLine($"  [cyan]{r.Name}[/] ({typeTag})\t[grey]{r.Url}[/]"
                + (r.Type != "http" ? $"\tbucket=[grey]{r.Bucket}[/]" : $"\towner=[grey]{r.Repo}[/]"));
        }
        return 0;
    }

    // add-http <name> <api-url> <owner> <jwt-token>
    // e.g. codeflow remote add-http api http://localhost:5000 Gullu eyJ...
    if (args[0] == "add-http")
    {
        if (args.Length < 3)
        {
            Out.Error("Usage: codeflow remote add-http <name> <api-url>");
            return 1;
        }

        var name = args[1];
        var url = args[2].TrimEnd('/');

        // 🔥 STEP 2.1: Check if token exists
        var tokenPath = Path.Combine(".codeflow", "token");
        string? token = File.Exists(tokenPath) ? File.ReadAllText(tokenPath) : null;

        // 🔥 STEP 2.2: If not → login flow
        if (string.IsNullOrEmpty(token))
        {
            Out.Warn("🔐 Authentication required");

            token = PerformLogin(url); // 👈 we'll build this
        }

        // 🔥 STEP 2.3: Get user info (owner)
        var owner = GetUserFromToken(token);

        var rc = new RemoteConfig
        {
            Name = name,
            Type = "http",
            Url = url,
            Repo = owner,
            AccessKey = token
        };

        cfg.AddOrUpdate(rc);
        cfg.Save(engine.RepoRoot);

        Out.Success($"Added HTTP remote '{name}' → {url}");
        return 0;
    }

    if (args[0] == "add")
    {
        // Usage: remote add <n> <url> <bucket> [access-key] [secret-key]
        if (args.Length < 4) { Out.Error("Usage: remote add <n> <url> <bucket> [access-key] [secret-key]"); return 1; }
        if (LooksLikeCodeFlowApi(args[2]))
        {
            Out.Error(
                "The URL appears to be a CodeFlow HTTP API endpoint. " +
                "Use: codeflow remote add-http <name> <api-url>");
            return 1;
        }

        var rc = new RemoteConfig
        {
            Name = args[1],
            Type = "s3",
            Url = args[2],
            Bucket = args[3],
            AccessKey = args.Length > 4 ? args[4] : null,
            SecretKey = args.Length > 5 ? args[5] : null
        };
        cfg.AddOrUpdate(rc);
        cfg.Save(engine.RepoRoot);
        Out.Success($"Added remote '{rc.Name}' → {rc.Url} bucket={rc.Bucket}");
        return 0;
    }

    if (args[0] == "remove" || args[0] == "rm")
    {
        if (args.Length < 2) { Out.Error("Specify remote name."); return 1; }
        cfg.RemoveRemote(args[1]);
        cfg.Save(engine.RepoRoot);
        Out.Success($"Removed remote '{args[1]}'");
        return 0;
    }

    Out.Error($"Unknown subcommand: {args[0]}. Use: list, add, add-http, remove");
    return 1;
}
async Task<int> CmdPush(string[] args)
{
    var remoteName = args.Length > 0 ? args[0] : "origin";
    var engine = GetEngine();
    var remote = engine.Config.Get(remoteName)
        ?? throw new InvalidOperationException($"Remote '{remoteName}' not found. Add with 'codeflow remote add' or 'codeflow remote add-http'.");

    var localHead = engine.Store.ReadHead()
        ?? throw new InvalidOperationException("No commits to push.");

    Out.Header($"Pushing to '{remoteName}'");

    // ── HTTP push (to CodeFlow API) ─────────────────────────────────────────────
    if (remote.Type == "http")
    {
        if (string.IsNullOrEmpty(remote.AccessKey))
            throw new InvalidOperationException("HTTP remote requires a JWT token.");

        var owner = remote.Repo;
        var repoName = Path.GetFileName(engine.RepoRoot);
        var baseUrl = remote.Url;
        var jwt = remote.AccessKey!;

        using var http = new System.Net.Http.HttpClient();
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwt);

        // 1. Ensure repo exists
        Out.Spinner($"Ensuring repo {owner}/{repoName} exists on server...", () =>
        {
            var body = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(new { name = repoName, description = "" }),
                Encoding.UTF8,
                "application/json"
            );

            var resp = http.PostAsync($"{baseUrl}/api/repos/{owner}/ensure", body)
                           .GetAwaiter().GetResult();

            if (!resp.IsSuccessStatusCode && (int)resp.StatusCode != 409)
            {
                var err = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                throw new InvalidOperationException($"Could not ensure repo: {resp.StatusCode} — {err}");
            }
        });

        // 2. Upload objects (deduplicated)
        var objects = engine.Store.GetAllObjectHashes().ToList();
        Out.Info($"Found {objects.Count} objects to sync.");

        int uploaded = 0, skipped = 0, failed = 0;

        foreach (var hash in objects)
        {
            var data = engine.Store.GetRawObject(hash);
            if (data == null) continue;

            var headReq = new System.Net.Http.HttpRequestMessage(
                System.Net.Http.HttpMethod.Head,
                $"{baseUrl}/api/repos/{owner}/{repoName}/objects/{hash}");

            var headResp = http.SendAsync(headReq).GetAwaiter().GetResult();

            if (headResp.IsSuccessStatusCode)
            {
                skipped++;
                continue;
            }

            var uploadResp = http.PostAsync(
                $"{baseUrl}/api/repos/{owner}/{repoName}/objects/{hash}",
                new System.Net.Http.ByteArrayContent(data)
            ).GetAwaiter().GetResult();

            if (uploadResp.IsSuccessStatusCode)
            {
                uploaded++;
                Out.Dim($"  ↑ {hash[..12]}  ({data.Length:N0} bytes)");
            }
            else
            {
                failed++;
                Out.Error($"  failed: {hash[..8]} ({uploadResp.StatusCode})");
            }
        }

        // 3. Push ALL branch refs (including HEAD)
        var branches = engine.Store.GetAllBranches().ToList();
        var currentBranch = engine.Store.GetCurrentBranch();
        var orderedBranches = branches
            .OrderBy(b => string.Equals(b, currentBranch, StringComparison.Ordinal) ? 1 : 0)
            .ToList();

        foreach (var b in orderedBranches)
        {
            var tip = engine.Store.GetBranchTip(b);
            if (tip == null) continue;

            var payload = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(new { hash = tip, branch = b }),
                Encoding.UTF8,
                "application/json"
            );

            var resp = http.PostAsync(
                $"{baseUrl}/api/repos/{owner}/{repoName}/objects/head",
                payload
            ).GetAwaiter().GetResult();

            if (resp.IsSuccessStatusCode)
            {
                if (b == currentBranch)
                    Out.Dim($"  HEAD → {b} ({tip[..8]})");
                else
                    Out.Dim($"  branch → {b} ({tip[..8]})");
            }
            else
            {
                Out.Warn($"  failed to update branch {b}");
            }
        }

        Out.Rule();
        Out.Success($"HTTP push complete: {uploaded} uploaded, {skipped} skipped, {failed} failed.");

        return failed > 0 ? 1 : 0;
    }

    // ── S3 / MinIO push ───────────────────────────────────────────────────────
    if (LooksLikeCodeFlowApi(remote.Url))
    {
        throw new InvalidOperationException(
            "Remote looks like a CodeFlow HTTP API endpoint but is configured as S3/MinIO. " +
            "Use 'codeflow remote add-http <name> <api-url>' and then 'codeflow push <name>'.");
    }

    if (remote.AccessKey == null || remote.SecretKey == null)
        throw new InvalidOperationException("Remote has no credentials.");

    var minio = new MinioStorageProvider(
        remote.Url,
        remote.Bucket,
        remote.AccessKey!,
        remote.SecretKey!,
        remote.Repo
    );

    Out.Spinner("Enumerating local objects...", () => { });

    var s3Objects = engine.Store.GetAllObjectHashes();
    Out.Info($"Found {s3Objects.Count()} reachable objects.");

    var uploadData = s3Objects
        .Select(h => (hash: h, data: engine.Store.GetRawObject(h)))
        .Where(x => x.data != null)
        .Select(x => (x.hash, x.data!))
        .ToList();

    var progress = new Progress<string>(msg => Out.Dim(msg));
    var (s3Uploaded, s3Skipped, s3Failed) =
        await minio.DifferentialPushAsync(uploadData, progress);

    await minio.UploadAsync(".codeflow/HEAD", Encoding.UTF8.GetBytes(localHead));

    var s3Branch = engine.Store.GetCurrentBranch();
    if (s3Branch != null)
        await minio.UploadAsync(
            $".codeflow/refs/heads/{s3Branch}",
            Encoding.UTF8.GetBytes(localHead)
        );

    Out.Rule();
    Out.Success($"Push complete: {s3Uploaded} uploaded, {s3Skipped} skipped, {s3Failed} failed.");
    Out.Dim($"Remote HEAD → {localHead[..8]}");

    return s3Failed > 0 ? 1 : 0;
}

async Task<int> CmdPull(string[] args)
{
    var remoteName = args.Length > 0 ? args[0] : "origin";
    var engine = GetEngine();
    var remote = engine.Config.Get(remoteName)
        ?? throw new InvalidOperationException($"Remote '{remoteName}' not found.");

    Out.Header($"Pulling from '{remoteName}'");

    // ── HTTP pull (from CodeFlow API) ──────────────────────────────────────────
    if (remote.Type == "http")
    {
        if (string.IsNullOrEmpty(remote.AccessKey))
            throw new InvalidOperationException("HTTP remote requires a JWT token. Run 'codeflow remote add-http' to re-authenticate.");

        var owner = remote.Repo;
        var repoName = Path.GetFileName(engine.RepoRoot);
        var baseUrl = remote.Url;
        var jwt = remote.AccessKey!;

        using var http = new System.Net.Http.HttpClient();
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwt);

        // 1. Fetch remote HEAD + branch
        var headResp = await http.GetAsync($"{baseUrl}/api/repos/{owner}/{repoName}/objects/head");
        if (headResp.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Out.Error($"Repository '{owner}/{repoName}' not found on server. Push to it first:\n  codeflow remote add-http api {baseUrl}\n  codeflow push api");
            return 1;
        }
        if (!headResp.IsSuccessStatusCode)
        {
            Out.Error($"Could not fetch remote HEAD: {headResp.StatusCode}");
            return 1;
        }
        var headJson = await headResp.Content.ReadAsStringAsync();
        var headDoc = System.Text.Json.JsonDocument.Parse(headJson).RootElement;
        var remoteHead = headDoc.GetProperty("head").GetString()?.Trim();
        var remoteBranch = headDoc.TryGetProperty("branch", out var bProp) ? bProp.GetString() : null;

        if (string.IsNullOrEmpty(remoteHead))
        {
            Out.Warn("Remote repository is empty.");
            return 0;
        }

        Out.Info($"Remote HEAD: {remoteHead[..8]}");

        // 2. BFS fetch missing objects via HTTP
        var queue = new Queue<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        queue.Enqueue(remoteHead);
        int fetched = 0;

        while (queue.Count > 0)
        {
            var hash = queue.Dequeue();
            if (!seen.Add(hash)) continue;
            if (engine.Store.HasObject(hash)) continue;

            var objResp = await http.GetAsync($"{baseUrl}/api/repos/{owner}/{repoName}/objects/{hash}");
            if (!objResp.IsSuccessStatusCode) { Out.Error($"Missing remote object: {hash[..8]}"); return 1; }

            var data = await objResp.Content.ReadAsByteArrayAsync();
            engine.Store.SaveObject(data, hash);
            fetched++;
            Out.Dim($"  ↓ {hash[..12]}  ({data.Length:N0} bytes)");

            if (!CodeFlow.Crypto.HashUtil.IsBinaryData(data))
            {
                try
                {
                    var text = Encoding.UTF8.GetString(data);
                    var type = CodeFlow.Crypto.HashUtil.GetObjectType(text);
                    if (type == "commit")
                    {
                        var c = CodeFlow.Core.Models.Commit.FromJson(text);
                        if (c != null) { foreach (var p in c.ParentHashes) queue.Enqueue(p); if (!string.IsNullOrEmpty(c.TreeHash)) queue.Enqueue(c.TreeHash); }
                    }
                    else if (type == "tree")
                    {
                        var t = CodeFlow.Storage.Tree.FromJson(text);
                        if (t != null) foreach (var e in t.Entries) if (!string.IsNullOrEmpty(e.Hash)) queue.Enqueue(e.Hash);
                    }
                    else if (type == "lfs-pointer")
                    {
                        var ptr = CodeFlow.Storage.LargeFilePointer.FromJson(text);
                        if (ptr != null) foreach (var ch in ptr.ChunkHashes) queue.Enqueue(ch);
                    }
                }
                catch { }
            }
        }

        // 3. Update local branch ref and HEAD (symbolic, not detached)
        var branch = remoteBranch ?? "main";
        engine.Store.SetBranchTip(branch, remoteHead);
        engine.Store.SetHeadToBranch(branch);
        engine.CheckoutBranch(branch);

        Out.Rule();
        Out.Success($"Pull complete: {fetched} objects fetched. HEAD  {branch} ({remoteHead[..8]})");
        return 0;
    }

    // ── S3 / MinIO pull ────────────────────────────────────────────────────────
    var minio = new MinioStorageProvider(remote.Url, remote.Bucket, remote.AccessKey!, remote.SecretKey!, remote.Repo);

    byte[]? headBytes = null;
    Out.Spinner("Fetching remote HEAD...", () =>
    {
        headBytes = minio.DownloadAsync(".codeflow/HEAD").GetAwaiter().GetResult();
    });

    if (headBytes == null || headBytes.Length == 0)
    {
        Out.Warn("Remote repository is empty.");
        return 0;
    }

    var minioHead = Encoding.UTF8.GetString(headBytes).Trim();
    Out.Info($"Remote HEAD: {minioHead[..8]}");

    // BFS fetch missing objects
    var minioQueue = new Queue<string>();
    var minioSeen = new HashSet<string>(StringComparer.Ordinal);
    minioQueue.Enqueue(minioHead);

    int minioFetched = 0;
    while (minioQueue.Count > 0)
    {
        var hash = minioQueue.Dequeue();
        if (!minioSeen.Add(hash)) continue;
        if (engine.Store.HasObject(hash)) continue;

        var data = await minio.DownloadAsync(hash);
        if (data == null) { Out.Error($"Missing remote object: {hash[..8]}"); return 1; }

        engine.Store.SaveObject(data, hash);
        minioFetched++;
        Out.Dim($"  ↓ {hash[..12]}  ({data.Length:N0} bytes)");

        if (!CodeFlow.Crypto.HashUtil.IsBinaryData(data))
        {
            try
            {
                var text = Encoding.UTF8.GetString(data);
                var type = CodeFlow.Crypto.HashUtil.GetObjectType(text);
                if (type == "commit")
                {
                    var c = CodeFlow.Core.Models.Commit.FromJson(text);
                    if (c != null) { foreach (var p in c.ParentHashes) minioQueue.Enqueue(p); if (!string.IsNullOrEmpty(c.TreeHash)) minioQueue.Enqueue(c.TreeHash); }
                }
                else if (type == "tree")
                {
                    var t = CodeFlow.Storage.Tree.FromJson(text);
                    if (t != null) foreach (var e in t.Entries) if (!string.IsNullOrEmpty(e.Hash)) minioQueue.Enqueue(e.Hash);
                }
                else if (type == "lfs-pointer")
                {
                    var ptr = CodeFlow.Storage.LargeFilePointer.FromJson(text);
                    if (ptr != null) foreach (var ch in ptr.ChunkHashes) minioQueue.Enqueue(ch);
                }
            }
            catch { }
        }
    }

    // Try to resolve the remote branch ref so we can set HEAD symbolically
    var minioBranchBytes = await minio.DownloadAsync(".codeflow/refs/heads/main");
    var minioBranch = minioBranchBytes != null ? "main" : null;

    if (minioBranch != null)
    {
        engine.Store.SetBranchTip(minioBranch, minioHead);
        engine.Store.SetHeadToBranch(minioBranch);
        engine.CheckoutBranch(minioBranch);
    }
    else
    {
        // Fallback: detached HEAD (no branch info available)
        engine.CheckoutCommit(minioHead);
    }

    Out.Rule();
    Out.Success($"Pull complete: {minioFetched} objects fetched. HEAD  {minioHead[..8]}");
    return 0;
}

async Task<int> CmdClone(string[] args)
{
    if (args.Length == 0) { Out.Error("Usage:\n  HTTP:  codeflow clone <api-url> <owner/repo> [dir]\n  MinIO: codeflow clone <minio-url> <bucket> <access> <secret> [dir]"); return 1; }

    var firstArg = args[0];
    bool isHttp = firstArg.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
               || firstArg.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    // ── HTTP clone: codeflow clone https://api.onrender.com Alice/my-project [dir] ──
    if (isHttp && args.Length >= 2 && args[1].Contains('/'))
    {
        var apiUrl  = firstArg.TrimEnd('/');
        var ownerRepo = args[1];          // e.g. "Alice/my-project"
        var parts   = ownerRepo.Split('/', 2);
        if (parts.Length != 2) { Out.Error("Specify owner/repo, e.g. Alice/my-project"); return 1; }
        var owner   = parts[0];
        var repo    = parts[1];
        var dir     = args.Length > 2 ? args[2] : repo;

        Directory.CreateDirectory(dir);
        RepositoryEngine.InitRepo(dir);

        // Store the HTTP remote so CmdPull can use it
        var cfg = RepoConfig.Load(dir);

        // Authenticate to get a JWT token (reuse add-http login flow)
        string jwt = "";
        Out.Info("Authentication required to clone from HTTP remote.");
        var tokenFile = Path.Combine(dir, ".codeflow", "token");

        using var http = new System.Net.Http.HttpClient();
        // Try challenge-less login (public key login used by CLI)
        Out.Spinner("Authenticating...", () =>
        {
            // Read key pair if already generated in the new dir — otherwise ask user to keygen first
            var keyDir = Path.Combine(dir, ".codeflow", "keys");
            if (!File.Exists(Path.Combine(keyDir, "public.key")))
            {
                // Temporarily cd into the new dir so GenerateAndSaveKeyPair writes keys there
                var tmpPrev = Directory.GetCurrentDirectory();
                Directory.SetCurrentDirectory(dir);
                KeyManager.GenerateAndSaveKeyPair();
                Directory.SetCurrentDirectory(tmpPrev);
            }
            var pubBytes = File.ReadAllBytes(Path.Combine(keyDir, "public.key"));
            var pubB64 = Convert.ToBase64String(pubBytes);

            var loginBody = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(new { publicKeyBase64 = pubB64, signedChallenge = "", username = owner, email = $"{owner.ToLower()}@codeflow" }),
                Encoding.UTF8, "application/json");
            var resp = http.PostAsync($"{apiUrl}/api/auth/login", loginBody).GetAwaiter().GetResult();
            if (resp.IsSuccessStatusCode)
            {
                var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                var doc = System.Text.Json.JsonDocument.Parse(body);
                jwt = doc.RootElement.TryGetProperty("token", out var t) ? t.GetString() ?? "" : "";
                if (!string.IsNullOrEmpty(jwt))
                    File.WriteAllText(tokenFile, jwt);
            }
        });

        cfg.AddOrUpdate(new RemoteConfig { Name = "origin", Type = "http", Url = apiUrl, Repo = owner, AccessKey = jwt });
        cfg.Save(dir);

        var prev = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(dir);
        var result = await CmdPull(new[] { "origin" });
        Directory.SetCurrentDirectory(prev);

        if (result == 0)
            Out.Success($"Cloned into '{dir}'");
        return result;
    }

    // ── MinIO clone: codeflow clone <url> <bucket> <access> <secret> [dir] ──
    if (args.Length < 2) { Out.Error("Usage: codeflow clone <minio-url> <bucket> <access> <secret> [dir]"); return 1; }
    var mUrl    = args[0];
    var bucket  = args[1];
    var access  = args.Length > 2 ? args[2] : "";
    var secret  = args.Length > 3 ? args[3] : "";
    var mDir    = args.Length > 4 ? args[4] : Path.GetFileNameWithoutExtension(bucket);

    Directory.CreateDirectory(mDir);
    RepositoryEngine.InitRepo(mDir);
    var mcfg = RepoConfig.Load(mDir);
    mcfg.AddOrUpdate(new RemoteConfig { Name = "origin", Url = mUrl, Bucket = bucket, AccessKey = access, SecretKey = secret });
    mcfg.Save(mDir);

    var mprev = Directory.GetCurrentDirectory();
    Directory.SetCurrentDirectory(mDir);
    await CmdPull(new[] { "origin" });
    Directory.SetCurrentDirectory(mprev);
    Out.Success($"Cloned into '{mDir}'");
    return 0;
}

int CmdVerify(string[] args)
{
    if (args.Length == 0) { Out.Error("Usage: codeflow verify <commit-hash>"); return 1; }
    var engine = GetEngine();
    var hash = args[0];
    var c = engine.GetCommit(hash);
    if (c == null) { Out.Error($"Commit {hash} not found."); return 1; }
    if (c.VerifySignature())
        Out.Success($"Commit {hash[..8]} — signature VALID (Ed25519)");
    else
        Out.Error($"Commit {hash[..8]} — signature INVALID or missing");
    return 0;
}

int CmdAudit(string[] args)
{
    var engine = GetEngine();
    Out.Header("CodeFlow Integrity Audit");

    var head = engine.Store.ReadHead();
    if (head == null) { Out.Warn("Repository is empty."); return 0; }

    int commits = 0, trees = 0, blobs = 0, badSigs = 0, missing = 0;

    Out.Spinner("Traversing object graph...", () =>
    {
        var all = engine.GetReachableObjects(head);
        foreach (var hash in all)
        {
            var raw = engine.Store.GetRawObject(hash);
            if (raw == null) { missing++; continue; }
            if (CodeFlow.Crypto.HashUtil.IsBinaryData(raw)) { blobs++; continue; }
            try
            {
                var text = Encoding.UTF8.GetString(raw);
                var type = CodeFlow.Crypto.HashUtil.GetObjectType(text);
                if (type == "commit") { commits++; var c = CodeFlow.Core.Models.Commit.FromJson(text); if (c != null && !c.VerifySignature()) badSigs++; }
                else if (type == "tree") trees++;
                else blobs++;
            }
            catch { blobs++; }
        }
    });

    var t = new Table().Border(TableBorder.Rounded).BorderColor(Color.Cyan1);
    t.AddColumn("Object Type"); t.AddColumn("Count");
    t.AddRow("Commits", commits.ToString());
    t.AddRow("Trees", trees.ToString());
    t.AddRow("Blobs/Chunks", blobs.ToString());
    t.AddRow("[red]Invalid Signatures[/]", badSigs.ToString());
    t.AddRow("[red]Missing Objects[/]", missing.ToString());
    AnsiConsole.Write(t);

    if (badSigs == 0 && missing == 0) Out.Success("Repository integrity VERIFIED.");
    else Out.Error("Repository integrity COMPROMISED — see above.");
    return (badSigs > 0 || missing > 0) ? 1 : 0;
}

int CmdGc(string[] args)
{
    var engine = GetEngine();
    Out.Header("Garbage Collection");

    var allBranches = engine.Store.GetAllBranches().ToList();
    var reachable = new HashSet<string>(StringComparer.Ordinal);
    foreach (var b in allBranches)
    {
        var tip = engine.Store.GetBranchTip(b);
        if (tip != null) foreach (var h in engine.GetReachableObjects(tip)) reachable.Add(h);
    }

    var allObjects = engine.Store.GetAllObjectHashes().ToList();
    int removed = 0;
    foreach (var h in allObjects)
    {
        if (!reachable.Contains(h))
        {
            // Could delete unreferenced objects (loose GC)
            Out.Dim($"  dangling: {h[..12]}");
            removed++;
        }
    }
    Out.Success($"GC complete. {removed} dangling objects found. (Use --prune to delete.)");
    return 0;
}

async Task<int> CmdServe(string[] args)
{
    // Launch the API server if CodeFlow.API is present, else simple fallback
    Out.Info("Starting CodeFlow web server... (For full UI, use CodeFlow.API project)");
    Out.Info("Listening on http://localhost:5000");
    await Task.Delay(-1); // Keep alive (replaced by real API in production)
    return 0;
}

int PrintHelp(string? unknown = null)
{
    if (unknown != null) AnsiConsole.MarkupLine($"[red]Unknown command: {unknown}[/]\n");

    var panel = new Panel(
        $"[bold cyan]CodeFlow[/] v{version} — Distributed Version Control System\n\n" +
        "[bold]Repository:[/]\n" +
        "  [green]init[/]        Initialize a new repository\n" +
        "  [green]keygen[/]      Generate Ed25519 signing key pair\n" +
        "  [green]config[/]      Set user.name / user.email\n\n" +
        "[bold]Staging & Committing:[/]\n" +
        "  [green]add[/] <path>  Stage files\n" +
        "  [green]unstage[/]     Remove files from staging\n" +
        "  [green]status[/]      Show working tree status\n" +
        "  [green]commit[/] -m   Create a signed commit\n\n" +
        "[bold]History & Inspection:[/]\n" +
        "  [green]log[/]         Show commit history\n" +
        "  [green]graph[/]       Visual commit DAG\n" +
        "  [green]diff[/]        Show changes\n" +
        "  [green]verify[/]      Verify commit signature\n" +
        "  [green]audit[/]       Full repository integrity check\n\n" +
        "[bold]Branching & Merging:[/]\n" +
        "  [green]branch[/]      List / create / delete branches\n" +
        "  [green]checkout[/]    Switch branch or commit\n" +
        "  [green]merge[/]       Merge a branch\n" +
        "  [green]tag[/]         Tag a commit\n\n" +
        "[bold]Remote:[/]\n" +
        "  [green]remote[/]      Manage remotes (add, add-http, remove)\n" +
        "  [green]push[/]        Push to remote (differential)\n" +
        "  [green]pull[/]        Pull from remote\n" +
        "  [green]clone[/]       Clone: HTTP: <api-url> <owner/repo> [dir]  |  MinIO: <url> <bucket> <key> <secret> [dir]\n\n" +
        "[bold]Maintenance:[/]\n" +
        "  [green]gc[/]          Garbage collection\n" +
        "  [green]serve[/]       Start web UI server\n\n" +
        "Run [green]codeflow <command> --help[/] for command details."
    ).Header("[bold cyan] CodeFlow CLI [/]").Border(BoxBorder.Rounded).BorderColor(Color.Cyan1);

    AnsiConsole.Write(panel);
    return unknown != null ? 1 : 0;
}

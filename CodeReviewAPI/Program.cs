using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.AI;
using OpenAI;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

var url = "http://localhost:5161";
Task.Run(async () => {
    await Task.Delay(1500);
    System.Diagnostics.Process.Start(
        new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true });
});

// Null when unset — BuildClientAsync only throws if Groq is actually requested,
// so an Ollama-only or key-less deployment still starts.
var groqKeyPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "groq_key.txt");
var groqApiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY")
    ?? (File.Exists(groqKeyPath)
        ? File.ReadAllText(groqKeyPath).Trim()
        : null);

const string GROQ_MODEL    = "llama-3.3-70b-versatile";
const string GROQ_BASE_URL = "https://api.groq.com/openai/v1";
const string OLLAMA_URL    = "http://localhost:11434";
const string OLLAMA_MODEL  = "qwen2.5:3b";

// Reports land here; Desktop locally, an explicit dir in a container.
var reportsDir = Environment.GetEnvironmentVariable("REPORTS_DIR")
    ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
try { Directory.CreateDirectory(reportsDir); }
catch { /* unwritable path — the per-report try/catch below handles the fallout */ }

var tokenPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "github_token.txt");
var GITHUB_TOKEN = File.Exists(tokenPath) ? File.ReadAllText(tokenPath).Trim() : "";
if (!string.IsNullOrEmpty(GITHUB_TOKEN))
    Console.WriteLine("GitHub token loaded from Desktop/github_token.txt");
else
    Console.WriteLine("No GitHub token found - using unauthenticated API (60 req/hr limit)");

// Warm the local Ollama model once at startup instead of on every request.
_ = Task.Run(async () => {
    try {
        var (warmupClient, _) = await BuildClientAsync("ollama");
        await warmupClient.GetResponseAsync("hi");
    } catch { /* ignore warmup failure */ }
});

app.MapGet("/api/health", () => "AI Code Review API is running!");

app.MapGet("/api/models", () => Results.Ok(new {
    groq = GROQ_MODEL,
    ollama = OLLAMA_MODEL
}));

// ── PR info endpoint ──────────────────────────────────────
app.MapGet("/api/pr-info", async (string prUrl) =>
{
    try
    {
        var (owner, repo, number) = ParsePrUrl(prUrl);
        var pr    = await GhGet($"repos/{owner}/{repo}/pulls/{number}", GITHUB_TOKEN);
        var files = await GhGet($"repos/{owner}/{repo}/pulls/{number}/files", GITHUB_TOKEN);

        using var prDoc    = JsonDocument.Parse(pr);
        using var filesDoc = JsonDocument.Parse(files);

        var title  = prDoc.RootElement.GetProperty("title").GetString() ?? "";
        var author = prDoc.RootElement.GetProperty("user").GetProperty("login").GetString() ?? "";
        var state  = prDoc.RootElement.GetProperty("state").GetString() ?? "";

        var changed = filesDoc.RootElement.EnumerateArray()
            .Where(f => CODE_EXTENSIONS.Contains(
                Path.GetExtension(f.GetProperty("filename").GetString() ?? ""),
                StringComparer.OrdinalIgnoreCase))
            .Select(f => new {
                filename  = f.GetProperty("filename").GetString() ?? "",
                status    = f.GetProperty("status").GetString() ?? "",
                additions = f.GetProperty("additions").GetInt32(),
                deletions = f.GetProperty("deletions").GetInt32(),
                patch     = f.TryGetProperty("patch", out var p) ? p.GetString() ?? "" : ""
            }).ToArray();

        return Results.Ok(new { title, author, state, files = changed });
    }
    catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
});

// ── PR file review endpoint ───────────────────────────────
app.MapPost("/api/review-pr-file", async (PrFileReviewRequest req) =>
{
    try
    {
        var (aiClient, providerName) = await BuildClientAsync(req.Provider ?? "groq");

        var fileName = req.FileName ?? "file";
        var patch = req.Patch ?? "";
        if (string.IsNullOrWhiteSpace(patch))
            return Results.BadRequest(new { error = "No diff/patch content provided" });
        if (patch.Length > 3000) patch = patch[..3000] + "\n\n[... truncated ...]";

        var numberedPatch = string.Join("\n",
            patch.Split('\n').Select((line, i) => $"{i + 1,4} | {line}"));

        var agentNames = new[]
        {
            "🐛 Bug Detector", "🔒 Security Checker",
            "📖 Change Explainer", "🔧 Fix Suggester", "🚀 Review Verdict"
        };

        var agentPrompts = new[]
        {
            $@"You are reviewing a GitHub PR diff for '{fileName}'.
+ lines = added, - lines = removed. Line numbers on the left.
Reference EXACT line numbers.

DIFF:
{numberedPatch}

BUGS FOUND:
- Line [N]: [bug] — [why]
(None found if no bugs)
SEVERITY: [Low/Medium/High]
MOST CRITICAL LINE: Line [N] — [reason]",

            $@"You are a security auditor reviewing a PR diff for '{fileName}'.
+ lines = added, - lines = removed. Reference EXACT line numbers.

DIFF:
{numberedPatch}

SECURITY ISSUES:
- Line [N]: [vulnerability] — [risk]
(None found if secure)
RISK LEVEL: [Low/Medium/High/Critical]
MOST CRITICAL LINE: Line [N] — [reason]",

            $@"You are explaining what changed in this PR file '{fileName}'.
+ lines = added, - lines = removed. Reference line numbers.

DIFF:
{numberedPatch}

WHAT CHANGED:
[2-3 sentences]

KEY CHANGES:
- Lines [N-N]: [what these lines do]
- Lines [N-N]: [what was removed]

IMPACT: [effect on codebase]",

            BuildFixSuggesterPrompt(fileName, numberedPatch, isDiff: true),

            $@"You are a senior engineer giving a final verdict on PR changes for '{fileName}'.

DIFF:
{numberedPatch}

VERDICT: [APPROVE / REQUEST CHANGES / NEEDS DISCUSSION]

REASON:
[2-3 sentences]

MUST FIX BEFORE MERGE:
- [issue or None]

NICE TO HAVE:
- [improvement or None]

SCORE: [X/10]
[one sentence]"
        };

        var tasks   = agentNames.Select((n, i) => RunAgent(aiClient, n, agentPrompts[i])).ToArray();
        var results = await Task.WhenAll(tasks);

        var supPrompt =
            $"Combine PR review findings for '{fileName}'.\n\n" +
            string.Join("\n\n", agentNames.Select((n,i) => $"=== {n} ===\n{results[i]}")) +
            "\n\nRespond EXACTLY:\n\n" +
            $"PR REVIEW — {fileName}\n\n" +
            "VERDICT: [APPROVE / REQUEST CHANGES / NEEDS DISCUSSION]\n\n" +
            "SUMMARY:\n[2-3 sentences]\n\n" +
            "KEY BUGS:\n[list with line numbers or None found]\n\n" +
            "SECURITY STATUS:\n[findings or Looks secure]\n\n" +
            "WHAT CHANGED:\n[plain English]\n\n" +
            "MUST FIX:\n[critical issues or None]\n\n" +
            "SCORE: [X/10]\n[reason]";

        string finalReport = ""; int score = 7; string verdict = "NEEDS DISCUSSION";
        try
        {
            var sup = await aiClient.GetResponseAsync(supPrompt);
            finalReport = sup.Text ?? "";
            var sl = finalReport.Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith("SCORE:"));
            if (sl != null) {
                var sm = System.Text.RegularExpressions.Regex.Match(sl, @"\b(\d{1,2})\b");
                if (sm.Success && int.TryParse(sm.Groups[1].Value, out int s) && s >= 1 && s <= 10)
                    score = s; }
            var vl = finalReport.Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith("VERDICT:"));
            if (vl != null)
            { var v=vl.Split(':').Last().Trim().ToUpper();
              verdict = v.Contains("APPROVE") ? "APPROVE"
                      : v.Contains("REQUEST") ? "REQUEST CHANGES"
                      : "NEEDS DISCUSSION"; }
        }
        catch (Exception ex)
        { finalReport = string.Join("\n\n", agentNames.Select((n,i)=>$"### {n}\n{results[i]}"))
                       + $"\n\n[Supervisor error: {ex.Message}]"; }

        string ts = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string rp = Path.Combine(reportsDir,
            $"pr_review_{Path.GetFileNameWithoutExtension(fileName)}_{ts}.md");
        try {
            await File.WriteAllTextAsync(rp,
                $"# PR Review — {fileName}\n\n**PR:** {req.PrUrl}\n**Verdict:** {verdict}\n\n{finalReport}");
        } catch { /* saving unavailable in this environment */ }

        var fixedCode = ExtractFixedCode(results[Array.IndexOf(agentNames, "🔧 Fix Suggester")]);

        return Results.Ok(new {
            fileName, provider = providerName,
            score, verdict, finalReport, sourceCode = patch, fixedCode,
            agentOutputs = agentNames.Select((n,i)=>new{name=n,output=results[i]}).ToArray(),
            reportSaved  = rp
        });
    }
    catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
});

// ── Existing review endpoint ──────────────────────────────
app.MapPost("/api/review", async (ReviewRequest req) =>
{
    try
    {
        var (aiClient, providerName) = await BuildClientAsync(req.Provider ?? "groq");
        var (code, fileName, srcError) = await ResolveSourceAsync(req);
        if (srcError != null) return Results.BadRequest(new{error=srcError});

        var num = string.Join("\n",code.Split('\n').Select((l,i)=>$"{i+1,4} | {l}"));
        var agentNames = new[]{"🐛 Bug Detector","🔒 Security Checker","📖 Code Explainer",
                               "🔧 Fix Suggester","🚀 Onboarding Assistant"};
        var agentPrompts = new[]
        {
            $"You are an expert bug detector reviewing '{fileName}'.\nLINE NUMBERS on left. Reference exact lines.\n\nCODE:\n{num}\n\nBUGS FOUND:\n- Line [N]: [bug] — [why]\n(None found if no bugs)\nSEVERITY: [Low/Medium/High]\nMOST CRITICAL LINE: Line [N] — [reason]",
            $"You are a security auditor reviewing '{fileName}'.\nLINE NUMBERS on left. Reference exact lines.\n\nCODE:\n{num}\n\nSECURITY ISSUES:\n- Line [N]: [type] — [risk]\n(None found)\nRISK LEVEL: [Low/Medium/High/Critical]\nMOST CRITICAL LINE: Line [N] — [reason]",
            $"You are a code explainer for '{fileName}'.\nLINE NUMBERS on left.\n\nCODE:\n{num}\n\nPURPOSE: [one sentence]\nLANGUAGE: [language]\nKEY SECTIONS:\n- Lines [N-N]: [what it does]\nHOW IT WORKS:\n[2-3 plain paragraphs]",
            BuildFixSuggesterPrompt(fileName, num),
            $"You are an onboarding assistant for '{fileName}'.\nLINE NUMBERS on left.\n\nCODE:\n{num}\n\nONBOARDING SUMMARY:\n[2-3 paragraphs]\nSTART READING HERE:\n- Line [N]: [why]\nWATCH OUT FOR:\n- Line [N]: [gotcha]\nDEPENDENCIES:\n[what this needs]"
        };
        var tasks = agentNames.Select((n,i)=>RunAgent(aiClient,n,agentPrompts[i])).ToArray();
        var results = await Task.WhenAll(tasks);
        var supPrompt = $"Combine findings for '{fileName}'.\n\n"+
            string.Join("\n\n",agentNames.Select((n,i)=>$"=== {n} ===\n{results[i]}"))+
            $"\n\nFINAL REPORT — {fileName}\n\nSUMMARY:\n[2-3 sentences]\n\nKEY BUGS:\n[line numbers]\n\nSECURITY STATUS:\n[findings]\n\nTOP IMPROVEMENTS:\n[top 3]\n\nWHAT THIS CODE DOES:\n[paragraph]\n\nSCORE: [X/10]\n[reason]";
        string finalReport=""; int score=7;
        try
        {
            var sup=await aiClient.GetResponseAsync(supPrompt); finalReport=sup.Text??"";
            var sl=finalReport.Split('\n').FirstOrDefault(l=>l.TrimStart().StartsWith("SCORE:"));
            if(sl!=null){
                var sm = System.Text.RegularExpressions.Regex.Match(sl, @"\b(\d{1,2})\b");
                if (sm.Success && int.TryParse(sm.Groups[1].Value, out int s) && s >= 1 && s <= 10)
                    score = s;}
        }
        catch(Exception ex){finalReport=string.Join("\n\n",agentNames.Select((n,i)=>$"### {n}\n{results[i]}"))+$"\n\n[Supervisor error: {ex.Message}]";}
        string ts=DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string rp=Path.Combine(reportsDir,
            $"review_{Path.GetFileNameWithoutExtension(fileName)}_{ts}.md");
        try {
            await File.WriteAllTextAsync(rp,$"# Code Review — {fileName}\n\n**Provider:** {providerName}\n\n{finalReport}");
        } catch { /* saving unavailable in this environment */ }
        var fixedCode = ExtractFixedCode(results[Array.IndexOf(agentNames, "🔧 Fix Suggester")]);
        return Results.Ok(new{fileName,provider=providerName,score,finalReport,sourceCode=code,fixedCode,
            agentOutputs=agentNames.Select((n,i)=>new{name=n,output=results[i]}).ToArray(),reportSaved=rp});
    }
    catch(Exception ex){return Results.BadRequest(new { error = ex.Message });}
});

// ── Chat endpoint ─────────────────────────────────────────
// Follow-up Q&A about a file the user just reviewed. Same client plumbing as
// RunAgent(), but multi-turn: a system prompt plus the running conversation.
app.MapPost("/api/chat", async (ChatRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.SystemPrompt))
        return Results.BadRequest(new { error = "systemPrompt is required." });
    if (req.Messages is null || req.Messages.Count == 0)
        return Results.BadRequest(new { error = "messages is required." });

    try
    {
        var (aiClient, _) = await BuildClientAsync(req.Provider ?? "groq");

        var turns = new List<ChatMessage> { new(ChatRole.System, req.SystemPrompt) };
        foreach (var m in req.Messages)
        {
            if (string.IsNullOrWhiteSpace(m.Content)) continue;
            var role = (m.Role ?? "user").Trim().ToLowerInvariant() switch
            {
                "assistant" => ChatRole.Assistant,
                "system"    => ChatRole.System,
                _           => ChatRole.User
            };
            turns.Add(new ChatMessage(role, m.Content));
        }

        var r = await aiClient.GetResponseAsync(turns);
        return Results.Ok(new { reply = r.Text ?? "" });
    }
    catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
});

// ── Modernization endpoint ────────────────────────────────
app.MapPost("/api/modernize", async (ReviewRequest req) =>
{
    try
    {
        var (aiClient, providerName) = await BuildClientAsync(req.Provider ?? "groq");
        var (code, fileName, srcError) = await ResolveSourceAsync(req);
        if (srcError != null) return Results.BadRequest(new { error = srcError });

        var num = string.Join("\n", code.Split('\n').Select((l, i) => $"{i + 1,4} | {l}"));

        var agentNames = new[]
        {
            "TechDebtAnalyzer", "DuplicateCodeDetector",
            "FrameworkVersionChecker", "ArchitectureReviewer"
        };

        var systemPrompts = new[]
        {
            "You are a technical debt analyst. Analyze the code and identify technical debt, code smells, overly complex logic, and shortcuts that will cause problems later. For every issue, reference exact line numbers using Line [N]: format. Be specific and actionable.",
            "You are a code duplication expert. Find repeated logic, copy-paste patterns, and DRY principle violations in the code. For every issue, reference exact line numbers using Line [N]: format. Suggest how to consolidate the duplication.",
            "You are a modernization expert. Identify outdated libraries, deprecated APIs, old language patterns, and legacy approaches that have better modern equivalents. For every issue, reference exact line numbers using Line [N]: format. Mention what the modern replacement is.",
            "You are a software architect. Identify structural problems such as god classes, poor separation of concerns, missing abstractions, tight coupling, and circular dependencies. For every issue, reference exact line numbers using Line [N]: format. Suggest the architectural fix."
        };

        var agentPrompts = systemPrompts
            .Select(sp => $"{sp}\n\nFILE: {fileName}\nLine numbers are shown on the left of each line; do not copy the `   N | ` gutter into any snippet.\n\nCODE:\n{num}")
            .ToArray();

        var results = await Task.WhenAll(
            agentNames.Select((n, i) => RunAgent(aiClient, n, agentPrompts[i])));

        var supPrompt =
            $"You are the Modernization Supervisor for '{fileName}'.\n" +
            "Four specialist agents reviewed the code. Their findings:\n\n" +
            string.Join("\n\n", agentNames.Select((n, i) => $"=== {n} ===\n{results[i]}")) +
            "\n\nProduce a prioritized modernization roadmap with EXACTLY these three sections, " +
            "in this order, using these exact headers:\n\n" +
            "PRIORITY 1 - FIX NOW\n(critical issues blocking maintainability or security)\n" +
            "- Issue: [name] | Flagged by: [agent] | Effort: [Low/Medium/High]\n  Business impact: [one sentence]\n\n" +
            "PRIORITY 2 - FIX SOON\n(important improvements worth doing in the next sprint)\n" +
            "- Issue: [name] | Flagged by: [agent] | Effort: [Low/Medium/High]\n  Business impact: [one sentence]\n\n" +
            "PRIORITY 3 - FIX LATER\n(nice to have, low risk if deferred)\n" +
            "- Issue: [name] | Flagged by: [agent] | Effort: [Low/Medium/High]\n  Business impact: [one sentence]\n\n" +
            "RULES:\n" +
            "- Emit all three section headers exactly as written, even if a section is empty (write 'None' beneath it).\n" +
            "- 'Flagged by' must name exactly one of: " + string.Join(", ", agentNames) + ".\n" +
            "- 'Effort' must be exactly one of: Low, Medium, High.\n" +
            "- 'Business impact' must be exactly one sentence.\n" +
            "- Every item must carry all four parts: issue name, flagging agent, effort, business impact.\n" +
            "- Output only the roadmap. No preamble, no closing summary.";

        string modernizationRoadmap;
        try
        {
            var sup = await aiClient.GetResponseAsync(supPrompt);
            modernizationRoadmap = sup.Text ?? "";
        }
        catch (Exception ex)
        {
            modernizationRoadmap = ex.Message;
        }

        // The three headers are part of this endpoint's contract, so fall back to a
        // skeleton carrying the raw agent findings rather than returning a roadmap without them.
        if (!modernizationRoadmap.Contains("PRIORITY 1")
            || !modernizationRoadmap.Contains("PRIORITY 2")
            || !modernizationRoadmap.Contains("PRIORITY 3"))
        {
            modernizationRoadmap =
                "PRIORITY 1 - FIX NOW\n(critical issues blocking maintainability or security)\n" +
                "- Supervisor did not return a usable roadmap; raw specialist findings below.\n\n" +
                "PRIORITY 2 - FIX SOON\n(important improvements worth doing in the next sprint)\nNone\n\n" +
                "PRIORITY 3 - FIX LATER\n(nice to have, low risk if deferred)\nNone\n\n" +
                "RAW FINDINGS:\n" +
                string.Join("\n\n", agentNames.Select((n, i) => $"=== {n} ===\n{results[i]}"));
        }

        var ts = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var reportSaved = Path.Combine(reportsDir,
            $"modernize_{Path.GetFileNameWithoutExtension(fileName)}_{ts}.md");

        try {
            await File.WriteAllTextAsync(reportSaved,
                $"# Modernization Roadmap — {fileName}\n\n**Provider:** {providerName}\n\n" +
                $"{modernizationRoadmap}\n\n---\n\n## Specialist Agent Findings\n\n" +
                string.Join("\n\n", agentNames.Select((n, i) => $"### {n}\n\n{results[i]}")));
        } catch { /* saving unavailable in this environment */ }

        return Results.Ok(new
        {
            agentOutputs = agentNames.Select((n, i) => new { name = n, output = results[i] }).ToArray(),
            modernizationRoadmap,
            reportSaved
        });
    }
    catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
});

// ── Repo files endpoint ───────────────────────────────────
app.MapGet("/api/repo-files", async (string repoUrl) =>
{
    try
    {
        var uri=new Uri(repoUrl.TrimEnd('/'));
        var segs=uri.AbsolutePath.Trim('/').Split('/');
        if(segs.Length<2) return Results.BadRequest(new{error="Invalid GitHub URL"});
        var files=await FetchRepoFiles(segs[0],segs[1],GITHUB_TOKEN);
        var rec=files.Where(f=>RANK_KEYWORDS.Any(k=>Path.GetFileNameWithoutExtension(f.filePath).ToLower().Contains(k))).Take(8).ToList();
        if(rec.Count==0) rec=files.Take(6).ToList();
        return Results.Ok(new{total=files.Count,recommended=rec.Select(f=>new{f.filePath,f.downloadUrl})});
    }
    catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
});

var port = Environment.GetEnvironmentVariable("PORT") ?? "5161";
app.Run($"http://0.0.0.0:{port}");

Task<(IChatClient client, string name)> BuildClientAsync(string provider)
{
    if(provider=="ollama"){
        var c=new OllamaChatClient(new Uri(OLLAMA_URL),OLLAMA_MODEL);
        return Task.FromResult(((IChatClient)c, $"Ollama ({OLLAMA_MODEL})"));
    }
    // Only the Groq path needs a key, so an Ollama-only install never hits this.
    if(groqApiKey == null)
        throw new InvalidOperationException(
            "Groq API key not set. Add GROQ_API_KEY environment variable.");
    var cred=new System.ClientModel.ApiKeyCredential(groqApiKey);
    var opts=new OpenAIClientOptions{Endpoint=new Uri(GROQ_BASE_URL)};
    return Task.FromResult((
        (IChatClient)new OpenAIClient(cred,opts).GetChatClient(GROQ_MODEL).AsIChatClient(),
        $"Groq ({GROQ_MODEL})"));
}
// Resolves the request into (code, fileName) or a client-facing error message.
// Shared by /api/review and /api/modernize.
async Task<(string code, string fileName, string? error)> ResolveSourceAsync(ReviewRequest req)
{
    var input = req.ResolvedInput;
    string code, fileName;
    switch (req.ResolvedMode)
    {
        case "github-file":
            var raw = input
                .Replace("https://github.com/","https://raw.githubusercontent.com/")
                .Replace("/blob/","/");
            code = await FetchUrl(raw, GITHUB_TOKEN);
            fileName = Path.GetFileName(new Uri(raw).LocalPath); break;
        case "paste":
            code = input; fileName = req.FileName ?? "pasted_code"; break;
        case "local-file":
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (!Directory.Exists(desktopPath))
                return ("", req.FileName ?? "file",
                    "Local file mode is not available in the hosted version. Please use paste or GitHub URL instead.");
            if (!File.Exists(input)) return ("", "", "File not found");
            code = await File.ReadAllTextAsync(input);
            fileName = Path.GetFileName(input); break;
        default: return ("", "", "Invalid mode");
    }
    if (string.IsNullOrWhiteSpace(code) || code.StartsWith("ERROR:"))
        return ("", fileName, $"Could not fetch: {code}");
    if (code.Length > 3000) code = code[..3000] + "\n\n[... truncated ...]";
    return (code, fileName, null);
}

// ── Fix Suggester: paired "line reference + fixed snippet" output ──
static string BuildFixSuggesterPrompt(string fileName, string numberedCode, bool isDiff = false)
{
    var source = isDiff
        ? $"You are a code fix expert reviewing a GitHub PR diff for '{fileName}'.\n+ lines = added, - lines = removed. Line numbers are on the left."
        : $"You are a code fix expert reviewing '{fileName}'.\nLine numbers are on the left of each line.";

    return source + $@"
Reference EXACT line numbers from the listing below.

CODE:
{numberedCode}

For EVERY issue you find, output a PAIR in EXACTLY this format:

Line [N]: Brief explanation of the issue.

FIXED CODE:
```language
// the corrected snippet here
```

Use `Line [N]-[M]:` instead of `Line [N]:` when the issue spans a range of lines.
Repeat this pair once per issue — multiple issues means multiple pairs, separated by a blank line.

Worked example of a single pair (for a Python file):

Line [2]: Hard-coded credential; read it from the environment instead.

FIXED CODE:
```python
- password = ""admin123""
+ password = os.environ[""ADMIN_PASSWORD""]
```

RULES:
- Output ONLY these pairs. No introduction, no summary, no closing remarks.
- Each fixed code block must contain ONLY the corrected snippet for that one issue.
- Do NOT output one giant rewritten version of the whole file at the end.
- Do NOT copy the `   N | ` line-number gutter into the snippet — it is not part of the code.
- The word `language` is a placeholder. Replace it with the file's actual language tag
  (python, csharp, javascript, typescript, java, go, ...). Never write the literal word `language`.
- For each FIXED CODE block, show the OLD line(s) prefixed with ""- "" and the NEW line(s)
  prefixed with ""+ "". Like a git diff.
  Example:
  - password = ""admin123""
  + password = os.environ[""ADMIN_PASSWORD""]
- Only show the specific lines that change, not the whole function.
- If you genuinely find no issues, output exactly: No issues found.";
}

// Pulls the line-reference + fixed-code pairs out of the Fix Suggester's raw output,
// dropping any preamble the model added before the first `Line [N]` reference.
static string ExtractFixedCode(string agentOutput)
{
    if (string.IsNullOrWhiteSpace(agentOutput)) return "";
    var text = agentOutput.Trim();
    if (text.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)) return "";

    var lines = text.Split('\n');
    var start = Array.FindIndex(lines, l =>
        System.Text.RegularExpressions.Regex.IsMatch(
            l.TrimStart().TrimStart('*', '-', '#', ' '),
            @"^Line\s*\[?\d+", System.Text.RegularExpressions.RegexOptions.IgnoreCase));

    return start > 0 ? string.Join("\n", lines.Skip(start)).Trim() : text;
}

static async Task<string> RunAgent(IChatClient c,string name,string prompt)
{try{var r=await c.GetResponseAsync(prompt);return r.Text??"";}catch(Exception ex){return $"Error: {ex.Message}";}}
static async Task<string> FetchUrl(string url,string token)
{
    using var client=new HttpClient();
    client.DefaultRequestHeaders.UserAgent.ParseAdd("CodeReviewAPI/1.0");
    if(!string.IsNullOrEmpty(token))
        client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token);
    try{var r=await client.GetAsync(url);
        return r.IsSuccessStatusCode?await r.Content.ReadAsStringAsync():$"ERROR: HTTP {(int)r.StatusCode}";}
    catch(Exception ex){return $"ERROR: {ex.Message}";}
}
static async Task<string> GhGet(string path, string token)
{
    using var client=new HttpClient();
    client.DefaultRequestHeaders.UserAgent.ParseAdd("CodeReviewAPI/1.0");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github.v3+json");
    if(!string.IsNullOrEmpty(token))
        client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token);
    var r=await client.GetAsync($"https://api.github.com/{path}");
    if(!r.IsSuccessStatusCode) throw new Exception($"GitHub API {(int)r.StatusCode} — {path}");
    return await r.Content.ReadAsStringAsync();
}
static (string owner,string repo,string number) ParsePrUrl(string url)
{
    var segs=new Uri(url.TrimEnd('/')).AbsolutePath.Trim('/').Split('/');
    if(segs.Length<4||segs[2]!="pull")
        throw new Exception("Invalid PR URL. Expected: https://github.com/owner/repo/pull/123");
    return(segs[0],segs[1],segs[3]);
}
static async Task<List<(string filePath,string downloadUrl)>> FetchRepoFiles(string owner,string repo,string token)
{
    var result=new List<(string,string)>();var queue=new Queue<string>();
    queue.Enqueue($"https://api.github.com/repos/{owner}/{repo}/contents");
    using var client=new HttpClient();
    client.DefaultRequestHeaders.UserAgent.ParseAdd("CodeReviewAPI/1.0");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github.v3+json");
    if(!string.IsNullOrEmpty(token))
        client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token);
    int pages=0;
    while(queue.Count>0&&pages++<10){
        var json=await FetchUrl(queue.Dequeue(),token);
        if(json.StartsWith("ERROR:")){
            if(json.Contains("403") || json.Contains("rate limit"))
                throw new Exception("GitHub API rate limit reached. Add a Personal Access Token to Desktop/github_token.txt for 5000 requests/hour instead of 60.");
            continue;
        }
        try{using var doc=JsonDocument.Parse(json);
            foreach(var item in doc.RootElement.EnumerateArray()){
                var type=item.GetProperty("type").GetString()??"";
                var path=item.GetProperty("path").GetString()??"";
                var dl=item.TryGetProperty("download_url",out var d)?d.GetString()??"":"";
                if(type=="file"&&CODE_EXTENSIONS.Any(e=>path.EndsWith(e,StringComparison.OrdinalIgnoreCase)))result.Add((path,dl));
                else if(type=="dir"){var du=item.GetProperty("url").GetString()??"";if(du!="")queue.Enqueue(du);}
            }}catch{}
    }
    return result;
}
// Top-level statements cannot declare fields, so the shared constants live on the
// synthesized Program class. Both top-level code and static local functions see them.
partial class Program
{
    static readonly string[] CODE_EXTENSIONS = new[]{
        ".cs",".py",".js",".ts",".java",".cpp",".c",".go",
        ".rb",".php",".swift",".kt",".rs"
    };
    static readonly string[] RANK_KEYWORDS = new[]{
        "main","app","program","index","controller","manager","service",
        "utils","helper","core","config","startup","handler","router",
        "model","agent"
    };
}

record ReviewRequest{
    public string?Provider{get;init;}
    public string?Mode{get;init;}
    public string?Input{get;init;}
    public string?FileName{get;init;}
    // Aliases so clients can also post {inputType, content} instead of {mode, input}
    public string?InputType{get;init;}
    public string?Content{get;init;}
    public string ResolvedMode  => Mode  ?? InputType ?? "";
    public string ResolvedInput => Input ?? Content   ?? "";
}
record PrFileReviewRequest{public string?Provider{get;init;}public string?PrUrl{get;init;}public string?FileName{get;init;}public string?Patch{get;init;}}
// Named ChatTurn, not ChatMessage: a global-namespace ChatMessage would shadow
// Microsoft.Extensions.AI.ChatMessage, which the /api/chat handler needs.
record ChatTurn{public string?Role{get;init;}public string?Content{get;init;}}
record ChatRequest{
    public string?Provider{get;init;}
    public string?SystemPrompt{get;init;}
    public List<ChatTurn>?Messages{get;init;}
}
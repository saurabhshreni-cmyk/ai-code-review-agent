# AI Code Review Agent

[![Live Demo](https://img.shields.io/badge/Live%20Demo-Online-brightgreen?style=for-the-badge)](https://ai-code-review-agent-boot.onrender.com) [![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=for-the-badge&logo=dotnet)](https://dotnet.microsoft.com/) [![License](https://img.shields.io/badge/License-MIT-blue?style=for-the-badge)](LICENSE) [![Docker](https://img.shields.io/badge/Docker-Ready-2496ED?style=for-the-badge&logo=docker)](Dockerfile) [![Render](https://img.shields.io/badge/Deployed-Render-46E3B7?style=for-the-badge&logo=render)](https://ai-code-review-agent-boot.onrender.com)

**Multi-agent AI system that fans out specialist reviewers in parallel using `Task.WhenAll` and the `IChatClient` abstraction from Microsoft.Extensions.AI — runs on Groq cloud or local Ollama, deployed as a single ASP.NET Core binary.**

---

## 🔗 Live Demo

> **[https://ai-code-review-agent-boot.onrender.com](https://ai-code-review-agent-boot.onrender.com)**

Paste any code snippet, drop in a GitHub file URL, or point it at a pull request — five AI specialist agents analyze it in parallel and return a scored, line-annotated report in seconds, no sign-up required.

*Hosted on Render's free tier — the app sleeps after inactivity, so the first request may take up to a minute to wake up.*

---

## What It Does

Instead of making a single LLM call and hoping for a comprehensive answer, this system fans out to five specialist agents simultaneously via `Task.WhenAll`, each with a narrowly-scoped prompt and a structured output contract — a Bug Detector, Security Checker, Code Explainer, Fix Suggester, and either an Onboarding Assistant (for code review) or a Review Verdict agent (for PRs). Once all five complete, a Supervisor agent synthesizes their findings into a scored, structured report. The architecture is built on Microsoft.Extensions.AI's `IChatClient` abstraction, which means the same orchestration logic drives both Groq's hosted `llama-3.3-70b-versatile` model and a locally-running Ollama instance with `qwen2.5:3b` — the provider is resolved per-request via `BuildClientAsync("groq" | "ollama")` with no changes at the call site. Beyond code review, the system includes a Modernization Advisor (4 parallel agents that produce a 3-priority roadmap), a GitHub repo crawler, PR diff analysis with a verdict, multi-turn chat about the reviewed code, and a full review history persisted in localStorage.

---

## Features

<table>
<tr>
  <td><strong>⚡ Parallel multi-agent execution</strong></td>
  <td>All specialist agents run concurrently via <code>Task.WhenAll</code> — wall-clock time equals the slowest single agent plus one supervisor call, not the sum of all agents</td>
</tr>
<tr>
  <td><strong>🐛 5-agent code review + supervisor</strong></td>
  <td>Bug Detector, Security Checker, Code Explainer, Fix Suggester, Onboarding Assistant — each with a structured output contract; Supervisor synthesises into SCORE X/10</td>
</tr>
<tr>
  <td><strong>🔀 PR review with verdict</strong></td>
  <td>Fetches unified diffs from GitHub REST API, runs 5 PR-specific agents, returns APPROVE / REQUEST CHANGES / NEEDS DISCUSSION with must-fix list</td>
</tr>
<tr>
  <td><strong>🏗️ Modernization Advisor</strong></td>
  <td>4 parallel agents (TechDebtAnalyzer, DuplicateCodeDetector, FrameworkVersionChecker, ArchitectureReviewer) produce a PRIORITY 1/2/3 roadmap with effort and business impact per item</td>
</tr>
<tr>
  <td><strong>📂 GitHub repo crawler</strong></td>
  <td>BFS walks any public repo tree up to 10 pages, filters by 13 code extensions, auto-ranks files by name keywords (main, controller, service, agent, …)</td>
</tr>
<tr>
  <td><strong>🔧 Recommended Fixes panel</strong></td>
  <td>Fix Suggester output rendered as diff-style <code>- old / + new</code> blocks pinned to exact line numbers, with a Copy All button</td>
</tr>
<tr>
  <td><strong>💬 Chat with your code</strong></td>
  <td>Multi-turn Q&amp;A (up to 10 turns) backed by the same <code>IChatClient</code> — system prompt includes the full reviewed source for grounded answers</td>
</tr>
<tr>
  <td><strong>🕐 Review history</strong></td>
  <td>All reviews persisted in <code>localStorage</code> (key <code>cra_history</code>, max 50 entries) with score, verdict, provider, timestamp, and chat transcript</td>
</tr>
<tr>
  <td><strong>📊 Session summary</strong></td>
  <td>After a batch repo or PR review, shows average score, total findings by severity, best/worst file callouts, and a comparison table across all files</td>
</tr>
<tr>
  <td><strong>🔄 Dual LLM support</strong></td>
  <td>Toggle between Groq cloud (<code>llama-3.3-70b-versatile</code>) and local Ollama (<code>qwen2.5:3b</code>) from the sidebar — no restart, Ollama is warmed at startup</td>
</tr>
<tr>
  <td><strong>📝 Markdown report export</strong></td>
  <td>Every review auto-saved as a <code>.md</code> file to <code>REPORTS_DIR</code>; in-browser Download button creates a self-contained report with all agent outputs</td>
</tr>
<tr>
  <td><strong>🐳 Docker + Render ready</strong></td>
  <td>Multi-stage root <code>Dockerfile</code>, <code>PORT</code> and <code>REPORTS_DIR</code> env vars, deployed to Render with a single push to <code>main</code></td>
</tr>
</table>

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│  Browser  (index.html — vanilla JS + CSS, zero npm, zero build) │
│                                                                   │
│  Sidebar modes: Code Review · Repo Review · PR Review            │
│                 Modernization Advisor · Review History            │
│  Provider toggle: [ Groq ]  [ Ollama ]                          │
└──────────────────────────┬──────────────────────────────────────┘
                            │  HTTP GET / POST  (same origin)
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│  ASP.NET Core Minimal API  ·  Program.cs  ·  .NET 10            │
│                                                                   │
│  GET  /api/health          ←  liveness probe                     │
│  GET  /api/models          ←  active model names                 │
│  GET  /api/pr-info         ←  PR metadata + changed files        │
│  GET  /api/repo-files      ←  BFS repo tree (≤10 pages)          │
│  POST /api/review          ←  5-agent code review + supervisor   │
│  POST /api/review-pr-file  ←  5-agent PR diff review + supervisor│
│  POST /api/chat            ←  multi-turn Q&A (up to 10 turns)    │
│  POST /api/modernize       ←  4-agent modernization + supervisor  │
│                                                                   │
│  ┌──────────── /api/review (and /api/review-pr-file) ─────────┐ │
│  │                                                              │ │
│  │  Task.WhenAll([                                             │ │
│  │    🐛 Bug Detector      → line-referenced bug list         │ │
│  │    🔒 Security Checker  → OWASP / credential scan          │ │
│  │    📖 Code Explainer    → purpose + key line sections      │ │
│  │    🔧 Fix Suggester     → diff-style before/after pairs    │ │
│  │    🚀 Onboarding Asst / Review Verdict                     │ │
│  │  ])                ↑ all five run concurrently              │ │
│  │       ↓                                                     │ │
│  │  Supervisor call → SUMMARY · KEY BUGS · SCORE X/10         │ │
│  └──────────────────────────────────────────────────────────── ┘ │
│                                                                   │
│  ┌──────────── /api/modernize ─────────────────────────────────┐ │
│  │  Task.WhenAll([                                              │ │
│  │    TechDebtAnalyzer · DuplicateCodeDetector                 │ │
│  │    FrameworkVersionChecker · ArchitectureReviewer           │ │
│  │  ])                                                          │ │
│  │  → Supervisor → PRIORITY 1 FIX NOW / 2 FIX SOON / 3 LATER │ │
│  └──────────────────────────────────────────────────────────── ┘ │
│                                                                   │
│  BuildClientAsync("groq" | "ollama") → IChatClient               │
│     ├─ "groq"   → OpenAIClient(GROQ_BASE_URL) → llama-3.3-70b   │
│     └─ "ollama" → OllamaChatClient(localhost:11434) → qwen2.5:3b │
└──────────────────────────────────────────────────────────────────┘
```

---

## Agents

### Code Review — 5 Agents + Supervisor

| Agent | Role |
|---|---|
| 🐛 Bug Detector | Finds logic errors, null-reference risks, off-by-one errors, and runtime exceptions; references exact line numbers |
| 🔒 Security Checker | Audits for hardcoded credentials, injection vectors, CSRF gaps, and OWASP Top 10 violations |
| 📖 Code Explainer | Explains the file's purpose in plain English, broken down by key line ranges; outputs PURPOSE · LANGUAGE · KEY SECTIONS |
| 🔧 Fix Suggester | Produces diff-style `- old / + new` snippets for every issue, each pinned to an exact line number |
| 🚀 Onboarding Assistant | Identifies the best entry point, flags gotchas, lists dependencies; outputs START READING HERE · WATCH OUT FOR |
| **Supervisor** | Synthesises all five reports into SUMMARY · KEY BUGS · SECURITY STATUS · TOP IMPROVEMENTS · SCORE X/10 |

### PR Review — 5 Agents + Supervisor

| Agent | Role |
|---|---|
| 🐛 Bug Detector | Scans added lines in the unified diff for bugs introduced by the changeset; references diff line numbers |
| 🔒 Security Checker | Audits new attack surface introduced by the PR — injections, exposed secrets, missing auth checks |
| 📖 Change Explainer | Describes what changed in plain English; outputs WHAT CHANGED · KEY CHANGES · IMPACT |
| 🔧 Fix Suggester | Suggests diff-style fixes for issues in added lines only |
| 🚀 Review Verdict | Issues a final recommendation with a must-fix list; outputs VERDICT · MUST FIX BEFORE MERGE · NICE TO HAVE · SCORE |
| **Supervisor** | Synthesises all five into VERDICT: APPROVE / REQUEST CHANGES / NEEDS DISCUSSION · MUST FIX · SCORE X/10 |

### Modernization Advisor — 4 Agents + Supervisor

| Agent | Role |
|---|---|
| TechDebtAnalyzer | Identifies technical debt, code smells, overly complex logic, and shortcuts that will cause problems later |
| DuplicateCodeDetector | Finds copy-paste patterns, repeated logic blocks, and DRY violations; suggests how to consolidate |
| FrameworkVersionChecker | Flags deprecated APIs, outdated library patterns, and old language features; names the modern replacement |
| ArchitectureReviewer | Spots god classes, poor separation of concerns, tight coupling, missing abstractions, circular dependencies |
| **Supervisor** | Produces a PRIORITY 1 – FIX NOW / PRIORITY 2 – FIX SOON / PRIORITY 3 – FIX LATER roadmap with Effort and Business impact per item |

---

## Tech Stack

| Technology | Version | Purpose |
|---|---|---|
| .NET / ASP.NET Core | 10.0 | Runtime and Minimal API host (`MapGet`/`MapPost`, static files, `UseDefaultFiles`) |
| Microsoft.Extensions.AI | 9.5.0 | Provider-agnostic `IChatClient` abstraction — all agent calls go through this |
| Microsoft.Extensions.AI.OpenAI | 9.6.0-preview.1.25310.2 | Groq via OpenAI-compatible endpoint (`https://api.groq.com/openai/v1`) |
| Microsoft.Extensions.AI.Ollama | 9.6.0-preview.1.25310.2 | Local inference via `OllamaChatClient(new Uri("http://localhost:11434"), "qwen2.5:3b")` |
| Groq LLM | `llama-3.3-70b-versatile` | Primary cloud model — fast inference, large context |
| Ollama LLM | `qwen2.5:3b` | Local/offline model — warmed via background `Task.Run` at startup |
| Frontend | Vanilla JS + CSS | Single `index.html`, zero npm, zero build step — served as static files by Kestrel |
| Fonts | Inter + JetBrains Mono | Google Fonts — UI text and monospace code/diff panes |
| GitHub REST API | v3 | Repo tree BFS, PR metadata, unified diffs, raw file fetching |
| Docker | Multi-stage | `mcr.microsoft.com/dotnet/aspnet:10.0` final image, `sdk:10.0` build stage |
| Render | — | Cloud deployment — reads root `Dockerfile`, injects `PORT=8080` |
| GitHub Actions | — | CI workflow: restore → build Release → verify binary output |
| localStorage | — | Client-side review history — key `cra_history`, max 50 entries |

---

## API Endpoints

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/health` | Liveness probe — returns `"AI Code Review API is running!"` |
| `GET` | `/api/models` | Returns `{ groq: "llama-3.3-70b-versatile", ollama: "qwen2.5:3b" }` |
| `GET` | `/api/pr-info?prUrl=` | Fetches PR title, author, state, and filtered list of changed code files (via GitHub REST API) |
| `GET` | `/api/repo-files?repoUrl=` | BFS-walks the repo tree (≤10 pages), returns code files ranked by filename keywords |
| `POST` | `/api/review` | 5-agent code review + supervisor; body: `{ provider, mode, input, fileName }` |
| `POST` | `/api/review-pr-file` | 5-agent PR diff review + supervisor; body: `{ provider, prUrl, fileName, patch }` |
| `POST` | `/api/chat` | Multi-turn follow-up Q&A; body: `{ provider, systemPrompt, messages: [{role, content}] }` |
| `POST` | `/api/modernize` | 4-agent modernization analysis + supervisor; body: `{ provider, mode, input, fileName }` |

**Input `mode`** for `/api/review` and `/api/modernize`: `"paste"` (raw code string) · `"github-file"` (GitHub blob URL, rewritten to `raw.githubusercontent.com` internally) · `"local-file"` (absolute server path — blocked in hosted version).

**Recognised code extensions:** `.cs` `.py` `.js` `.ts` `.java` `.cpp` `.c` `.go` `.rb` `.php` `.swift` `.kt` `.rs`

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- A free [Groq API key](https://console.groq.com/) **or** [Ollama](https://ollama.ai/) with `qwen2.5:3b` pulled
- Git

### Run Locally

```bash
git clone https://github.com/saurabhshreni-cmyk/ai-code-review-agent
cd ai-code-review-agent/CodeReviewAPI
export GROQ_API_KEY=your_key_here   # get free at console.groq.com
dotnet run
# Open http://localhost:5161
```

The server opens your browser automatically after 1.5 seconds. Set `GITHUB_TOKEN` to a personal access token to enable PR and repo review features (raises GitHub API limit from 60 to 5,000 req/hr).

**Windows PowerShell:**
```bash
$env:GROQ_API_KEY = "your_key_here"
dotnet run
```

### Run with Docker

```bash
docker build -t ai-code-review-agent .
docker run -p 8080:8080 \
  -e GROQ_API_KEY=your_key \
  -e GITHUB_TOKEN=your_token \
  -e REPORTS_DIR=/tmp \
  ai-code-review-agent
```

---

## Environment Variables

| Variable | Required | Default | Description |
|---|---|---|---|
| `GROQ_API_KEY` | Yes (for Groq) | `null` | Groq API key. Falls back to `Desktop/groq_key.txt`. If absent, Groq requests throw at call time — Ollama still works without it. |
| `GITHUB_TOKEN` | No | `""` | GitHub PAT (`repo:read`). Read from environment variable first; falls back to `Desktop/github_token.txt`. Raises rate limit from 60 to 5,000 req/hr. |
| `REPORTS_DIR` | No | User's Desktop | Directory where `.md` report files are saved after each review. Set to `/tmp` or `/reports` in Docker. |
| `PORT` | No | `5161` | Port the server binds to on `0.0.0.0`. Dockerfile sets `PORT=8080`; Render injects this automatically. |

---

## Screenshots

### Code Review
![Code Review](screenshots/code-review.png)

### Modernization Roadmap
![Modernization](screenshots/modernization.png)

### Recommended Fixes
![Fixes](screenshots/fixes.png)

### Review History
![History](screenshots/history.png)

*Screenshots coming soon — [try the live demo](https://ai-code-review-agent-boot.onrender.com) instead*

---

## License

[MIT](LICENSE) © 2026 Saurabh Shreni

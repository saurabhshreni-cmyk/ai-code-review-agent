# AI Code Review Agent

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/) [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE) [![Live Demo](https://img.shields.io/badge/Live%20Demo-Open%20App-brightgreen?logo=railway&logoColor=white)](https://ai-code-review-agent-production-5a51.up.railway.app) [![Build & Verify](https://github.com/saurabhshreni-cmyk/ai-code-review-agent/actions/workflows/build.yml/badge.svg)](https://github.com/saurabhshreni-cmyk/ai-code-review-agent/actions/workflows/build.yml) [![Deployed on Railway](https://img.shields.io/badge/Deployed%20on-Railway-7B2FBE?logo=railway&logoColor=white)](https://ai-code-review-agent-production-5a51.up.railway.app)

**Multi-agent AI system that fans out 5 specialist reviewers in parallel to analyze code, pull requests, and entire repositories — built with Microsoft.Extensions.AI on ASP.NET Core, deployable as a single binary.**

---

## 🚀 Live Demo

> **[https://ai-code-review-agent-production-5a51.up.railway.app](https://ai-code-review-agent-production-5a51.up.railway.app)**
>
> Paste any code snippet or point it at a public GitHub file or PR — five AI specialists analyze it in parallel and return a scored, line-annotated report in seconds. No sign-up required.

---

## Demo

![Demo](demo.gif)

*Code review in action — 5 agents run in parallel, results appear with line-level findings and copyable fixes*

---

## Features

| | Feature | Detail |
|---|---|---|
| ⚡ | **Parallel multi-agent execution** | All specialist agents run concurrently via `Task.WhenAll` — total latency = slowest single agent + one supervisor call, not the sum |
| 🧠 | **5 specialist agents per review + supervisor** | Bug Detector, Security Checker, Code Explainer, Fix Suggester, Onboarding Assistant — each with a structured output contract |
| 🏗️ | **Modernization Advisor** | 4 dedicated agents (TechDebt, Duplication, Framework, Architecture) produce a 3-priority roadmap: Fix Now / Fix Soon / Fix Later |
| 🔀 | **PR review with verdict** | Fetches unified diffs from GitHub, reviews file-by-file, returns APPROVE / REQUEST CHANGES / NEEDS DISCUSSION |
| 📂 | **GitHub repo crawler** | Walks any public repo tree up to 10 directory levels, auto-ranks files by `main`, `app`, `controller`, `service`, etc. |
| 🔧 | **Recommended Fixes panel** | Fix Suggester output rendered as diff-style `+`/`-` blocks per line reference, with a Copy All button |
| 💬 | **Chat with your code** | Multi-turn, context-aware Q&A backed by the same LLM — up to 10 turns, system prompt includes the full reviewed source |
| 🕐 | **Review history** | All reviews persisted in `localStorage` with score, verdict, provider, timestamp, and chat transcript |
| 📊 | **Multi-file session summary** | After a batch repo or PR review, shows average score, total findings by severity, best/worst file callouts, and a comparison table |
| 🔄 | **Dual LLM support** | Switch between Groq cloud (`llama-3.3-70b-versatile`) and local Ollama (`qwen2.5:3b`) from the sidebar — no restart needed |
| 📝 | **Markdown report export** | Every review saved as a `.md` file; in-browser Download button creates a self-contained report with agent outputs + chat transcript |
| 🐳 | **Docker + Railway ready** | Multi-stage Dockerfile, `PORT` and `REPORTS_DIR` env vars, deployed to Railway with a single push |

---

## Architecture

```
┌──────────────────────────────────────────────────────────────────┐
│  Browser  (index.html — vanilla JS + CSS, zero npm, zero build)  │
│                                                                    │
│  Sidebar: [Code Review] [Repo Review] [PR Review]                 │
│           [Modernization] [History]    Provider: [Groq] [Ollama]  │
└─────────────────────────┬────────────────────────────────────────┘
                           │  HTTP POST / GET  (same origin)
                           ▼
┌──────────────────────────────────────────────────────────────────┐
│  ASP.NET Core Minimal API  ·  Program.cs  ·  .NET 10             │
│                                                                    │
│   GET  /api/health          GET  /api/models                      │
│   GET  /api/pr-info         GET  /api/repo-files                  │
│   POST /api/chat            POST /api/review                      │
│   POST /api/review-pr-file  POST /api/modernize                   │
│                                                                    │
│  ┌─────────────── /api/review ──────────────────────────────┐    │
│  │  Task.WhenAll([                                           │    │
│  │    🐛 Bug Detector      → line-referenced bug list       │    │
│  │    🔒 Security Checker  → OWASP / credential scan        │    │
│  │    📖 Code Explainer    → purpose + key sections         │    │
│  │    🔧 Fix Suggester     → diff-style before/after pairs  │    │
│  │    🚀 Onboarding Asst.  → entry point + gotchas          │    │
│  │  ])                                                       │    │
│  │  → Supervisor call → SUMMARY · KEY BUGS · SCORE/10       │    │
│  └───────────────────────────────────────────────────────────┘    │
│                                                                    │
│  ┌─────────────── /api/review-pr-file ──────────────────────┐    │
│  │  Task.WhenAll([                                           │    │
│  │    🐛 Bug Detector      → diff-aware bug scan            │    │
│  │    🔒 Security Checker  → new attack surface check       │    │
│  │    📖 Change Explainer  → what changed + impact          │    │
│  │    🔧 Fix Suggester     → fixes for added lines only     │    │
│  │    🚀 Review Verdict    → APPROVE / REQUEST / DISCUSS    │    │
│  │  ])                                                       │    │
│  │  → Supervisor call → VERDICT · MUST FIX · SCORE/10       │    │
│  └───────────────────────────────────────────────────────────┘    │
│                                                                    │
│  ┌─────────────── /api/modernize ───────────────────────────┐    │
│  │  Task.WhenAll([                                           │    │
│  │    TechDebtAnalyzer       → debt + code smells           │    │
│  │    DuplicateCodeDetector  → DRY violations               │    │
│  │    FrameworkVersionChecker→ deprecated APIs + upgrades   │    │
│  │    ArchitectureReviewer   → coupling + god classes       │    │
│  │  ])                                                       │    │
│  │  → Supervisor call → 3-priority roadmap (Now/Soon/Later) │    │
│  └───────────────────────────────────────────────────────────┘    │
│                                                                    │
│  BuildClientAsync("groq" | "ollama")                              │
│     ├─ Groq:   OpenAIClient → llama-3.3-70b-versatile            │
│     └─ Ollama: OllamaChatClient → qwen2.5:3b                     │
│                    (warmed at startup via background Task)         │
└─────────────────────────┬────────────────────────────────────────┘
                           │
              ┌────────────┴───────────┐
              ▼                        ▼
   Groq API                    Ollama (localhost:11434)
   api.groq.com/openai/v1      IChatClient abstraction
   (Microsoft.Extensions.AI.OpenAI)   (Microsoft.Extensions.AI.Ollama)
              │
              └──→ Report saved to REPORTS_DIR as .md
                   (Desktop locally · /reports in Docker)
```

---

## Agents

### Code Review — 5 parallel agents + supervisor (`POST /api/review`)

| Agent | Emoji | Role | Output format |
|---|---|---|---|
| Bug Detector | 🐛 | Finds logic errors, null-reference risks, off-by-one errors, and runtime exceptions | `Line [N]: bug — why` · SEVERITY · MOST CRITICAL LINE |
| Security Checker | 🔒 | Audits for hardcoded credentials, injection vectors, CSRF gaps, and OWASP Top 10 | `Line [N]: type — risk` · RISK LEVEL · MOST CRITICAL LINE |
| Code Explainer | 📖 | Explains purpose in plain English, broken down by key line ranges | PURPOSE · LANGUAGE · KEY SECTIONS · HOW IT WORKS |
| Fix Suggester | 🔧 | Produces diff-style `- old / + new` snippets for every issue, pinned to exact lines | `Line [N]: explanation` + ` ```lang ... ``` ` fenced block |
| Onboarding Assistant | 🚀 | Writes an onboarding summary, identifies the best entry point, flags gotchas, lists dependencies | ONBOARDING SUMMARY · START READING HERE · WATCH OUT FOR · DEPENDENCIES |
| **Supervisor** | — | Synthesises all five reports into a single scored review | SUMMARY · KEY BUGS · SECURITY STATUS · TOP IMPROVEMENTS · WHAT THIS CODE DOES · SCORE X/10 |

### PR Review — 5 PR-specific agents + supervisor (`POST /api/review-pr-file`)

| Agent | Emoji | Role | Output format |
|---|---|---|---|
| Bug Detector | 🐛 | Hunts bugs introduced by the diff — added lines only, references unified diff line numbers | `Line [N]: bug — why` · SEVERITY · MOST CRITICAL LINE |
| Security Checker | 🔒 | Audits new attack surface introduced by the changeset | `Line [N]: vulnerability — risk` · RISK LEVEL |
| Change Explainer | 📖 | Describes what changed in plain English, lists key addition and deletion ranges | WHAT CHANGED · KEY CHANGES · IMPACT |
| Fix Suggester | 🔧 | Suggests diff-style fixes for issues in the added code | `Line [N]: explanation` + fenced diff block |
| Review Verdict | 🚀 | Issues a final recommendation with a must-fix list | VERDICT: APPROVE / REQUEST CHANGES / NEEDS DISCUSSION · MUST FIX BEFORE MERGE · NICE TO HAVE · SCORE |
| **Supervisor** | — | Synthesises all five into a complete PR review | PR REVIEW · VERDICT · SUMMARY · KEY BUGS · SECURITY STATUS · WHAT CHANGED · MUST FIX · SCORE X/10 |

### Modernization Advisor — 4 parallel agents + supervisor (`POST /api/modernize`)

| Agent | Role | Output format |
|---|---|---|
| TechDebtAnalyzer | Identifies technical debt, code smells, overly complex logic, and shortcuts with future maintenance risk | `Line [N]: issue` · actionable recommendations |
| DuplicateCodeDetector | Finds copy-paste patterns, repeated logic blocks, and DRY principle violations with consolidation suggestions | `Line [N]: duplication` · how to consolidate |
| FrameworkVersionChecker | Flags outdated libraries, deprecated APIs, old language patterns, and identifies modern replacements | `Line [N]: legacy approach` · modern equivalent |
| ArchitectureReviewer | Spots god classes, poor separation of concerns, tight coupling, missing abstractions, and circular dependencies | `Line [N]: structural issue` · architectural fix |
| **Supervisor** | Produces a 3-column prioritized roadmap with effort and business impact per item | PRIORITY 1 – FIX NOW · PRIORITY 2 – FIX SOON · PRIORITY 3 – FIX LATER; each item: Issue · Flagged by · Effort (Low/Medium/High) · Business impact |

---

## Tech Stack

| Technology | Version | Purpose |
|---|---|---|
| .NET | 10.0 | Runtime and SDK |
| ASP.NET Core | 10.0 | Minimal API host — `MapGet`/`MapPost`, static files, `UseDefaultFiles` |
| Microsoft.Extensions.AI | 9.5.0 | Provider-agnostic `IChatClient` abstraction |
| Microsoft.Extensions.AI.OpenAI | 9.6.0-preview | Groq via OpenAI-compatible endpoint (`https://api.groq.com/openai/v1`) |
| Microsoft.Extensions.AI.Ollama | 9.6.0-preview | Local inference via `OllamaChatClient` |
| Groq LLM | `llama-3.3-70b-versatile` | Primary cloud model — fast, highly capable |
| Ollama LLM | `qwen2.5:3b` | Local/offline model — warmed at startup |
| Frontend | Vanilla JS + CSS | Single `index.html`, zero build step, zero npm dependencies |
| Fonts | Inter + JetBrains Mono | Google Fonts — UI text and monospace code panes |
| GitHub REST API | v3 | Repo tree crawling, PR metadata + diffs, raw file fetching |
| Docker | Multi-stage | `mcr.microsoft.com/dotnet/aspnet:10.0` final image |
| Railway | — | Cloud deployment — reads root `Dockerfile`, binds `PORT=8080` |
| GitHub Actions | — | CI: restore → build Release → verify output |
| localStorage | — | Client-side history — up to 50 entries, survives page refresh |

---

## API Reference

| Method | Route | Auth Required | Description |
|---|---|---|---|
| `GET` | `/api/health` | No | Liveness probe — returns `"AI Code Review API is running!"` |
| `GET` | `/api/models` | No | Returns active model names: `{ groq: "llama-3.3-70b-versatile", ollama: "qwen2.5:3b" }` |
| `GET` | `/api/pr-info?prUrl=` | Optional PAT | Fetches PR title, author, state, and filtered list of changed code files |
| `GET` | `/api/repo-files?repoUrl=` | Optional PAT | BFS walks repo tree (≤10 pages), returns all code files + ranked recommendations |
| `POST` | `/api/review` | No | 5-agent code review + supervisor; body: `{ provider, mode, input, fileName }` |
| `POST` | `/api/review-pr-file` | No | 5-agent PR diff review + supervisor; body: `{ provider, prUrl, fileName, patch }` |
| `POST` | `/api/modernize` | No | 4-agent modernization analysis + supervisor; body: `{ provider, mode, input, fileName }` |
| `POST` | `/api/chat` | No | Multi-turn follow-up Q&A; body: `{ provider, systemPrompt, messages: [{role, content}] }` |

**Input modes** for `/api/review` and `/api/modernize`:

| `mode` | `input` | Notes |
|---|---|---|
| `"paste"` | Raw source code string | `fileName` used for context |
| `"github-file"` | `https://github.com/owner/repo/blob/main/file.py` | Rewritten to `raw.githubusercontent.com` internally |
| `"local-file"` | Absolute path on the server | Blocked with a helpful message in the hosted version |

All endpoints accept `provider: "groq"` (default) or `provider: "ollama"`. The `ReviewRequest` record also accepts legacy aliases `inputType`/`content` in place of `mode`/`input`.

**Code file extensions** recognised across all endpoints: `.cs` `.py` `.js` `.ts` `.java` `.cpp` `.c` `.go` `.rb` `.php` `.swift` `.kt` `.rs`

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- A free [Groq API key](https://console.groq.com/) **or** [Ollama](https://ollama.ai/) running locally with `qwen2.5:3b` pulled
- Git

### Quick Start

```bash
# 1. Clone
git clone https://github.com/saurabhshreni-cmyk/ai-code-review-agent.git
cd ai-code-review-agent

# 2. Set your Groq API key (Linux / macOS)
export GROQ_API_KEY=gsk_your_key_here

# Windows PowerShell
$env:GROQ_API_KEY = "gsk_your_key_here"

# 3. (Optional) Set a GitHub Personal Access Token for repo/PR features
#    Without this, GitHub's unauthenticated limit is 60 req/hr
export GITHUB_TOKEN=ghp_your_token_here

# 4. Run
dotnet run --project CodeReviewAPI/CodeReviewAPI.csproj
```

The API starts on `http://localhost:5161` and opens your browser automatically after 1.5 seconds.

**Alternative key delivery (local testing only):** create `Desktop/groq_key.txt` and paste your key as the only line. Same for `Desktop/github_token.txt`. The server reads these as fallbacks if the environment variables are absent.

### Local Ollama

```bash
# Pull the model once
ollama pull qwen2.5:3b

# Run the app — Ollama is warmed at startup automatically
dotnet run --project CodeReviewAPI/CodeReviewAPI.csproj
```

Select **Ollama** in the sidebar to switch providers without restarting.

### Docker

```bash
# Build from the repo root (uses the root Dockerfile)
docker build -t code-review-agent .

# Run
docker run -p 8080:8080 \
  -e GROQ_API_KEY=gsk_your_key_here \
  -e GITHUB_TOKEN=ghp_your_token_here \
  -e REPORTS_DIR=/reports \
  -v $(pwd)/reports:/reports \
  code-review-agent
```

Open **[http://localhost:8080](http://localhost:8080)**.

### Environment Variables

| Variable | Required | Default | Description |
|---|---|---|---|
| `GROQ_API_KEY` | Yes (for Groq) | `null` | Groq API key. Falls back to `Desktop/groq_key.txt`. If unset, Groq requests throw at call time — Ollama still works. |
| `GITHUB_TOKEN` | No | `""` | GitHub PAT with `repo:read`. Raises rate limit from 60 to 5,000 req/hr. Falls back to `Desktop/github_token.txt`. |
| `REPORTS_DIR` | No | User's Desktop | Directory where `.md` report files are written after each review. Set to `/reports` or similar in Docker. |
| `PORT` | No | `5161` | HTTP port the server binds to on `0.0.0.0`. Dockerfile sets `PORT=8080`. Railway injects this automatically. |

---

## Screenshots

### Code Review — agent cards with line-level findings
*[Screenshot coming soon]*

### Recommended Fixes — diff-style +/- blocks with Copy button
*[Screenshot coming soon]*

### Modernization Roadmap — 3-column Fix Now / Fix Soon / Fix Later
*[Screenshot coming soon]*

### Review History — scored card list with verdict badges
*[Screenshot coming soon]*

---

## Internship Context

This project was built during an internship at **HCLTech** (June–August 2026) under the **PE_Teams_Insight_FY24** programme.

The project represents a full learning arc through the **Microsoft Agent Framework** model of agent orchestration:

1. **Single agent** — one LLM call per review request
2. **Sequential agents** — multiple calls chained one after another
3. **Parallel agents** — `Task.WhenAll` across all specialists simultaneously, then a supervisor synthesises the results
4. **Full web API** — parallel agent orchestration exposed through a production-grade ASP.NET Core minimal API, deployed on Railway, with a complete UI built in vanilla JavaScript

The implementation uses `Microsoft.Extensions.AI` as the provider-agnostic `IChatClient` abstraction, allowing the same orchestration logic to drive both Groq's hosted API (via an OpenAI-compatible endpoint) and a locally-running Ollama instance without any code changes at the call site. The provider is resolved at request time via a single `BuildClientAsync("groq" | "ollama")` call, making the system fully swappable at runtime.

The live deployment at **[https://ai-code-review-agent-production-5a51.up.railway.app](https://ai-code-review-agent-production-5a51.up.railway.app)** runs the full agent pipeline in the cloud.

---

## Contributing

1. Fork the repository
2. Create a feature branch: `git checkout -b feat/your-feature`
3. Commit using conventional commits: `feat:`, `fix:`, `docs:`, etc.
4. Push and open a pull request against `main`

Please ensure `dotnet build` passes before submitting. There are no runtime secrets committed to the repository — all keys are injected via environment variables.

---

## License

[MIT](LICENSE) © 2026 Saurabh Shreni

---

<div align="center">

Built with **Microsoft.Extensions.AI** + **Groq** · Deployed on **Railway** · HCLTech Internship 2026

**[Open the live app →](https://ai-code-review-agent-production-5a51.up.railway.app)**

</div>

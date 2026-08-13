# AI Code Review Agent

> Multi-agent AI pipeline that reviews code, pull requests, and technical debt in parallel — built on .NET 10, Microsoft.Extensions.AI, Groq (LLaMA 3.3 70B), and Ollama.

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Live Demo](https://img.shields.io/badge/Live%20Demo-Visit-brightgreen)](LIVE_URL_HERE)
[![Build & Verify](https://github.com/saurabhshreni/csharp-code-review-agent/actions/workflows/build.yml/badge.svg)](../../actions/workflows/build.yml)

---

## Live Demo

**[→ Open the live app](LIVE_URL_HERE)**

The demo is fully functional. Paste any code snippet or point it at a public GitHub file and five AI specialists will review it in parallel, returning a scored report in seconds.

---

## What it does

This project ships a single-binary .NET 10 web API that orchestrates multiple AI agents to review source code concurrently. Each request fans out to a pool of specialist agents — bug detectors, security auditors, code explainers, fix suggesters — whose findings are then synthesized by a supervisor agent into a unified scored report. The frontend (a single self-contained `index.html`) renders results with line-number annotations, diff-style fix blocks, and an interactive chat panel so you can ask follow-up questions about any reviewed file.

---

## Features

| Feature | Detail |
|---|---|
| **Parallel agent execution** | All specialist agents run concurrently via `Task.WhenAll`, not sequentially |
| **Dual LLM providers** | Groq (`llama-3.3-70b-versatile`) for cloud speed; Ollama (`qwen2.5:3b`) for local/offline use |
| **Code Review mode** | Paste code, provide a GitHub file URL, or give a local path — 5 agents + supervisor |
| **PR Review mode** | Point at any GitHub PR URL; fetches diffs and reviews each changed file individually |
| **Repo Review mode** | Load a GitHub repository, pick files from the auto-ranked list, review in batch |
| **Modernization Advisor** | 4 debt/architecture specialists produce a 3-priority actionable roadmap (Fix Now / Fix Soon / Fix Later) |
| **Interactive chat** | After any review, ask follow-up questions about the file in a multi-turn chat panel |
| **Review history** | All past reviews are stored in `localStorage` and viewable in the History tab |
| **Markdown reports** | Every review is saved as a `.md` file to `REPORTS_DIR` (Desktop by default) |
| **Line-level findings** | Every agent references exact line numbers; clicking a finding scrolls the code pane to that line |
| **Diff viewer** | PR review mode renders added/removed lines in colour inside the code pane |
| **Recommended Fixes panel** | The Fix Suggester's output is rendered as diff-style before/after blocks with a Copy button |
| **Score circle** | Supervisor assigns a 1–10 score; animated circle turns green/amber/red accordingly |
| **GitHub token support** | Optional PAT raises the GitHub API rate limit from 60 to 5,000 req/hr |
| **Docker-ready** | Multi-stage Dockerfile targets `aspnet:10.0`; `PORT` and `REPORTS_DIR` are env-configurable |

---

## Agents

### Code Review (`POST /api/review`) — 5 agents + supervisor

| Agent | Emoji | Role |
|---|---|---|
| Bug Detector | 🐛 | Finds logic errors, null-reference risks, off-by-one errors, and runtime exceptions — with exact line numbers |
| Security Checker | 🔒 | Audits for hardcoded credentials, injection vectors, CSRF gaps, and OWASP Top 10 issues |
| Code Explainer | 📖 | Explains what the file does in plain English, broken down by key sections and line ranges |
| Fix Suggester | 🔧 | Produces diff-style before/after snippets for every issue it finds, pinned to exact lines |
| Onboarding Assistant | 🚀 | Writes an onboarding summary, identifies the best entry point, flags gotchas, and lists dependencies |
| **Supervisor** | — | Combines all five findings into a single report with a SUMMARY, KEY BUGS, SECURITY STATUS, TOP IMPROVEMENTS, WHAT THIS CODE DOES, and a SCORE out of 10 |

### PR Review (`POST /api/review-pr-file`) — 5 agents + supervisor

| Agent | Emoji | Role |
|---|---|---|
| Bug Detector | 🐛 | Hunts bugs in the diff — added lines only, with exact line numbers from the unified diff |
| Security Checker | 🔒 | Security-audits the changeset; flags new attack surface introduced by the PR |
| Change Explainer | 📖 | Describes what changed in plain English, listing key addition and deletion ranges |
| Fix Suggester | 🔧 | Suggests diff-style fixes for issues in the added code |
| Review Verdict | 🚀 | Issues APPROVE / REQUEST CHANGES / NEEDS DISCUSSION with a MUST FIX list and a score |
| **Supervisor** | — | Synthesises all five into a PR REVIEW report with VERDICT, SUMMARY, KEY BUGS, SECURITY STATUS, WHAT CHANGED, MUST FIX, and SCORE |

### Modernization Advisor (`POST /api/modernize`) — 4 agents + supervisor

| Agent | Role |
|---|---|
| TechDebtAnalyzer | Identifies technical debt, code smells, overly complex logic, and shortcuts with future maintenance risk |
| DuplicateCodeDetector | Finds copy-paste patterns, repeated logic blocks, and DRY principle violations |
| FrameworkVersionChecker | Flags outdated libraries, deprecated APIs, old language patterns, and their modern replacements |
| ArchitectureReviewer | Spots god classes, poor separation of concerns, tight coupling, missing abstractions, and circular dependencies |
| **Supervisor** | Produces a 3-column prioritized roadmap: PRIORITY 1 – FIX NOW / PRIORITY 2 – FIX SOON / PRIORITY 3 – FIX LATER, each item tagged with the originating agent, effort estimate, and one-sentence business impact |

---

## Tech Stack

| Layer | Technology |
|---|---|
| **Runtime** | .NET 10 (ASP.NET Core minimal API) |
| **AI abstraction** | `Microsoft.Extensions.AI` 9.5 |
| **Groq client** | `Microsoft.Extensions.AI.OpenAI` (OpenAI-compatible endpoint) |
| **Ollama client** | `Microsoft.Extensions.AI.Ollama` |
| **Groq model** | `llama-3.3-70b-versatile` |
| **Ollama model** | `qwen2.5:3b` |
| **Frontend** | Vanilla JS + CSS (single `index.html`, zero build step, zero npm) |
| **Fonts** | Inter (UI) + JetBrains Mono (code) via Google Fonts |
| **GitHub integration** | GitHub REST API v3 (repos, pulls, contents) |
| **Containerisation** | Docker (multi-stage, `mcr.microsoft.com/dotnet/aspnet:10.0`) |
| **CI** | GitHub Actions (`.github/workflows/build.yml`) |

---

## Architecture

```
Browser (index.html)
       │
       │  HTTP POST { provider, mode, input }
       ▼
┌─────────────────────────────────────────────────────┐
│  ASP.NET Core Minimal API  (Program.cs)             │
│                                                     │
│  /api/review          /api/modernize   /api/review- │
│       │                    │           pr-file      │
│       ▼                    ▼               │        │
│  ┌──────────┐        ┌──────────┐          │        │
│  │  Agent 1 │        │  Agent 1 │          ▼        │
│  │  Agent 2 │        │  Agent 2 │    ┌──────────┐   │
│  │  Agent 3 │──WhenAll  Agent 3 │──WhenAll       │   │
│  │  Agent 4 │        │  Agent 4 │    │  5 agents│   │
│  │  Agent 5 │        └──────────┘    └──────────┘   │
│  └────┬─────┘              │               │        │
│       │                    │               │        │
│       ▼                    ▼               ▼        │
│  Supervisor LLM call  Supervisor LLM  Supervisor    │
│  (combine + score)    (roadmap)       (PR verdict)  │
└─────────────────────────────────────────────────────┘
       │                         │
       ├── IChatClient (Groq)    │
       └── IChatClient (Ollama) ─┘
              BuildClientAsync("groq" | "ollama")
```

The key design: **all specialist agents for a single review run concurrently** (`Task.WhenAll`). The supervisor call runs after all agents complete, so total latency ≈ slowest single agent + one supervisor call, not the sum of all agents.

---

## API Endpoints

| Method | Route | Query / Body | Description |
|---|---|---|---|
| `GET` | `/api/health` | — | Liveness check — returns `"AI Code Review API is running!"` |
| `GET` | `/api/models` | — | Returns configured model names: `{ groq, ollama }` |
| `GET` | `/api/pr-info` | `?prUrl=` | Fetches PR title, author, state, and list of changed code files from GitHub |
| `GET` | `/api/repo-files` | `?repoUrl=` | Walks a repo's file tree and returns ranked code files with download URLs |
| `POST` | `/api/review` | `{ provider, mode, input, fileName }` | Runs 5-agent code review + supervisor; returns score, report, fixedCode, agentOutputs |
| `POST` | `/api/review-pr-file` | `{ provider, prUrl, fileName, patch }` | Runs 5-agent PR diff review + supervisor; returns verdict, score, report |
| `POST` | `/api/modernize` | `{ provider, mode, input, fileName }` | Runs 4-agent modernization analysis + supervisor; returns 3-priority roadmap |
| `POST` | `/api/chat` | `{ provider, systemPrompt, messages[] }` | Multi-turn chat about a reviewed file; appends to a running conversation |

All endpoints accept `provider: "groq"` (default) or `provider: "ollama"`.

Input modes for `/api/review` and `/api/modernize`:

| Mode | `mode` value | `input` value |
|---|---|---|
| Paste code | `"paste"` | Raw source code string |
| GitHub file URL | `"github-file"` | `https://github.com/owner/repo/blob/main/file.py` |
| Local file path | `"local-file"` | Absolute path on the server's filesystem |

---

## Running Locally

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- A [Groq API key](https://console.groq.com/) **or** [Ollama](https://ollama.ai/) running locally
- Git

### 1. Clone

```bash
git clone https://github.com/saurabhshreni/csharp-code-review-agent.git
cd csharp-code-review-agent
```

### 2. Set your Groq API key

**Option A — Environment variable (recommended):**

```bash
# Linux / macOS
export GROQ_API_KEY=your_key_here

# Windows (PowerShell)
$env:GROQ_API_KEY = "your_key_here"
```

**Option B — Desktop file (for quick local testing):**

Create `%USERPROFILE%\Desktop\groq_key.txt` and paste your key as the only line.

### 3. (Optional) GitHub token

To avoid GitHub's 60 req/hr unauthenticated rate limit when reviewing repos or PRs, create a Personal Access Token with `repo:read` scope and:

```bash
# Linux / macOS
export GITHUB_TOKEN=ghp_your_token_here

# Windows (PowerShell)
$env:GITHUB_TOKEN = "ghp_your_token_here"
```

Or write it to `%USERPROFILE%\Desktop\github_token.txt`.

### 4. (Optional) Local Ollama model

```bash
ollama pull qwen2.5:3b
```

The app warms the model at startup. Select "Ollama" in the sidebar to use it.

### 5. Run

```bash
dotnet run --project CodeReviewAPI/CodeReviewAPI.csproj
```

The app starts on `http://localhost:5161` and opens your browser automatically after 1.5 seconds.

### Running with Docker

```bash
docker build -t code-review-agent CodeReviewAPI/
docker run -p 8080:8080 \
  -e GROQ_API_KEY=your_key_here \
  -e REPORTS_DIR=/reports \
  -v $(pwd)/reports:/reports \
  code-review-agent
```

Then open `http://localhost:8080`.

---

## Environment Variables

| Variable | Required | Default | Description |
|---|---|---|---|
| `GROQ_API_KEY` | Yes (for Groq) | `null` | Groq API key. Falls back to `Desktop/groq_key.txt`. If unset, Groq requests throw at runtime — Ollama still works. |
| `GITHUB_TOKEN` | No | `""` | GitHub Personal Access Token. Raises API rate limit from 60 to 5,000 req/hr. Falls back to `Desktop/github_token.txt`. |
| `REPORTS_DIR` | No | User's Desktop | Directory where `.md` report files are written after each review. Set to a writable path in Docker (e.g. `/reports`). |
| `PORT` | No | `5161` | HTTP port the server binds to. Dockerfile sets this to `8080`. |

---

## Screenshots

[Screenshots coming soon]

---

## Internship Context

This project was built as part of an internship at **HCLTech** during **June–August 2026**, under the **PE_Teams_Insight_FY24** programme.

The agent orchestration model is inspired by the **Microsoft Agent Framework** — specifically the pattern of spawning independent specialist agents in parallel and aggregating their structured outputs through a supervisor LLM call. The implementation uses `Microsoft.Extensions.AI` as the provider-agnostic abstraction layer, allowing the same orchestration logic to work identically against Groq's hosted API and a locally-running Ollama instance without any code changes.

---

## License

[MIT](LICENSE) © 2026 Saurabh Shreni

---

*Built with .NET 10 · Microsoft.Extensions.AI · Groq · Ollama*

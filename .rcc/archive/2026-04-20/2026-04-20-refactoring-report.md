# Agent System Refactoring Report

**Date:** 2026-04-20 (resumed session)

## Changes Made

| # | Component | Change | Rationale |
|---|-----------|--------|-----------|
| R1 | `settings.json` | Added PostToolUse hook (dotnet build on .cs/.csproj/.sln edit) | Hook was in `.claude/` but not registered in settings — never fired |
| R2 | `settings.json` hook | Fixed timeout unit: `60000` → `60` (seconds) | Hook validator flagged millisecond value as invalid |
| R3 | `settings.json` hook | Added `$CLAUDE_PROJECT_DIR` anchor, `set -o pipefail`, `${PIPESTATUS[0]}` | Hook used hardcoded path; pipeline broke exit code propagation |
| R4 | `settings.local.json` | Removed Windows-only commands (taskkill, dir, del, findstr, powershell, timeout) | Cross-platform law violation; these break macOS/Linux |
| R5 | `CLAUDE.md` | Simplified 4 duplicate laws to pointer sentences referencing rule files | Duplicate content between CLAUDE.md laws and rule files |
| R6 | `CLAUDE.md` | Deleted Language section (duplicate of law) | Exact duplicate removed |
| R7 | `CLAUDE.md` | Fixed test project paths (added `.csproj` suffix) | Paths were ambiguous without project file extensions |
| R8 | `update-docs.md` | Renamed to `updating-docs.md`, fixed frontmatter name + description | Skill name not gerund form; description didn't start with "Use when..." |
| R8b | `updating-docs.md` | Added boundary clarification section (vs documentation-writer agent) | No documented separation of concerns between skill and agent |
| R9 | `chinese-conventions.md` | Deleted entirely | 100% duplicate of CLAUDE.md laws; pure dead weight |
| R10 | All `new-*.md` skills | Deleted (new-entity, new-repository, new-service, new-view, new-viewmodel, new-test, new-feature) | User uses superpowers for planning/implementation; scaffolding skills unused |
| R11 | `clean-architecture.md` | Added `paths: ["src/**/*.cs", "tests/**/*.cs"]`, updated diagram to show McpServer/Cli | Rule loaded unconditionally (session-start overload); diagram missing new layers |
| R12 | `mvvm-patterns.md` | Trimmed 77→25 body lines; removed code examples, kept abstract directives | Over 50-line limit; code blocks duplicated CLAUDE.md laws |
| R13 | `repository-pattern.md` | Trimmed 61→27 body lines; removed interface/impl code blocks, kept abstract directives | Over 50-line limit; procedural content belongs in skills not rules |
| R13b | `repository-pattern.md` | Fixed dead glob `**/*Repository.Tests.cs` → `**/*RepositoryTests.cs` | Glob matched zero files in repo |
| R14 | `cross-platform-scripts.md` | Removed dead `**/*.ps1` glob | No PowerShell files in project; dead glob wastes context |
| R15 | `code-reviewer.md` | Added standardized YAML output format `{pass, issues[]}` | No output contract; callers couldn't parse results |
| R15b | `code-reviewer.md` | Fixed description to start with "Use when..." | Description started with "Use after..." |
| R16 | `test-runner.md` | Narrowed to execution+analysis only; removed test-writing responsibility | Single responsibility violation; writing belongs in dev workflow |
| R16b | `test-runner.md` | Fixed description to start with "Use when..."; added `context: fork` | Description format invalid; verbose test output pollutes main context |
| R17 | `CLAUDE.md` | Updated dependency diagram to show McpServer and Cli | Architecture table listed McpServer/Cli but diagram didn't |
| R18 | All 3 agents | Fixed model names from full IDs → short names (opus/sonnet) | Full model IDs (`claude-opus-4-7`) invalid in agent frontmatter |
| R18b | `debugging-issues.md` | Removed workflow summary from description | "採用 TDD 方式系統性除錯" describes method not trigger condition |

## Before/After Comparison

| Metric | Before | After |
|--------|--------|-------|
| Agent components | 13 | 13 (modified) |
| Skills | 9 (including 7 unused new-*) | 2 (active only) |
| Rules | 5 | 4 (chinese-conventions deleted) |
| Session-start lines (CLAUDE.md + global rules) | ~389 | ~176 |
| Critical issues | 2 | 0 |
| Warnings | 8 | 0 |
| INFO items | 3 | 0 |
| Dead globs | 3 | 0 |
| Hook registered in settings | No | Yes |

## Remaining Items (INFO)

None — all Critical and Major issues from the review report are resolved.

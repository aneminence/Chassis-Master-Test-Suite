# Official ECC file provenance

- Upstream repository: https://github.com/affaan-m/ECC
- Upstream commit: 90606c8315ee09cafc303515c5f33024db6b9882
- License: MIT (see `.ai-workflows/LICENSE`; copyright Affaan Mustafa)
- Copied into CMTS on: 2026-10-24
- Policy: only common + C# pieces useful for a Windows desktop / sensor app.
  Full ECC language packs (Laravel, Django, Go, …) were **not** copied.
- Files under `.ai-workflows/custom/` are **CMTS-written**, not from ECC.

## Upstream paths (at that commit)

### skills/
- skills/security-review/ (+ cloud-infrastructure-security.md)
- skills/tdd-workflow/
- skills/verification-loop/
- skills/csharp-testing/
- skills/coding-standards/
- skills/change-impact/

### agents/
- agents/code-reviewer.md
- agents/architect.md
- agents/csharp-reviewer.md

### rules/
- rules/common/coding-style.md, security.md, testing.md, code-review.md
- rules/csharp/coding-style.md, testing.md, security.md, patterns.md

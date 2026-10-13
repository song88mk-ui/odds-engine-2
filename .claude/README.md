# .claude/ — odds-engine-2

Repo-specific commands, skills and task guides, plus `settings.json` (the standing-rules prompt hook).
Read [../CLAUDE.md](../CLAUDE.md) and [../AGENTS.md](../AGENTS.md) first.

## commands/
/trace-fixture · /trace-market · /trace-selection · /trace-websocket · /trace-platform-api ·
/trace-feature · /trace-request · /find-pattern · /find-service · /find-component ·
/impact-analysis · /review-feature · /security-review · /performance-review ·
/investigate-production (read-only) · /update-docs

## skills/
feature-development · architecture-review · platform-api-investigation · websocket-investigation ·
mongodb-investigation · sql-investigation · translation-investigation · performance-analysis ·
regression-analysis · bug-investigation · production-investigation · code-review ·
repository-analysis · jira-updates · blazor-development (redirects to platform-web)

## playbooks/
implement-feature · investigate-bug · trace-websocket · trace-api · production-readonly ·
performance-investigation · regression-investigation

## checklists/
standard-workflow (every task) · before-coding · before-pull-request · architecture-review ·
regression-review · production-investigation · deployment-review · documentation-review

## templates/
new-service · new-message-type · mongo-collection-change

## prompts/
onboard · change-proposal · production-safe

## Rules and synced files

Workflow and rules: [../CLAUDE.md](../CLAUDE.md) (standing rules, § Development workflow, § Engineering principles).

The `jira-updates` skill and [`.github/pull_request_template.md`](../.github/pull_request_template.md) are
generated from `song88-claude` by `sync_standing_rules.py`. Do not edit them here; change them in
`song88-claude`.

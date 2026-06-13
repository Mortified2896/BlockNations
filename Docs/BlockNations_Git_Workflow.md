# BlockNations Git Workflow

This note documents the local Git/GitHub workflow for completed BlockNations work from this macOS machine.

## Repository

```text
/Users/Jo/GitHub/BlockNations
```

## Remote

The normal `origin` remote uses SSH:

```text
git@github.com:Mortified2896/BlockNations.git
```

Normal push command:

```bash
git push origin main
```

## Local Git identity

This repository uses repo-local Git identity:

```text
Jo <jo@users.noreply.github.com>
```

Do not change global Git config for this project unless explicitly requested.

## SSH authentication

GitHub SSH authentication is expected to authenticate as:

```text
Mortified2896
```

A successful SSH auth check looks like:

```text
Hi Mortified2896! You've successfully authenticated, but GitHub does not provide shell access.
```

Do not print private key contents or secrets when diagnosing GitHub auth.

## Preferred completion workflow

For completed, validated BlockNations work, prefer:

1. Implement the scoped change.
2. Run relevant checks/tests.
3. Commit the cleanly scoped change.
4. Push to `origin main`.

Avoid leaving completed local commits unpushed unless the user explicitly asks to hold them locally.

## Safety rule

Only push cleanly scoped commits after relevant validation has passed. If unrelated uncommitted changes exist, leave them out of the commit unless they are explicitly part of the requested task.

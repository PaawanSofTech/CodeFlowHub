# CodeFlow Remote/Push/Clone Testing Guide

This guide validates the CLI behavior around remote setup, HTTP push, and clone.

## 1) Misconfigured remote guard (expected failure with clear message)

Use an HTTP API URL with `remote add` (S3 mode) to confirm fast-fail guidance:

```bash
codeflow remote add origin https://codeflow-api-qbd9.onrender.com my-bucket codeflow codeflow123
```

Expected: CLI rejects this and instructs you to use:

```bash
codeflow remote add-http <name> <api-url>
```

## 2) Correct HTTP remote flow

Add the remote using HTTP mode and push:

```bash
codeflow remote add-http api https://codeflow-api-qbd9.onrender.com
codeflow push api
```

Expected:
- repo ensure step succeeds
- objects upload (or skip if already present)
- branch refs update successfully
- final push summary reports uploaded/skipped/failed counts

## 3) Verify remote head/branch API response

After push, verify branch metadata is present:

```bash
curl https://codeflow-api-qbd9.onrender.com/api/repos/<owner>/<repo>/objects/head
```

Expected JSON contains both:
- `head` (commit hash)
- `branch` (active branch name, e.g. `main`; should not be null for branch pushes)

## 4) Clone validation

Clone from HTTP remote:

```bash
codeflow clone https://codeflow-api-qbd9.onrender.com <owner>/<repo> cloned-repo
```

Expected:
- auth flow/token setup succeeds
- pull fetches reachable objects
- local checkout lands on the reported branch (not detached unless remote is detached)


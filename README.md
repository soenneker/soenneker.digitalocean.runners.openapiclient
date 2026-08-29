[![](https://img.shields.io/github/actions/workflow/status/soenneker/Soenneker.DigitalOcean.Runners.OpenApiClient/build-and-test.yml?style=for-the-badge)](https://github.com/soenneker/Soenneker.DigitalOcean.Runners.OpenApiClient/actions/workflows/build-and-test.yml)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/Soenneker.DigitalOcean.Runners.OpenApiClient/daily-automatic-update.yml?style=for-the-badge&label=Daily%20Update)](https://github.com/soenneker/Soenneker.DigitalOcean.Runners.OpenApiClient/actions/workflows/daily-automatic-update.yml)

# Soenneker.DigitalOcean.Runners.OpenApiClient

Provides file cleanup and filesystem operations used by the generated-client update workflow.

> This is an automation runner, not a package intended for application consumption.

## What the runner does

- `IFileOperationsUtil.Process(cancellationToken)` — Runs the OpenAPI client regeneration workflow, including cleanup and post-processing.

## What you get

- `IFileOperationsUtil` — Provides file cleanup and filesystem operations used by the generated-client update workflow.

## API at a glance

| API | What it does | Result / important behavior |
| --- | --- | --- |
| `IFileOperationsUtil.Process(cancellationToken)` | Runs the OpenAPI client regeneration workflow, including cleanup and post-processing. | A task that completes when the full processing workflow has finished. |

## Practical notes

- Cancellation stops pending work; it does not undo work that has already completed.

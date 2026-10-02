# Codex Reset

A Windows 11 desktop widget that shows expected Codex reset times, public announcements and reset history.

[简体中文](docs/README.zh-CN.md)

The current version is **1.0.0-rc.2**, a portable release candidate. Final user acceptance and testing on a clean Windows installation are still pending. See the [validation record](docs/validation.md) for the tested boundaries and remaining environments.

## Run

Download the Windows x64 portable ZIP from [GitHub Releases](https://github.com/Mengman/codex-reset-widget/releases), extract the entire archive and run `CodexResetWidget.exe`. The package includes .NET; no separate runtime installation is required. Local builds are available under `artifacts/releases/`.

Before upgrading, exit the previous version from its tray menu. Extract the new version to a new folder and run it. Compatible settings and cached data are retained.

## Features

- Start in compact mode with the reset countdown. Expand to show announcements and the calendar; the app remembers your choice.
- Browse announcements with the previous and next buttons. Long posts scroll inside a fixed-height card.
- Drag the title bar to move the window. Use the pin button to keep it on top.
- Closing the window hides it in the tray. Use **Exit** in the widget or tray menu to quit.
- The widget stays out of the taskbar and Alt+Tab. Click its tray icon to bring it forward or restore it.
- **More → Start with Windows** enables automatic launch when you sign in. It is off by default and changes only after you choose it.
- Choose the system, light or dark theme. Dates follow your computer's time zone.
- After the announced time, show the last expected reset time without claiming that your personal quota has been restored.

### Language

English and Simplified Chinese are supported. By default, the app follows the Windows display language: both Simplified and Traditional Chinese select Simplified Chinese; all other languages select English.

Use **More → Language** to select **Use system language**, **English** or **Simplified Chinese**. The selection is saved and applied immediately. Changing the interface language preserves the selected announcement, calendar month and reading position. Original announcement text is displayed as provided by the source.

![English widget layout](docs/design/widget-layout.en.png)

Dates and posts in the illustration are layout examples. The [timer logo](docs/design/countdown-logo.svg) is shared by both languages. The [Chinese layout](docs/design/widget-layout.png) is also available.

## Build and test

The project uses C#, WPF and .NET 10. `global.json` pins SDK **10.0.401**. Install that SDK system-wide or under `.tools/dotnet/`.

```powershell
.\scripts\build.ps1                    # Release build and functional tests
.\scripts\build.ps1 -Publish -RunUiChecks -RunDesktopChecks -RunLiveChecks
```

Live checks need access to the public API. Omit `-RunLiveChecks` for offline package and desktop checks. To test an upgrade, also pass `-UpgradeDataDirectory <test-data-folder>` containing `settings.json` and `cache/snapshot.json`; the verifier copies them to an isolated folder before use.

Artifacts are written to `artifacts/releases/`. The ZIP includes instructions, runtime licenses and `RELEASE.json` with file hashes. A separate `.sha256` file verifies the ZIP. SDK files, dependency caches and generated artifacts are excluded from Git.

Tests are organized by functionality, including language policy, resource consistency, live switching and settings compatibility. See [development and testing](docs/development.md) for the full grouping and package-verification commands.

## CI and releases

GitHub Actions builds and verifies the Windows x64 package on pull requests and pushes to `main`. The CI workflow can also be started manually. Reports and portable packages are available as workflow artifacts for 14 days.

To publish, tag a commit that includes the workflows, then push the tag:

```powershell
git tag -a v1.0.0 -m "Release 1.0.0"
git push origin v1.0.0
```

Tags use `vMAJOR.MINOR.PATCH`, optionally with a prerelease suffix such as `v1.0.1-rc.1`. The tag supplies the program version, About version, ZIP name and package manifest; editing the project version first is unnecessary. Prerelease tags create GitHub Prereleases, and plain version tags create regular Releases.

The release workflow runs functional tests, WPF scene checks and package verification before publishing the ZIP and its SHA256 file. It creates a draft, uploads both assets, then publishes the release with generated notes. Failed uploads leave the draft available for a rerun; reruns replace assets of the same name. The built-in `GITHUB_TOKEN` handles publishing; no personal access token is needed. Actions must be enabled and repository policy must allow the publish job's `contents: write` permission. [GitHub documents that permission here](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#permissions).

Hosted CI uses Windows Server runners. Physical desktop, monitor, tray and real-network checks remain separate local checks. To reproduce a tag build locally, run `./scripts/build.ps1 -Publish -RunUiChecks -Version 1.0.0`. Do not move or reuse a published version tag.

## Documentation

The detailed project documents currently use Simplified Chinese:

- [Requirements](docs/requirements.md): current scope, data semantics and acceptance criteria.
- [Architecture](docs/technical-design.md): modules, API, reset state, cache and Windows integration.
- [Visual specification](docs/design/visual-spec.md) and [UI layout](docs/design/ui-layout.md): current dimensions, themes and icons.
- [Development and testing](docs/development.md): builds, functional tests and UI checks.
- [Validation record](docs/validation.md): release results, screenshots and unverified environments.
- [English usage instructions](docs/release-usage.en.txt) and [Chinese usage instructions](docs/release-usage.txt).
- [Third-party notices](docs/third-party-notices.txt) and [MIT license](LICENSE).

## Data and storage

Data from [Codex Resets](https://codex-resets.com/). The app reads its status and history APIs and uses the parsed expected time. It does not scrape X, predict reset dates, sign in to OpenAI or read your personal quota. **Check Codex for your actual quota.**

Settings, cache and bounded logs are stored under `%LocalAppData%/CodexResetWidget/`. The current package targets Windows 11 x64. Notifications, history filters, alternative sources, ARM64 and an installer are future options.

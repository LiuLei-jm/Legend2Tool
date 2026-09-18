# Repository Guidelines

## Project Structure & Module Organization

`Legend2Tool.sln` contains the .NET 8 WPF application in `src/Legend2Tool.WPF/`. The project follows MVVM: XAML views and their code-behind are in `Views/`, UI state and commands are in `ViewModels/`, and file/configuration operations belong in `Services/`. Domain objects are organized under `Models/`; shared state, messages, converters, enums, and attributes have their own folders. Put static files and embedded templates in `Resources/`. Treat `Backup/` as historical material, not an active project target. Do not commit generated `bin/`, `obj/`, or `.vs/` files.

## Build, Test, and Development Commands

Run these commands from the repository root on Windows:

- `dotnet restore Legend2Tool.sln` restores NuGet dependencies.
- `dotnet build Legend2Tool.sln` compiles the Debug configuration.
- `dotnet run --project src/Legend2Tool.WPF/Legend2Tool.WPF.csproj` starts the WPF application.
- `dotnet build Legend2Tool.sln -c Release` verifies the release build.
- `dotnet publish src/Legend2Tool.WPF/Legend2Tool.WPF.csproj -c Release -r win-x64 --self-contained true` creates the self-contained Windows package.

## Coding Style & Naming Conventions

Use four-space indentation in C# and preserve surrounding XAML formatting. Nullable references and implicit usings are enabled. Use `PascalCase` for types, methods, and public properties; `_camelCase` for private fields; and `I` prefixes for interfaces. Keep feature pairs aligned, for example `PortConfView.xaml`, `PortConfViewModel.cs`, and related service interfaces. Put UI behavior in view models and filesystem or configuration logic in services. Follow the existing CommunityToolkit.Mvvm attributes and messaging patterns.

## Testing Guidelines

No test project or coverage threshold currently exists. For behavior changes, add tests under `tests/`, for example `tests/Legend2Tool.WPF.Tests`, and name tests after outcomes such as `LoadConfig_InvalidPath_ReturnsFailure`. Run the full suite with `dotnet test Legend2Tool.sln`. Until coverage is established, build the solution and manually exercise every affected screen and supported engine configuration.

## Commit & Pull Request Guidelines

Use short, imperative commit subjects, such as `Add DialogService` or `Optimizing scripts`. Keep each commit focused on one logical change. Pull requests should explain the user-visible result, list verification steps, link related issues, and include screenshots for XAML/UI changes. Explicitly call out changes to configuration parsing, file encodings, generated resources, or deployment behavior.

## Agent Workflow

For non-trivial changes:

- Inspect the relevant code before editing.
- Establish the actual execution path.
- Identify the root cause before modifying code.
- Make the smallest coherent change.
- Modify only relevant files.
- Verify the result.

Do not guess when repository evidence can confirm the answer.

## verification

- Run relevant tests when available.
- Run `dotnet build`.
- Report failed verification.
- Do not claim completion when verification fails.
- Summarize changed files and verification performed.

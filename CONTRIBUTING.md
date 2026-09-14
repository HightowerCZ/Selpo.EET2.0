# Contributing to Selpo.EET2.0

Thanks for your interest in contributing! This project welcomes bug reports, feature suggestions, and pull requests.

## Ground rules

- There is no direct push access to `main` for anyone other than the maintainer. All changes — including the maintainer's own — go through pull requests.
- Every pull request must be reviewed and approved before it can be merged.
- Continuous integration (`.github/workflows/ci.yml`) must pass (build + tests on both `net481` and `net10.0`) before a pull request can be merged.

## How to contribute

1. **Fork the repository** on GitHub (there is no need to request collaborator access — forking is the expected workflow for all contributors).
2. **Clone your fork** locally:
   ```bash
   git clone https://github.com/<your-username>/Selpo.EET2.0.git
   cd Selpo.EET2.0
   ```
3. **Create a feature branch** off `main`:
   ```bash
   git checkout -b feature/short-description
   ```
4. **Make your changes.** Keep pull requests focused on a single concern (one bug fix or one feature per PR) to make review easier.
5. **Build and test locally** before opening a PR:
   ```powershell
   dotnet build Selpo.EET2.0.sln
   dotnet test Selpo.EET2.0.sln
   ```
6. **Commit and push** your branch to your fork:
   ```bash
   git add .
   git commit -m "Describe your change"
   git push origin feature/short-description
   ```
7. **Open a pull request** against `HightowerCZ/Selpo.EET2.0:main`, describing what changed and why.
8. Address any review feedback. Once the PR is approved and CI passes, it will be merged by the maintainer.

## Code style and conventions

- Follow the existing coding conventions in the codebase (see `.editorconfig`).
- Public APIs should have XML documentation comments, consistent with the existing types.
- Prefer adding or updating unit tests (`tests/Selpo.EET2.0.Tests`) alongside behavioral changes.
- Avoid introducing new third-party dependencies unless there's a strong justification; this library aims to stay lightweight.
- The library targets both `net481` and `net10.0` — avoid APIs that are unavailable on .NET Framework 4.8.1 unless guarded with `#if NET` / `#if NET10_0_OR_GREATER` conditional compilation, matching the existing pattern (see `EetClient.cs`).

## Reporting issues

If you find a bug or have a feature request, please open a GitHub issue with:

- A clear description of the problem or proposal.
- Steps to reproduce (for bugs), including relevant configuration (target framework, `EetClientOptions` used, etc. — never include real certificates, passwords, or production credentials).
- Expected vs. actual behavior.

## Security

Please do not open public issues for security-sensitive reports (e.g. issues related to certificate handling or signing). Instead, contact the maintainer directly.

## License

By contributing, you agree that your contributions will be licensed under the project's [MIT License](LICENSE).

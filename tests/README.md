# tests

Automated tests for the application in [`src/`](../src/): xUnit in [`TextTemplateManager.Tests/`](TextTemplateManager.Tests/).

Run them with `pwsh -File tests/run-tests.ps1` (optionally `-Filter <name>`). The script builds the app unpackaged and self-contained, which the tests need; a plain `dotnet test` fails with `REGDB_E_CLASSNOTREG`. Every test runs against a throwaway data folder (`TTM_DATA_DIR` is set before any test starts), never against real templates or settings. What is covered: [Architecture, page 12](../docs/architecture/12-testing.md).

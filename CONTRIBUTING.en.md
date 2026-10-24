# Contributing to Postacı

[Türkçe](CONTRIBUTING.md) · **English**

Thank you for spending your time on Postacı. Postacı is not a company product but **a personal open-source project
that anyone can help develop**, and most of it was written with AI assistance (details in the
[README](README.en.md#built-with-ai)). Don't hesitate even if this is your first contribution; if you don't know
where to start, just open an issue and ask.

This guide covers setting up the development environment, how the code is organized, adding a new Exchange
operation, writing tests, the rules for contributing with AI tools, and the pull request process.

Short path: [Environment](#development-environment) → [Build and test](#build-and-test) →
[Make the change](#layering-rules) → [Add tests](#writing-tests) → [Open a PR](#pull-request-process).

## Contents

- [Before you start](#before-you-start)
- [Privacy rule](#privacy-rule)
- [Contributing with AI tools](#contributing-with-ai-tools)
- [Development environment](#development-environment)
- [Build and test](#build-and-test)
- [Repository layout](#repository-layout)
- [Layering rules](#layering-rules)
- [Code style](#code-style)
- [Adding a new EWS operation](#adding-a-new-ews-operation)
- [UI (WinUI) development](#ui-winui-development)
- [Writing tests](#writing-tests)
- [Commit and branch conventions](#commit-and-branch-conventions)
- [Pull request process](#pull-request-process)
- [Releasing (maintainers)](#releasing-maintainers)
- [Regenerating the icons](#regenerating-the-icons)
- [Common problems](#common-problems)

## Before you start

- Any kind of contribution counts: bug reports, suggestions, documentation and typo fixes, tests, reports from
  trying it on a real server, code. Issues labelled **`iyi ilk issue`** (good first issue) are picked for newcomers.
- Our community rules are in the [Code of Conduct](CODE_OF_CONDUCT.en.md); everyone taking part follows them.
- Use the [issue templates](.github/ISSUE_TEMPLATE) for **bug reports** and **feature requests**. Check first
  whether an issue on the same topic is already open. Issues, PRs and commit messages may be written in English or
  Turkish.
- **Do not report security vulnerabilities as issues**; use the private reporting channel described in the
  [security policy](SECURITY.en.md).
- If you are planning a large change (a new screen, a new dependency, an architectural change), open an issue to
  discuss the approach before you start coding, so no effort is wasted.
- For small fixes (a typo, clearly wrong behaviour) you can open a PR directly.
- The project is distributed under the [MIT License](LICENSE). By submitting a PR you agree that your contribution
  is published under the same license. If you copy code from another project, make sure its license is compatible
  with MIT and name the source in the PR description.

## Privacy rule

Postacı works with corporate mailboxes. Therefore **never** put any of the following into the repository, issues,
PRs, test data or screenshots:

- real email addresses, people's names, phone numbers,
- real mail, meeting or contact content,
- internal server names, EWS URLs, domain names,
- passwords, tokens or network captures (Fiddler/Wireshark output, etc.).

Use made-up values in test data, such as `sirket.com`, `mail.test.local`, `ad.soyad@sirket.com` ("şirket" means
"company"). If you need to share a SOAP response as an example, first replace all names, addresses, identifiers
(`ItemId`, `ChangeKey`) and content.

If you notice that confidential data has been shared by mistake (by you or someone else), tell the maintainers
immediately; the content will be deleted and, if necessary, removed from the Git history as well. If a password or
token leaked, change it right away: even deleted content may survive in caches or forks.

## Contributing with AI tools

Since the project itself was developed with AI assistance, contributions prepared with tools such as Claude,
Copilot or ChatGPT are welcome too. The only conditions are transparency and responsibility:

1. **Disclose it.** In the "AI usage" section of the PR template, briefly state which tool you used and for what.
   Adding a `Co-Authored-By:` trailer to your commits is encouraged but not required.
2. **Own it.** Read and understand every line you submit; during review you must be able to answer "why is it like
   this?". "The AI wrote it this way" is not a valid justification.
3. **Verify it.** Run the tests locally and try UI changes on Windows. AI tools often "invent" EWS elements,
   attributes or APIs that do not exist; check against Microsoft's documentation.
4. **Keep it small.** Open focused, reviewable PRs instead of hundreds of lines of unreviewed generated code.
5. **Don't feed it secrets.** Never paste passwords, tokens, real mail content, internal server names or another
   company's source code into an AI tool. The [privacy rule](#privacy-rule) applies here too.
6. **Mind the license.** Don't submit long pieces of code you suspect were reproduced verbatim from another
   project; your contribution must be publishable under the MIT license.
7. **This applies to issues too.** Before submitting a bug report written by an AI, check that you can actually
   reproduce it.

Reviews use the same criteria whether or not a contribution was prepared with AI.

## Development environment

| What you want to do | What you need |
|---|---|
| Build and run the full app | Windows 10 (19041+) or 11, [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), WebView2 Runtime (ships with Windows 11) |
| Work in Visual Studio (optional) | Visual Studio 2026 or 2022 17.14+ with the **WinUI application development** workload |
| Only `Core` / `ViewModels` / tests | The .NET 10 SDK is enough; works on Linux and macOS too |
| Regenerate the icons | Python 3, Node.js and Playwright (Chromium) |

You can contribute even without access to a real Exchange server: all code that talks to Exchange is tested against
a [fake server](#writing-tests). UI changes, however, need to be tried by running the app on Windows.

Recommended editor setup:

- [`.editorconfig`](.editorconfig) is picked up automatically by Visual Studio, Rider and VS Code (C# Dev Kit).
- Files are UTF-8 with 4-space indentation; line endings are normalized by Git via
  [`.gitattributes`](.gitattributes) (YAML, Python and shell scripts are always LF).

## Build and test

```powershell
# Restore dependencies and build the libraries (any OS)
dotnet build src/ExchangeMail.Core
dotnet build src/ExchangeMail.ViewModels

# Run the tests (any OS)
dotnet test --project tests/ExchangeMail.Core.Tests

# Run a single test class or test
dotnet test --project tests/ExchangeMail.Core.Tests --filter-class "*FeatureTests"
dotnet test --project tests/ExchangeMail.Core.Tests --filter-method "*Saving_an_existing_draft*"

# Build and run the app (Windows only)
dotnet build src/ExchangeMail.App -c Debug -p:Platform=x64
./src/ExchangeMail.App/bin/x64/Debug/net10.0-windows10.0.19041.0/win-x64/Postaci.exe
```

Notes:

- The tests run on **Microsoft.Testing.Platform** (selected by [`global.json`](global.json)). That's why
  `dotnet test` is given a project with `--project` rather than a folder, and filters use the xUnit v3 options
  (`--filter-class`, `--filter-method`, `--filter-trait`).
- Don't run `dotnet build ExchangeMail.slnx` on Linux/macOS: the WinUI project's XAML compiler only runs on Windows.
  Build the libraries and the test project individually.
- The app is built unpackaged and self-contained; no MSIX or Windows App SDK runtime installation is needed.

## Repository layout

```text
.
├── src/
│   ├── ExchangeMail.Core/          # EWS client and models (no UI, platform-independent)
│   │   ├── Ews/                    # EwsClient (partial), Autodiscover, Xml helpers, errors
│   │   ├── Localization/           # Strings.resx (Turkish), Strings.en.resx (English), Loc
│   │   └── Models/                 # Mail, Calendar, Contact, AutoReply records
│   ├── ExchangeMail.ViewModels/    # MVVM layer (CommunityToolkit.Mvvm), settings, formatting
│   └── ExchangeMail.App/           # WinUI 3 app (Windows only)
│       ├── Views/                  # Pages, windows, dialogs (XAML + code-behind)
│       ├── Controls/               # HtmlEditor, WeekGridView, AvailabilityView
│       ├── Services/               # Credentials, notifications, tray, startup, fonts
│       ├── Helpers/                # Ui, HtmlViewer
│       └── Assets/                 # Postaci.ico, Postaci-unread.ico, PNGs
├── tests/ExchangeMail.Core.Tests/  # xUnit v3 tests + FakeEwsServer
├── assets/icon/                    # Icon sources and generator scripts
└── .github/                        # CI, release, Dependabot, issue/PR templates
```

> Project folders and namespaces carry the historical name `ExchangeMail`; the product name, executable and data
> folder are `Postacı` / `Postaci`. Use `AppInfo.Name` to get the product name from a single place.

## Layering rules

Dependencies go one way: **App → ViewModels → Core**.

| Layer | Contains | Does not contain |
|---|---|---|
| **Core** | EWS SOAP requests, response parsing, models, Autodiscover | UI, settings files, Windows APIs, references to `ViewModels` |
| **ViewModels** | Screen state, commands, settings (`UserPreferences`), HTML helpers (`MailFormat`) | WinUI types (`Microsoft.UI.*`), `DispatcherQueue`, P/Invoke |
| **App** | XAML, visual behaviour, Windows services (Credential Manager, notifications, tray, registry) | Business logic, direct EWS calls |

In practice:

- If a ViewModel needs something platform-specific, define an interface in ViewModels (e.g. `ICredentialStore`,
  `IStartupService`) and implement it in App. That way the ViewModel runs with a fake implementation in tests.
- ViewModels surface exception messages to the user through properties such as `ErrorMessage` / `StatusText`;
  don't swallow errors. If you do swallow an error the user doesn't need to know about (e.g. the password could
  not be saved), explain why in a comment.
- Identifiers (class, method, variable names) and code comments are written in **English**. User-facing text is
  never written in code; it goes into the [translation resources](#ui-text-and-translations).

### UI text and translations

The interface is in Turkish and English. All user-facing text lives in `src/ExchangeMail.Core/Localization/`:

| File | Contents |
|---|---|
| `Strings.resx` | Turkish (the default language) |
| `Strings.en.resx` | English |
| `Loc.cs` | Language selection, formatting (`Loc.Format`) and singular/plural (`Loc.Count`) |

- **When adding text**, add the same key to **both files**. Key names follow `Area_What`
  (e.g. `Mail_Reply`, `Calendar_EventCreated`, `Common_Cancel`). Visual Studio's resource editor or any text editor
  works.
- **In C#** use the class generated at build time: `Strings.Mail_Reply`. For text with values use a `{0}`
  placeholder and `Loc.Format(Strings.Contacts_DeleteQuestion, name)`; for counts add a singular key with the `_One`
  suffix and use `Loc.Count(count, Strings.Mail_ItemCount_One, Strings.Mail_ItemCount)` (both are the same in
  Turkish).
- **In XAML** use the markup extension: `Text="{h:L Key=Mail_Reply}"` (`xmlns:h="using:ExchangeMail.App.Helpers"`).
  Texts are read once when a page loads, which is why changing the language needs a restart.
- **For dates**, don't hard-code patterns; use standard formats such as `"D"`, `"t"`, `"Y"` or the
  `MailFormat.DayMonth` / `DayMonthYear` helpers, so day–month order and 12/24-hour time follow the language.
- `LocalizationTests` checks that both files have the same keys and placeholders, that every key used in XAML
  exists, and that no hard-coded Turkish text is left in XAML or code. It cannot catch a hard-coded string without
  Turkish-specific letters (e.g. `"Kaydet"`), so keep an eye out for those in reviews.
- **To add a new language**, create `Strings.<lang>.resx`, register the language in `Loc` and add it to the language
  list in Settings. Please open an issue first to let us know.

## Code style

Formatting rules live in [`.editorconfig`](.editorconfig); the build must pass without warnings. In addition:

- **Nullable is enabled**, implicit usings are on, `LangVersion` is latest
  ([`Directory.Build.props`](Directory.Build.props)). Prefer an explicit check over `!` (null-forgiving) where
  possible.
- **File-scoped namespaces** (`namespace X;`), private fields `_camelCase`, constants and `static readonly` fields
  `PascalCase`.
- Async methods end with `Async` and take `CancellationToken ct = default` as the last parameter; pass the token all
  the way down to `ExecuteAsync`.
- Use the CommunityToolkit.Mvvm source generators for MVVM:

  ```csharp
  public partial class ExampleViewModel : ObservableObject
  {
      [ObservableProperty]
      [NotifyPropertyChangedFor(nameof(CanSave))]
      private string _subject = "";

      public bool CanSave => Subject.Length > 0;

      [RelayCommand]
      private async Task SaveAsync(CancellationToken ct) { /* ... */ }
  }
  ```

- Comments should explain **why**, not what. In particular, add a short comment to every line that relies on an
  unusual Exchange behaviour (e.g. *"Exchange requires a change key unless ConflictResolution is
  AlwaysOverwrite"*), so the next person doesn't delete it as "unnecessary".
- Discuss in an issue before adding a new NuGet dependency. Added packages should fit the Dependabot groups
  ([`dependabot.yml`](.github/dependabot.yml)).

## Adding a new EWS operation

Instead of the EWS Managed API, Postacı builds SOAP requests itself with `System.Xml.Linq`. This means no extra
dependency, and the exact request shape can be verified in tests. A new operation is added in these steps.

### 1. Pick the right file

`EwsClient` is a `partial` class:

| File | Topic |
|---|---|
| `EwsClient.cs` | Folders, listing/reading mail, read state, delete, move, attachments, send, drafts, name resolution, SOAP plumbing |
| `EwsClient.Calendar.cs` | Listing/reading/creating/updating/deleting appointments, meeting responses, availability |
| `EwsClient.Contacts.cs` | Contacts, directory search |
| `EwsClient.AutoReply.cs` | Automatic replies (Out of Office) |

If you are adding a new area (e.g. tasks), create a new partial file such as `EwsClient.Tasks.cs` and put the models
under `Models/`.

### 2. Build the request

Use the `Xml` helpers via `using static ExchangeMail.Core.Ews.Xml;`: `M(...)` creates an element in the messages
namespace and `T(...)` in the types namespace; `Field`, `ItemIdElement`, `FolderIdElement`, `DistinguishedFolder`,
`MailboxElement` and `ParseItemId` cover the recurring pieces (for date formatting, use `EwsDate` inside
`EwsClient`).

Example (setting a follow-up flag on a message — an illustrative operation that does not exist in the project yet):

```csharp
/// <summary>Sets or clears the follow-up flag and returns the item's new id.</summary>
public async Task<EwsItemId> SetFlagAsync(EwsItemId id, bool flagged, CancellationToken ct = default)
{
    // Without a change key Exchange only accepts AlwaysOverwrite; a flag change never needs merging.
    var request = M("UpdateItem",
        new XAttribute("MessageDisposition", "SaveOnly"),
        new XAttribute("ConflictResolution", "AlwaysOverwrite"),
        M("ItemChanges", T("ItemChange",
            ItemIdElement(id with { ChangeKey = null }),
            T("Updates", T("SetItemField", Field("item:Flag"),
                T("Message", T("Flag", T("FlagStatus", flagged ? "Flagged" : "NotFlagged"))))))));

    var message = Single(await ExecuteAsync(request, ct));
    return ParseItemId(message.Element(MNs + "Items")?.Elements().FirstOrDefault()?.Element(TNs + "ItemId"));
}
```

Things to watch out for:

- **Schema order matters.** EWS expects elements in XSD order; if the order is wrong it returns
  `ErrorSchemaValidation`. When adding a field to an element, follow the order in the
  [EWS XML reference](https://learn.microsoft.com/exchange/client-developer/web-service-reference/ews-xml-elements-in-exchange)
  (e.g. in `CalendarItem`: `Subject` → `Body` → … → `Start` → `End` → … → `Location`).
- **ChangeKey.** If you don't send a ChangeKey in an `UpdateItem` request, `ConflictResolution="AlwaysOverwrite"`
  is mandatory; `AutoResolve` fails in that case. The ChangeKey changes on every update: if the operation returns a
  new id, update the id the caller keeps.
- **Read receipts.** Use `SuppressReadReceipts="true"` in requests that change the read flag; otherwise the user
  might send read receipts unintentionally.
- **Time zone.** Call `ExecuteAsync(request, ct, withTimeZone: true)` for calendar requests; only then is the
  `TimeZoneContext` header added. For all-day events take the date with `LocalDateTime.Date`; don't use the UTC
  value directly.
- **Recurring meetings.** Use `FindItem` + `CalendarView` for calendar views; recurrences are expanded on the server.
- **Error handling.** Always process the response with `Single(response)` or
  `ResponseMessages(response).Select(Ok)`, so messages with `ResponseClass="Error"` turn into `EwsException`. Some
  operations (e.g. OOF) return a single `ResponseMessage`; write a separate helper for those (like
  `EnsureOofSuccess`).
- **Busy server.** `ErrorServerBusy` is retried automatically, honouring the back-off time; don't write your own
  retry loop.
- **Version.** Requests are sent with the `Exchange2013_SP1` schema. Don't use elements that need a newer schema;
  everything must work on Exchange 2016, 2019 and SE.
- **Error messages shown to the user** come from the translation resources
  (`throw new EwsException(Strings.Ews_MessageNotFound)`); add both the Turkish and the English text.

### 3. Map the response to a model

Models are immutable `record` types under `Models/`. Be tolerant of missing elements when parsing
(`(string?)element ?? ""`, `(bool?)element ?? false`); servers may omit some fields entirely.

### 4. Write tests

Add at least one [fake server test](#writing-tests) for every new operation. The test should verify both **the
request shape** (correct elements, attributes, order) and **that the response is parsed correctly**. Add a test for
the error response as well.

### 5. ViewModel and UI

Wire the operation to the relevant ViewModel as a command, then add its button/menu item in App. Test the ViewModel
behaviour too (e.g. the list updates after the operation, the error message is shown).

### 6. Trying it on a real server

If possible, try the change on a test mailbox and mention the Exchange version in the PR description. Never add
any data taken from a real mailbox to the repository.

## UI (WinUI) development

- **XAML compiles only on Windows.** If you work on Linux/macOS, your XAML changes are only compiled by the Windows
  job in CI. Make sure the `app` job on your PR is green, and if possible try it by running it on a Windows machine.
- Use **`x:Bind`** instead of `{Binding}` (checked at compile time). Don't forget `Mode=OneWay` for values that
  change. Instead of complex logic inside `x:Bind` expressions, use a computed property on the ViewModel or a helper
  function in `Helpers/Ui`.
- Pages are cached with `NavigationCacheMode="Required"` and receive their ViewModel as the `Frame.Navigate`
  parameter. `OnNavigatedTo` runs again when you navigate back to a page; don't subscribe to events twice.
- **Theme.** Check every change in both **light** and **dark** themes. Use `ThemeResource` brushes
  (`TextFillColorSecondaryBrush`, `CardBackgroundFillColorDefaultBrush`, etc.) instead of fixed colors.
- **Fonts.** The interface font and size can be changed in Settings. If you need a fixed `FontSize` in code,
  multiply it by `Ui.TextScale`; in XAML use the default text styles (`BodyTextBlockStyle`, `CaptionTextBlockStyle`…),
  which are redefined at startup according to the user's settings.
- **Accessibility.** Give icon-only buttons a `ToolTipService.ToolTip` and `AutomationProperties.Name`. Check
  keyboard reachability; add new shortcuts to the shortcut table in the README. Scope shortcuts as narrowly as
  possible (e.g. the Delete key only in the list).
- **ContentDialog.** Dialogs are opened with `XamlRoot` and `RequestedTheme` set (see `Dialogs.ConfirmAsync` in
  `Helpers/Ui`). Only one dialog can be open at a time.
- **WebView2 security.** Mail content is untrusted data:
  - Scripts must stay **disabled** in the reading pane and the CSP must be kept. External images load only when the
    user allows them.
  - In the compose editor (`HtmlEditor`) page scripts are disabled; the toolbar works through
    `ExecuteScriptAsync`. **Always** escape user input with `JsonSerializer.Serialize` when adding it to commands.
  - Links never open inside WebView2; they are handed to the default browser. Only the `http`, `https` and `mailto`
    schemes are allowed.
  - Host objects, web messages, devtools and autofill must stay disabled.
- **Credentials** are stored only in Windows Credential Manager (`CredentialManagerStore`); never write a password
  to the settings file, a log or an error message.

## Writing tests

Tests live in `tests/ExchangeMail.Core.Tests` (xUnit v3). **Tests never connect to a real server**; CI needs no
network access.

### FakeEwsServer

`FakeEwsServer` is an `HttpMessageHandler`. It queues the responses to return for each SOAP operation in order and
records the incoming requests:

```csharp
[Fact]
public async Task Flagging_an_item_overwrites_without_change_key()
{
    var server = new FakeEwsServer()
        .On("UpdateItem", Ok("UpdateItem", """<m:Items><t:Message><t:ItemId Id="A1" ChangeKey="k2" /></t:Message></m:Items>"""));
    using var client = server.CreateClient();

    var id = await client.SetFlagAsync(new EwsItemId("A1", "k1"), flagged: true, TestContext.Current.CancellationToken);

    Assert.Equal(new EwsItemId("A1", "k2"), id);
    var request = server.Request("UpdateItem");
    Assert.Equal("AlwaysOverwrite", (string?)request.Attribute("ConflictResolution"));
    Assert.Null(request.Descendants(T + "ItemId").Single().Attribute("ChangeKey"));
    Assert.Equal("Flagged", request.Descendants(T + "FlagStatus").Single().Value);
}
```

Useful members:

| Member | Purpose |
|---|---|
| `On(operation, bodyXml)` | Queues a successful SOAP body for the operation (with `m:` / `t:` prefixes) |
| `OnRaw(operation, factory)` | For special cases such as HTTP 401, a SOAP fault or a malformed response |
| `Requests` / `Request(op)` | The operation elements that were sent (`Request` expects exactly one) |
| `Operations` | The sequence of operation names (e.g. `["UpdateItem", "DeleteAttachment", "GetItem"]`) |
| `Headers` | SOAP headers (e.g. is `TimeZoneContext` only sent with calendar requests?) |
| `Envelope`, `Xml` | For building responses by hand |

If an operation is called with no queued response, the test fails with `No canned response for ...`, which means an
unexpected request was sent.

### ViewModel tests

ViewModels are created with the fake server through a `MailSession`
(`new MailSession(server.CreateClient(), new AccountSettings { Email = "ben@sirket.com" })`). For data loaded in
the background, wait for a condition instead of a fixed `Task.Delay`:

```csharp
await WaitUntil(() => vm.Messages.Count == 2 && !vm.IsLoadingList);
```

### A good test

- Its name describes the behaviour: `Saving_an_existing_draft_overwrites_it_and_removes_attachments`.
- Verifies a single behaviour; checks both the request shape and the result.
- Uses made-up data (`sirket.com`, `A1`, `k1`) and also exercises text with Turkish characters (e.g. `"Güncel"`).
- Passes `TestContext.Current.CancellationToken`.
- Does not depend on the time zone, culture or operating system (CI runs on both Ubuntu and Windows).

## Commit and branch conventions

- `main` must always build and pass the tests. Don't push to `main` directly; create a branch.
- Branch names: `ozellik/short-description` (feature), `duzeltme/short-description` (fix), `belge/...` (docs),
  `bakim/...` (maintenance), e.g. `duzeltme/read-flag`.
- Commit message: first line of at most ~72 characters, in the imperative mood, describing what it does; if needed,
  explain why after a blank line. English or Turkish is fine; be consistent within a PR.

  ```text
  Calendar: show all-day events on their local date

  Exchange returns all-day events as UTC midnight, which shifted them to
  the previous day in UTC+3. Use LocalDateTime.Date.

  Fixes #42
  ```

- One commit, one logical change. Don't mix formatting changes with functional changes.

## Pull request process

1. Fork the repository (or create a branch if you have write access) and create a branch from an up-to-date `main`.
2. Make the change, add tests and run them locally with `dotnet test --project tests/ExchangeMail.Core.Tests`.
3. If the change is user-visible, update the README (features, shortcuts, troubleshooting) — both
   [Turkish](README.md) and [English](README.en.md) if you can; otherwise mention it in the PR and someone will help.
4. Open the PR and fill in the sections of the [template](.github/pull_request_template.md). For UI changes, attach
   screenshots in light and dark themes (without real data!).
5. Both CI jobs must be green:
   - **test** — libraries + tests on Ubuntu and Windows,
   - **app** — publishing the app for x64 and ARM64 on Windows (this is where the XAML is compiled).

   You can download the `Postaci-win-x64` artifact from the PR's "Checks" tab to try the change.
6. Address review comments with new commits (instead of force-pushing); the maintainer may squash the commits when
   merging.

### Review checklist

- [ ] Layering rules respected (Core/ViewModels don't depend on WinUI)
- [ ] New EWS requests follow schema order and are verified by tests
- [ ] New texts were added to both `Strings.resx` and `Strings.en.resx`
- [ ] Errors reach the user clearly; no swallowed errors
- [ ] Long-running operations don't block the UI thread and can be cancelled
- [ ] The UI doesn't break in light/dark themes, with an enlarged text size or in either language (English texts
      are often longer)
- [ ] WebView2 security settings untouched (or the reason is explained)
- [ ] No real people, addresses, server names or mail content
- [ ] If AI was used, it is disclosed in the PR and the code is understood and tested by the author

## Releasing (maintainers)

Releases follow [SemVer](https://semver.org/): bug fix `1.0.1`, backwards-compatible new feature `1.1.0`, change
that breaks the settings/data format `2.0.0`.

```bash
git switch main && git pull
git tag v1.1.0          # for a pre-release: v1.1.0-beta.1
git push origin v1.1.0
```

[`release.yml`](.github/workflows/release.yml) runs the tests, takes the version number from the tag, builds the
x64 and ARM64 zips, computes `SHA256SUMS.txt`, and publishes the GitHub release with notes generated from the PR
titles. If the tag contains `-`, the release is marked as a pre-release. The `Version` value in the project is only
used for local builds.

Before publishing:

- [ ] The latest CI run on `main` is green
- [ ] Sign-in, reading mail, sending and the calendar were tried on at least one Windows machine with the
      `Postaci-win-x64` artifact
- [ ] If the settings file format changed, starting with old settings was tested (is there migration code?)

## Regenerating the icons

Icon sources and scripts are in [`assets/icon`](assets/icon):

```bash
cd assets/icon
python3 make_svg.py   # generates the SVGs (large/small size, with unread badge)
node render.js        # 16–256 px PNGs with Playwright Chromium
python3 make_ico.py   # Postaci.ico and Postaci-unread.ico
```

Copy the outputs to `src/ExchangeMail.App/Assets/` and update `onizleme.png` (preview). The ICO files contain BMP
entries for ≤128 px and a PNG entry for 256 px; keep this layout so the icons look sharp in the Windows tray and
taskbar.

## Common problems

| Symptom | Cause / fix |
|---|---|
| `dotnet test` fails with something like "VSTest is not supported" | Pass `--project tests/ExchangeMail.Core.Tests` instead of a folder; don't delete `global.json` |
| The solution doesn't build on Linux (XAML error) | Expected; build only Core, ViewModels and the test project |
| `ErrorSchemaValidation` | Wrong element order or namespace (`m:`/`t:`) in the request; check the XSD order |
| `[Key_Name]` appears in the UI | The key is missing from `Strings.resx` or misspelled in XAML; `LocalizationTests` catches this |
| `ErrorChangeKeyRequiredForWriteOperations` | `ConflictResolution="AlwaysOverwrite"` missing on an `UpdateItem` without a ChangeKey |
| An all-day event shifts by a day | A UTC date was used; use `LocalDateTime.Date` and pass `withTimeZone: true` for the calendar request |
| A test fails with `No canned response for X` | The code sends an unexpected `X` request, or `On("X", ...)` is missing |
| The app shows a WebView2 error on startup | WebView2 Runtime is not installed; install the [Evergreen Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) |

If you get stuck, don't hesitate to open an issue. Thank you for contributing!

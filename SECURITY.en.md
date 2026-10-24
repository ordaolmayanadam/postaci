# Security policy

[Türkçe](SECURITY.md) · **English**

## Reporting a vulnerability

**Do not open public issues for security vulnerabilities.** Instead, report them from the repository's
**Security → Report a vulnerability** page (GitHub's private vulnerability reporting). The repository owner needs
to enable this under *Settings → Code security → Private vulnerability reporting*.

Please include:

- The affected version or commit
- The impact of the vulnerability (e.g. script execution, credential leak, authentication bypass)
- Steps to reproduce, **without** real mail data or credentials

Reports may be written in English or Turkish.

## Scope

Findings in these areas are especially important:

- Script execution from message content in the reading pane (WebView2) or bypassing the Content-Security-Policy
- The password being written anywhere other than Windows Credential Manager, or leaking
- TLS certificate validation being disabled
- Path traversal through file names when saving attachments

## Supported versions

Only the latest published release receives security fixes.

# Privacy policy

[Türkçe](PRIVACY.md) · **English**

Last updated: October 28, 2026

Postacı is an independent, open-source Windows app that connects to on-premises Exchange Server mailboxes. This policy
explains which data the app handles and where it is kept.

## In short

- Postacı **does not collect any personal data and does not send it to the developer or to any third party.**
- **There is no telemetry, advertising, analytics or usage tracking.**
- Your mail, calendar and contact data travel only between your computer and the Exchange server you configure.

## Data the app handles

To work, the app handles the following data **only on your computer**:

| Data | Where it is kept |
|---|---|
| Account settings (email address, user name, EWS address) | The app's data folder (`settings.json`) |
| Preferences (theme, language, column widths, signature, etc.) | The app's data folder (`preferences.json`) |
| Password | Windows Credential Manager (protected by DPAPI); never written to disk as plain text |
| Browser profile used to display message bodies | The app's data folder (`WebView2`) |
| Reading pane temporary files | `%TEMP%\Postaci` (cleared on every start) |
| Error log | The app's data folder (`postaci.log`); it never leaves your computer unless you share it |

Mail, calendar and contact content is read from the Exchange server when needed; the app does not keep a permanent copy
of it.

When you uninstall the app, Windows deletes its data folder. You can remove the password from Windows Credential Manager
(Generic credentials → `Postaci:<user>`) or by signing out in the app.

## Network connections

Postacı makes only these connections:

- **The Exchange server you configure:** mail, calendar and contact operations (Exchange Web Services). During the first
  setup, the server address may be found through the Autodiscover addresses of your email address's domain.
- **Remote images in messages:** blocked by default. If you allow images for a message, they are downloaded from the
  servers named by the sender, which may let the sender learn that you opened the message.
- **Links:** links in messages open in your default browser, not inside the app.

Message bodies are displayed with Microsoft Edge WebView2, a Windows component. Any diagnostic data WebView2 may send to
Microsoft depends on your Windows and Microsoft Edge settings and is covered by Microsoft's privacy statement.
Installation and updates through the Microsoft Store are covered by Microsoft's terms as well.

## Notifications and startup

New mail notifications are shown locally through the Windows notification system; their content (sender and subject)
does not leave your computer. Starting with Windows is enabled only if you turn it on.

## Children

Postacı is a work app and is not directed at children; it does not knowingly collect data from children.

## Changes

If this policy changes, the current version is published on this page. The history of changes is visible in the
repository's commit history.

## Contact

You can ask privacy questions on the repository's [Issues](https://github.com/ordaolmayanadam/postaci/issues) page. For
security vulnerabilities, see the [security policy](SECURITY.en.md).

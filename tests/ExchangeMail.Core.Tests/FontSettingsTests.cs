using ExchangeMail.Core.Ews;
using ExchangeMail.Core.Models;
using ExchangeMail.ViewModels;

namespace ExchangeMail.Core.Tests;

public class FontSettingsTests
{
    [Fact]
    public void Reading_preferences_set_default_font_and_zoom_in_head()
    {
        var prepared = MailFormat.PrepareForDisplay("<p>x</p>", true, false);

        var html = MailFormat.ApplyReadingPreferences(prepared, "Calibri", overrideFonts: false, zoomPercent: 125);

        Assert.Contains("<style>body{font-family:'Calibri','Segoe UI',Calibri,Arial,sans-serif}html{zoom:1.25}</style></head>", html);
    }

    [Fact]
    public void Reading_override_forces_font_on_all_elements_and_defaults_change_nothing()
    {
        var prepared = MailFormat.PrepareForDisplay("<p style=\"font-family:Arial\">x</p>", true, false);

        Assert.Contains("body,body *{font-family:'Verdana','Segoe UI',Calibri,Arial,sans-serif !important}",
            MailFormat.ApplyReadingPreferences(prepared, "Verdana", overrideFonts: true, zoomPercent: 100));
        Assert.Equal(prepared, MailFormat.ApplyReadingPreferences(prepared, "", overrideFonts: true, zoomPercent: 100));
    }

    [Fact]
    public void Font_names_cannot_break_out_of_css()
    {
        Assert.Equal("'Evilbodyx:yscript','Segoe UI',Calibri,Arial,sans-serif", MailFormat.FontStack("Evil'}body{x:y<script>"));
    }

    [Fact]
    public void Plain_text_messages_inherit_the_reading_font()
    {
        Assert.Contains("font-family:inherit", MailFormat.PrepareForDisplay("düz metin", false, false));
    }

    [Fact]
    public async Task New_messages_are_wrapped_in_the_compose_font()
    {
        var server = new FakeEwsServer().On("CreateItem", """
            <m:CreateItemResponse><m:ResponseMessages><m:CreateItemResponseMessage ResponseClass="Success"><m:ResponseCode>NoError</m:ResponseCode><m:Items /></m:CreateItemResponseMessage></m:ResponseMessages></m:CreateItemResponse>
            """);
        var session = new MailSession(server.CreateClient(), new AccountSettings { Email = "ben@sirket.com" },
            new UserPreferences { ComposeFontFamily = "Calibri", ComposeFontSize = 12 });
        var vm = new ComposeViewModel(session, ComposeMode.New, null) { To = "a@sirket.com", Subject = "s" };
        vm.ReadBodyHtmlAsync = () => Task.FromResult("<p>merhaba</p>");

        await vm.SendCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        var body = server.Request("CreateItem").Descendants(System.Xml.Linq.XName.Get("Body", FakeEwsServer.T)).Single().Value;
        Assert.Equal("<div style=\"font-family:'Calibri','Segoe UI',Calibri,Arial,sans-serif;font-size:12pt\"><p>merhaba</p></div>", body);
    }

    [Fact]
    public void Font_choices_map_default_label_and_flag_restart_for_interface_changes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "postaci-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var session = new MailSession(
                EwsClient.Create(new Uri("https://mail.test.local/EWS/Exchange.asmx"), System.Net.CredentialCache.DefaultNetworkCredentials),
                new AccountSettings { Email = "ben@sirket.com" });
            var vm = new SettingsViewModel(session, new SettingsStore(dir), new NullCredentials()) { FontChoices = ["Verdana", "Arial", "Arial"] };

            Assert.Equal([SettingsViewModel.DefaultFontLabel, "Arial", "Verdana"], vm.FontChoices);
            Assert.Equal(SettingsViewModel.DefaultFontLabel, vm.UiFontChoice);
            Assert.False(vm.IsRestartNeeded);

            vm.ReadingFontChoice = "Verdana";
            vm.ReadingZoomIndex = 4;
            Assert.False(vm.IsRestartNeeded); // reading settings apply live

            vm.UiFontChoice = "Arial";
            vm.UiTextScaleIndex = 2;
            Assert.True(vm.IsRestartNeeded);
            Assert.Equal("Arial", session.Preferences.UiFontFamily);
            Assert.Equal(115, session.Preferences.UiTextScale);
            Assert.Equal(125, session.Preferences.ReadingZoom);

            vm.ReadingFontChoice = null!; // a ComboBox clearing its selection must not reset the choice
            Assert.Equal("Verdana", session.Preferences.ReadingFontFamily);

            vm.UiFontChoice = SettingsViewModel.DefaultFontLabel;
            vm.UiTextScaleIndex = 1;
            Assert.False(vm.IsRestartNeeded);
            Assert.Equal("", new SettingsStore(dir).LoadPreferences().UiFontFamily);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    private sealed class NullCredentials : ICredentialStore
    {
        public string? GetPassword(string userName) => null;
        public void SavePassword(string userName, string password) { }
        public void RemovePassword(string userName) { }
    }
}

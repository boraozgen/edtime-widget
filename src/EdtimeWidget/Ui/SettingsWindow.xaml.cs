using System.Windows;
using EdtimeWidget.Auth;

namespace EdtimeWidget.Ui;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly bool _hasSavedPassword;

    /// <summary>True when the login changed and the session must be reset.</summary>
    public bool LoginChanged { get; private set; }

    public SettingsWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();

        var login = CredentialStore.LoadLogin();
        UserBox.Text = login?.User ?? string.Empty;
        _hasSavedPassword = login is not null;
        if (_hasSavedPassword) PasswordBox.ToolTip = "Leer lassen, um das gespeicherte Passwort zu behalten";
        LogoutButton.IsEnabled = _hasSavedPassword;

        PollBox.Text = settings.PollSeconds.ToString();
        OffsetBox.Text = settings.OffsetX.ToString();
        (settings.Display == DisplayMode.Taskbar ? TaskbarModeBox : TrayModeBox).IsChecked = true;
        AutostartBox.IsChecked = AppSettings.Autostart;

        Loaded += (_, _) => (UserBox.Text.Length == 0 ? (UIElement)UserBox : PasswordBox).Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var user = UserBox.Text.Trim();
        var password = PasswordBox.Password;
        if (user.Length == 0 || (password.Length == 0 && !_hasSavedPassword))
        {
            ShowError("Bitte Benutzername und Passwort/PIN eingeben.");
            return;
        }
        if (!int.TryParse(PollBox.Text, out var poll) || poll < 15 || poll > 3600)
        {
            ShowError("Abfrageintervall: 15 bis 3600 Sekunden.");
            return;
        }
        if (!int.TryParse(OffsetBox.Text, out var offset))
        {
            ShowError("Abstand muss eine Zahl sein.");
            return;
        }

        var previous = CredentialStore.LoadLogin();
        if (password.Length == 0) password = previous!.Value.Password;
        if (previous is null || previous.Value.User != user || previous.Value.Password != password)
        {
            CredentialStore.SaveLogin(user, password);
            LoginChanged = true;
        }

        _settings.PollSeconds = poll;
        _settings.Display = TaskbarModeBox.IsChecked == true ? DisplayMode.Taskbar : DisplayMode.Tray;
        _settings.OffsetX = offset;
        _settings.Save();
        AppSettings.Autostart = AutostartBox.IsChecked == true;

        DialogResult = true;
    }

    // The offset only positions the pill.
    private void DisplayMode_Changed(object sender, RoutedEventArgs e)
    {
        if (OffsetBox is not null) OffsetBox.IsEnabled = TaskbarModeBox.IsChecked == true;
    }

    private void Logout_Click(object sender, RoutedEventArgs e)
    {
        CredentialStore.DeleteLogin();
        LoginChanged = true;
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}

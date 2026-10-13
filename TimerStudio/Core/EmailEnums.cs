namespace TimerStudio
{
    /// <summary>
    /// TS-13: how email alerts are sent. Persisted as a number in settings.json
    /// (values unchanged from the former int constants).
    /// </summary>
    public enum EmailMode
    {
        Disabled       = 0,
        MailKit        = 1,
        Outlook        = 2,
        MicrosoftOAuth = 3,   // SMTP XOAUTH2 — personal @hotmail/@outlook/@live
    }

    /// <summary>
    /// TS-03: SMTP transport security. Persisted as a number in settings.json.
    /// <see cref="None"/> is only honoured when no SMTP username is set.
    /// </summary>
    public enum SmtpSecurity
    {
        Auto     = 0,
        SslTls   = 1,
        StartTls = 2,
        None     = 3,
    }
}

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TimerStudio.Controls
{
    /// <summary>
    /// TS-10: Email settings UI shared by SettingsDialog and CalendarSettingsDialog.
    /// Logic lives in <see cref="EmailSettingsViewModel"/>; this file only bridges
    /// PasswordBox (not bindable) and async button clicks.
    /// </summary>
    public partial class EmailSettingsPanel : UserControl
    {
        private readonly EmailSettingsViewModel _vm = new();
        private bool _pwdDirty;
        private bool _settingPlaceholder;

        public EmailSettingsPanel()
        {
            InitializeComponent();
            DataContext = _vm;
            Loaded += (_, _) =>
            {
                _vm.Initialize();
                _settingPlaceholder = true;
                PwdSmtp.Password = _vm.HasStoredPassword && !_vm.PasswordUnreadable
                    ? EmailSettingsViewModel.PasswordPlaceholder : "";
                _settingPlaceholder = false;
                _pwdDirty = false;
            };
        }

        /// <summary>Flush pending edits (focused field + typed password). Host dialogs call this on Done.</summary>
        public void Commit()
        {
            if (Keyboard.FocusedElement is TextBox tb)
                tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            SavePasswordIfDirty();
        }

        private void SavePasswordIfDirty()
        {
            if (!_pwdDirty) return;
            _pwdDirty = false;
            _vm.SetPassword(PwdSmtp.Password);
        }

        private void PwdSmtp_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (!_settingPlaceholder) _pwdDirty = true;
        }

        private void PwdSmtp_LostFocus(object sender, RoutedEventArgs e) => SavePasswordIfDirty();

        private async void BtnTestEmail_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Commit();
                await _vm.SendTestAsync();
            }
            catch (Exception ex)
            {
                DiagLog.WriteException("EmailSettingsPanel.SendTest", ex, redactEmails: true);
            }
        }

        private async void BtnMsSignIn_Click(object sender, RoutedEventArgs e)
        {
            try { await _vm.SignInAsync(); }
            catch (Exception ex) { DiagLog.WriteException("EmailSettingsPanel.SignIn", ex, redactEmails: true); }
        }

        private async void BtnMsSignOut_Click(object sender, RoutedEventArgs e)
        {
            try { await _vm.SignOutAsync(); }
            catch (Exception ex) { DiagLog.WriteException("EmailSettingsPanel.SignOut", ex, redactEmails: true); }
        }
    }
}

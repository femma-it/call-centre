﻿using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using AutoUpdaterDotNET;
using CallCentre.Browser;
using CallCentre.Models;
using CallCentre.Properties;
using IdentityModel.OidcClient;
using NAudio.Wave;

namespace CallCentre
{
    public partial class Main : Form
    {
        private Process _process;
        private SipAccount _account;
        private readonly OidcClient _oidcClient;
        private LoginResult _loginResult;


        private readonly string _microSipPath = Path.Combine(Environment.CurrentDirectory, "MicroSip");

        protected override CreateParams CreateParams
        {
            get
            {
                var myCp = base.CreateParams;
                myCp.ClassStyle |= 0x200;
                return myCp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            CheckDevices();
            base.WndProc(ref m);
        }


        public Main()
        {
            InitializeComponent();

            AutoUpdater.RunUpdateAsAdmin = false;
            AutoUpdater.Start(Constants.UpdateUrl);

            var version = Assembly.GetEntryAssembly()?.GetName().Version;
            versionLabel.Text = $@"{Resources.Version}: {version}";

            var options = new OidcClientOptions
            {
                Authority = Constants.ApiUrl,
                ClientId = Constants.ClientId,
                ClientSecret = Constants.ClientSecret,
                RedirectUri = Constants.RedirectUri,
                Scope = Constants.Scope,
                Browser = new WinFormsWebView(),
            };

            _oidcClient = new OidcClient(options);

            LoginUser();
        }

        private async void LoginUser()
        {
            try
            {
                _loginResult = await _oidcClient.LoginAsync();
            }
            catch (Exception exception)
            {
                var result = MessageBox.Show(exception.Message, Resources.Error, MessageBoxButtons.RetryCancel);
                if (result == DialogResult.Retry)
                {
                    LoginUser();
                }
                else
                {
                    Close();
                }

                return;
            }

            if (_loginResult.IsError)
            {
                var result = MessageBox.Show(this, _loginResult.Error, Resources.Login, MessageBoxButtons.RetryCancel, MessageBoxIcon.Error);
                if (result == DialogResult.Retry)
                {
                    LoginUser();
                }
                else
                {
                    Close();
                }
            }
            else
            {
                var http = new HttpWrapper(_loginResult.AccessToken);
                _account = http.Invoke<SipAccount>("GET", Constants.AutoProvisioningUrl, string.Empty);

                userLabel.Text = _account?.DisplayName;
                lineLabel.Text = _account?.InternalNumber;

                CheckDevices();
                OpenPhone(_account);
            }
        }

        private void Main_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                ClosePhone();
            }
            catch
            {
                // ignored
            }
        }

        private void CheckDevices()
        {
            Invoke(new MethodInvoker(delegate
            {
                var microphone = WaveIn.DeviceCount > 0;
                var headphones = WaveOut.DeviceCount > 0;

                micLabel.Text = microphone ? Resources.Connected : Resources.Not_connected;
                micLabel.ForeColor = microphone ? Color.ForestGreen : Color.Red;

                handphonesLabel.Text = headphones ? Resources.Connected : Resources.Not_connected;
                handphonesLabel.ForeColor = headphones ? Color.ForestGreen : Color.Red;
            }));
        }


        #region Phone

        private void CreateConfig(SipAccount account)
        {
            var path = Path.Combine(_microSipPath, "microsip.ini");

            if (File.Exists(path))
            {
                File.Delete(path);
            }

            Thread.Sleep(1000);

            using (var sw = File.CreateText(path))
            {
                sw.Write(account.Settings);
            }
        }

        private void OpenPhone(SipAccount account)
        {
            CreateConfig(account);

            try
            {
                _process = new Process
                {
                    StartInfo =
                    {
                        WorkingDirectory = _microSipPath,
                        FileName = "microsip.exe"
                    }
                };


                var result = _process.Start();
                if (result)
                {
                    phoneLabel.Text = Resources.Launched;
                    phoneLabel.ForeColor = Color.ForestGreen;
                    OpenPhoneButton.Enabled = false;
                }
                else
                {
                    phoneLabel.Text = Resources.Not_launched;
                    phoneLabel.ForeColor = Color.Red;
                    MessageBox.Show(Resources.Phone_startup_error, Resources.Error, MessageBoxButtons.RetryCancel, MessageBoxIcon.Error);
                    OpenPhoneButton.Enabled = true;
                }


                _process.EnableRaisingEvents = true;
                _process.Exited += Process_Exited;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Resources.Phone_startup_error, MessageBoxButtons.RetryCancel, MessageBoxIcon.Error);
            }
        }

        private void ClosePhone()
        {
            Invoke(new MethodInvoker(delegate
            {
                phoneLabel.Text = Resources.Not_launched;
                phoneLabel.ForeColor = Color.Red;
                OpenPhoneButton.Enabled = true;
            }));

            try
            {
                _process = new Process
                {
                    StartInfo =
                    {
                        WorkingDirectory = _microSipPath,
                        FileName = "microsip.exe",
                        Arguments = "/exit"
                    }
                };
                _process.Start();

                // _process.Kill();
            }
            catch
            {
                MessageBox.Show(Resources.Failed_to_close_module_MicroSip, Resources.Error, MessageBoxButtons.RetryCancel, MessageBoxIcon.Error);
            }

            try
            {
                var path = Path.Combine(_microSipPath, "microsip.ini");

                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                MessageBox.Show(Resources.Failed_to_delete_settings_file, Resources.Error, MessageBoxButtons.RetryCancel, MessageBoxIcon.Error);
            }
        }

        private void Process_Exited(object sender, EventArgs e)
        {
            ClosePhone();
        }

        #endregion

        #region Buttons

        private void OpenPhoneButton_Click(object sender, EventArgs e)
        {
            OpenPhone(_account);
        }

        private void CloseApplicationButton_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void OpenSoundSettings_Click(object sender, EventArgs e)
        {
            Process.Start("mmsys.cpl");
        }

        #endregion
    }
}
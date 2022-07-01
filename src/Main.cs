using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Forms;
using AutoUpdaterDotNET;
using CallCentre.Models;
using IdentityModel.OidcClient;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Win32;
using NAudio.Wave;
using Newtonsoft.Json;
using Timer = System.Timers.Timer;

namespace CallCentre
{
    public partial class Main : Form
    {
        private Process _process;
        private SipAccount _account;
        private OidcClient _oidcClient;
        private Timer _timer;
        private HubConnection _connection;
        private string _accessToken;
        private string _refreshToken;

        private readonly string _microSipPath = Path.Combine(Environment.CurrentDirectory, "MicroSip");
        private readonly string _microSipConfigPath = Path.Combine(Environment.CurrentDirectory, "MicroSip", "microsip.ini");

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

            if (m.Msg == 0x11)
            {
                Close();
            }

            base.WndProc(ref m);
        }

        public Main()
        {
            InitializeComponent();

            AutoUpdater.RunUpdateAsAdmin = false;
            AutoUpdater.Start(Constants.UpdateUrl);

            var version = Assembly.GetEntryAssembly()?.GetName().Version;
            versionLabel.Text = $"Версія: {version}";

            LoginUser();
        }

        private async Task StartSignalR()
        {
            var version = Assembly.GetEntryAssembly()?.GetName().Version;

            _connection = new HubConnectionBuilder()
                .WithUrl(Constants.HubUrl, options =>
                {
                    options.Headers.Add("Version", version?.ToString());
                    options.Headers.Add("MAC", Helpers.GetMacAddress());
                    options.Headers.Add("Os", Helpers.GetWindowsVersion());
                    options.AccessTokenProvider = () => Task.FromResult(_accessToken);
                })
                .WithAutomaticReconnect(new RetryPolicy())
                .Build();
            await _connection.StartAsync();

            _connection.On("ClosePhone", ClosePhone);
            _connection.On("OpenPhone", () => OpenPhone(_account));
            _connection.On("HangUpCall", HangUpCall);

            _connection.Closed += async error =>
            {
                await Task.Delay(1000);
                await _connection.StartAsync();
            };
        }

        private void HangUpCall()
        {
            try
            {
                _process = new Process
                {
                    StartInfo =
                    {
                        WorkingDirectory = _microSipPath,
                        FileName = "microsip.exe",
                        Arguments = "/hangupall"
                    }
                };
                
                _process.Start();

                // _process.Kill();
            }
            catch
            {
                MessageBox.Show("Не вдалося закрити модуль 'MicroSip'", "Помилка", MessageBoxButtons.RetryCancel, MessageBoxIcon.Error);
            }
        }

        private void Main_Load(object sender, EventArgs e)
        {
            RegisterForSystemEvents();

            _timer = new Timer(180000);

            _timer.Elapsed += MyTimer_TickAsync;
            _timer.Enabled = true;

            GC.KeepAlive(_timer);
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

        private async void MyTimer_TickAsync(object sender, ElapsedEventArgs e)
        {
            var refreshToken = await _oidcClient.RefreshTokenAsync(_refreshToken);
            if (refreshToken.IsError)
            {
                Close();
            }

            _accessToken = refreshToken.AccessToken;
            _refreshToken = refreshToken.RefreshToken;
        }

        private void CheckDevices()
        {
            Invoke(new MethodInvoker(delegate
            {
                var microphone = WaveIn.DeviceCount > 0;
                var headphones = WaveOut.DeviceCount > 0;

                micLabel.Text = microphone ? "Підключено" : "Не під'єднано";
                micLabel.ForeColor = microphone ? Color.ForestGreen : Color.Red;

                handphonesLabel.Text = headphones ? "Підключено" : "Не під'єднано";
                handphonesLabel.ForeColor = headphones ? Color.ForestGreen : Color.Red;
            }));
        }

        #region Phone

        private void OpenPhone(SipAccount account)
        {
            using (var sw = File.CreateText(_microSipConfigPath))
            {
                sw.Write(account?.Settings);
            }

            try
            {
                _process = new Process
                {
                    StartInfo =
                    {
                        WorkingDirectory = _microSipPath,
                        FileName = "microsip.exe",
                        // Arguments = "/hidden"
                    }
                };


                var result = _process.Start();
                if (!result)
                {
                    MessageBox.Show("Помилка запуску телефону", "Помилка", MessageBoxButtons.RetryCancel, MessageBoxIcon.Error);
                }

                _process.EnableRaisingEvents = true;
                _process.Exited += Process_Exited;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Помилка запуску телефону", MessageBoxButtons.RetryCancel, MessageBoxIcon.Error);
            }
        }
        
        private void ClosePhone()
        {
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
                MessageBox.Show("Не вдалося закрити модуль 'MicroSip'", "Помилка", MessageBoxButtons.RetryCancel, MessageBoxIcon.Error);
            }

            try
            {
                if (File.Exists(_microSipConfigPath))
                    File.Delete(_microSipConfigPath);
            }
            catch
            {
                _process?.Kill();

                Thread.Sleep(1000);

                if (File.Exists(_microSipConfigPath))
                    File.Delete(_microSipConfigPath);
            }
        }

        private void Process_Exited(object sender, EventArgs e)
        {
            ClosePhone();
        }

        #endregion

        #region Buttons

        private void CloseApplicationButton_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void OpenSoundSettings_Click(object sender, EventArgs e)
        {
            Process.Start("mmsys.cpl");
        }

        #endregion

        #region SystemEvents

        private void RegisterForSystemEvents()
        {
            SystemEvents.EventsThreadShutdown += OnEventsThreadShutdown;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.SessionEnding += OnSessionEnding;
        }

        private void UnregisterFromSystemEvents()
        {
            SystemEvents.EventsThreadShutdown -= OnEventsThreadShutdown;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.SessionEnding -= OnSessionEnding;
        }

        private void OnEventsThreadShutdown(object sender, EventArgs e)
        {
            UnregisterFromSystemEvents();
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            switch (e.Mode)
            {
                case PowerModes.Suspend:
                    Close();
                    break;
            }
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            switch (e.Reason)
            {
                case SessionSwitchReason.SessionLock:
                    ClosePhone();
                    break;
                case SessionSwitchReason.SessionLogoff:
                    Close();
                    break;
            }
        }

        private void OnSessionEnding(object sender, SessionEndingEventArgs e)
        {
            e.Cancel = false;

            switch (e.Reason)
            {
                case SessionEndReasons.Logoff:
                case SessionEndReasons.SystemShutdown:
                    Close();
                    break;
            }
        }

        #endregion

        #region Auth

        private async void LoginUser()
        {
            LoginResult loginResult;
            const string redirectUri = "http://127.0.0.1:7890/";

            var httpListener = new HttpListener();

            try
            {
                httpListener.Prefixes.Add(redirectUri);
                httpListener.Start();
            }
            catch
            {
                httpListener.Stop();
                httpListener.Close();
                throw;
            }

            try
            {
                var options = new OidcClientOptions
                {
                    Authority = Constants.ApiUrl,
                    ClientId = Constants.ClientId,
                    Scope = Constants.Scope,
                    RedirectUri = redirectUri
                };

                _oidcClient = new OidcClient(options);
                var state = await _oidcClient.PrepareLoginAsync();
                OpenBrowser(state.StartUrl);

                var context = await httpListener.GetContextAsync();
                BringFormToFront();
                await SendResponseAsync(context);

                loginResult = await _oidcClient.ProcessResponseAsync(context.Request.RawUrl, state);

                _accessToken = loginResult.AccessToken;
                _refreshToken = loginResult.RefreshToken;

                _timer.Start();

                await StartSignalR();
            }
            catch (Exception exception)
            {
                httpListener.Stop();

                var result = MessageBox.Show(exception.Message, "Помилка", MessageBoxButtons.RetryCancel);
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
            finally
            {
                httpListener.Stop();
                httpListener.Close();
            }

            if (loginResult.IsError)
            {
                var result = MessageBox.Show(this, loginResult.Error, "Вхід", MessageBoxButtons.RetryCancel, MessageBoxIcon.Error);
                if (result == DialogResult.Retry)
                {
                    LoginUser();
                }
            }
            else
            {
                try
                {
                    var http = new HttpWrapper(loginResult.AccessToken);
                    _account = http.Invoke<SipAccount>("GET", Constants.AutoProvisioningUrl, string.Empty);

                    userLabel.Text = _account?.DisplayName;
                    lineLabel.Text = _account?.InternalNumber;

                    CheckDevices();
                }
                catch
                {
                    using (var reserve = new Reserve())
                    {
                        if (reserve.ShowDialog() == DialogResult.OK)
                        {
                            var reserveCode = reserve.richTextBox1.Text;
                            if (!string.IsNullOrEmpty(reserveCode))
                            {
                                var data = Convert.FromBase64String(reserveCode);
                                var config = Encoding.UTF8.GetString(data);
                                var account = JsonConvert.DeserializeObject<SipAccount>(config);

                                if (account.DateTime.Date != DateTime.Now.Date)
                                {
                                    MessageBox.Show("Застосовано невірний код", "Помилка", MessageBoxButtons.RetryCancel, MessageBoxIcon.Error);
                                    Close();
                                }

                                _account = account;

                                userLabel.Text = _account?.DisplayName;
                                lineLabel.Text = _account?.InternalNumber;

                                CheckDevices();
                                OpenPhone(_account);
                            }
                        }
                    }
                }
            }
        }

        private void BringFormToFront()
        {
            WindowState = FormWindowState.Minimized;
            Show();
            WindowState = FormWindowState.Normal;
        }

        private static async Task SendResponseAsync(HttpListenerContext context)
        {
            var response = context.Response;
            const string responseString = "<html><head></head><body>Please return to the app.</body>" +
                                          "<script type=\"text/javascript\">window.close();</script>" +
                                          "</html>";
            var buffer = Encoding.UTF8.GetBytes(responseString);
            response.ContentLength64 = buffer.Length;
            var responseOutput = response.OutputStream;
            await responseOutput.WriteAsync(buffer, 0, buffer.Length);
            responseOutput.Close();
        }

        private static void OpenBrowser(string url)
        {
            try
            {
                Process.Start(url);
            }
            catch
            {
                url = url.Replace("&", "^&");
                Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
            }
        }

        #endregion
    }
}
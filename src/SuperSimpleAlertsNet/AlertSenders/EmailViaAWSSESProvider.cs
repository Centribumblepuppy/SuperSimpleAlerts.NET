using System;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;

namespace SuperSimpleAlertsNet
{

    public class EmailViaAWSSESProvider : IAlertSender
    {
        public bool IsInitialized { get; private set; }

        public const string CODE = "email";

        private SmtpClient _client;
        private string _emailSender;
        private int _retryAttempts;
        private string _host;
        private int _port;
        private string _username;
        private string _password;

        public async Task Init(IConfiguration rawConfig, AlertingConfig config)
        {
            _host = rawConfig["smtpHost"] ?? "email-smtp.us-east-1.amazonaws.com";
            _port = int.Parse(rawConfig["smtpPort"] ?? "587");
            _username = rawConfig["smtpUsername"];
            _password = rawConfig["smtpPassword"];
            _emailSender = rawConfig["emailSender"];
            _retryAttempts = int.Parse(rawConfig["smtpMaxRetry"] ?? "2");

            if (string.IsNullOrEmpty(_username))
                throw new InvalidOperationException("SMTP username not specified in config");
            if (string.IsNullOrEmpty(_password))
                throw new InvalidOperationException("SMTP password not specified in config");

            await RefreshClient();

            IsInitialized = true;
        }

        private async Task RefreshClient()
        {
            if (_client == null)
                _client = new SmtpClient {Timeout = 100 * 1000};

            if (!_client.IsConnected)
                await _client.ConnectAsync(_host, _port, SecureSocketOptions.StartTls);

            if (!_client.IsAuthenticated)
                await _client.AuthenticateAsync(_username, _password);
        }

        private void DisconnectAndDisposeClient()
        {
            _client.Disconnect(true);
            _client.Dispose();
        }

        public async Task Send(string endpoint, string alertLevel, string alertCode, string message,
            ILambdaLogger contextLogger)
        {
            try
            {
                if (string.IsNullOrEmpty(endpoint))
                    throw new ArgumentNullException(nameof(endpoint));
                if (string.IsNullOrEmpty(alertLevel))
                    throw new ArgumentNullException(nameof(alertLevel));
                if (string.IsNullOrEmpty(alertCode))
                    throw new ArgumentNullException(nameof(alertCode));
                if (string.IsNullOrEmpty(message))
                    throw new ArgumentNullException(nameof(message));

                if (string.IsNullOrEmpty(_emailSender))
                    throw new InvalidOperationException(
                        "Email sender not set. Ensure configured and call Init first.");

                var mimeMessage = new MimeMessage
                {
                    Subject = $"Alert Level: {alertLevel}. Alert Code: {alertCode}",
                    Sender = new MailboxAddress(_emailSender),
                    From = {new MailboxAddress(_emailSender),},
                    To = {new MailboxAddress(endpoint)},
                    Body = new TextPart("plain") {Text = message},
                };

                for (var i = 0; i < _retryAttempts; i++)
                {
                    await RefreshClient();

                    try
                    {
                        await _client.SendAsync(mimeMessage);
                        break;
                    }
                    catch (Exception ex) when (ex is ServiceNotConnectedException || ex is SmtpCommandException)
                    {
                        contextLogger.LogLine(ex.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                contextLogger.LogLine(ex.ToString());
            }
        }

        public void Dispose()
        {
            if (_client != null)
            {
                DisconnectAndDisposeClient();
            }
        }
    }
}

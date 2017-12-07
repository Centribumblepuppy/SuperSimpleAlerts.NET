using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Microsoft.Extensions.Configuration;

namespace SuperSimpleAlertsNet
{
    public class SmsViaTwilioProvider : IAlertSender
    {
        public bool IsInitialized { get; private set; }

        public const string CODE = "sms";

        private HttpClient _httpClient;
        private string _fromNumber;
        private string _twilioAccountSid;
        private string _twilioAuthToken;
        private bool _truncateSmsToOnePart;

        public Task Init(IConfiguration rawConfig, AlertingConfig config)
        {
            _httpClient = new HttpClient();

            _twilioAccountSid = rawConfig["twilioAccountSid"];
            _twilioAuthToken = rawConfig["twilioAuthToken"];
            _fromNumber = rawConfig["twilioSender"] ?? rawConfig["twilioSender"];
            _truncateSmsToOnePart = bool.Parse(rawConfig["truncateSmsToOnePart"] ?? "true");

            IsInitialized = true;

            return Task.CompletedTask;
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

                if (_httpClient == null)
                    throw new InvalidOperationException("HttpClient is null. Ensure Init called first");

                if (string.IsNullOrEmpty(_twilioAccountSid))
                    throw new InvalidOperationException(
                        "Account Sid not set. Ensure configured and call Init first.");
                if (string.IsNullOrEmpty(_fromNumber))
                    throw new InvalidOperationException(
                        "From number not set. Ensure configured and call Init first.");
                if (string.IsNullOrEmpty(_twilioAuthToken))
                    throw new InvalidOperationException(
                        "Auth token not set. Ensure configured and call Init first.");


                var body = alertLevel + ">" + alertCode + ": " + message;

                if (_truncateSmsToOnePart && body.Length > 160)
                {
                    body = body.Substring(0, 160);
                }

                var request = new HttpRequestMessage(HttpMethod.Post,
                    $"https://api.twilio.com/2010-04-01/Accounts/{_twilioAccountSid}/Messages.json")
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        {"To", endpoint},
                        {"From", _fromNumber},
                        {"Body", body}
                    }),
                };

                request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(
                        Encoding.ASCII.GetBytes($"{_twilioAccountSid}:{_twilioAuthToken}")));

                var response = await _httpClient.SendAsync(
                    request
                );

                if (response == null)
                    throw new InvalidOperationException("Response was null.");

                if (response.StatusCode != HttpStatusCode.OK
                    && response.StatusCode != HttpStatusCode.Created)
                {
                    contextLogger.LogLine(
                        $"Error sending sms through Twilio. Status code: {response.StatusCode}. Response text: {response.Content.ReadAsStringAsync()}");
                }
            }
            catch (Exception ex)
            {
                contextLogger.LogLine(ex.ToString());
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
            _httpClient = null;
        }
    }
}

using System;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Microsoft.Extensions.Configuration;

namespace SuperSimpleAlertsNet
{
    public interface IAlertSender : IDisposable
    {
        bool IsInitialized { get; }
        Task Init(IConfiguration rawConfig, AlertingConfig config);
        Task Send(string endpoint, string alertLevel, string alertCode, string message, ILambdaLogger contextLogger);
    }
}

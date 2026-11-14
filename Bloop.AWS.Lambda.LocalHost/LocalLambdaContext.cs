using Amazon.Lambda.Core;
using Microsoft.Extensions.Logging;

namespace Bloop.AWS.Lambda.LocalHost
{
    public sealed class LocalLambdaContext(string functionName, string requestId, ILogger logger) : ILambdaContext
    {
        public string AwsRequestId => requestId;
        public IClientContext ClientContext => null!;
        public string FunctionName => functionName;
        public string FunctionVersion => "local";
        public ICognitoIdentity Identity => null!;
        public string InvokedFunctionArn => $"arn:aws:lambda:local:000000000000:function:{functionName}";
        public ILambdaLogger Logger { get; } = new LocalLambdaLogger(logger);
        public string LogGroupName => $"/local/{functionName}";
        public string LogStreamName => "local";
        public int MemoryLimitInMB => 512;
        public TimeSpan RemainingTime => TimeSpan.FromMinutes(15);

        private sealed class LocalLambdaLogger(ILogger logger) : ILambdaLogger
        {
            public void Log(string message) => logger.LogInformation("{Message}", message);

            public void LogLine(string message) => logger.LogInformation("{Message}", message);
        }
    }
}

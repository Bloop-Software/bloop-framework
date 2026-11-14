using Amazon.Lambda.Core;

namespace Bloop.AWS.Lambda.Tests;

internal sealed class TestLambdaContext : ILambdaContext
{
    public List<string> Errors { get; } = [];

    public string AwsRequestId => "test-request";
    public IClientContext ClientContext => null!;
    public string FunctionName => "test";
    public string FunctionVersion => "1";
    public ICognitoIdentity Identity => null!;
    public string InvokedFunctionArn => "arn:test";
    public ILambdaLogger Logger => new TestLogger(Errors);
    public string LogGroupName => "test";
    public string LogStreamName => "test";
    public int MemoryLimitInMB => 256;
    public TimeSpan RemainingTime => TimeSpan.FromSeconds(30);

    private sealed class TestLogger(List<string> errors) : ILambdaLogger
    {
        public void Log(string message) => errors.Add(message);
        public void LogLine(string message) => errors.Add(message);
    }
}

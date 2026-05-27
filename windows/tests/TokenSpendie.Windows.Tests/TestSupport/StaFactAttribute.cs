using System.Threading;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace TokenSpendie.Windows.Tests.TestSupport;

[XunitTestCaseDiscoverer(
    "TokenSpendie.Windows.Tests.TestSupport.StaFactDiscoverer",
    "TokenSpendie.Windows.Tests")]
public sealed class StaFactAttribute : FactAttribute { }

public sealed class StaFactDiscoverer : IXunitTestCaseDiscoverer
{
    private readonly IMessageSink _diag;
    public StaFactDiscoverer(IMessageSink diag) => _diag = diag;

    public System.Collections.Generic.IEnumerable<IXunitTestCase> Discover(
        ITestFrameworkDiscoveryOptions opts, ITestMethod method, IAttributeInfo factAttribute)
    {
        yield return new StaTestCase(_diag, opts.MethodDisplayOrDefault(), opts.MethodDisplayOptionsOrDefault(), method);
    }
}

public sealed class StaTestCase : XunitTestCase
{
    [System.Obsolete("Called by serializer", error: true)]
    public StaTestCase() { }

    public StaTestCase(IMessageSink diag, TestMethodDisplay display, TestMethodDisplayOptions options, ITestMethod method)
        : base(diag, display, options, method) { }

    public override System.Threading.Tasks.Task<RunSummary> RunAsync(
        IMessageSink diagnosticMessageSink, IMessageBus messageBus,
        object[] constructorArguments, ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource)
    {
        var tcs = new System.Threading.Tasks.TaskCompletionSource<RunSummary>();
        var thread = new Thread(() =>
        {
            try
            {
                var summary = base.RunAsync(diagnosticMessageSink, messageBus,
                    constructorArguments, aggregator, cancellationTokenSource)
                    .GetAwaiter().GetResult();
                tcs.SetResult(summary);
            }
            catch (System.Exception ex) { tcs.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}

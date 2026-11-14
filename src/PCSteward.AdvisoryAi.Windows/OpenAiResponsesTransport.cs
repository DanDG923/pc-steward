using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Headers;
using OpenAI.Responses;
using PCSteward.Application;
using PCSteward.Domain;

// OpenAI 2.15.0 marks its Responses client "for evaluation" (OPENAI001). Only its constructor and the protocol method (approved bytes
// in, raw bytes out) are used, the package version is pinned, and the counting mock tests below prove the behaviour this class relies
// on, so a change in a later SDK version shows up as a failing test rather than a silent difference. Task 69 records it.
#pragma warning disable OPENAI001

namespace PCSteward.AdvisoryAi.Windows;

/// <summary>
/// FR-AI-04 (ADR-028 Q1/F11, Task 67 stage 3): one approved request through the official OpenAI .NET SDK's protocol method, so the
/// SDK sends the exact approved bytes rather than a body it builds itself. The SDK's pipeline has no retry, no logging and no
/// tracing; the HTTP handler follows no redirect and has a 10-second connect deadline; one 45-second deadline covers the whole
/// attempt including the response read. A 3xx is returned as <see cref="AdvisoryAiTransportOutcome.Redirected"/> before the SDK sees
/// it, because the SDK's own response handling can follow a same-host redirect regardless of the HTTP handler's setting.
/// <para>
/// A guard is the last step before the network. It refuses (and nothing is sent) unless this is the first attempt, the method and
/// address are the one compiled route, the body is byte for byte the approved body and the authorization is the key given for this
/// request. It then rebuilds the headers to the closed profile the preview showed, so nothing the SDK adds (its own user agent with
/// runtime and OS details, request IDs, tracing) leaves the PC. It stops reading a response at the admitted size, so neither the SDK
/// nor this class ever buffers more than that.
/// </para>
/// <para>
/// Residual (recorded for the Task 70 review): the SDK takes the key as a string for its authorization header, so a copy of the key
/// lives in managed memory that cannot be cleared until it is collected. No text this class returns or throws carries the key, a
/// header, the body or exception text.
/// </para>
/// </summary>
public sealed class OpenAiResponsesTransport : IAdvisoryAiTransport
{
    private readonly Func<HttpMessageHandler> createNetworkHandler;
    private readonly TimeSpan overallDeadline;

    /// <summary>The production transport: the system's TLS validation and proxy settings, no redirects, 10-second connect.</summary>
    public OpenAiResponsesTransport()
        : this(CreateNetworkHandler, AdvisoryAiResourcePolicy.OverallDeadline)
    {
    }

    /// <summary>
    /// For tests: <paramref name="createNetworkHandler"/> stands in for the network and is created and disposed per attempt;
    /// <paramref name="overallDeadline"/> replaces the 45-second deadline so a timeout can be proven quickly.
    /// </summary>
    internal OpenAiResponsesTransport(Func<HttpMessageHandler> createNetworkHandler, TimeSpan overallDeadline)
    {
        ArgumentNullException.ThrowIfNull(createNetworkHandler);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(overallDeadline, TimeSpan.Zero);
        this.createNetworkHandler = createNetworkHandler;
        this.overallDeadline = overallDeadline;
    }

    public AdvisoryAiProviderId Provider => AdvisoryAiProviderId.OpenAI;

    /// <summary>The SDK endpoint the Responses client appends "/responses" to; the guard checks the result is exactly the route.</summary>
    internal static Uri SdkEndpoint { get; } = new(AdvisoryAiProviders.OpenAI.Endpoint, ".");

    public async Task<AdvisoryAiTransportResult> SendOnceAsync(AdvisoryAiApproval approval, AdvisoryAiApiKey key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(approval);
        ArgumentNullException.ThrowIfNull(key);
        // ADR-028 addendum A1: this transport sends only an OpenAI model's request, to OpenAI's one route.
        if (cancellationToken.IsCancellationRequested || approval.Preview.Model.Provider != Provider) return AdvisoryAiTransportResult.Of(AdvisoryAiTransportOutcome.NotSent);

        long admitted = AdvisoryAiResponseReader.AdmittedBytes(approval);
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(overallDeadline);
        WireGuard guard = new(approval, key, admitted, createNetworkHandler());
        using HttpClient http = new(guard, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan, MaxResponseContentBufferSize = admitted + 1 };
        using HttpClientPipelineTransport pipelineTransport = new(http);
        ResponsesClientOptions options = new()
        {
            Endpoint = SdkEndpoint,
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
            Transport = pipelineTransport,
            NetworkTimeout = overallDeadline,
            EnableDistributedTracing = false,
            ClientLoggingOptions = new() { EnableLogging = false, EnableMessageLogging = false, EnableMessageContentLogging = false }
        };
        RequestOptions request = new() { CancellationToken = deadline.Token, BufferResponse = false, ErrorOptions = ClientErrorBehaviors.NoThrow };

        try
        {
            ResponsesClient client = new(new ApiKeyCredential(new string(key.Reveal())), options);
            ClientResult result = await client.CreateResponseAsync(BinaryContent.Create(BinaryData.FromBytes(approval.Body.ToArray())), request).ConfigureAwait(false);
            using PipelineResponse response = result.GetRawResponse();
            int status = response.Status;
            if (response.ContentStream is not Stream stream) return new(AdvisoryAiTransportOutcome.ResponseReceived, status, []);
            ImmutableArray<byte>? body = await ReadBoundedAsync(stream, admitted, deadline.Token).ConfigureAwait(false);
            return body is null
                ? new(AdvisoryAiTransportOutcome.ResponseTooLarge, status, [])
                : new(AdvisoryAiTransportOutcome.ResponseReceived, status, body.Value);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Classify(exception, guard, cancellationToken);
        }
    }

    private static AdvisoryAiTransportResult Classify(Exception exception, WireGuard guard, CancellationToken caller)
    {
        // Nothing left the PC unless the guard handed the request to the network handler.
        if (!guard.Dispatched) return AdvisoryAiTransportResult.Of(AdvisoryAiTransportOutcome.NotSent);
        if (guard.RedirectStatus is int redirect) return new(AdvisoryAiTransportOutcome.Redirected, redirect, []);
        if (guard.ResponseTooLarge) return AdvisoryAiTransportResult.Of(AdvisoryAiTransportOutcome.ResponseTooLarge);
        if (caller.IsCancellationRequested) return AdvisoryAiTransportResult.Of(AdvisoryAiTransportOutcome.Cancelled);
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is OperationCanceledException or TimeoutException) return AdvisoryAiTransportResult.Of(AdvisoryAiTransportOutcome.TimedOut);
            if (current is HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError or HttpRequestError.SecureConnectionError or HttpRequestError.ProxyTunnelError })
                return AdvisoryAiTransportResult.Of(AdvisoryAiTransportOutcome.ConnectFailed);
        }
        return AdvisoryAiTransportResult.Of(AdvisoryAiTransportOutcome.Failed);
    }

    /// <summary>Reads at most <paramref name="admitted"/> bytes; null when the body is longer.</summary>
    private static async Task<ImmutableArray<byte>?> ReadBoundedAsync(Stream stream, long admitted, CancellationToken cancellationToken)
    {
        using MemoryStream body = new();
        byte[] buffer = new byte[16 * 1024];
        while (true)
        {
            int read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) return ImmutableArray.Create(body.GetBuffer(), 0, (int)body.Length);
            if (body.Length + read > admitted) return null;
            body.Write(buffer, 0, read);
        }
    }

    internal static SocketsHttpHandler CreateNetworkHandler() => new()
    {
        AllowAutoRedirect = false,
        ConnectTimeout = AdvisoryAiResourcePolicy.ConnectDeadline,
        AutomaticDecompression = DecompressionMethods.None,
        UseCookies = false,
        Credentials = null,
        PreAuthenticate = false,
        MaxConnectionsPerServer = 1,
        PooledConnectionLifetime = TimeSpan.Zero
    };

    /// <summary>The last handler before the network: refuses anything but the approved request and closes the header set.</summary>
    private sealed class WireGuard(AdvisoryAiApproval approval, AdvisoryAiApiKey key, long admitted, HttpMessageHandler network) : DelegatingHandler(network)
    {
        private int attempts;

        /// <summary>True once the request was handed to the network handler: from then on it may have been sent.</summary>
        public bool Dispatched { get; private set; }

        public bool ResponseTooLarge { get; private set; }

        /// <summary>The 3xx status the network returned, which is never followed.</summary>
        public int? RedirectStatus { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref attempts) != 1) throw Refused();
            if (request.Method != HttpMethod.Post || request.RequestUri is null ||
                !string.Equals(request.RequestUri.AbsoluteUri, AdvisoryAiProviders.OpenAI.Endpoint.AbsoluteUri, StringComparison.Ordinal))
                throw Refused();
            if (!IsTheKey(request.Headers.Authorization)) throw Refused();
            byte[] body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            if (!approval.MatchesWireBody(body)) throw Refused();

            AuthenticationHeaderValue authorization = request.Headers.Authorization!;
            request.Headers.Clear();
            request.Headers.Authorization = authorization;
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(AdvisoryAiHeaderProfile.Accept));
            request.Headers.UserAgent.ParseAdd(AdvisoryAiHeaderProfile.UserAgent);
            ByteArrayContent content = new(body);
            content.Headers.ContentType = new MediaTypeHeaderValue(AdvisoryAiHeaderProfile.ContentType);
            content.Headers.ContentLength = body.Length;
            request.Content?.Dispose();
            request.Content = content;

            Dispatched = true;
            HttpResponseMessage response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            int status = (int)response.StatusCode;
            if (status is >= 300 and < 400)
            {
                // The SDK's pipeline follows some redirects itself, whatever the HTTP handler is told; it never sees this one.
                RedirectStatus = status;
                response.Dispose();
                throw Refused();
            }
            if (response.Content.Headers.ContentLength > admitted)
            {
                ResponseTooLarge = true;
                response.Dispose();
                throw Refused();
            }
            response.Content = new BoundedContent(response.Content, admitted, () => ResponseTooLarge = true);
            return response;
        }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) => throw Refused();

        private bool IsTheKey(AuthenticationHeaderValue? authorization) =>
            authorization is { Scheme: "Bearer", Parameter: string value } && value.AsSpan().SequenceEqual(key.Reveal());
    }

    /// <summary>Carries no message: what was refused is never described in text.</summary>
    private static InvalidOperationException Refused() => new();

    /// <summary>A response body that fails once more than the admitted bytes are read, whoever reads it.</summary>
    private sealed class BoundedContent : HttpContent
    {
        private readonly HttpContent inner;
        private readonly long admitted;
        private readonly Action onTooLarge;

        public BoundedContent(HttpContent inner, long admitted, Action onTooLarge)
        {
            this.inner = inner;
            this.admitted = admitted;
            this.onTooLarge = onTooLarge;
            foreach (KeyValuePair<string, IEnumerable<string>> header in inner.Headers) Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        protected override async Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) =>
            new BoundedStream(await inner.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), admitted, onTooLarge);

        protected override Task<Stream> CreateContentReadStreamAsync() => CreateContentReadStreamAsync(CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            Stream source = await CreateContentReadStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (source.ConfigureAwait(false)) await source.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class BoundedStream(Stream inner, long admitted, Action onTooLarge) : Stream
    {
        private long total;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => total; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Count(await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }

        private int Count(int read)
        {
            total += read;
            if (total <= admitted) return read;
            onTooLarge();
            throw Refused();
        }
    }
}

using System.Security.Cryptography;

namespace PCSteward.Execution;

/// <summary>
/// What remains of the original helper channel once protocol 1 is retired (Phase D stage D4b, stage 3; design note §8): the frame
/// bound of <see cref="PipeMessageFraming"/>, shared by protocol 2 and the qualification probe, and the 256-bit nonce generator. The
/// protocol-1 request and response, their validator and the protocol-1 pipe locator are removed; the helper refuses a protocol-1
/// line before it connects (<see cref="ElevatedHelperEntryPoint.Protocol1RefusedExitCode"/>).
/// </summary>
internal static class ElevatedHelperProtocol
{
    internal const int MaximumMessageBytes = 16 * 1024;

    internal static string CreateNonce() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}

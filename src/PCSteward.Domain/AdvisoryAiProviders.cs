using System.Text.Json;

namespace PCSteward.Domain;

/// <summary>
/// ADR-028 addendum A1 (owner request 2026-11-14): the AI providers this build knows. Adding one (for example Anthropic) needs its
/// own reviewed ADR amendment for privacy, retention, route and headers, its own official SDK approval, Task 69 evidence for each of
/// its models and the Task 70 review before any model of it is reviewed. Values are stored, so they never change meaning.
/// </summary>
public enum AdvisoryAiProviderId
{
    OpenAI = 1
}

/// <summary>
/// ADR-028 addendum A1: how one provider's request body is written and its response read. Implementations are pure: they never
/// add evidence, metadata or identifiers beyond the model, the fixed instructions, the owner-approved input and the approved limits.
/// </summary>
public interface IAdvisoryAiWireFormat
{
    /// <summary>The exact UTF-8 request body the owner previews and approves. Only this body is ever sent.</summary>
    byte[] SerializeBody(AdvisoryAiModelProfile model, AdvisoryAiLimits limits, string instructions, string inputText);

    /// <summary>Reads one response strictly and boundedly against the approved request.</summary>
    AdvisoryAiResponse Read(ReadOnlySpan<byte> body, AdvisoryAiApproval approval);
}

/// <summary>
/// FR-AI-04 (ADR-028 Q1/Q8/F11 and addendum A1): one provider's fixed route, credential target and header profile. Each value is
/// compiled in: there is no custom URL, region, proxy endpoint, query, user-info, organization or project routing. A change is a
/// reviewed policy change and needs fresh consent.
/// </summary>
/// <param name="Id">The stored provider identity.</param>
/// <param name="Name">The provider's name as the owner sees it.</param>
/// <param name="Endpoint">The one HTTPS endpoint a request is POSTed to.</param>
/// <param name="CredentialTarget">The one exact Windows generic credential target for this provider's key (ADR-028 Q8).</param>
/// <param name="AuthorizationName">The header that carries the key.</param>
/// <param name="AuthorizationPreview">What the preview shows in place of the key, which is never shown.</param>
/// <param name="FixedHeaders">Further fixed, non-identifying headers the provider requires, shown in the preview and sent as shown.</param>
/// <param name="WireFormat">The provider's request and response format.</param>
/// <param name="DataControlsPage">The provider's own page on what it keeps, linked at connect and before the first request (ADR-028).</param>
/// <param name="PricingPage">The provider's official price page, which the owner reads before confirming prices (ADR-029 F6).</param>
public sealed record AdvisoryAiProvider(
    AdvisoryAiProviderId Id,
    string Name,
    Uri Endpoint,
    string CredentialTarget,
    string AuthorizationName,
    string AuthorizationPreview,
    IReadOnlyList<KeyValuePair<string, string>> FixedHeaders,
    IAdvisoryAiWireFormat WireFormat,
    Uri DataControlsPage,
    Uri PricingPage)
{
    public const string Method = "POST";
}

/// <summary>ADR-028 addendum A1: the provider registry. Only OpenAI exists in this build (ADR-028 D2).</summary>
public static class AdvisoryAiProviders
{
    public static AdvisoryAiProvider OpenAI { get; } = new(
        AdvisoryAiProviderId.OpenAI,
        "OpenAI",
        new Uri("https://api.openai.com/v1/responses", UriKind.Absolute),
        "PCSteward/OpenAI/ApiKey.v1",
        "Authorization",
        "Bearer [your key, never shown]",
        [],
        OpenAiResponsesWireFormat.Instance,
        new Uri("https://developers.openai.com/api/docs/guides/your-data", UriKind.Absolute),
        new Uri("https://developers.openai.com/api/docs/pricing", UriKind.Absolute));

    public static IReadOnlyList<AdvisoryAiProvider> All { get; } = [OpenAI];

    public static AdvisoryAiProvider? Find(AdvisoryAiProviderId id) => All.FirstOrDefault(provider => provider.Id == id);
}

/// <summary>
/// FR-AI-02/04 (ADR-028 Q1 and consent item 4): the OpenAI Responses body. Fields: model, the fixed instructions, the draft's canonical
/// text as input, the output limit, <c>store: false</c>, truncation disabled, the profile's service tier and, when the profile sets
/// one, its reasoning effort. No tools, conversation, previous response, background mode, metadata, streaming or user identifier is
/// ever written.
/// </summary>
public sealed class OpenAiResponsesWireFormat : IAdvisoryAiWireFormat
{
    public static OpenAiResponsesWireFormat Instance { get; } = new();

    private OpenAiResponsesWireFormat()
    {
    }

    public byte[] SerializeBody(AdvisoryAiModelProfile model, AdvisoryAiLimits limits, string instructions, string inputText)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(instructions);
        ArgumentNullException.ThrowIfNull(inputText);
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, AdvisoryAiInputText.WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("model", model.ModelId);
            writer.WriteString("instructions", instructions);
            writer.WriteString("input", inputText);
            writer.WriteNumber("max_output_tokens", limits.MaximumOutputTokens);
            writer.WriteBoolean("store", false);
            writer.WriteString("truncation", "disabled");
            writer.WriteString("service_tier", model.ServiceTier);
            if (model.ReasoningEffort is string effort)
            {
                writer.WriteStartObject("reasoning");
                writer.WriteString("effort", effort);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    public AdvisoryAiResponse Read(ReadOnlySpan<byte> body, AdvisoryAiApproval approval) => AdvisoryAiResponseReader.Read(body, approval);
}

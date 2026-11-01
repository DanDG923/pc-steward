using System.Globalization;
using System.Reflection;
using System.Text;
using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Execution;
using PCSteward.Provider.Chocolatey;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-HIS-01; FR-UPD-15; TM-07/13. Task 50 adapter gates §B.2/§B.7: the single Execution → Application adapter maps by
/// name and copies reported versions verbatim; the history reader's version grammar never diverges from the codec's (O9
/// corpus differential); writer and reader agree (§R B2-L2); and no conversion to or from candidate provenance exists.
/// </summary>
[TestClass]
public sealed class ReviewedHelperCheckAdapterTests
{
    private static readonly IReviewedHelperVersionGrammar Grammar = new ChocolateyHelperVersionGrammar();

    [TestMethod]
    public void AdapterEnumsMatchTheWireAndCheckEnumsByName()
    {
        CollectionAssert.AreEqual(Enum.GetNames<ReviewedHelperCandidateCheckKind>(), Enum.GetNames<RecordedHelperCheckKind>());
        CollectionAssert.AreEqual(Enum.GetNames<ReviewedHelperRefusalReason>(), Enum.GetNames<RecordedHelperRefusalReason>());
    }

    [TestMethod]
    public void ToRecordedMapsEveryKindAndReasonByNameAndCopiesVersionsVerbatim()
    {
        Assert.AreSame(RecordedHelperCandidateCheck.NotObserved, ReviewedHelperCandidateCheck.NotObserved.ToRecorded());
        RecordedHelperCandidateCheck confirmed = ReviewedHelperCandidateCheck.FromAcceptedFacts(Protocol2InterpreterFixture.Matched()).ToRecorded();
        Assert.AreEqual(RecordedHelperCheckKind.Confirmed, confirmed.Kind);
        Assert.AreEqual(RecordedHelperRefusalReason.None, confirmed.RefusalReason);
        Assert.AreEqual(Protocol2InterpreterFixture.Installed, confirmed.ReportedInstalledVersion);
        Assert.AreEqual(Protocol2InterpreterFixture.Candidate, confirmed.ReportedCandidateVersion);
        RecordedHelperCandidateCheck unconfirmed = ReviewedHelperCandidateCheck.FromAcceptedFacts(Protocol2InterpreterFixture.Undetermined()).ToRecorded();
        Assert.AreEqual(RecordedHelperCheckKind.Unconfirmed, unconfirmed.Kind);
        Assert.IsNull(unconfirmed.ReportedInstalledVersion);
        foreach (ReviewedHelperRefusalReason reason in Enum.GetValues<ReviewedHelperRefusalReason>().Where(value => value != ReviewedHelperRefusalReason.None))
        {
            (string? installed, string? candidate) = reason switch
            {
                ReviewedHelperRefusalReason.RequestNotAdmitted or ReviewedHelperRefusalReason.ApprovalExpired => ((string?)null, (string?)null),
                ReviewedHelperRefusalReason.InstalledMismatch => (Protocol2InterpreterFixture.OtherInstalled, null),
                ReviewedHelperRefusalReason.CandidateMismatch => (Protocol2InterpreterFixture.Installed, Protocol2InterpreterFixture.OtherCandidate),
                ReviewedHelperRefusalReason.PreflightQueryFailed or ReviewedHelperRefusalReason.ProfileMismatch => (Protocol2InterpreterFixture.Installed, null),
                _ => (Protocol2InterpreterFixture.Installed, Protocol2InterpreterFixture.Candidate),
            };
            RecordedHelperCandidateCheck recorded = ReviewedHelperCandidateCheck.FromAcceptedFacts(
                Protocol2InterpreterFixture.Refused(reason, installed, candidate, Protocol2InterpreterFixture.Fingerprint)).ToRecorded();
            Assert.AreEqual(RecordedHelperCheckKind.Refused, recorded.Kind);
            Assert.AreEqual(reason.ToString(), recorded.RefusalReason.ToString());
            Assert.AreSame(installed, recorded.ReportedInstalledVersion);
            Assert.AreSame(candidate, recorded.ReportedCandidateVersion);
        }
    }

    [TestMethod]
    public void WorkflowObservationAppendsOnlyTheHelperCheckToTheInterpretedObservation()
    {
        ReviewedUpdateOperation frozen = Protocol2InterpreterFixture.Operation();
        foreach ((byte[] frame, RecordedHelperCheckKind kind) in new[]
        {
            (Protocol2InterpreterFixture.Frame(frozen, Protocol2InterpreterFixture.Started(), Protocol2InterpreterFixture.Matched()), RecordedHelperCheckKind.Confirmed),
            (Protocol2InterpreterFixture.Frame(frozen, Protocol2InterpreterFixture.NotStarted(), Protocol2InterpreterFixture.Refused(ReviewedHelperRefusalReason.CandidateMismatch,
                Protocol2InterpreterFixture.Installed, Protocol2InterpreterFixture.OtherCandidate, Protocol2InterpreterFixture.Fingerprint)), RecordedHelperCheckKind.Refused),
            (Protocol2InterpreterFixture.Frame(frozen, Protocol2InterpreterFixture.NotStarted(timedOut: true), Protocol2InterpreterFixture.Undetermined()), RecordedHelperCheckKind.Unconfirmed),
            (Protocol2InterpreterFixture.Frame(frozen, Protocol2InterpreterFixture.Started(), Protocol2InterpreterFixture.Matched(fingerprint: Protocol2InterpreterFixture.OtherFingerprint)), RecordedHelperCheckKind.NotObserved),
        })
        {
            ReviewedUpdateProtocol2Interpretation interpretation = Protocol2InterpreterFixture.Interpret(frozen, frame);
            Assert.IsNull(interpretation.Observation.HelperCheck);
            ReviewedUpdateDispatchObservation workflow = interpretation.WorkflowObservation;
            Assert.AreEqual(kind, workflow.HelperCheck!.Kind);
            Assert.AreEqual(interpretation.Observation, workflow with { HelperCheck = null });
            // The recorded value is consistent with the frozen operation for the outcome the workflow derives from it.
            if (kind != RecordedHelperCheckKind.NotObserved)
                Assert.IsTrue(workflow.HelperCheck.IsConsistentWith(frozen, kind switch
                {
                    RecordedHelperCheckKind.Confirmed => UpdateDispatchOutcome.Success,
                    RecordedHelperCheckKind.Refused => UpdateDispatchOutcome.Refused,
                    _ => UpdateDispatchOutcome.Failed,
                }, kind == RecordedHelperCheckKind.Confirmed ? UpdateDispatchSettlementState.OwnedWorkStopped : UpdateDispatchSettlementState.NotInvoked));
        }
    }

    [TestMethod]
    public void HistoryReaderAndCodecAcceptExactlyTheSameVersionsOverTheSharedO9Corpus()
    {
        int accepted = 0, rejected = 0;
        foreach (string text in ReviewedUpdateWireCodecReviewedFactTests.O9VersionCorpus().Append("None").Append("none").Append("None "))
        {
            bool codec = ReviewedUpdateWireCodec.IsNormalizedVersion(text);
            bool reader = ReviewedUpdateTerminalRecord.TryParse(ForgeConfirmed(text), Grammar, out ReviewedUpdateTerminalRecord? parsed);
            Assert.AreEqual(codec, reader, text);
            if (reader) Assert.AreEqual(text, parsed!.HelperCheck.ReportedInstalledVersion);
            if (codec)
            {
                // B2-L2: every value the codec accepts can be written through the single factory and read back.
                accepted++;
                byte[] written = new UpdateDispatchTerminal(UpdateDispatchOutcome.Cancelled, Protocol2InterpreterFixture.ReviewedAt, null, "x",
                    RecordedHelperCandidateCheck.FromDesktopDerived(RecordedHelperCheckKind.Confirmed, RecordedHelperRefusalReason.None, text, text)).GetReservedPayload(NotInvoked());
                Assert.IsTrue(ReviewedUpdateTerminalRecord.TryParse(written, Grammar, out _), text);
            }
            else rejected++;
        }
        Assert.IsGreaterThan(1_000, accepted);
        Assert.IsGreaterThan(1_000, rejected);
    }

    [TestMethod]
    public void NoMethodConvertsBetweenTheHelperCheckAndCandidateProvenance()
    {
        Type[] checks = [typeof(RecordedHelperCandidateCheck), typeof(ReviewedHelperCandidateCheck), typeof(RecordedHelperCheckKind), typeof(ReviewedHelperCandidateCheckKind)];
        Type provenance = typeof(ReviewedCandidateProvenance);
        Assembly[] assemblies = [typeof(RecordedHelperCandidateCheck).Assembly, typeof(ReviewedUpdateOperation).Assembly,
            typeof(ReviewedHelperCandidateCheck).Assembly, typeof(ChocolateyHelperVersionGrammar).Assembly];
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        int inspected = 0;
        foreach (Type type in assemblies.SelectMany(assembly => assembly.GetTypes()))
        foreach (MethodInfo method in type.GetMethods(All))
        {
            inspected++;
            IEnumerable<Type> inputs = method.GetParameters().Select(parameter => parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType()! : parameter.ParameterType);
            if (!method.IsStatic) inputs = inputs.Append(type);
            Type[] outputs = [method.ReturnType, .. method.GetParameters().Where(parameter => parameter.IsOut).Select(parameter => parameter.ParameterType.GetElementType()!)];
            List<Type> inputList = inputs.ToList();
            bool checkToProvenance = inputList.Any(checks.Contains) && outputs.Contains(provenance);
            bool provenanceToCheck = inputList.Contains(provenance) && outputs.Any(checks.Contains);
            Assert.IsFalse(checkToProvenance || provenanceToCheck, $"{type.FullName}.{method.Name}");
        }
        Assert.IsGreaterThan(1_000, inspected);
    }

    // Forged version-3 Confirmed slot (bypasses the factory) so the reader alone judges the version text.
    // Live writers always pass a settlement; v3 without settlement facts is unreadable (review 12 L2).
    private static UpdateDispatchSettlement NotInvoked()
    {
        ReviewedUpdateOperation op = Protocol2InterpreterFixture.Operation();
        UpdateDispatchClaim claim = new(Guid.NewGuid(), Guid.NewGuid(), new ReviewedUpdateDraft(op, Guid.NewGuid()),
            new FreshUpdateDispatchEvidence(op.Selection, op.RequestingUserSid, op.NativeInstanceId, op.InstallationContextId, "fresh:1",
                op.ReviewedAt, op.RouteContract, op.Mode, true, true), op.ReviewedAt);
        OperationOwnership owner = new(op.RequestingUserSid, OperationGateContract.NamePrefix + op.RequestingUserSid, false, reconciled: true);
        return UpdateDispatchSettlement.BeforeInvocation(claim, owner);
    }

    private static byte[] ForgeConfirmed(string version)
    {
        byte[] diagnostic = "d"u8.ToArray();
        string header = string.Join('\n', "pc-steward-update-terminal:3", "Cancelled",
            Protocol2InterpreterFixture.ReviewedAt.ToString("O", CultureInfo.InvariantCulture), "None", "NotInvoked", "False/False/False/False/False/False",
            "helper-check:Confirmed", "helper-refusal:None", "helper-installed:" + version, "helper-candidate:" + version,
            diagnostic.Length.ToString(CultureInfo.InvariantCulture)) + "\n";
        byte[] reserved = new byte[UpdateDispatchTerminal.ReservedBytes];
        byte[] headerBytes = Encoding.UTF8.GetBytes(header);
        if (headerBytes.Length + diagnostic.Length > reserved.Length) return reserved;
        headerBytes.CopyTo(reserved, 0);
        diagnostic.CopyTo(reserved, headerBytes.Length);
        return reserved;
    }
}

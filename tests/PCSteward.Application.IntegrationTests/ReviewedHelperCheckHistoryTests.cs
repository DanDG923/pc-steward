using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using PCSteward.Domain;
using PCSteward.Persistence.Sqlite;
using PCSteward.Provider.Chocolatey;
using Kind = PCSteward.Application.RecordedHelperCheckKind;
using Reason = PCSteward.Application.RecordedHelperRefusalReason;
using Outcome = PCSteward.Application.UpdateDispatchOutcome;
using Settled = PCSteward.Application.UpdateDispatchSettlementState;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// FR-HIS-01; FR-UPD-15; FR-UPD-01; TM-03/04/07/13. Task 50 adapter gates Part B (§B.2–§B.7, §R B2-L1/B2-L2): the
/// desktop-derived helper check is persisted in the version-3 terminal slot, read back strictly, presented only as a
/// separate recorded-at-the-time report, and never changes outcome, settlement or blocking.
/// </summary>
[TestClass]
public sealed class ReviewedHelperCheckHistoryTests
{
    private const string User = "S-1-5-21-11-22-33-1001";
    private const string Installed = "1.2.3";
    private const string Candidate = "1.2.4";
    private const string OtherInstalled = "1.2.2";
    private const string OtherCandidate = "1.2.5";
    private const string ConfirmedReport =
        "Recorded at the time: reported by PC Steward's administrator helper: the exact reviewed version matched its protected machine checks.";
    private static readonly DateTimeOffset ReviewedAt = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly ChocolateyHelperVersionGrammar Grammar = new();
    private static readonly Regex ForbiddenReportWords = new(@"\b(updated|success|verified|completed)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // ---- Factory: structural rules R1–R4 and the version form ----

    [TestMethod]
    [DataRow(Kind.Confirmed, Reason.ProfileMismatch, Installed, Candidate)]   // R1
    [DataRow(Kind.Unconfirmed, Reason.ApprovalExpired, null, null)]          // R1
    [DataRow(Kind.Refused, Reason.None, null, null)]                        // R1
    [DataRow(Kind.NotObserved, Reason.None, Installed, null)]
    [DataRow(Kind.Confirmed, Reason.None, Installed, null)]                 // R2
    [DataRow(Kind.Confirmed, Reason.None, null, Candidate)]                 // R2
    [DataRow(Kind.Unconfirmed, Reason.None, Installed, null)]               // R3
    [DataRow(Kind.Refused, Reason.RequestNotAdmitted, Installed, null)]     // R4 / S6
    [DataRow(Kind.Refused, Reason.SourceNotCommunity, null, Candidate)]     // R4 / S7
    [DataRow(Kind.Refused, Reason.PreflightQueryFailed, Installed, Candidate)] // R4 / S7
    [DataRow(Kind.Refused, Reason.ProfileMismatch, null, null)]             // R4 / S9
    [DataRow(Kind.Confirmed, Reason.None, "", Candidate)]
    [DataRow(Kind.Confirmed, Reason.None, "v1.2.3", Candidate)]
    [DataRow(Kind.Confirmed, Reason.None, "1.0+meta", Candidate)]
    [DataRow(Kind.Confirmed, Reason.None, "1.0 ", Candidate)]
    [DataRow(Kind.Confirmed, Reason.None, "1.0.0é", Candidate)]
    [DataRow((Kind)9, Reason.None, null, null)]
    [DataRow(Kind.Refused, (Reason)42, null, null)]
    public void FactoryRejectsStructuralViolations(Kind kind, Reason reason, string? installed, string? candidate) =>
        Assert.ThrowsExactly<ArgumentException>(() => RecordedHelperCandidateCheck.FromDesktopDerived(kind, reason, installed, candidate));

    [TestMethod]
    public void FactoryAcceptsEveryStructurallyValidShapeAndNotObservedIsTheSingleton()
    {
        Assert.AreSame(RecordedHelperCandidateCheck.NotObserved, RecordedHelperCandidateCheck.FromDesktopDerived(Kind.NotObserved, Reason.None, null, null));
        Assert.AreEqual(Kind.Confirmed, Check(Kind.Confirmed, Reason.None, Installed, Candidate).Kind);
        Assert.AreEqual(Kind.Unconfirmed, Check(Kind.Unconfirmed, Reason.None, null, null).Kind);
        // The factory checks only the structural form (a superset); "1.0" is not native-normalized and fails on read.
        Assert.AreEqual("1.0", Check(Kind.Confirmed, Reason.None, "1.0", "1.0").ReportedInstalledVersion);
        foreach (Reason reason in Enum.GetValues<Reason>().Where(value => value != Reason.None))
        {
            (string? installed, string? candidate) = RefusedVersions(reason);
            RecordedHelperCandidateCheck check = Check(Kind.Refused, reason, installed, candidate);
            Assert.AreEqual(reason, check.RefusalReason);
            Assert.AreEqual(installed, check.ReportedInstalledVersion);
            Assert.AreEqual(candidate, check.ReportedCandidateVersion);
        }
    }

    [TestMethod]
    public void ConsistencyNeedsTheRuntimeLimitOperationAndNotObservedIsAlwaysConsistent()
    {
        RecordedHelperCandidateCheck confirmed = Check(Kind.Confirmed, Reason.None, Installed, Candidate);
        Assert.IsTrue(confirmed.IsConsistentWith(RuntimeLimits(), Outcome.Success, Settled.OwnedWorkStopped));
        Assert.IsFalse(confirmed.IsConsistentWith(null, Outcome.Success, Settled.OwnedWorkStopped));
        Assert.IsFalse(confirmed.IsConsistentWith(WinGet(), Outcome.Success, Settled.OwnedWorkStopped));
        Assert.IsFalse(confirmed.IsConsistentWith(RuntimeLimits(route: UpdateRouteContract.ChocolateyMachineProvisionalV3), Outcome.Success, Settled.OwnedWorkStopped));
        Assert.IsTrue(RecordedHelperCandidateCheck.NotObserved.IsConsistentWith(null, Outcome.Refused, Settled.OwnedWorkStopped));
    }

    // ---- Writer: version 2 byte identity and version 3 round trip ----

    [TestMethod]
    public void NotObservedStillWritesTheVersion2BytesOfEarlierBuilds()
    {
        ReviewedUpdateOperation op = RuntimeLimits();
        UpdateDispatchClaim claim = new(Guid.NewGuid(), Guid.NewGuid(), new ReviewedUpdateDraft(op, Guid.NewGuid()), Evidence(op), ReviewedAt);
        OperationOwnership owner = Owner();
        foreach (UpdateDispatchSettlement? settlement in new[] { null, UpdateDispatchSettlement.BeforeInvocation(claim, owner),
            UpdateDispatchSettlement.FromObservedWork(claim, owner, Task.CompletedTask, true, true, true, false, false) })
        foreach (UpdateDispatchTerminal terminal in new[]
        {
            new UpdateDispatchTerminal(Outcome.Success, ReviewedAt, 0, "verified ✓"),
            new UpdateDispatchTerminal(Outcome.Uncertain, ReviewedAt, -7, "x", RecordedHelperCandidateCheck.NotObserved),
        })
        {
            byte[] payload = terminal.GetReservedPayload(settlement);
            CollectionAssert.AreEqual(FrozenV2Writer(terminal, settlement), payload);
            Assert.IsTrue(ReviewedUpdateTerminalRecord.TryParse(payload, Grammar, out ReviewedUpdateTerminalRecord? parsed));
            Assert.AreSame(RecordedHelperCandidateCheck.NotObserved, parsed!.HelperCheck);
            Assert.IsTrue(ReviewedUpdateTerminalRecord.TryParse(payload, null, out _), "v2 never needs the grammar");
        }
    }

    [TestMethod]
    public void Version3WithoutSettlementFactsIsNeverReadable()
    {
        // Review 12 L2: GetReservedPayload(null) can encode a v3 "InvocationUnknown|None" header that no live writer produces.
        RecordedHelperCandidateCheck check = Check(Kind.Refused, Reason.MachineInputsUnsupported, null, null);
        byte[] payload = new UpdateDispatchTerminal(Outcome.Uncertain, ReviewedAt, null, "forged", check).GetReservedPayload(null);
        Assert.IsFalse(ReviewedUpdateTerminalRecord.TryParse(payload, Grammar, out _));
    }

    [TestMethod]
    public void MaximalVersion3FrameRoundTripsByteExactly()
    {
        string longInstalled = "1.0.0-" + new string('a', 122), longCandidate = "1.0.1-" + new string('b', 122);
        Assert.IsTrue(Grammar.IsNormalized(longInstalled) && Grammar.IsNormalized(longCandidate));
        ReviewedUpdateOperation op = RuntimeLimits();
        UpdateDispatchClaim claim = new(Guid.NewGuid(), Guid.NewGuid(), new ReviewedUpdateDraft(op, Guid.NewGuid()), Evidence(op), ReviewedAt);
        UpdateDispatchSettlement settlement = UpdateDispatchSettlement.FromObservedWork(claim, Owner(), new TaskCompletionSource().Task, false, false, false, false, false);
        RecordedHelperCandidateCheck check = Check(Kind.Refused, Reason.MachineInputsUnsupported, longInstalled, longCandidate);
        UpdateDispatchTerminal terminal = new(Outcome.RestartRequired, new DateTimeOffset(2026, 12, 31, 23, 59, 59, TimeSpan.Zero).AddTicks(9_999_999),
            int.MinValue, new string('€', 4096), check);
        byte[] payload = terminal.GetReservedPayload(settlement);
        int headerLength = payload.Length - payload.Reverse().TakeWhile(value => value == 0).Count() - 4096 * 3;
        Assert.IsLessThanOrEqualTo(509, headerLength);
        Assert.IsGreaterThan(500, headerLength);

        Assert.IsTrue(ReviewedUpdateTerminalRecord.TryParse(payload, Grammar, out ReviewedUpdateTerminalRecord? parsed));
        Assert.AreEqual(Kind.Refused, parsed!.HelperCheck.Kind);
        Assert.AreEqual(longInstalled, parsed.HelperCheck.ReportedInstalledVersion);
        Assert.AreEqual(longCandidate, parsed.HelperCheck.ReportedCandidateVersion);
        Assert.AreEqual(terminal.Diagnostic, parsed.Diagnostic);
        UpdateDispatchTerminal rewritten = new(parsed.Outcome, parsed.OccurredAt, parsed.ExitCode, parsed.Diagnostic, parsed.HelperCheck);
        CollectionAssert.AreEqual(payload, rewritten.GetReservedPayload(settlement));
        Assert.IsFalse(ReviewedUpdateTerminalRecord.TryParse(payload, null, out _), "v3 without a grammar is unreadable");
    }

    [TestMethod]
    [DataRow(Kind.Confirmed, Reason.None, Outcome.Success, Settled.OwnedWorkStopped)]
    [DataRow(Kind.Confirmed, Reason.None, Outcome.Unverified, Settled.OwnedWorkStopped)]
    [DataRow(Kind.Confirmed, Reason.None, Outcome.Failed, Settled.NotInvoked)]
    [DataRow(Kind.Confirmed, Reason.None, Outcome.Failed, Settled.OwnedWorkStopped)]
    [DataRow(Kind.Confirmed, Reason.None, Outcome.Cancelled, Settled.NotInvoked)]
    [DataRow(Kind.Confirmed, Reason.None, Outcome.Uncertain, Settled.OwnedWorkUnsettled)]
    [DataRow(Kind.Refused, Reason.RequestNotAdmitted, Outcome.Refused, Settled.NotInvoked)]
    [DataRow(Kind.Refused, Reason.MachineInputsUnsupported, Outcome.Refused, Settled.NotInvoked)]
    [DataRow(Kind.Refused, Reason.SourceNotCommunity, Outcome.Refused, Settled.NotInvoked)]
    [DataRow(Kind.Refused, Reason.InstalledMismatch, Outcome.Refused, Settled.NotInvoked)]
    [DataRow(Kind.Refused, Reason.ProfileMismatch, Outcome.Refused, Settled.NotInvoked)]
    [DataRow(Kind.Refused, Reason.PreflightQueryFailed, Outcome.Refused, Settled.NotInvoked)]
    [DataRow(Kind.Refused, Reason.CandidateMismatch, Outcome.Refused, Settled.NotInvoked)]
    [DataRow(Kind.Refused, Reason.ApprovalExpired, Outcome.Refused, Settled.NotInvoked)]
    [DataRow(Kind.Refused, Reason.CandidateMismatch, Outcome.Uncertain, Settled.OwnedWorkUnsettled)]
    [DataRow(Kind.Unconfirmed, Reason.None, Outcome.Failed, Settled.NotInvoked)]
    [DataRow(Kind.Unconfirmed, Reason.None, Outcome.Cancelled, Settled.NotInvoked)]
    [DataRow(Kind.Unconfirmed, Reason.None, Outcome.Uncertain, Settled.OwnedWorkUnsettled)]
    public async Task EveryAllowedCombinationIsWrittenReadableAndRoundTripsByteExactly(Kind kind, Reason reason, Outcome outcome, Settled settled)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        UpdateDispatchClaim claim = await fixture.ClaimRuntimeLimitsAsync();
        OperationOwnership owner = Owner();
        UpdateDispatchSettlement settlement = Settlement(claim, owner, settled);
        RecordedHelperCandidateCheck check = CheckFor(kind, reason);
        UpdateDispatchTerminal terminal = new(outcome, ReviewedAt.AddMinutes(2), ExitFor(outcome, settled), "recorded", check);
        await fixture.Store.RecordFinalAsync(claim, owner, terminal, settlement, CancellationToken.None);

        byte[] stored = await fixture.ReadTerminalAsync(claim.Draft.Operation.JobId);
        CollectionAssert.AreEqual(terminal.GetReservedPayload(settlement), stored);
        StringAssert.StartsWith(Encoding.ASCII.GetString(stored, 0, 29), "pc-steward-update-terminal:3\n");
        ReviewedUpdateHistoryRecord record = (await fixture.Store.GetReviewedUpdateHistoryAsync(10, CancellationToken.None)).Single();
        Assert.IsTrue(record.IntegrityVerified, "B2-L2: every written v3 row is readable");
        Assert.AreEqual(outcome, record.Terminal!.Outcome);
        Assert.AreEqual(settled, record.Terminal.Settlement);
        Assert.AreEqual(kind, record.Terminal.HelperCheck.Kind);
        Assert.AreEqual(reason, record.Terminal.HelperCheck.RefusalReason);
        Assert.AreEqual(check.ReportedInstalledVersion, record.Terminal.HelperCheck.ReportedInstalledVersion);
        Assert.AreEqual(check.ReportedCandidateVersion, record.Terminal.HelperCheck.ReportedCandidateVersion);
        ReviewedUpdateHistoryView view = ReviewedUpdateHistoryPresentation.Present(record);
        Assert.AreEqual(outcome == Outcome.Uncertain, view.BlocksOtherUpdates);
        Assert.AreEqual(view.BlocksOtherUpdates, await fixture.Store.HasUnresolvedDispatchesAsync(Owner(), CancellationToken.None));
        Assert.AreEqual(kind == Kind.Unconfirmed ? null : ExpectedReport(check), view.HelperReport);
        AssertPresentationRules(view);
    }

    // ---- Store write: R/P rules, outcome matrix, route, missing grammar ----

    [TestMethod]
    [DataRow("confirmed-installed-differs")]
    [DataRow("confirmed-candidate-differs")]
    [DataRow("profile-mismatch-installed-differs")]
    [DataRow("candidate-mismatch-candidate-equal")]
    [DataRow("other-reason-candidate-differs")]
    [DataRow("installed-mismatch-installed-equal")]
    [DataRow("confirmed-refused")]
    [DataRow("confirmed-elevation-denied")]
    [DataRow("confirmed-restart-required")]
    [DataRow("refused-success")]
    [DataRow("refused-failed")]
    [DataRow("refused-cancelled")]
    [DataRow("refused-owned-work-stopped")]
    [DataRow("unconfirmed-success")]
    [DataRow("unconfirmed-unverified")]
    [DataRow("unconfirmed-refused")]
    [DataRow("confirmed-uncertain-not-invoked")]
    public async Task StoreRefusesInconsistentHelperChecksAndWritesNothing(string violation)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        UpdateDispatchClaim claim = await fixture.ClaimRuntimeLimitsAsync();
        OperationOwnership owner = Owner();
        (RecordedHelperCandidateCheck check, Outcome outcome, Settled settled) = Violation(violation);
        UpdateDispatchTerminal terminal = new(outcome, ReviewedAt.AddMinutes(2), ExitFor(outcome, settled), "x", check);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            fixture.Store.RecordFinalAsync(claim, owner, terminal, Settlement(claim, owner, settled), CancellationToken.None));
        Assert.AreEqual(UpdateDispatchState.DispatchClaimed, await fixture.Store.GetDispatchStateAsync(claim.Draft.Operation.JobId, CancellationToken.None));
    }

    [TestMethod]
    public async Task StoreRefusesAHelperCheckOnAWinGetRowAndAStoreWithoutGrammarRefusesVersion3()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        UpdateDispatchClaim winget = (await fixture.ClaimWinGetAsync())!;
        OperationOwnership owner = Owner();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Store.RecordFinalAsync(winget, owner,
            new(Outcome.Failed, ReviewedAt.AddMinutes(2), null, "x", Check(Kind.Unconfirmed, Reason.None, null, null)),
            UpdateDispatchSettlement.BeforeInvocation(winget, owner), CancellationToken.None));
        Assert.AreEqual(UpdateDispatchState.DispatchClaimed, await fixture.Store.GetDispatchStateAsync(winget.Draft.Operation.JobId, CancellationToken.None));
        // Resolve the WinGet barrier (without a check) so the next claim can be created.
        OperationOwnership other = Owner();
        await fixture.Store.RecordFinalAsync(winget, other, new(Outcome.Cancelled, ReviewedAt.AddMinutes(2), null, "x"),
            UpdateDispatchSettlement.BeforeInvocation(winget, other), CancellationToken.None);

        UpdateDispatchClaim runtime = await fixture.ClaimRuntimeLimitsAsync();
        OperationOwnership runtimeOwner = Owner();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.NoGrammarStore.RecordFinalAsync(runtime, runtimeOwner,
            new(Outcome.Refused, ReviewedAt.AddMinutes(2), null, "x", CheckFor(Kind.Refused, Reason.ApprovalExpired)),
            UpdateDispatchSettlement.BeforeInvocation(runtime, runtimeOwner), CancellationToken.None));
        Assert.AreEqual(UpdateDispatchState.DispatchClaimed, await fixture.Store.GetDispatchStateAsync(runtime.Draft.Operation.JobId, CancellationToken.None));
    }

    // ---- Reader: forged rows (enumerations, R/P rules, outcome matrix, structure) ----

    [TestMethod]
    public async Task ForgedBaselineVersion3RowIsReadable()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        UpdateDispatchClaim claim = await fixture.ClaimRuntimeLimitsAsync();
        await fixture.ForgeFinalAsync(claim, "Final", Forge(BaseLines()));
        ReviewedUpdateHistoryRecord record = (await fixture.Store.GetReviewedUpdateHistoryAsync(10, CancellationToken.None)).Single();
        Assert.IsTrue(record.IntegrityVerified);
        Assert.AreEqual(ConfirmedReport, ReviewedUpdateHistoryPresentation.Present(record).HelperReport);
    }

    [TestMethod]
    [DataRow("kind-lowercase")]
    [DataRow("kind-numeric")]
    [DataRow("kind-unknown")]
    [DataRow("kind-not-observed")]
    [DataRow("reason-unknown")]
    [DataRow("reason-numeric")]
    [DataRow("confirmed-with-reason")]
    [DataRow("unconfirmed-with-reason")]
    [DataRow("refused-with-none")]
    [DataRow("confirmed-null-version")]
    [DataRow("confirmed-installed-differs")]
    [DataRow("request-not-admitted-with-values")]
    [DataRow("candidate-without-installed")]
    [DataRow("candidate-with-preflight")]
    [DataRow("profile-mismatch-installed-null")]
    [DataRow("profile-mismatch-installed-differs")]
    [DataRow("candidate-mismatch-candidate-equal")]
    [DataRow("other-reason-candidate-differs")]
    [DataRow("installed-mismatch-installed-equal")]
    [DataRow("confirmed-refused")]
    [DataRow("confirmed-elevation-denied")]
    [DataRow("confirmed-restart-required")]
    [DataRow("refused-success")]
    [DataRow("refused-failed")]
    [DataRow("refused-cancelled")]
    [DataRow("refused-owned-work-stopped")]
    [DataRow("unconfirmed-success")]
    [DataRow("unconfirmed-unverified")]
    [DataRow("unconfirmed-refused")]
    [DataRow("facts-none-with-settlement")]
    [DataRow("facts-malformed")]
    [DataRow("prefix-missing")]
    [DataRow("v3-header-seven-lines")]
    [DataRow("v2-header-eleven-lines")]
    [DataRow("missing-length-line")]
    [DataRow("header-without-final-lf")]
    [DataRow("non-zero-padding")]
    [DataRow("diagnostic-too-long")]
    public async Task ForgedVersion3RowsAreUnreadable(string change)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        UpdateDispatchClaim claim = await fixture.ClaimRuntimeLimitsAsync();
        await fixture.ForgeFinalAsync(claim, "Final", ForgedCase(change));
        ReviewedUpdateHistoryRecord record = (await fixture.Store.GetReviewedUpdateHistoryAsync(10, CancellationToken.None)).Single();
        Assert.IsFalse(record.IntegrityVerified, change);
        Assert.IsNull(record.Terminal);
        ReviewedUpdateHistoryView view = ReviewedUpdateHistoryPresentation.Present(record);
        Assert.IsNull(view.HelperReport);
        Assert.IsFalse(view.BlocksOtherUpdates, "a corrupt Final row is untrusted, not a barrier (Task 90 L1)");
        StringAssert.StartsWith(view.Status, "Record failed integrity checks");
    }

    [TestMethod]
    public async Task Version3OnAWinGetRowIsUnreadable()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        UpdateDispatchClaim winget = (await fixture.ClaimWinGetAsync())!;
        string[] lines = BaseLines();
        lines[1] = "Failed";
        lines[3] = "None";
        lines[4] = "NotInvoked";
        lines[5] = "False/False/False/False/False/False";
        (lines[6], lines[7], lines[8], lines[9]) = ("helper-check:Unconfirmed", "helper-refusal:None", "helper-installed:None", "helper-candidate:None");
        await fixture.ForgeFinalAsync(winget, "Final", Forge(lines));
        Assert.IsFalse((await fixture.Store.GetReviewedUpdateHistoryAsync(10, CancellationToken.None)).Single().IntegrityVerified);
    }

    [TestMethod]
    [DataRow("canonical-digest")]
    [DataRow("canonical-payload")]
    public async Task Version3RowWhoseOperationFailsIntegrityHasNoReadableTerminal(string corruption)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        UpdateDispatchClaim claim = await fixture.ClaimRuntimeLimitsAsync();
        OperationOwnership owner = Owner();
        await fixture.Store.RecordFinalAsync(claim, owner, new(Outcome.Success, ReviewedAt.AddMinutes(2), 0, "verified", CheckFor(Kind.Confirmed, Reason.None)),
            Settlement(claim, owner, Settled.OwnedWorkStopped), CancellationToken.None);
        await fixture.ExecuteAsync("PRAGMA ignore_check_constraints=ON;" + (corruption == "canonical-digest"
            ? "UPDATE update_dispatch_claims SET canonical_digest=upper(hex(randomblob(32)));"
            : "UPDATE update_dispatch_claims SET canonical_payload=x'00';"));
        ReviewedUpdateHistoryRecord record = (await fixture.Store.GetReviewedUpdateHistoryAsync(10, CancellationToken.None)).Single();
        Assert.IsFalse(record.IntegrityVerified);
        Assert.IsNull(record.Operation);
        Assert.IsNull(record.Terminal, "O-none: a recorded helper check without a verified operation makes the terminal unreadable");
        Assert.IsNull(ReviewedUpdateHistoryPresentation.Present(record).HelperReport);
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(0)]
    [DataRow(6)]
    [DataRow(7)]
    [DataRow(8)]
    [DataRow(9)]
    [DataRow(16383)]
    public async Task SingleByteTamperingIsUnreadableWithOrWithoutARecomputedDigest(int lineOrOffset)
    {
        foreach (bool recompute in new[] { false, true })
        {
            await using Fixture fixture = await Fixture.CreateAsync();
            UpdateDispatchClaim claim = await fixture.ClaimRuntimeLimitsAsync();
            OperationOwnership owner = Owner();
            await fixture.Store.RecordFinalAsync(claim, owner, new(Outcome.Refused, ReviewedAt.AddMinutes(2), null, "refused",
                CheckFor(Kind.Refused, Reason.CandidateMismatch)), Settlement(claim, owner, Settled.NotInvoked), CancellationToken.None);
            byte[] stored = await fixture.ReadTerminalAsync(claim.Draft.Operation.JobId);
            byte[] tampered = (byte[])stored.Clone();
            int offset = lineOrOffset switch
            {
                -1 => 27,                                  // header version digit '3' → '2'
                16383 => 16383,                            // padding
                // Inside the value: kind/reason letters become unknown names; a version '.' becomes '/', outside the form.
                _ => LineOffset(stored, lineOrOffset) + (lineOrOffset == 0 ? 0 : 18),
            };
            tampered[offset] = lineOrOffset == -1 ? (byte)'2' : tampered[offset] == 0 ? (byte)1 : (byte)(tampered[offset] ^ 0x01);
            string digest = Convert.ToHexString(SHA256.HashData(recompute ? tampered : stored));
            await fixture.ExecuteAsync("UPDATE update_dispatch_claims SET terminal_payload=$p, terminal_digest=$d;", ("$p", tampered), ("$d", digest));
            ReviewedUpdateHistoryRecord record = (await fixture.Store.GetReviewedUpdateHistoryAsync(10, CancellationToken.None)).Single();
            Assert.IsFalse(record.IntegrityVerified, $"{lineOrOffset}/{recompute}");
            Assert.IsNull(ReviewedUpdateHistoryPresentation.Present(record).HelperReport);
        }
    }

    // ---- Parser: enumerations and versions (operation-independent) ----

    [TestMethod]
    [DataRow("")]
    [DataRow("None ")]
    [DataRow("none")]
    [DataRow("None")]
    [DataRow("1.0+meta")]
    [DataRow("01.0")]
    [DataRow("1.0")]
    [DataRow("1.0.0.0")]
    [DataRow("1.0.0-")]
    [DataRow("1.0.0é")]
    [DataRow("-1.0.0")]
    [DataRow("1.0.0-a..b")]
    public void ParserRejectsVersionsOutsideTheNativeNormalizedGrammar(string version)
    {
        Assert.IsTrue(ReviewedUpdateTerminalRecord.TryParse(Forge(BaseLines()), Grammar, out _));
        foreach (int line in new[] { 8, 9 })
        {
            string[] lines = BaseLines();
            lines[line] = (line == 8 ? "helper-installed:" : "helper-candidate:") + version;
            Assert.IsFalse(ReviewedUpdateTerminalRecord.TryParse(Forge(lines), Grammar, out _), $"{line}:{version}");
        }
        string tooLong = "1.0.0-" + new string('a', 123);
        Assert.AreEqual(129, tooLong.Length);
        string[] longLines = BaseLines();
        longLines[8] = "helper-installed:" + tooLong;
        Assert.IsFalse(ReviewedUpdateTerminalRecord.TryParse(Forge(longLines), Grammar, out _));
    }

    [TestMethod]
    public void NoGrammarMakesEveryVersion3TerminalUnreadable()
    {
        foreach (Kind kind in new[] { Kind.Confirmed, Kind.Refused, Kind.Unconfirmed })
        {
            Outcome outcome = kind == Kind.Confirmed ? Outcome.Cancelled : kind == Kind.Refused ? Outcome.Refused : Outcome.Failed;
            ReviewedUpdateOperation op = RuntimeLimits();
            UpdateDispatchClaim claim = new(Guid.NewGuid(), Guid.NewGuid(), new ReviewedUpdateDraft(op, Guid.NewGuid()), Evidence(op), ReviewedAt);
            // Live writers always pass a settlement (review 12 L2); these outcomes settle as NotInvoked.
            byte[] payload = new UpdateDispatchTerminal(outcome, ReviewedAt, null, "x", CheckFor(kind, kind == Kind.Refused ? Reason.ApprovalExpired : Reason.None))
                .GetReservedPayload(UpdateDispatchSettlement.BeforeInvocation(claim, Owner()));
            Assert.IsTrue(ReviewedUpdateTerminalRecord.TryParse(payload, Grammar, out _));
            Assert.IsFalse(ReviewedUpdateTerminalRecord.TryParse(payload, null, out _));
        }
    }

    // ---- Rollback and migration fixtures (V2 contract) ----

    [TestMethod]
    public void FrozenVersion2ParserReturnsFalseOnVersion3WithoutThrowing()
    {
        byte[] v3 = new UpdateDispatchTerminal(Outcome.Refused, ReviewedAt, null, "x", CheckFor(Kind.Refused, Reason.ProfileMismatch)).GetReservedPayload();
        byte[] v2 = new UpdateDispatchTerminal(Outcome.Refused, ReviewedAt, null, "x").GetReservedPayload();
        Assert.IsFalse(FrozenV2Parser.TryParse(v3));
        Assert.IsTrue(FrozenV2Parser.TryParse(v2));
        Assert.IsFalse(FrozenV2Parser.TryParse(Forge(BaseLines())));
    }

    [TestMethod]
    public async Task OlderBuildAndNoGrammarReaderSeeVersion3RowsAsUntrustedWithBlockingByStateOnly()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        UpdateDispatchClaim final = await fixture.ClaimRuntimeLimitsAsync();
        OperationOwnership owner = Owner();
        await fixture.Store.RecordFinalAsync(final, owner, new(Outcome.Success, ReviewedAt.AddMinutes(2), 0, "verified", CheckFor(Kind.Confirmed, Reason.None)),
            Settlement(final, owner, Settled.OwnedWorkStopped), CancellationToken.None);
        UpdateDispatchClaim uncertain = await fixture.ClaimRuntimeLimitsAsync();
        OperationOwnership owner2 = Owner();
        await fixture.Store.RecordFinalAsync(uncertain, owner2, new(Outcome.Uncertain, ReviewedAt.AddMinutes(2), null, "unsettled", CheckFor(Kind.Refused, Reason.ApprovalExpired)),
            Settlement(uncertain, owner2, Settled.OwnedWorkUnsettled), CancellationToken.None);

        // Store rollback: the dispatch-state paths check only the terminal digest, so they keep working.
        Assert.AreEqual(UpdateDispatchState.Final, await fixture.NoGrammarStore.GetDispatchStateAsync(final.Draft.Operation.JobId, CancellationToken.None));
        Assert.AreEqual(UpdateDispatchState.Uncertain, await fixture.NoGrammarStore.GetDispatchStateAsync(uncertain.Draft.Operation.JobId, CancellationToken.None));
        Assert.AreEqual(0, await fixture.NoGrammarStore.RecoverUnfinishedDispatchesAsync(Owner(), ReviewedAt.AddMinutes(5), CancellationToken.None));
        Assert.IsTrue(await fixture.NoGrammarStore.HasUnresolvedDispatchesAsync(Owner(), CancellationToken.None));

        // An older build (frozen v2 parser) and a reader without the grammar both see untrusted rows; blocking follows state.
        foreach (byte[] stored in new[] { await fixture.ReadTerminalAsync(final.Draft.Operation.JobId), await fixture.ReadTerminalAsync(uncertain.Draft.Operation.JobId) })
            Assert.IsFalse(FrozenV2Parser.TryParse(stored));
        IReadOnlyList<ReviewedUpdateHistoryRecord> rows = await fixture.NoGrammarStore.GetReviewedUpdateHistoryAsync(10, CancellationToken.None);
        Assert.HasCount(2, rows);
        foreach (ReviewedUpdateHistoryRecord row in rows)
        {
            Assert.IsFalse(row.IntegrityVerified);
            ReviewedUpdateHistoryView view = ReviewedUpdateHistoryPresentation.Present(row);
            Assert.AreEqual(row.State == UpdateDispatchState.Uncertain, view.BlocksOtherUpdates);
            Assert.IsNull(view.HelperReport);
        }
        // The current reader with the grammar trusts both.
        Assert.IsTrue((await fixture.Store.GetReviewedUpdateHistoryAsync(10, CancellationToken.None)).All(row => row.IntegrityVerified));
    }

    [TestMethod]
    public async Task RecoveryOfAClaimedRowBesideAVersion3FinalRowStillWritesVersion2Uncertain()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        UpdateDispatchClaim final = await fixture.ClaimRuntimeLimitsAsync();
        OperationOwnership owner = Owner();
        await fixture.Store.RecordFinalAsync(final, owner, new(Outcome.Refused, ReviewedAt.AddMinutes(2), null, "refused", CheckFor(Kind.Refused, Reason.InstalledMismatch)),
            Settlement(final, owner, Settled.NotInvoked), CancellationToken.None);
        UpdateDispatchClaim claimed = await fixture.ClaimRuntimeLimitsAsync();
        Assert.IsTrue(await fixture.NoGrammarStore.HasUnresolvedDispatchesAsync(Owner(), CancellationToken.None));
        Assert.AreEqual(1, await fixture.NoGrammarStore.RecoverUnfinishedDispatchesAsync(Owner(), ReviewedAt.AddMinutes(5), CancellationToken.None));
        byte[] recovered = await fixture.ReadTerminalAsync(claimed.Draft.Operation.JobId);
        StringAssert.StartsWith(Encoding.ASCII.GetString(recovered, 0, 29), "pc-steward-update-terminal:2\n");
        Assert.IsTrue(FrozenV2Parser.TryParse(recovered));
        IReadOnlyList<ReviewedUpdateHistoryRecord> rows = await fixture.Store.GetReviewedUpdateHistoryAsync(10, CancellationToken.None);
        Assert.IsTrue(rows.All(row => row.IntegrityVerified));
        Assert.AreSame(RecordedHelperCandidateCheck.NotObserved, rows.Single(row => row.State == UpdateDispatchState.Uncertain).Terminal!.HelperCheck);
        Assert.IsTrue(ReviewedUpdateHistoryPresentation.Present(rows.Single(row => row.State == UpdateDispatchState.Uncertain)).BlocksOtherUpdates);
    }

    [TestMethod]
    public async Task Version2RowReadByTheNewReaderIsNotObservedWithIdenticalText()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        UpdateDispatchClaim claim = (await fixture.ClaimWinGetAsync())!;
        OperationOwnership owner = Owner();
        await fixture.Store.RecordFinalAsync(claim, owner, new(Outcome.Success, ReviewedAt.AddMinutes(2), 0, "verified"),
            Settlement(claim, owner, Settled.OwnedWorkStopped), CancellationToken.None);
        ReviewedUpdateHistoryRecord record = (await fixture.Store.GetReviewedUpdateHistoryAsync(10, CancellationToken.None)).Single();
        Assert.IsTrue(record.IntegrityVerified);
        Assert.AreSame(RecordedHelperCandidateCheck.NotObserved, record.Terminal!.HelperCheck);
        ReviewedUpdateHistoryView view = ReviewedUpdateHistoryPresentation.Present(record);
        Assert.AreEqual("Updated: the target version was verified after the update.", view.Status);
        Assert.IsNull(view.HelperReport);
        Assert.IsTrue((await fixture.NoGrammarStore.GetReviewedUpdateHistoryAsync(10, CancellationToken.None)).Single().IntegrityVerified);
    }

    // ---- Presentation ----

    [TestMethod]
    public void HelperReportTextsAreExactSeparateAndNeverWordedAsSuccess()
    {
        Assert.IsFalse(ForbiddenReportWords.IsMatch("it refused before updating"), "whole-word match only");
        Assert.IsTrue(ForbiddenReportWords.IsMatch("Updated."));
        ReviewedUpdateOperation op = RuntimeLimits();
        ReviewedUpdateHistoryView success = Present(op, Outcome.Success, Settled.OwnedWorkStopped, CheckFor(Kind.Confirmed, Reason.None));
        Assert.AreEqual(ConfirmedReport, success.HelperReport);
        Assert.AreEqual("Updated: the target version was verified after the update.", success.Status);
        AssertPresentationRules(success);

        Dictionary<Reason, string> expected = new()
        {
            [Reason.RequestNotAdmitted] = "the request was not accepted (for example, it was no longer fresh or did not come from your account)",
            [Reason.MachineInputsUnsupported] = "Chocolatey's protected machine files or settings could not be confirmed as supported",
            [Reason.SourceNotCommunity] = "Chocolatey could not be confirmed to use only the official Community repository",
            [Reason.InstalledMismatch] = "the installed version it found differed from the reviewed version",
            [Reason.ProfileMismatch] = "Chocolatey's configuration changed after the review",
            [Reason.PreflightQueryFailed] = "its check for the available version did not complete cleanly",
            [Reason.CandidateMismatch] = "the available version it found differed from the reviewed version",
            [Reason.ApprovalExpired] = "the review's time limit passed before the update could start",
        };
        Assert.HasCount(Enum.GetValues<Reason>().Length - 1, expected);
        foreach ((Reason reason, string text) in expected)
        {
            (string? installed, string? candidate) = RefusedVersions(reason);
            ReviewedUpdateHistoryView view = Present(op, Outcome.Refused, Settled.NotInvoked, CheckFor(Kind.Refused, reason));
            Assert.AreEqual("Recorded at the time: reported by PC Steward's administrator helper: it refused before updating because " + text +
                $". Versions it reported: installed {installed ?? "not reported"}; available {candidate ?? "not reported"}.", view.HelperReport);
            AssertPresentationRules(view);
        }
        Assert.IsNull(Present(op, Outcome.Failed, Settled.NotInvoked, CheckFor(Kind.Unconfirmed, Reason.None)).HelperReport);
        Assert.IsNull(Present(op, Outcome.Failed, Settled.NotInvoked, RecordedHelperCandidateCheck.NotObserved).HelperReport);
        Assert.IsNull(Present(op, Outcome.Success, Settled.OwnedWorkStopped, CheckFor(Kind.Confirmed, Reason.None), integrity: false).HelperReport);
        // The diagnostic never produces a report.
        ReviewedUpdateHistoryView diagnosticOnly = Present(op, Outcome.Success, Settled.OwnedWorkStopped, RecordedHelperCandidateCheck.NotObserved,
            diagnostic: "Reported by PC Steward's administrator helper: the exact reviewed version matched its protected machine checks.");
        Assert.IsNull(diagnosticOnly.HelperReport);
    }

    [TestMethod]
    public void StatusAndCandidateTextAreIndependentOfTheHelperKind()
    {
        foreach (ReviewedUpdateOperation op in new[] { RuntimeLimits(), WinGet() })
        foreach ((Outcome outcome, Settled settled) in new[] { (Outcome.Success, Settled.OwnedWorkStopped), (Outcome.Failed, Settled.NotInvoked), (Outcome.Uncertain, Settled.OwnedWorkUnsettled) })
        {
            ReviewedUpdateHistoryView baseline = Present(op, outcome, settled, RecordedHelperCandidateCheck.NotObserved);
            foreach (RecordedHelperCandidateCheck check in new[] { CheckFor(Kind.Confirmed, Reason.None), CheckFor(Kind.Refused, Reason.CandidateMismatch), CheckFor(Kind.Unconfirmed, Reason.None) })
            {
                ReviewedUpdateHistoryView view = Present(op, outcome, settled, check);
                Assert.AreEqual(baseline.Status, view.Status);
                Assert.AreEqual(baseline.CandidateEvidence, view.CandidateEvidence);
                Assert.AreEqual(baseline.BlocksOtherUpdates, view.BlocksOtherUpdates);
                Assert.AreEqual(baseline.Diagnostic, view.Diagnostic);
            }
        }
    }

    // ---- helpers ----

    private static void AssertPresentationRules(ReviewedUpdateHistoryView view)
    {
        Assert.IsFalse(view.Status.Contains("administrator helper", StringComparison.OrdinalIgnoreCase), view.Status);
        if (view.HelperReport is { } report)
        {
            Assert.IsFalse(ForbiddenReportWords.IsMatch(report), report);
            StringAssert.StartsWith(report, "Recorded at the time: reported by PC Steward's administrator helper: ");
            Assert.IsFalse(view.Status.Contains(report, StringComparison.Ordinal));
        }
        foreach (string text in new[] { view.Title, view.Status, view.InstallerMode, view.CandidateEvidence, view.Disclosure ?? string.Empty, view.HelperReport ?? string.Empty })
            Assert.IsFalse(text.Contains("helper qualified", StringComparison.OrdinalIgnoreCase), text);
    }

    private static ReviewedUpdateHistoryView Present(ReviewedUpdateOperation op, Outcome outcome, Settled settled, RecordedHelperCandidateCheck check,
        bool integrity = true, string diagnostic = "d") =>
        ReviewedUpdateHistoryPresentation.Present(new(op.JobId, op, outcome == Outcome.Uncertain ? UpdateDispatchState.Uncertain : UpdateDispatchState.Final,
            ReviewedAt, new ReviewedUpdateTerminalRecord(outcome, ReviewedAt.AddMinutes(1), null, settled, diagnostic, check), integrity));

    private static string ExpectedReport(RecordedHelperCandidateCheck check) => check.Kind == Kind.Confirmed
        ? ConfirmedReport
        : Present(RuntimeLimits(), Outcome.Refused, Settled.NotInvoked, check).HelperReport!;

    private static RecordedHelperCandidateCheck Check(Kind kind, Reason reason, string? installed, string? candidate) =>
        RecordedHelperCandidateCheck.FromDesktopDerived(kind, reason, installed, candidate);

    private static RecordedHelperCandidateCheck CheckFor(Kind kind, Reason reason) => kind switch
    {
        Kind.Confirmed => Check(kind, Reason.None, Installed, Candidate),
        Kind.Refused => Check(kind, reason, RefusedVersions(reason).Installed, RefusedVersions(reason).Candidate),
        _ => Check(kind, Reason.None, null, null),
    };

    private static (string? Installed, string? Candidate) RefusedVersions(Reason reason) => reason switch
    {
        Reason.SourceNotCommunity => (Installed, Candidate),
        Reason.InstalledMismatch => (OtherInstalled, null),
        Reason.ProfileMismatch or Reason.PreflightQueryFailed => (Installed, null),
        Reason.CandidateMismatch => (Installed, OtherCandidate),
        _ => (null, null),
    };

    private static (RecordedHelperCandidateCheck, Outcome, Settled) Violation(string violation) => violation switch
    {
        "confirmed-installed-differs" => (Check(Kind.Confirmed, Reason.None, OtherInstalled, Candidate), Outcome.Success, Settled.OwnedWorkStopped),
        "confirmed-candidate-differs" => (Check(Kind.Confirmed, Reason.None, Installed, OtherCandidate), Outcome.Success, Settled.OwnedWorkStopped),
        "profile-mismatch-installed-differs" => (Check(Kind.Refused, Reason.ProfileMismatch, OtherInstalled, null), Outcome.Refused, Settled.NotInvoked),
        "candidate-mismatch-candidate-equal" => (Check(Kind.Refused, Reason.CandidateMismatch, Installed, Candidate), Outcome.Refused, Settled.NotInvoked),
        "other-reason-candidate-differs" => (Check(Kind.Refused, Reason.SourceNotCommunity, Installed, OtherCandidate), Outcome.Refused, Settled.NotInvoked),
        "installed-mismatch-installed-equal" => (Check(Kind.Refused, Reason.InstalledMismatch, Installed, null), Outcome.Refused, Settled.NotInvoked),
        "confirmed-refused" => (CheckFor(Kind.Confirmed, Reason.None), Outcome.Refused, Settled.NotInvoked),
        "confirmed-elevation-denied" => (CheckFor(Kind.Confirmed, Reason.None), Outcome.ElevationDenied, Settled.NotInvoked),
        "confirmed-restart-required" => (CheckFor(Kind.Confirmed, Reason.None), Outcome.RestartRequired, Settled.OwnedWorkStopped),
        "refused-success" => (CheckFor(Kind.Refused, Reason.ApprovalExpired), Outcome.Success, Settled.OwnedWorkStopped),
        "refused-failed" => (CheckFor(Kind.Refused, Reason.ApprovalExpired), Outcome.Failed, Settled.NotInvoked),
        "refused-cancelled" => (CheckFor(Kind.Refused, Reason.ApprovalExpired), Outcome.Cancelled, Settled.NotInvoked),
        "refused-owned-work-stopped" => (CheckFor(Kind.Refused, Reason.ApprovalExpired), Outcome.Refused, Settled.OwnedWorkStopped),
        "unconfirmed-success" => (CheckFor(Kind.Unconfirmed, Reason.None), Outcome.Success, Settled.OwnedWorkStopped),
        "unconfirmed-unverified" => (CheckFor(Kind.Unconfirmed, Reason.None), Outcome.Unverified, Settled.OwnedWorkStopped),
        "unconfirmed-refused" => (CheckFor(Kind.Unconfirmed, Reason.None), Outcome.Refused, Settled.NotInvoked),
        "confirmed-uncertain-not-invoked" => (CheckFor(Kind.Confirmed, Reason.None), Outcome.Uncertain, Settled.NotInvoked),
        _ => throw new ArgumentOutOfRangeException(nameof(violation)),
    };

    private static UpdateDispatchSettlement Settlement(UpdateDispatchClaim claim, OperationOwnership owner, Settled settled) => settled switch
    {
        Settled.NotInvoked => UpdateDispatchSettlement.BeforeInvocation(claim, owner),
        Settled.OwnedWorkStopped => UpdateDispatchSettlement.FromObservedWork(claim, owner, Task.CompletedTask, true, true, true, false, false),
        Settled.OwnedWorkUnsettled => UpdateDispatchSettlement.FromObservedWork(claim, owner, Task.CompletedTask, false, true, true, false, false),
        _ => throw new ArgumentOutOfRangeException(nameof(settled)),
    };

    private static int? ExitFor(Outcome outcome, Settled settled) =>
        settled == Settled.NotInvoked ? null : outcome == Outcome.RestartRequired ? 3010 : outcome == Outcome.Failed ? 1 : 0;

    // Base: Confirmed, Success, OwnedWorkStopped, frozen versions — readable against the fixture's runtime-limit claim.
    private static string[] BaseLines() =>
    [
        "pc-steward-update-terminal:3", "Success", ReviewedAt.AddMinutes(2).ToString("O", CultureInfo.InvariantCulture), "0", "OwnedWorkStopped",
        "True/True/True/True/False/False", "helper-check:Confirmed", "helper-refusal:None", "helper-installed:" + Installed, "helper-candidate:" + Candidate,
    ];

    private static byte[] Forge(IReadOnlyList<string> lines, string diagnostic = "forged", bool appendLength = true, bool finalLf = true)
    {
        byte[] body = Encoding.UTF8.GetBytes(diagnostic);
        List<string> all = [.. lines];
        if (appendLength) all.Add(body.Length.ToString(CultureInfo.InvariantCulture));
        byte[] header = Encoding.UTF8.GetBytes(string.Join('\n', all) + (finalLf ? "\n" : string.Empty));
        byte[] reserved = new byte[UpdateDispatchTerminal.ReservedBytes];
        header.CopyTo(reserved, 0);
        body.CopyTo(reserved, header.Length);
        return reserved;
    }

    private static string[] Set(params (int Index, string Value)[] changes)
    {
        string[] lines = BaseLines();
        foreach ((int index, string value) in changes) lines[index] = value;
        return lines;
    }

    private static readonly string NotInvokedFacts = "False/False/False/False/False/False";

    private static byte[] ForgedCase(string change) => change switch
    {
        "kind-lowercase" => Forge(Set((6, "helper-check:confirmed"))),
        "kind-numeric" => Forge(Set((6, "helper-check:1"))),
        "kind-unknown" => Forge(Set((6, "helper-check:Matched"))),
        "kind-not-observed" => Forge(Set((6, "helper-check:NotObserved"), (8, "helper-installed:None"), (9, "helper-candidate:None"))),
        "reason-unknown" => Forge(Set((1, "Refused"), (3, "None"), (4, "NotInvoked"), (5, NotInvokedFacts), (6, "helper-check:Refused"), (7, "helper-refusal:Expired"), (8, "helper-installed:None"), (9, "helper-candidate:None"))),
        "reason-numeric" => Forge(Set((1, "Refused"), (3, "None"), (4, "NotInvoked"), (5, NotInvokedFacts), (6, "helper-check:Refused"), (7, "helper-refusal:8"), (8, "helper-installed:None"), (9, "helper-candidate:None"))),
        "confirmed-with-reason" => Forge(Set((7, "helper-refusal:ProfileMismatch"))),
        "unconfirmed-with-reason" => Forge(Set((1, "Failed"), (3, "None"), (4, "NotInvoked"), (5, NotInvokedFacts), (6, "helper-check:Unconfirmed"), (7, "helper-refusal:ApprovalExpired"), (8, "helper-installed:None"), (9, "helper-candidate:None"))),
        "refused-with-none" => Forge(Set((1, "Refused"), (3, "None"), (4, "NotInvoked"), (5, NotInvokedFacts), (6, "helper-check:Refused"), (8, "helper-installed:None"), (9, "helper-candidate:None"))),
        "confirmed-null-version" => Forge(Set((9, "helper-candidate:None"))),
        "confirmed-installed-differs" => Forge(Set((8, "helper-installed:" + OtherInstalled))),
        "request-not-admitted-with-values" => RefusedLines("RequestNotAdmitted", Installed, "None"),
        "candidate-without-installed" => RefusedLines("SourceNotCommunity", "None", Candidate),
        "candidate-with-preflight" => RefusedLines("PreflightQueryFailed", Installed, Candidate),
        "profile-mismatch-installed-null" => RefusedLines("ProfileMismatch", "None", "None"),
        "profile-mismatch-installed-differs" => RefusedLines("ProfileMismatch", OtherInstalled, "None"),
        "candidate-mismatch-candidate-equal" => RefusedLines("CandidateMismatch", Installed, Candidate),
        "other-reason-candidate-differs" => RefusedLines("SourceNotCommunity", Installed, OtherCandidate),
        "installed-mismatch-installed-equal" => RefusedLines("InstalledMismatch", Installed, "None"),
        "confirmed-refused" => Forge(Set((1, "Refused"), (3, "None"), (4, "NotInvoked"), (5, NotInvokedFacts))),
        "confirmed-elevation-denied" => Forge(Set((1, "ElevationDenied"), (3, "None"), (4, "NotInvoked"), (5, NotInvokedFacts))),
        "confirmed-restart-required" => Forge(Set((1, "RestartRequired"), (3, "3010"))),
        "refused-success" => RefusedLines("ApprovalExpired", "None", "None", outcome: "Success", exit: "0", settlement: "OwnedWorkStopped", facts: "True/True/True/True/False/False"),
        "refused-failed" => RefusedLines("ApprovalExpired", "None", "None", outcome: "Failed"),
        "refused-cancelled" => RefusedLines("ApprovalExpired", "None", "None", outcome: "Cancelled"),
        "refused-owned-work-stopped" => RefusedLines("ApprovalExpired", "None", "None", exit: "0", settlement: "OwnedWorkStopped", facts: "True/True/True/True/False/False"),
        "unconfirmed-success" => Forge(Set((6, "helper-check:Unconfirmed"), (8, "helper-installed:None"), (9, "helper-candidate:None"))),
        "unconfirmed-unverified" => Forge(Set((1, "Unverified"), (6, "helper-check:Unconfirmed"), (8, "helper-installed:None"), (9, "helper-candidate:None"))),
        "unconfirmed-refused" => Forge(Set((1, "Refused"), (3, "None"), (4, "NotInvoked"), (5, NotInvokedFacts), (6, "helper-check:Unconfirmed"), (8, "helper-installed:None"), (9, "helper-candidate:None"))),
        "facts-none-with-settlement" => Forge(Set((5, "None"))),
        "facts-malformed" => Forge(Set((5, "True/True/True/True/False"))),
        "prefix-missing" => Forge(Set((8, Installed))),
        "v3-header-seven-lines" => Forge(BaseLines()[..6]),
        "v2-header-eleven-lines" => Forge(Set((0, "pc-steward-update-terminal:2"))),
        "missing-length-line" => Forge(BaseLines(), appendLength: false),
        "header-without-final-lf" => Forge(BaseLines(), diagnostic: string.Empty, finalLf: false),
        "non-zero-padding" => NonZeroPadding(Forge(BaseLines())),
        "diagnostic-too-long" => Forge(BaseLines(), diagnostic: new string('x', 4097)),
        _ => throw new ArgumentOutOfRangeException(nameof(change)),
    };

    private static byte[] RefusedLines(string reason, string installed, string candidate, string outcome = "Refused", string exit = "None",
        string settlement = "NotInvoked", string facts = "False/False/False/False/False/False") =>
        Forge(Set((1, outcome), (3, exit), (4, settlement), (5, facts), (6, "helper-check:Refused"), (7, "helper-refusal:" + reason),
            (8, "helper-installed:" + installed), (9, "helper-candidate:" + candidate)));

    private static byte[] NonZeroPadding(byte[] payload)
    {
        payload[^1] = 1;
        return payload;
    }

    private static int LineOffset(byte[] payload, int line)
    {
        int offset = 0;
        for (int index = 0; index < line; index++) offset = Array.IndexOf(payload, (byte)'\n', offset) + 1;
        return offset;
    }

    // Verbatim copy of the version-2 writer before Part B (rollback fixture: v2 bytes must not change).
    private static byte[] FrozenV2Writer(UpdateDispatchTerminal terminal, UpdateDispatchSettlement? settlement)
    {
        byte[] diagnostic = new UTF8Encoding(false, true).GetBytes(terminal.Diagnostic);
        byte[] header = Encoding.ASCII.GetBytes(string.Join('\n', "pc-steward-update-terminal:2", terminal.Outcome.ToString(),
            terminal.OccurredAt.ToString("O", CultureInfo.InvariantCulture), terminal.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "None",
            settlement?.State.ToString() ?? "InvocationUnknown", settlement?.Facts ?? "None",
            diagnostic.Length.ToString(CultureInfo.InvariantCulture)) + "\n");
        byte[] reserved = new byte[UpdateDispatchTerminal.ReservedBytes];
        header.CopyTo(reserved, 0);
        diagnostic.CopyTo(reserved, header.Length);
        return reserved;
    }

    /// <summary>Verbatim copy of the version-2 parser before Part B, standing in for an older build's history reader.</summary>
    private static class FrozenV2Parser
    {
        internal static bool TryParse(ReadOnlySpan<byte> reserved)
        {
            if (reserved.Length != UpdateDispatchTerminal.ReservedBytes) return false;
            try
            {
                int offset = 0;
                string[] fields = new string[7];
                for (int index = 0; index < fields.Length; index++)
                {
                    int newline = reserved[offset..].IndexOf((byte)'\n');
                    if (newline < 0 || newline > 256) return false;
                    ReadOnlySpan<byte> field = reserved.Slice(offset, newline);
                    foreach (byte value in field) if (value is < 0x20 or > 0x7E) return false;
                    fields[index] = Encoding.ASCII.GetString(field);
                    offset += newline + 1;
                }
                if (fields[0] != "pc-steward-update-terminal:2" ||
                    !Enum.TryParse(fields[1], false, out UpdateDispatchOutcome outcome) || !Enum.IsDefined(outcome) || outcome.ToString() != fields[1] ||
                    !DateTimeOffset.TryParseExact(fields[2], "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
                    !Enum.TryParse(fields[4], false, out UpdateDispatchSettlementState settlement) || !Enum.IsDefined(settlement) || settlement.ToString() != fields[4] ||
                    !int.TryParse(fields[6], NumberStyles.None, CultureInfo.InvariantCulture, out int length) ||
                    length.ToString(CultureInfo.InvariantCulture) != fields[6] || length > 4096 * 4 || offset + length > reserved.Length)
                    return false;
                if (fields[3] != "None" && (!int.TryParse(fields[3], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int exit) ||
                    exit.ToString(CultureInfo.InvariantCulture) != fields[3])) return false;
                foreach (byte value in reserved[(offset + length)..]) if (value != 0) return false;
                _ = new UTF8Encoding(false, true).GetString(reserved.Slice(offset, length));
                return true;
            }
            catch (Exception exception) when (exception is ArgumentException or DecoderFallbackException) { return false; }
        }
    }

    private static OperationOwnership Owner() => new(User, OperationGateContract.NamePrefix + User, false, reconciled: true);

    private static ReviewedUpdateOperation RuntimeLimits(UpdateRouteContract route = UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3)
    {
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new UpdateJobSelection("choco:git", UpdateProvider.Chocolatey,
            UpdateSource.ChocolateyCommunity, "git", Installed, Candidate, UpdateScope.Machine, UpdateArchitecture.Neutral, UpdateChannel.Stable,
            "mapping-v2")).Confirm(ReviewedAt);
        return new(job, User, "choco:git", "machine:choco", "choco-profile-v2:" + new string('A', 64), ReviewedAt.AddSeconds(-1), route,
            ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled), ReviewedUpdateArchitecture.ProviderScriptSelected());
    }

    private static ReviewedUpdateOperation WinGet()
    {
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new("winget:contoso.widget", UpdateProvider.WinGet, UpdateSource.WinGet,
            "Contoso.Widget", "1.0", "1.1", UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "mapping-v1")).Confirm(ReviewedAt);
        return new(job, User, "native:1", "context:1", "review:1", ReviewedAt.AddSeconds(-1), UpdateRouteContract.WinGetUserExactV3,
            ReviewedInstallerMode.Selected(InstallerMode.Silent));
    }

    private static FreshUpdateDispatchEvidence Evidence(ReviewedUpdateOperation op) => new(op.Selection, op.RequestingUserSid, op.NativeInstanceId,
        op.InstallationContextId, "fresh:1", ReviewedAt.AddMinutes(1), op.RouteContract, op.Mode, true, true);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"pcsteward-task50-helper-history-{Guid.NewGuid():N}.db");
        private Fixture()
        {
            Store = new(path, new ChocolateyHelperVersionGrammar());
            NoGrammarStore = new(path);
        }

        /// <summary>The production shape (B2-L1).</summary>
        internal SqliteDatabase Store { get; }
        /// <summary>Test-only constructor: equivalent to an older build for version-3 history reads.</summary>
        internal SqliteDatabase NoGrammarStore { get; }

        internal static async Task<Fixture> CreateAsync()
        {
            Fixture result = new();
            await result.Store.MigrateAsync(CancellationToken.None);
            return result;
        }

        internal async Task<UpdateDispatchClaim?> ClaimWinGetAsync()
        {
            ReviewedUpdateDraft draft = await Store.SaveReviewedOperationAsync(WinGet(), CancellationToken.None);
            return await Store.TryClaimAsync(draft, Evidence(draft.Operation), Owner(), ReviewedAt.AddMinutes(1), CancellationToken.None);
        }

        /// <summary>
        /// The runtime-limit route is not admitted yet (ReviewedUpdateReview.HasSupportedMode), so the store cannot claim it.
        /// This inserts exactly the row TryClaimAsync would have written, for the history-format tests only.
        /// </summary>
        internal async Task<UpdateDispatchClaim> ClaimRuntimeLimitsAsync()
        {
            ReviewedUpdateDraft draft = await Store.SaveReviewedOperationAsync(RuntimeLimits(), CancellationToken.None);
            ReviewedUpdateOperation op = draft.Operation;
            UpdateDispatchClaim claim = new(Guid.NewGuid(), Guid.NewGuid(), draft, Evidence(op), ReviewedAt.AddMinutes(1));
            await ExecuteAsync("""
                INSERT INTO update_dispatch_claims(job_id,claim_id,terminal_event_id,draft_generation,canonical_payload,canonical_digest,job_digest,fresh_evidence,claimed_at,state,terminal_payload)
                VALUES($job,$claim,$event,$generation,$payload,$digest,$jobDigest,$evidence,$at,'DispatchClaimed',zeroblob(16384));
                """,
                ("$job", op.JobId.ToString("D")), ("$claim", claim.ClaimId.ToString("D")), ("$event", claim.TerminalEventId.ToString("D")),
                ("$generation", draft.Generation.ToString("D")), ("$payload", op.GetCanonicalBytes()), ("$digest", op.Digest),
                ("$jobDigest", op.LegacyJobDigest), ("$evidence", claim.Evidence.GetCanonicalBytes()),
                ("$at", claim.ClaimedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)));
            return claim;
        }

        internal Task ForgeFinalAsync(UpdateDispatchClaim claim, string state, byte[] payload) => ExecuteAsync(
            "UPDATE update_dispatch_claims SET state=$state, terminal_payload=$p, terminal_digest=$d WHERE job_id=$job;",
            ("$state", state), ("$p", payload), ("$d", Convert.ToHexString(SHA256.HashData(payload))), ("$job", claim.Draft.Operation.JobId.ToString("D")));

        internal async Task<byte[]> ReadTerminalAsync(Guid jobId)
        {
            await using SqliteConnection connection = new(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT terminal_payload FROM update_dispatch_claims WHERE job_id=$job;";
            command.Parameters.AddWithValue("$job", jobId.ToString("D"));
            return (byte[])(await command.ExecuteScalarAsync())!;
        }

        internal async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using SqliteConnection connection = new(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            foreach ((string name, object value) in parameters) command.Parameters.AddWithValue(name, value);
            await command.ExecuteNonQueryAsync();
        }

        public ValueTask DisposeAsync()
        {
            foreach (string file in new[] { path, path + "-wal", path + "-shm" }) File.Delete(file);
            return ValueTask.CompletedTask;
        }
    }
}

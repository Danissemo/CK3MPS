using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CK3MPS
{
    internal sealed partial class MainForm
    {
        private sealed class OosWatcherScanResult
        {
            public bool HadWork;
            public bool IncidentChanged;
            public bool WorkflowRefreshSuggested;
            public string StatusMessage;
        }

        private void StartOosWatcherServices()
        {
            lock (oosWatcherSync)
            {
                if (oosWatcherCancelSource == null || oosWatcherCancelSource.IsCancellationRequested)
                    oosWatcherCancelSource = new CancellationTokenSource();
                oosWatcherStopping = false;
            }

            ResetOosWatcherFileWatchers();
            oosWatcherTimer.Start();
            ScheduleOosWatcherScan("startup");
        }

        private void StopOosWatcherServices(int timeoutMs)
        {
            Task runningTask = null;
            CancellationTokenSource cancelSource = null;
            lock (oosWatcherSync)
            {
                if (oosWatcherStopping)
                    return;

                oosWatcherStopping = true;
                oosWatcherPending = false;
                oosWatcherPendingReason = "";
                runningTask = oosWatcherTask;
                cancelSource = oosWatcherCancelSource;
                oosWatcherCancelSource = null;
            }

            oosWatcherTimer.Stop();
            DisposeOosWatcher(ref oosWatcherFolderWatcher);
            DisposeOosWatcher(ref oosWatcherLogsWatcher);
            if (cancelSource != null)
                cancelSource.Cancel();

            if (runningTask != null)
            {
                try
                {
                    runningTask.Wait(Math.Max(0, timeoutMs));
                }
                catch (AggregateException)
                {
                }
            }
        }

        private void ResetOosWatcherFileWatchers()
        {
            try
            {
                DisposeOosWatcher(ref oosWatcherFolderWatcher);
                DisposeOosWatcher(ref oosWatcherLogsWatcher);
                TryConfigureOosWatcher(Path.Combine(ck3Docs, "oos"), true, ref oosWatcherFolderWatcher, "*.*");
                TryConfigureOosWatcher(Path.Combine(ck3Docs, "logs"), false, ref oosWatcherLogsWatcher, "error.log");
            }
            catch
            {
                ReportOosWatcherWarningThrottled("Watcher setup failed. CK3MPS will keep using periodic scans.");
            }
        }

        private void TryConfigureOosWatcher(string path, bool includeSubdirectories, ref FileSystemWatcher watcher, string filter)
        {
            if (String.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return;

            watcher = new FileSystemWatcher(path);
            watcher.IncludeSubdirectories = includeSubdirectories;
            watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size;
            watcher.Filter = String.IsNullOrWhiteSpace(filter) ? "*.*" : filter;
            watcher.Created += HandleOosWatcherFileEvent;
            watcher.Changed += HandleOosWatcherFileEvent;
            watcher.Deleted += HandleOosWatcherFileEvent;
            watcher.Renamed += HandleOosWatcherRenamedEvent;
            watcher.Error += HandleOosWatcherError;
            watcher.EnableRaisingEvents = true;
        }

        private void DisposeOosWatcher(ref FileSystemWatcher watcher)
        {
            try
            {
                if (watcher != null)
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                }
            }
            catch
            {
            }
            watcher = null;
        }

        private void HandleOosWatcherFileEvent(object sender, FileSystemEventArgs e)
        {
            ScheduleOosWatcherScan("fsw:" + (e == null ? "unknown" : e.ChangeType.ToString()));
        }

        private void HandleOosWatcherRenamedEvent(object sender, RenamedEventArgs e)
        {
            ScheduleOosWatcherScan("fsw:rename");
        }

        private void HandleOosWatcherError(object sender, ErrorEventArgs e)
        {
            Exception ex = e == null ? null : e.GetException();
            ReportOosWatcherWarningThrottled("Watcher event stream failed. CK3MPS will keep using periodic scans. " + (ex == null ? "" : ex.Message));
        }

        private void ScheduleOosWatcherScan(string reason)
        {
            bool startWorker = false;
            CancellationTokenSource cancelSource;
            DateTime nowUtc = DateTime.UtcNow;
            lock (oosWatcherSync)
            {
                if (oosWatcherStopping)
                    return;

                if (oosWatcherCancelSource == null || oosWatcherCancelSource.IsCancellationRequested)
                    oosWatcherCancelSource = new CancellationTokenSource();
                cancelSource = oosWatcherCancelSource;

                if (oosWatcherPending && (nowUtc - oosWatcherLastQueuedUtc).TotalMilliseconds < 750)
                    return;

                oosWatcherPending = true;
                oosWatcherPendingReason = reason ?? "";
                oosWatcherLastQueuedUtc = nowUtc;
                if (oosWatcherTask == null || oosWatcherTask.IsCompleted)
                {
                    startWorker = true;
                    oosWatcherTask = Task.Run(delegate { RunOosWatcherLoop(cancelSource.Token); });
                }
            }

            if (startWorker)
                LogVerbose("OOS watcher scheduled from " + NullText(reason) + ".");
        }

        private void RunOosWatcherLoop(CancellationToken cancellationToken)
        {
            while (true)
            {
                string reason;
                lock (oosWatcherSync)
                {
                    if (oosWatcherStopping || cancellationToken.IsCancellationRequested)
                    {
                        oosWatcherTask = null;
                        return;
                    }

                    if (!oosWatcherPending)
                    {
                        oosWatcherTask = null;
                        return;
                    }

                    oosWatcherPending = false;
                    reason = oosWatcherPendingReason;
                    oosWatcherPendingReason = "";
                    oosWatcherProcessCount++;
                }

                try
                {
                    OosWatcherScanResult result = RunOosWatcherScanCore(cancellationToken);
                    if (result != null && result.HadWork)
                        PostOosWatcherUiUpdates(result);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    ReportOosWatcherWarningThrottled("OOS watcher failed during " + NullText(reason) + ": " + ex.Message);
                }
            }
        }

        private OosWatcherScanResult RunOosWatcherScanCore(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string signature = BuildOosWatcherSignatureUnsafe();
            if (String.IsNullOrWhiteSpace(signature))
                return new OosWatcherScanResult();

            lock (oosWatcherSync)
            {
                if (String.Equals(signature, oosWatcherLastSignature, StringComparison.Ordinal))
                    return new OosWatcherScanResult();
                oosWatcherLastSignature = signature;
            }

            DelayOosWatcherForTestsIfRequested(cancellationToken);
            AnalyzeLatestOosReport();
            cancellationToken.ThrowIfCancellationRequested();

            OosDeepInsight insight = AnalyzeLatestOosDeepInsight();
            cancellationToken.ThrowIfCancellationRequested();

            OosIncidentState incident = AnalyzeOosIncidentState();
            string incidentSignature = BuildIncidentStateSignature(incident);
            bool incidentChanged;
            lock (oosWatcherSync)
            {
                incidentChanged = !String.Equals(incidentSignature, currentIncidentStateSignature, StringComparison.Ordinal);
                if (incidentChanged)
                    currentIncidentStateSignature = incidentSignature;
                oosWatcherLastHandledUtc = DateTime.UtcNow;
            }

            if (incidentChanged)
                RecordIncidentHistoryEvent("watcher_detected", incident, "Automatic watcher refresh");

            OosWatcherScanResult result = new OosWatcherScanResult();
            result.HadWork = true;
            result.IncidentChanged = incidentChanged;
            result.WorkflowRefreshSuggested = incidentChanged;
            if (insight.WatcherRecoveryState)
                result.StatusMessage = "OOS watcher: recovery state detected. Review Workflow -> After OOS.";
            else if (!incident.StartAllowed)
                result.StatusMessage = "Incident state: " + incident.Stage + ". Recommended path: " + incident.RecommendedPath + ".";
            return result;
        }

        private void DelayOosWatcherForTestsIfRequested(CancellationToken cancellationToken)
        {
            string raw = Environment.GetEnvironmentVariable("CK3MPS_TEST_OOS_WATCHER_DELAY_MS");
            int delayMs;
            if (!Int32.TryParse(raw, out delayMs) || delayMs <= 0)
                return;

            int remaining = delayMs;
            while (remaining > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int slice = Math.Min(remaining, 100);
                Thread.Sleep(slice);
                remaining -= slice;
            }
        }

        private void PostOosWatcherUiUpdates(OosWatcherScanResult result)
        {
            if (result == null || IsDisposed || Disposing)
                return;

            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (IsDisposed || Disposing)
                        return;

                    if (!String.IsNullOrWhiteSpace(result.StatusMessage))
                        SetStatusText(result.StatusMessage);
                    if (result.WorkflowRefreshSuggested && mainTabs.SelectedTab == workflowPage && !workflowRefreshPending)
                        BeginWorkflowScenarioRefresh();
                });
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void ReportOosWatcherWarningThrottled(string message)
        {
            if (String.IsNullOrWhiteSpace(message))
                return;

            bool shouldReport = false;
            lock (oosWatcherSync)
            {
                DateTime nowUtc = DateTime.UtcNow;
                if ((nowUtc - oosWatcherLastWarningUtc).TotalSeconds >= 15)
                {
                    oosWatcherLastWarningUtc = nowUtc;
                    shouldReport = true;
                }
            }

            if (!shouldReport)
                return;

            Log("WARN " + message);
            if (IsDisposed || Disposing)
                return;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (!IsDisposed && !Disposing)
                        SetStatusText("OOS watcher warning. Review log for details.");
                });
            }
            catch (InvalidOperationException)
            {
            }
        }

        private string BuildOosWatcherSignatureUnsafe()
        {
            StringBuilder sb = new StringBuilder();
            string latestMetadata = FindLatestOosMetadataFile();
            if (!String.IsNullOrWhiteSpace(latestMetadata) && File.Exists(latestMetadata))
            {
                FileInfo info = new FileInfo(latestMetadata);
                sb.Append("meta|").Append(latestMetadata).Append('|').Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks);
            }

            string activeOos = Path.Combine(ck3Docs, "oos");
            if (Directory.Exists(activeOos))
            {
                BoundedTraversalUtilities.TraversalResult files = BoundedTraversalUtilities.EnumerateFilesBounded(
                    activeOos,
                    "*.*",
                    new BoundedTraversalUtilities.TraversalSettings
                    {
                        MaxDirectories = MaxBoundedTraversalDirectories,
                        MaxFiles = MaxWatcherFiles,
                        MaxDepth = MaxBoundedTraversalDepth,
                        MaxElapsedMs = MaxBoundedTraversalElapsedMs
                    });
                foreach (string file in files.Paths)
                {
                    FileInfo info = new FileInfo(file);
                    sb.Append("\n").Append(file).Append('|').Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks);
                }
            }

            string liveErrorLog = Path.Combine(ck3Docs, "logs", "error.log");
            if (File.Exists(liveErrorLog))
            {
                FileInfo info = new FileInfo(liveErrorLog);
                sb.Append("\nerror|").Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks);
            }

            return sb.ToString();
        }

        private OosDeepInsight AnalyzeLatestOosDeepInsight()
        {
            OosDeepInsight insight = new OosDeepInsight();
            string latestMetadata = FindLatestOosMetadataFile();
            insight.MetadataPath = latestMetadata;
            if (String.IsNullOrWhiteSpace(latestMetadata) || !File.Exists(latestMetadata))
                return insight;

            insight.FolderPath = Path.GetDirectoryName(latestMetadata) ?? "";
            insight.OosType = DetectOosTypeFromMetadata(latestMetadata);
            insight.Date = ExtractMetadataValue(latestMetadata, "date");
            if (String.IsNullOrWhiteSpace(insight.Date))
                insight.Date = ExtractMetadataValue(latestMetadata, "time_utc");
            insight.SaveDumpPath = FindSiblingOosFile(insight.FolderPath, "savegame_oos_machineid_*.oos");
            insight.ModifierDumpPath = FindSiblingOosFile(insight.FolderPath, "modifiers_oos_machineid_*.oos");
            insight.ErrorLogPath = FindSiblingOosFile(insight.FolderPath, "error_*.log");
            if (String.IsNullOrWhiteSpace(insight.ErrorLogPath))
                insight.ErrorLogPath = FindSiblingOosFile(insight.FolderPath, "error_*.txt");

            if (File.Exists(insight.SaveDumpPath))
                ParseSavegameOosDump(insight, ReadAllTextSafe(insight.SaveDumpPath));
            if (File.Exists(insight.ModifierDumpPath))
                ParseModifierOosDump(insight, ReadAllTextSafe(insight.ModifierDumpPath));
            if (File.Exists(insight.ErrorLogPath))
                ParseOosErrorLog(insight, ReadAllTextSafe(insight.ErrorLogPath));

            FinalizeOosContamination(insight);
            BuildOosRecoveryDecision(insight);
            return insight;
        }

        private void ParseSavegameOosDump(OosDeepInsight insight, string text)
        {
            insight.RandomSeed = ParseIntMatch(text, @"(?im)^\s*random_seed\s*=\s*(\d+)");
            insight.RandomCount = ParseIntMatch(text, @"(?im)^\s*random_count\s*=\s*(\d+)");
            insight.CharacterMentions += CountRegex(text, @"(?im)\bcharacter\s*=\s*(\d+)");
            insight.ArmyMentions += CountRegex(text, @"(?im)\barmy[_a-z]*\s*=");
            insight.AiMentions += CountRegex(text, @"(?im)\bai_[a-z0-9_]+\s*=");
            insight.ModifierMentions += CountRegex(text, @"(?im)\bmodifier\s*=");

            CollectRegexSamples(text, @"(?im)\bcharacter\s*=\s*(\d+)", insight.CharacterSamples, 8);
            CollectRegexSamples(text, @"(?im)\bmodifier\s*=\s*([a-z0-9_]+)", insight.ModifierSamples, 8);
            CollectRegexSamples(text, @"(?im)\b(ai_[a-z0-9_]+)\s*=", insight.AiSamples, 8);
            CollectRegexSamples(text, @"(?im)\b(army_[a-z0-9_]+)\s*=", insight.ArmySamples, 8);

            if (insight.RandomCount > 0)
                insight.Findings.Add("Save dump contains random_count=" + insight.RandomCount + ".");
            if (insight.CharacterMentions > 0)
                insight.Findings.Add("Save dump references " + insight.CharacterMentions + " character nodes.");
            if (insight.ArmyMentions > 0)
                insight.Findings.Add("Save dump contains " + insight.ArmyMentions + " army-related nodes.");
            if (insight.AiMentions > 0)
                insight.Findings.Add("Save dump contains " + insight.AiMentions + " AI-related nodes.");
        }

        private void ParseModifierOosDump(OosDeepInsight insight, string text)
        {
            insight.ModifierMentions += CountRegex(text, @"(?im)\bcharacter_modifier\s*=\s*\{");
            insight.ModifierMentions += CountRegex(text, @"(?im)\btimed_modifier\s*=\s*\{");
            insight.AiMentions += CountRegex(text, @"(?im)\bai_[a-z0-9_]+\s*=");
            insight.ArmyMentions += CountRegex(text, @"(?im)\barmy_[a-z0-9_]+\s*=");
            insight.ArmyMentions += CountRegex(text, @"(?im)\bmen_at_arms_[a-z0-9_]+\s*=");

            CollectRegexSamples(text, @"(?im)\bowner\s*=\s*(\d+)", insight.CharacterSamples, 8);
            CollectRegexSamples(text, @"(?im)\bmodifier\s*=\s*([a-z0-9_]+)", insight.ModifierSamples, 8);
            CollectRegexSamples(text, @"(?im)\b(ai_[a-z0-9_]+)\s*=", insight.AiSamples, 8);
            CollectRegexSamples(text, @"(?im)\b(army_[a-z0-9_]+|men_at_arms_[a-z0-9_]+)\s*=", insight.ArmySamples, 8);

            if (insight.ModifierMentions > 0)
                insight.Findings.Add("Modifier dump contains " + insight.ModifierMentions + " modifier nodes.");
        }

        private void ParseOosErrorLog(OosDeepInsight insight, string text)
        {
            insight.ScriptErrorCount = CountRegex(text, @"(?im)script system error");
            insight.FailedContextSwitchCount = CountRegex(text, @"(?im)failed context switch");
            insight.NullTargetCount = CountRegex(text, @"(?im)target character was null|target character not found|target character \(.*\) was null");
            insight.RelationErrorCount = CountRegex(text, @"(?im)\bhas_relation_|opinion trigger|is_spouse_of trigger|is_close_family_of trigger|is_consort_of trigger");

            CollectRegexSamples(text, @"(?im)Scope:\s*([^\r\n]+)", insight.CharacterSamples, 8);
            CollectRegexSamples(text, @"(?im)file:\s*common/scripted_modifiers/([^\r\n\[]+)", insight.ModifierSamples, 8);
            CollectRegexSamples(text, @"(?im)\b(ai_[a-z0-9_]+)", insight.AiSamples, 8);

            if (insight.FailedContextSwitchCount > 0)
                insight.Findings.Add("Error log shows " + insight.FailedContextSwitchCount + " failed context switches.");
            if (insight.NullTargetCount > 0)
                insight.Findings.Add("Error log shows " + insight.NullTargetCount + " null-target character errors.");
            if (insight.RelationErrorCount > 0)
                insight.Findings.Add("Error log shows " + insight.RelationErrorCount + " relation/scheme trigger failures.");
        }

        private void FinalizeOosContamination(OosDeepInsight insight)
        {
            int score = 0;
            string latestType = NullText(insight.OosType).ToLowerInvariant();
            List<string> history = new List<string>();
            foreach (string file in FindAllOosMetadataFiles())
            {
                string type = NullText(ExtractMetadataValue(file, "oos_type")).ToLowerInvariant();
                if (type.Length > 0)
                    history.Add(type);
            }

            if (latestType.Contains("living"))
                score += 35;
            if (latestType.Contains("modifier"))
                score += 32;
            if (latestType.Contains("arm"))
                score += 28;
            if (latestType.Contains("ai"))
                score += 24;
            if (latestType.Contains("relation"))
                score += 18;

            score += Math.Min(18, insight.FailedContextSwitchCount * 4);
            score += Math.Min(18, insight.NullTargetCount * 3);
            score += Math.Min(14, insight.RelationErrorCount * 2);
            if (insight.AiMentions > 0)
                score += 12;
            if (insight.ArmyMentions > 0)
                score += 14;
            if (insight.ModifierMentions > 0)
                score += 16;
            if (insight.RandomCount > 0)
                score += 8;

            int repeatedStateful = 0;
            foreach (string item in history)
            {
                if (item.Contains("living") || item.Contains("modifier") || item.Contains("arm") || item.Contains("ai") || item.Contains("relation"))
                    repeatedStateful++;
            }
            insight.RepeatedStateDivergenceCount = Math.Max(0, repeatedStateful - 1);
            if (insight.RepeatedStateDivergenceCount > 0)
                score += Math.Min(28, insight.RepeatedStateDivergenceCount * 12);

            if (insight.RepeatedStateDivergenceCount > 0 && insight.RandomCount > 0)
                score += 14;

            if (score > 100)
                score = 100;
            insight.SessionContaminationScore = score;
            insight.SessionContaminationLevel = score >= 80 ? "CRITICAL" : (score >= 60 ? "HIGH" : (score >= 30 ? "MEDIUM" : "LOW"));
        }

        private void BuildOosRecoveryDecision(OosDeepInsight insight)
        {
            string latestType = NullText(insight.OosType).ToLowerInvariant();
            bool aiState = latestType.Contains("ai")
                || insight.FailedContextSwitchCount > 0
                || insight.NullTargetCount > 0
                || insight.RelationErrorCount > 0;
            bool livingOrModifier = latestType.Contains("living") || latestType.Contains("modifier");
            bool armies = latestType.Contains("arm") || insight.ArmyMentions > 0;
            bool repeated = insight.RepeatedStateDivergenceCount > 0;
            bool contaminated = insight.SessionContaminationScore >= 80 || (insight.RandomCount > 0 && (repeated || livingOrModifier || armies));

            if (contaminated || repeated || livingOrModifier || armies)
            {
                insight.RecoveryPath = "Rollback";
                insight.RecoveryReason = "State divergence is already contaminated or repeated. Continue/hotjoin is unsafe.";
                insight.HotjoinForbidden = true;
            }
            else if (aiState || insight.SessionContaminationScore >= 45)
            {
                insight.RecoveryPath = "Full rehost";
                insight.RecoveryReason = "AI/script divergence is present. Rehost is safer than hotjoin.";
                insight.HotjoinForbidden = true;
            }
            else
            {
                insight.RecoveryPath = "Controlled hotjoin";
                insight.RecoveryReason = "No strong save-state contamination signals were detected.";
                insight.HotjoinForbidden = false;
            }

            insight.WatcherRecoveryState = insight.HotjoinForbidden
                || insight.FailedContextSwitchCount > 0
                || insight.NullTargetCount > 0
                || latestType.Contains("ai");

            insight.Runbook.Add("Stop the session immediately and keep the newest OOS folder.");
            insight.Runbook.Add("Every affected player sends parity first, then OOS data.");
            insight.Runbook.Add("Treat the selected host manual save as the baseline, not Continue.");
            if (String.Equals(insight.RecoveryPath, "Rollback", StringComparison.OrdinalIgnoreCase))
            {
                insight.Runbook.Add("Load an older clean manual save from before the first divergent OOS.");
                insight.Runbook.Add("Create a fresh lobby, compare parity again, then rehost.");
                insight.Runbook.Add("Do not allow controlled hotjoin for this incident.");
            }
            else if (String.Equals(insight.RecoveryPath, "Full rehost", StringComparison.OrdinalIgnoreCase))
            {
                insight.Runbook.Add("Rehost from the selected clean host save after profile and parity checks pass.");
                insight.Runbook.Add("Wait for all players in lobby before unpause.");
                insight.Runbook.Add("Do not allow controlled hotjoin for this incident.");
            }
            else
            {
                insight.Runbook.Add("Keep the game paused, invite only after parity is confirmed clean.");
                insight.Runbook.Add("If the same OOS repeats, escalate immediately to rollback.");
            }
        }

        private void WriteOosDeepReports()
        {
            OosDeepInsight insight = AnalyzeLatestOosDeepInsight();
            OosIncidentState incident = AnalyzeOosIncidentState();

            WriteTextFileIfMeaningfullyChanged(
                StabilizerFile("ck3_stabilizer_latest_oos_deep_report.txt"),
                BuildOosDeepReportText(insight),
                "FILE Deep OOS report written: ",
                "INFO Deep OOS report already up to date: ",
                true);

            WriteTextFileIfMeaningfullyChanged(
                StabilizerFile("ck3_stabilizer_session_contamination_score.txt"),
                BuildOosContaminationReportText(insight),
                "FILE Session contamination score written: ",
                "INFO Session contamination score already up to date: ",
                true);

            WriteTextFileIfMeaningfullyChanged(
                StabilizerFile("ck3_stabilizer_recovery_runbook.txt"),
                BuildOosRecoveryRunbookText(insight),
                "FILE Recovery runbook written: ",
                "INFO Recovery runbook already up to date: ",
                true);

            WriteTextFileIfMeaningfullyChanged(
                StabilizerFile("ck3_stabilizer_incident_state.txt"),
                BuildOosIncidentStateReportText(incident),
                "FILE Incident state report written: ",
                "INFO Incident state report already up to date: ",
                true);
        }

        private OosIncidentState AnalyzeOosIncidentState()
        {
            OosIncidentState state = new OosIncidentState();
            List<string> files = FindAllOosMetadataFiles();
            OosDeepInsight current = AnalyzeLatestOosDeepInsight();
            state.IncidentId = BuildIncidentId(files, current);
            state.SelectedBaselineSave = workflowSelectedSavePath;
            state.RecommendedParityFingerprint = BuildLocalParityFingerprint();
            HostSuitabilityResult host = AnalyzeHostSuitability();
            HostSaveCandidateResult save = AnalyzeWorkflowHostSaveCandidate();
            state.HostSuitabilityScore = host.Score;
            state.SaveSuitabilityScore = save.Score;
            List<string[]> historyRows = ReadIncidentHistoryRows();

            foreach (string file in files)
            {
                OosIncidentEvent evt = new OosIncidentEvent();
                evt.MetadataPath = file;
                evt.OosType = ExtractMetadataValue(file, "oos_type");
                evt.Date = ExtractMetadataValue(file, "date");
                evt.TimestampUtc = File.Exists(file) ? File.GetLastWriteTimeUtc(file) : DateTime.MinValue;
                evt.RecoveryPath = String.Equals(file, current.MetadataPath, StringComparison.OrdinalIgnoreCase) ? current.RecoveryPath : InferHistoricalRecoveryPath(evt.OosType);
                evt.ContaminationLevel = String.Equals(file, current.MetadataPath, StringComparison.OrdinalIgnoreCase) ? current.SessionContaminationLevel : InferHistoricalContaminationLevel(evt.OosType);
                evt.ContaminationScore = String.Equals(file, current.MetadataPath, StringComparison.OrdinalIgnoreCase) ? current.SessionContaminationScore : InferHistoricalContaminationScore(evt.OosType);
                evt.HotjoinForbidden = evt.RecoveryPath != "Controlled hotjoin";
                state.Events.Add(evt);
            }

            if (state.Events.Count == 0 && !String.IsNullOrWhiteSpace(current.MetadataPath))
            {
                OosIncidentEvent evt = new OosIncidentEvent();
                evt.MetadataPath = current.MetadataPath;
                evt.OosType = current.OosType;
                evt.Date = current.Date;
                evt.TimestampUtc = File.Exists(current.MetadataPath) ? File.GetLastWriteTimeUtc(current.MetadataPath) : DateTime.UtcNow;
                evt.RecoveryPath = current.RecoveryPath;
                evt.ContaminationLevel = current.SessionContaminationLevel;
                evt.ContaminationScore = current.SessionContaminationScore;
                evt.HotjoinForbidden = current.HotjoinForbidden;
                state.Events.Add(evt);
            }

            state.Events.Sort(delegate (OosIncidentEvent left, OosIncidentEvent right)
            {
                return left.TimestampUtc.CompareTo(right.TimestampUtc);
            });

            int priorAttempts = 0;
            foreach (string[] row in historyRows)
            {
                if (row.Length < 9)
                    continue;
                if (!String.Equals(row[2], state.IncidentId, StringComparison.OrdinalIgnoreCase))
                    continue;
                priorAttempts++;
            }
            state.PriorAttempts = priorAttempts;

            BuildIncidentStateDecision(state, current, host, save, priorAttempts);
            return state;
        }

        private void BuildIncidentStateDecision(OosIncidentState state, OosDeepInsight current, HostSuitabilityResult host, HostSaveCandidateResult save, int priorAttempts)
        {
            bool hasIncident = !String.IsNullOrWhiteSpace(current.OosType);
            state.StartAllowed = !hasIncident;
            state.HotjoinAllowed = !current.HotjoinForbidden;
            state.RecommendedPath = hasIncident ? current.RecoveryPath : "Start session";
            state.ContinuationRiskScore = hasIncident ? current.SessionContaminationScore : 0;
            state.Confidence = ComputeIncidentConfidence(state, current);

            if (!hasIncident)
            {
                state.Stage = "Ready";
                state.AllowedActions.Add("Start a fresh lobby after parity is clean.");
                return;
            }

            state.Stage = "OOS detected";
            state.Observed.Add("Latest OOS type: " + current.OosType);
            state.Observed.Add("Contamination: " + current.SessionContaminationLevel + " (" + current.SessionContaminationScore + "/100)");
            if (current.FailedContextSwitchCount > 0 || current.NullTargetCount > 0)
                state.Observed.Add("Script/AI errors: failed_context=" + current.FailedContextSwitchCount + ", null_target=" + current.NullTargetCount);
            if (current.CharacterMentions > 0 || current.ModifierMentions > 0 || current.ArmyMentions > 0 || current.AiMentions > 0)
                state.Observed.Add("Deep dump signals: characters=" + current.CharacterMentions + ", modifiers=" + current.ModifierMentions + ", armies=" + current.ArmyMentions + ", ai=" + current.AiMentions);
            if (state.Events.Count > 1)
                state.Observed.Add("Incident history contains " + state.Events.Count + " OOS events.");

            if (current.RecoveryPath == "Rollback")
                state.Interpreted.Add("Session state is contaminated or repeated enough to require rollback.");
            else if (current.RecoveryPath == "Full rehost")
                state.Interpreted.Add("Session state is unsafe for hotjoin and should be restarted through rehost.");
            else
                state.Interpreted.Add("No strong contamination blocker was found yet.");

            if (state.Events.Count >= 2)
                state.Interpreted.Add("This incident has already escalated beyond a first-time transient issue.");

            if (priorAttempts > 1)
                state.Interpreted.Add("This incident was seen across previous runs or recovery attempts: " + priorAttempts + " history entries.");

            state.EscalationLevel = ComputeIncidentEscalation(state, current);
            if (priorAttempts >= 3 && state.EscalationLevel < 3)
                state.EscalationLevel = 3;
            if (state.EscalationLevel >= 3)
                state.Stage = "Rollback required";
            else if (!state.HotjoinAllowed)
                state.Stage = "Recovery decision";
            else
                state.Stage = "Evidence collecting";

            state.RequiredActions.Add("Host must do: keep the selected manual save as the single baseline.");
            state.RequiredActions.Add("Everyone must do: export parity and send OOS data before retrying.");
            if (current.RecoveryPath == "Rollback")
                state.RequiredActions.Add("Host must do: load an older clean manual save from before the first divergent OOS.");
            else if (current.RecoveryPath == "Full rehost")
                state.RequiredActions.Add("Host must do: create a fresh lobby and rehost from the selected clean save.");
            else
                state.RequiredActions.Add("Everyone must do: keep the game paused and only allow controlled hotjoin after parity is clean.");

            if (host.Score < 60)
                state.RequiredActions.Add("Host must do: fix host suitability blockers before any recovery path is attempted.");
            if (save.Score < 70)
                state.RequiredActions.Add("Host must do: repair or replace the selected save before recovery.");

            state.PlayerResponsibilities.Add("Host: choose one recovery path and enforce it for the whole lobby.");
            state.PlayerResponsibilities.Add("All players: use the same save baseline and the same parity snapshot.");
            if (current.HotjoinForbidden)
                state.PlayerResponsibilities.Add("Returning players: do not attempt controlled hotjoin for this incident.");

            state.BlockedActions.Add("Do not continue unpaused from the desynced running session.");
            if (current.HotjoinForbidden)
                state.BlockedActions.Add("Do not use controlled hotjoin for this incident.");
            if (state.Events.Count > 1 && current.SessionContaminationScore >= 60)
                state.BlockedActions.Add("Do not treat this as a simple parity-only issue anymore.");
            if (priorAttempts >= 3)
                state.BlockedActions.Add("Do not repeat the same recovery path blindly; previous attempts already failed or were interrupted.");

            state.AllowedActions.Add("Collect parity, deep OOS reports and raw OOS dumps.");
            if (current.RecoveryPath == "Rollback")
                state.AllowedActions.Add("Rollback to an older clean manual save.");
            if (current.RecoveryPath == "Full rehost" || current.RecoveryPath == "Rollback")
                state.AllowedActions.Add("Create a fresh lobby after host and player blockers are fixed.");
            if (!current.HotjoinForbidden)
                state.AllowedActions.Add("Controlled hotjoin remains available only after parity and OOS checks pass.");
            if (priorAttempts > 0)
                state.PlayerResponsibilities.Add("Host: do not reuse a recovery path that already failed " + priorAttempts + " time(s) for this incident.");
        }

        private string BuildOosIncidentStateReportText(OosIncidentState state)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CK3 incident state");
            sb.AppendLine("Stabilizer: " + AppVersion);
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine("Incident ID: " + state.IncidentId);
            sb.AppendLine("Stage: " + state.Stage);
            sb.AppendLine("Confidence: " + state.Confidence);
            sb.AppendLine("Recommended path: " + state.RecommendedPath);
            sb.AppendLine("Start allowed: " + YesNo(state.StartAllowed));
            sb.AppendLine("Hotjoin allowed: " + YesNo(state.HotjoinAllowed));
            sb.AppendLine("Continuation risk: " + state.ContinuationRiskScore + "/100");
            sb.AppendLine("Escalation level: " + state.EscalationLevel);
            sb.AppendLine("Previous attempts: " + state.PriorAttempts);
            sb.AppendLine("Baseline save: " + NullText(state.SelectedBaselineSave));
            sb.AppendLine("Parity fingerprint: " + NullText(state.RecommendedParityFingerprint));
            sb.AppendLine("Host suitability: " + state.HostSuitabilityScore + "/100");
            sb.AppendLine("Save suitability: " + state.SaveSuitabilityScore + "/100");
            sb.AppendLine();
            AppendIncidentList(sb, "Observed", state.Observed);
            AppendIncidentList(sb, "Interpreted", state.Interpreted);
            AppendIncidentList(sb, "Required actions", state.RequiredActions);
            AppendIncidentList(sb, "Blocked actions", state.BlockedActions);
            AppendIncidentList(sb, "Allowed actions", state.AllowedActions);
            AppendIncidentList(sb, "Player responsibilities", state.PlayerResponsibilities);
            sb.AppendLine();
            sb.AppendLine("History");
            if (state.Events.Count == 0)
            {
                sb.AppendLine("- (none)");
            }
            else
            {
                foreach (OosIncidentEvent evt in state.Events)
                    sb.AppendLine("- " + evt.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + " | " + evt.OosType + " | " + evt.RecoveryPath + " | " + evt.ContaminationLevel + " (" + evt.ContaminationScore + ")");
            }
            return sb.ToString();
        }

        private void AppendIncidentList(StringBuilder sb, string title, List<string> items)
        {
            sb.AppendLine(title);
            if (items == null || items.Count == 0)
                sb.AppendLine("- (none)");
            else
                foreach (string item in items)
                    sb.AppendLine("- " + item);
            sb.AppendLine();
        }

        private int ComputeIncidentEscalation(OosIncidentState state, OosDeepInsight current)
        {
            int level = 0;
            if (!String.IsNullOrWhiteSpace(current.OosType))
                level = 1;
            if (current.HotjoinForbidden || current.SessionContaminationScore >= 45)
                level = 2;
            if (state.Events.Count >= 2 || current.SessionContaminationScore >= 80)
                level = 3;
            if (state.Events.Count >= 3 || (current.RepeatedStateDivergenceCount > 0 && current.RandomCount > 0))
                level = 4;
            return level;
        }

        private string ComputeIncidentConfidence(OosIncidentState state, OosDeepInsight current)
        {
            int score = 0;
            if (!String.IsNullOrWhiteSpace(current.OosType))
                score++;
            if (current.FailedContextSwitchCount > 0 || current.NullTargetCount > 0)
                score++;
            if (current.CharacterMentions > 0 || current.ModifierMentions > 0 || current.ArmyMentions > 0 || current.AiMentions > 0)
                score++;
            if (state.Events.Count >= 2)
                score++;
            return score >= 4 ? "High" : (score >= 2 ? "Medium" : "Low");
        }

        private string BuildIncidentId(List<string> files, OosDeepInsight current)
        {
            if ((files == null || files.Count == 0) && String.IsNullOrWhiteSpace(current.MetadataPath))
                return "";
            if (files == null || files.Count == 0)
                return Path.GetFileNameWithoutExtension(current.MetadataPath) + "|" + NullText(current.Date).Replace(".", "_").Replace(" ", "_");
            string first = Path.GetFileNameWithoutExtension(files[files.Count - 1]);
            string date = NullText(current.Date).Replace(".", "_").Replace(" ", "_");
            return first + "|" + date;
        }

        private string BuildIncidentStateSignature(OosIncidentState state)
        {
            if (state == null)
                return "";
            return state.IncidentId
                + "|" + state.Stage
                + "|" + state.RecommendedPath
                + "|" + state.ContinuationRiskScore
                + "|" + state.EscalationLevel
                + "|" + state.Events.Count;
        }

        private void RecordIncidentHistoryEvent(string trigger, OosIncidentState state, string note)
        {
            try
            {
                if (state == null || String.IsNullOrWhiteSpace(state.IncidentId))
                    return;

                string path = StabilizerFile("ck3_stabilizer_incident_history.jsonl");
                AtomicWriteResult result = SafeAtomicFile.TryAppendText(path, IncidentHistoryJsonUtilities.BuildJsonLine(
                    DateTime.UtcNow.ToString("o"),
                    EscapeIncidentHistory(trigger),
                    EscapeIncidentHistory(state.IncidentId),
                    EscapeIncidentHistory(state.Stage),
                    EscapeIncidentHistory(state.RecommendedPath),
                    state.ContinuationRiskScore,
                    EscapeIncidentHistory(state.Confidence),
                    state.HotjoinAllowed,
                    EscapeIncidentHistory(note)) + Environment.NewLine, Encoding.UTF8);
                if (!result.Succeeded)
                    throw new IOException(result.Message);
            }
            catch
            {
            }
        }

        private List<string[]> ReadIncidentHistoryRows()
        {
            List<string[]> rows = new List<string[]>();
            try
            {
                string path = StabilizerFile("ck3_stabilizer_incident_history.jsonl");
                if (!File.Exists(path))
                    return rows;

                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    if (String.IsNullOrWhiteSpace(raw))
                        continue;
                    string[] parsed = IncidentHistoryJsonUtilities.ParseLine(raw);
                    if (parsed != null && parsed.Length > 0)
                        rows.Add(parsed);
                }
            }
            catch
            {
            }
            return rows;
        }

        private string EscapeIncidentHistory(string text)
        {
            return NullText(text).Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
        }

        private string InferHistoricalRecoveryPath(string oosType)
        {
            string type = NullText(oosType).ToLowerInvariant();
            if (type.Contains("living") || type.Contains("modifier") || type.Contains("arm"))
                return "Rollback";
            if (type.Contains("ai") || type.Contains("relation"))
                return "Full rehost";
            return "Controlled hotjoin";
        }

        private string InferHistoricalContaminationLevel(string oosType)
        {
            int score = InferHistoricalContaminationScore(oosType);
            return score >= 80 ? "CRITICAL" : (score >= 60 ? "HIGH" : (score >= 30 ? "MEDIUM" : "LOW"));
        }

        private int InferHistoricalContaminationScore(string oosType)
        {
            string type = NullText(oosType).ToLowerInvariant();
            if (type.Contains("living"))
                return 85;
            if (type.Contains("modifier"))
                return 80;
            if (type.Contains("arm"))
                return 75;
            if (type.Contains("ai"))
                return 60;
            if (type.Contains("relation"))
                return 50;
            return 25;
        }

        private string BuildOosDeepReportText(OosDeepInsight insight)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CK3 deep OOS analysis");
            sb.AppendLine("Stabilizer: " + AppVersion);
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            if (String.IsNullOrWhiteSpace(insight.MetadataPath))
            {
                sb.AppendLine("No OOS metadata found.");
                return sb.ToString();
            }

            sb.AppendLine("Metadata: " + insight.MetadataPath);
            sb.AppendLine("Folder: " + insight.FolderPath);
            sb.AppendLine("OOS type: " + NullText(insight.OosType));
            sb.AppendLine("Date: " + NullText(insight.Date));
            sb.AppendLine("Save dump: " + NullText(insight.SaveDumpPath));
            sb.AppendLine("Modifier dump: " + NullText(insight.ModifierDumpPath));
            sb.AppendLine("Error log: " + NullText(insight.ErrorLogPath));
            sb.AppendLine();
            sb.AppendLine("Signals");
            sb.AppendLine("- Session contamination: " + insight.SessionContaminationLevel + " (" + insight.SessionContaminationScore + "/100)");
            sb.AppendLine("- Recovery path: " + insight.RecoveryPath);
            sb.AppendLine("- Controlled hotjoin: " + (insight.HotjoinForbidden ? "FORBIDDEN" : "ALLOWED"));
            sb.AppendLine("- Random seed: " + insight.RandomSeed);
            sb.AppendLine("- Random count: " + insight.RandomCount);
            sb.AppendLine("- Character mentions: " + insight.CharacterMentions);
            sb.AppendLine("- Modifier mentions: " + insight.ModifierMentions);
            sb.AppendLine("- Army mentions: " + insight.ArmyMentions);
            sb.AppendLine("- AI mentions: " + insight.AiMentions);
            sb.AppendLine("- Failed context switch: " + insight.FailedContextSwitchCount);
            sb.AppendLine("- Null target errors: " + insight.NullTargetCount);
            sb.AppendLine("- Relation/script failures: " + insight.RelationErrorCount);
            sb.AppendLine();
            AppendSampleLine(sb, "Character samples", insight.CharacterSamples);
            AppendSampleLine(sb, "Modifier samples", insight.ModifierSamples);
            AppendSampleLine(sb, "Army samples", insight.ArmySamples);
            AppendSampleLine(sb, "AI samples", insight.AiSamples);
            sb.AppendLine();
            sb.AppendLine("Findings");
            if (insight.Findings.Count == 0)
                sb.AppendLine("- (none)");
            else
                foreach (string line in insight.Findings)
                    sb.AppendLine("- " + line);
            return sb.ToString();
        }

        private string BuildOosContaminationReportText(OosDeepInsight insight)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CK3 session contamination score");
            sb.AppendLine("Stabilizer: " + AppVersion);
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine("Level: " + insight.SessionContaminationLevel);
            sb.AppendLine("Score: " + insight.SessionContaminationScore + "/100");
            sb.AppendLine("Repeated state divergence count: " + insight.RepeatedStateDivergenceCount);
            sb.AppendLine("Reason: " + insight.RecoveryReason);
            sb.AppendLine();
            sb.AppendLine("Escalation model");
            sb.AppendLine("- First AI-only OOS or script-context failure: high caution, prefer full rehost.");
            sb.AppendLine("- Repeated LivingCharacters/Modifiers/Armies: rollback is strongly preferred.");
            sb.AppendLine("- Random count plus repeated state divergence: session already contaminated.");
            return sb.ToString();
        }

        private string BuildOosRecoveryRunbookText(OosDeepInsight insight)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CK3 recovery runbook");
            sb.AppendLine("Stabilizer: " + AppVersion);
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine("Recommended path: " + insight.RecoveryPath);
            sb.AppendLine("Controlled hotjoin: " + (insight.HotjoinForbidden ? "FORBIDDEN" : "ALLOWED"));
            sb.AppendLine("Reason: " + insight.RecoveryReason);
            sb.AppendLine();
            sb.AppendLine("Runbook");
            if (insight.Runbook.Count == 0)
                sb.AppendLine("- No runbook available.");
            else
                for (int i = 0; i < insight.Runbook.Count; i++)
                    sb.AppendLine((i + 1).ToString() + ". " + insight.Runbook[i]);
            return sb.ToString();
        }

        private string FindSiblingOosFile(string folder, string pattern)
        {
            try
            {
                if (String.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                    return "";
                string[] files = Directory.GetFiles(folder, pattern, SearchOption.TopDirectoryOnly);
                if (files.Length == 0)
                    return "";
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                return files[0];
            }
            catch
            {
                return "";
            }
        }

        private string ReadAllTextSafe(string path)
        {
            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    int limit = MaxOosTextReadBytes;
                    char[] buffer = new char[8192];
                    StringBuilder sb = new StringBuilder();
                    while (limit > 0)
                    {
                        int read = reader.Read(buffer, 0, Math.Min(buffer.Length, limit));
                        if (read <= 0)
                            break;
                        sb.Append(buffer, 0, read);
                        limit -= read;
                    }
                    if (!reader.EndOfStream)
                        sb.Append(Environment.NewLine).Append("[truncated by CK3MPS]");
                    return sb.ToString();
                }
            }
            catch
            {
                return "";
            }
        }

        private int ParseIntMatch(string text, string pattern)
        {
            Match match = Regex.Match(NullText(text), pattern);
            int value;
            return match.Success && Int32.TryParse(match.Groups[1].Value, out value) ? value : 0;
        }

        private int CountRegex(string text, string pattern)
        {
            return Regex.Matches(NullText(text), pattern).Count;
        }

        private void CollectRegexSamples(string text, string pattern, List<string> target, int limit)
        {
            foreach (Match match in Regex.Matches(NullText(text), pattern))
            {
                if (!match.Success || match.Groups.Count < 2)
                    continue;

                string value = match.Groups[1].Value.Trim();
                if (value.Length == 0)
                    continue;

                bool exists = false;
                foreach (string existing in target)
                {
                    if (String.Equals(existing, value, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                    target.Add(value);
                if (target.Count >= limit)
                    break;
            }
        }

        private void AppendSampleLine(StringBuilder sb, string label, List<string> items)
        {
            sb.AppendLine(label + ": " + (items == null || items.Count == 0 ? "(none)" : String.Join(", ", items.ToArray())));
        }
    }
}

using System;
using System.Collections;
using System.Reflection;
using System.Text;

internal static class WorkflowRepairHarness
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Environment.SetEnvironmentVariable("CK3MPS_SKIP_ELEVATION", "1");
            Environment.SetEnvironmentVariable("CK3MPS_TEST_MODE", "1");

            string assemblyPath = args != null && args.Length > 0 ? args[0] : "CK3MPS.exe";
            bool useManualSelection = args != null && args.Length > 2 && String.Equals(args[1], "manual", StringComparison.OrdinalIgnoreCase);
            string manualSavePath = useManualSelection ? args[2] : "";
            Assembly assembly = Assembly.LoadFrom(assemblyPath);
            Type mainFormType = assembly.GetType("CK3MPS.MainForm", true);
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            object form = Activator.CreateInstance(mainFormType, true);
            try
            {
                if (useManualSelection)
                {
                    mainFormType.GetField("workflowSelectedSavePath", flags).SetValue(form, manualSavePath);
                }

                MethodInfo ensureMethod = mainFormType.GetMethod("EnsureSafeWorkflowHostSave", flags);
                MethodInfo analyzeMethod = mainFormType.GetMethod(useManualSelection ? "AnalyzeWorkflowHostSaveCandidate" : "AnalyzeBestHostSaveCandidate", flags);
                bool repairResult = ensureMethod != null && (bool)ensureMethod.Invoke(form, null);
                object best = analyzeMethod.Invoke(form, null);
                object save = best.GetType().GetField("Save").GetValue(best);

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("RepairResult=" + repairResult);
                sb.AppendLine("Mode=" + (useManualSelection ? "Manual" : "Auto"));
                sb.AppendLine("Verdict=" + NullText(best.GetType().GetField("Verdict").GetValue(best)));
                sb.AppendLine("Score=" + NullText(best.GetType().GetField("Score").GetValue(best)));
                sb.AppendLine("SavePath=" + NullText(save.GetType().GetField("Path").GetValue(save)));
                sb.AppendLine("Rules");

                IEnumerable rules = (IEnumerable)save.GetType().GetField("Rules").GetValue(save);
                foreach (object rule in rules)
                {
                    Type ruleType = rule.GetType();
                    sb.AppendLine(
                        NullText(ruleType.GetField("Id").GetValue(rule))
                        + "|" + NullText(ruleType.GetField("Actual").GetValue(rule))
                        + "|" + NullText(ruleType.GetField("Found").GetValue(rule))
                        + "|" + NullText(ruleType.GetField("Safe").GetValue(rule)));
                }

                Console.Write(sb.ToString());
                return 0;
            }
            finally
            {
                IDisposable disposable = form as IDisposable;
                if (disposable != null)
                    disposable.Dispose();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static string NullText(object value)
    {
        return value == null ? "" : Convert.ToString(value);
    }
}

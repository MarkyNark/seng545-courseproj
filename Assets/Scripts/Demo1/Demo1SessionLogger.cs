using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Exports one anonymized timed mass-sorting trial to a local CSV file.
/// Results are saved to Application.persistentDataPath for both Editor and builds.
/// No participant names or other personally identifying data are collected.
/// </summary>
public static class Demo1SessionLogger
{
    public static string SaveTrial(string application, string sessionId,
        IReadOnlyList<Demo1Controller.SortRecord> records,
        int totalCorrect, int totalDrops, float durationSeconds)
    {
        try
        {
            string directory = Path.Combine(Application.persistentDataPath, "ENSF545_Trials");
            Directory.CreateDirectory(directory);
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            string filePath = Path.Combine(directory, application + "_" + stamp + ".csv");
            StringBuilder csv = new StringBuilder();
            csv.AppendLine("session_id,record_type,application,canister_number,actual_class,chosen_bin,is_correct,drops_before_sort,elapsed_seconds,total_correct,total_drops,total_duration_seconds");
            foreach (Demo1Controller.SortRecord record in records)
            {
                csv.Append(sessionId).Append(",SAMPLE,").Append(application).Append(',');
                csv.Append(record.CanisterNumber).Append(',');
                csv.Append(record.WasAnomalous ? "anomalous" : "standard").Append(',');
                csv.Append(record.PlacedInRejectBin ? "reject" : "standard").Append(',');
                csv.Append(record.Correct ? "1" : "0").Append(',');
                csv.Append(record.DropsBeforeSort).Append(',');
                csv.Append(record.ElapsedSeconds.ToString("F3", CultureInfo.InvariantCulture));
                csv.AppendLine(",,,");
            }
            csv.Append(sessionId).Append(",SUMMARY,").Append(application).Append(",,,,,,,");
            csv.Append(totalCorrect).Append(',').Append(totalDrops).Append(',');
            csv.AppendLine(durationSeconds.ToString("F3", CultureInfo.InvariantCulture));
            File.WriteAllText(filePath, csv.ToString(), new UTF8Encoding(false));
            Debug.Log("Demo1 V4: CSV saved to " + filePath);
            return filePath;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            Debug.LogError("Demo1 V4: CSV export failed: " + ex.Message);
            return null;
        }
    }
}

using PCSteward.Execution;

File.WriteAllText(
    Path.Combine(AppContext.BaseDirectory, ".pcsteward-evidence-helper-started"),
    DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
return await ElevatedHelperEntryPoint.RunNoOpEvidenceAsync(args);

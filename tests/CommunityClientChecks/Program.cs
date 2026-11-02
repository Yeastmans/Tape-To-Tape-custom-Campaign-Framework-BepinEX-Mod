var folder = Path.Combine(Path.GetTempPath(), "t2t-large-upload-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
int checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
try
{
    await LargeUploadChecks.Run(Check, folder);
    Console.WriteLine($"PASS: {checks} large upload client checks");
}
catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
finally { Directory.Delete(folder, true); }
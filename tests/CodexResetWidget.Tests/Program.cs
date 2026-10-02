L10n.Apply(LanguageMode.English);
var passed = 0;
var failed = 0;
void Include((int Passed, int Failed) result) { passed += result.Passed; failed += result.Failed; }

Include(ResetStateTests.Run());
Include(TimeDisplayTests.Run());
Include(CalendarTests.Run());
Include(ScenarioDataTests.Run());
Include(WindowPlacementTests.Run());
Include(SettingsStoreTests.Run());
Include(await ApiClientTests.RunAsync());
Include(await SyncControllerTests.RunAsync());
Include(await JsonCacheStoreTests.RunAsync());
Include(await StorageCompatibilityTests.RunAsync());
Include(await DiagnosticLogTests.RunAsync());
Include(LocalizationTests.Run());
Include(StartupTests.Run());

Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;

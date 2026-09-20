# Localization (i18n) Foundation Architecture Note

## 1. Context & Problem Statement
Prior to this retrofit, Wadd forced all thread and UI cultures unconditionally to `"en-US"` across both desktop and Android entry points (`Wadd.Desktop/Program.cs` and `Wadd.UI/App.axaml.cs`). Hardcoded English strings were baked directly into the 19 Avalonia XAML views and ViewModel properties.

## 2. Architecture Decision: Dynamic Runtime-Switchable Pattern
Avalonia UI lacks WPF's first-class `x:Uid` / compiled BAML localization binding mechanism. Using static lookups such as `{x:Static resx:Strings.SomeKey}` evaluates only once during initial XAML parse time, preventing runtime language changes without restarting the application.

### Chosen Pattern:
- **Service Abstraction**: `ILocalizationService` interface (in `Wadd.Core.Interfaces`) and `LocalizationManager` singleton (in `Wadd.UI.Localization`).
- **Data Binding Reactivity**: `LocalizationManager` implements `INotifyPropertyChanged`. It exposes an indexer `this[string key]` and `GetString(string key)`. When `SetLanguage(code)` is called, it updates `CurrentCulture`, adjusts `CultureInfo.CurrentUICulture` and `CultureInfo.DefaultThreadCurrentUICulture`, and raises `PropertyChanged` for `Item[]`, `Item`, and `string.Empty` (which refreshes all indexer bindings throughout the Avalonia visual tree).
- **XAML Markup Extension**: `LocalizeExtension` (`{loc:Localize SomeKey}`) creates a reactive binding to `LocalizationManager.Instance[SomeKey]`. This ensures consistent, trim-safe, and immediate UI updates across all converted views without needing app restarts.
- **Backing Store**: Standard .NET `.resx` resource files (`Strings.resx` for default/neutral English and `Strings.<culture>.resx` per language). Compiled into assembly resources, trim-safe, AOT-compatible, and leveraged via `ResourceManager`.
- **Fallback Guarantee**: Lookups route through `ResourceManager.GetString(key, culture)`. When a key is missing from a satellite resource set, .NET automatically falls back through parent cultures to the neutral English resource set (`Strings.resx`). If a key is completely absent from all resource files, `LocalizationManager` gracefully returns the raw key name rather than null or throwing an exception.

## 3. Language Selection & Persistence
- **Settings Storage**: `Language` is stored in `AppSettingsData` as an ISO language code (e.g. `"en"`, `"id"`, or `"system"`).
- **Startup Resolution**: On startup, both `Program.cs` and `App.axaml.cs` load `AppSettingsData`. If the setting is `"system"` or empty, it detects `CultureInfo.CurrentUICulture ?? CultureInfo.InstalledUICulture`. If matching supported resources exist (`id`), it activates Indonesian (`id-ID`); otherwise it cleanly falls back to English (`en-US`).
- **Immediate Application**: A language selector in `SettingsView.axaml` triggers `MainViewModel.SelectedLanguage`, which calls `LocalizationManager.Instance.SetLanguage(...)` and immediately re-evaluates all UI text while saving the user preference to local configuration.

## 4. Android-Native Strings (Separation of Concerns)
`TaskReminderReceiver.cs` (background alarm broadcast receiver) and notification channel setup in `AndroidNotificationService.cs` execute outside the Avalonia view tree and often in standalone background receiver processes where the Avalonia UI framework and `LocalizationManager` may not be initialized.

### Intentional Architectural Split:
- Native Android notification strings (channel display names, descriptions, and notification title/body templates) reside in Android's native resource folders:
  - `src/Wadd.Android/Resources/values/strings.xml` (English default)
  - `src/Wadd.Android/Resources/values-id/strings.xml` and `values-in/strings.xml` (Indonesian)
- Strings are retrieved using Android's `context.GetString(Resource.String.xxx)`.
- Android automatically resolves the correct locale according to the system/device configuration.
- **Note**: This split is intentional and documented in code comments within `AndroidNotificationService.cs` and `TaskReminderReceiver.cs` so future contributors do not inadvertently re-route native notification channels through the Avalonia MVVM layer.

## 5. Scope & Converted Views (Phase 1)
To manage retrofitting 19 XAML views safely, Phase 1 focuses on high-traffic surfaces:

### Converted in Phase 1:
1. **`SettingsView.axaml` / `SettingsViewModel.cs`**: Language selection picker, general, appearance, notification, audio, sync, export, and AI configuration headers and descriptions.
2. **`MainView.axaml` / `MainViewModel.cs`**: Navigation drawer items, storage & sync footer, dynamic option lists (`TasksLayoutOptions`, `UpcomingTasksRangeOptions`, `LanguageOptions`), date headers, theme toggles, and status notifications.
3. **`TasksView.axaml`**: Stationary page header, filter chips, composer watermark, section headers ("TODAY TASKS", "COMPLETED TODAY", "UPCOMING TASKS"), empty states, action card shortcuts, and FAB.
4. **`GoalsView.axaml`**: Page title and vision description, new goal button, category pills, progress headers, milestone checklist actions, empty selection states, and goal creation dialog.

### Explicitly Scheduled for Phase 2 (Remaining Views):
The remaining views currently retain hardcoded strings and are scheduled for subsequent migration passes:
1. `CalendarView.axaml`
2. `CompletedTasksView.axaml`
3. `ConflictCenterView.axaml`
4. `DailyNoteView.axaml`
5. `DetailDrawerView.axaml`
6. `ExportDialogView.axaml`
7. `HabitsView.axaml`
8. `LogViewerDialogView.axaml`
9. `MobileNavigationDrawer.axaml`
10. `MobileTaskComposerView.axaml`
11. `QuickAddWindow.axaml`
12. `RecurringTasksView.axaml`
13. `SearchView.axaml`
14. `SyncLogViewerDialogView.axaml`
15. `TagsManagementView.axaml`

## 6. Verification & Automated Testing
- **Key Parity**: `LocalizationTests.ResxFiles_KeyParity_EnglishAndIndonesianMatch` verifies that every key in `Strings.resx` exists in `Strings.id.resx`.
- **Fallback Verification**: `LocalizationTests.LocalizationManager_MissingInNonEnglishCulture_FallsBackToEnglishValue` verifies fallback to English when a translation key is missing in a non-English culture.
- **Dynamic Property Changed**: `LocalizationTests.LocalizationManager_RaisesPropertyChangedOnCultureChange` verifies the `PropertyChanged` event for Avalonia dynamic bindings.
- **Persistence**: `LocalizationTests.AppSettingsHelper_SaveAndLoadSettings_PersistsLanguage` verifies language preference persistence round-trip.

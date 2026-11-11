using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ProfileJanitor
{
    // Okno pomocy (F1). Tresc jest tutaj, w obu jezykach, zeby zmieniala sie razem z programem.
    static class Help
    {
        // H = naglowek, P = akapit, B = punkt listy, C = polecenie (czcionka o stalej szerokosci)
        static readonly string[][] Text =
        {
            new[] { "H", "Do czego służy program", "What the program is for" },
            new[] { "P", "ProfileJanitor pokazuje profile użytkowników na komputerze z Windows i pozwala usunąć te, które nie są już potrzebne: dawno nieużywane, osierocone (konto usunięto, profil został) oraz pozostałości po profilach, które powodują kłopoty z logowaniem. Usuwa wyłącznie profile, czyli folder użytkownika i jego wpis w rejestrze. Nigdy nie usuwa kont - ani lokalnych, ani domenowych.",
                         "ProfileJanitor lists the user profiles on a Windows computer and lets you delete the ones no longer needed: long unused, orphaned (the account was deleted, the profile stayed) and profile leftovers that cause logon problems. It deletes profiles only - the user's folder and its registry entry. It never deletes accounts, local or domain." },

            new[] { "H", "Jak korzystać", "How to use it" },
            new[] { "B", "Program pokazuje profile tego komputera. Żeby obejrzeć inny, wpisz jego nazwę w polu u góry i kliknij Odśwież.", "The program shows this computer's profiles. To look at another one, type its name in the box at the top and click Refresh." },
            new[] { "B", "Zaznacz profile ręcznie albo przyciskami z wiersza \"Zaznacz\": nieużywane od podanej liczby dni, osierocone, pozostałości. Na starcie nic nie jest zaznaczone.", "Tick profiles by hand or with the buttons in the \"Select\" row: unused for a number of days, orphaned, leftovers. Nothing is ticked at start." },
            new[] { "B", "Kliknięcie wiersza rozwija szczegóły: folder, klucz rejestru, rozmiary Pulpitu, Dokumentów i Pobranych. Dwuklik otwiera folder profilu.", "Clicking a row expands its details: folder, registry key, sizes of Desktop, Documents and Downloads. A double-click opens the profile folder." },
            new[] { "B", "Kliknij \"Usuń zaznaczone\". Program pokaże listę do potwierdzenia, a po zakończeniu raport z wynikiem dla każdej pozycji.", "Click \"Delete selected\". The program shows the list for confirmation and, when done, a report with the result for every item." },

            new[] { "H", "Typ i rola", "Type and role" },
            new[] { "B", "Lokalne / Domenowe - konto istnieje.", "Local / Domain - the account exists." },
            new[] { "B", "Osierocone - konta już nie ma, został sam profil. Zwykle bezpieczne do usunięcia.", "Orphaned - the account is gone, only the profile is left. Usually safe to delete." },
            new[] { "B", "Niezweryfikowane - nie udało się sprawdzić konta, bo kontroler domeny nie odpowiada. Program nigdy nie zaznacza takich profili sam.", "Unverified - the account could not be checked because the domain controller does not answer. The program never selects such profiles on its own." },
            new[] { "B", "Pozostałość - klucz .bak w rejestrze, folder bez wpisu w rejestrze albo wpis bez folderu. To one powodują logowanie na profil tymczasowy i foldery z dopiskiem w nazwie.", "Leftover - a .bak registry key, a folder without a registry entry, or an entry without a folder. These cause temporary-profile logons and folders with a suffix in the name." },
            new[] { "B", "Rola: czerwona etykieta to wbudowany administrator, pomarańczowa to administrator tego komputera, zielona to zwykły użytkownik. Rola konta domenowego pochodzi z jego ostatniego logowania na tym komputerze.", "Role: a red tag is the built-in administrator, orange is an administrator of this computer, green is a standard user. A domain account's role comes from its last logon on this computer." },

            new[] { "H", "Na co uważać", "What to watch out for" },
            new[] { "B", "Usunięcia nie da się cofnąć. Znika cały folder profilu: pulpit, dokumenty, pobrane pliki, ustawienia programów, zapisane hasła.", "Deletion cannot be undone. The whole profile folder goes: desktop, documents, downloads, application settings, saved passwords." },
            new[] { "B", "Jeśli nie masz pewności, włącz \"Kopia ZIP do\". Program najpierw spakuje profil, a gdy kopia się nie uda, niczego nie usunie.", "If unsure, turn on \"ZIP backup to\". The program zips the profile first and deletes nothing if the backup fails." },
            new[] { "B", "\"Wyloguj zalogowanych\" wylogowuje użytkownika bez pytania go o zgodę. Jego niezapisana praca przepada.", "\"Log off signed-in users\" logs the user off without asking them. Their unsaved work is lost." },
            new[] { "B", "Profil administratora usuwaj tylko świadomie. Zaznaczanie wg wieku domyślnie pomija administratorów i konta lokalne.", "Delete an administrator's profile only deliberately. Selecting by age skips administrators and local accounts by default." },
            new[] { "B", "Konta, które mają zostać na zawsze, dopisz do wykluczeń (przycisk Wykluczenia albo \"Nigdy nie usuwaj\" w szczegółach wiersza). Wykluczonego profilu nie da się zaznaczyć.", "Add accounts that must stay forever to the exclusions (the Exclusions button or \"Never delete\" in the row details). An excluded profile cannot be ticked." },
            new[] { "B", "Wiek profilu liczy się od ostatniego logowania zapisanego w rejestrze. Profil bez tej daty program pokazuje jako \"brak danych\" i nie zaznacza go sam.", "Profile age counts from the last logon recorded in the registry. A profile without that date is shown as \"no data\" and is never selected automatically." },
            new[] { "B", "Do pracy na innym komputerze potrzebne są tam uprawnienia administratora, usługa WMI i udział C$.", "Working on another computer requires administrator rights there, the WMI service and the C$ share." },

            new[] { "H", "Wiersz poleceń", "Command line" },
            new[] { "P", "Tak, programu można używać bez okna - na przykład w zaplanowanym zadaniu. Uruchom wiersz poleceń jako administrator.", "Yes, the program can be used without the window - for example in a scheduled task. Run the command prompt as administrator." },
            new[] { "C", "ProfileJanitor.exe --dump\n    lista profili\n\nProfileJanitor.exe --auto --days 90 --whatif\n    pokazuje, co usunąłby tryb automatyczny - niczego nie usuwa\n\nProfileJanitor.exe --auto --days 90 --orphans --leftovers --backup D:\\Kopie\n    usuwa profile domenowe nieużywane od 90 dni, osierocone i pozostałości, z kopią ZIP\n\nProfileJanitor.exe --delete S-1-5-21-...-1105\n    usuwa jeden profil wskazany przez SID\n\nProfileJanitor.exe --help\n    pełna lista parametrów",
                         "ProfileJanitor.exe --dump\n    list profiles\n\nProfileJanitor.exe --auto --days 90 --whatif\n    show what automatic mode would delete - deletes nothing\n\nProfileJanitor.exe --auto --days 90 --orphans --leftovers --backup D:\\Backups\n    delete domain profiles unused for 90 days, orphaned ones and leftovers, with a ZIP backup\n\nProfileJanitor.exe --delete S-1-5-21-...-1105\n    delete one profile given by its SID\n\nProfileJanitor.exe --help\n    the full list of parameters" },
            new[] { "B", "Tryb --auto domyślnie pomija konta lokalne i administratorów; zmieniają to --include-local i --include-admins.", "--auto skips local accounts and administrators by default; --include-local and --include-admins change that." },
            new[] { "B", "Zawsze zacznij od --whatif. Tryb automatyczny nie pyta o potwierdzenie.", "Always start with --whatif. Automatic mode does not ask for confirmation." },
            new[] { "B", "Z wiersza poleceń program nigdy nie wylogowuje użytkowników; profil w użyciu jest pomijany z błędem.", "From the command line the program never logs users off; a profile in use is skipped with an error." },
            new[] { "B", "Kod wyjścia: 0 = w porządku, 1 = były błędy usuwania, 2 = złe parametry, 3 = błąd krytyczny.", "Exit code: 0 = fine, 1 = some deletions failed, 2 = bad parameters, 3 = fatal error." },

            new[] { "H", "Gdzie zostaje ślad", "Where the record is kept" },
            new[] { "B", "Każde usuwanie trafia do dziennika zdarzeń Windows (Aplikacja, źródło ProfileJanitor) na komputerze, na którym uruchomiono program - razem z nazwą osoby, która je wykonała.", "Every deletion is written to the Windows event log (Application, source ProfileJanitor) on the computer where the program was run - with the name of the person who did it." },
            new[] { "B", "Lista wykluczeń to plik ProfileJanitor.exclude.txt obok programu. Przenosząc program, zabierz go ze sobą.", "The exclusion list is the file ProfileJanitor.exclude.txt next to the program. Take it along when you move the program." },

            new[] { "H", "Licencja i odpowiedzialność", "License and liability" },
            new[] { "P", "ProfileJanitor jest darmowy, na licencji MIT. Używasz go na własną odpowiedzialność: program jest udostępniany w stanie \"takim, jaki jest\", bez żadnej gwarancji, a autor nie odpowiada za utratę danych ani inne szkody wynikłe z jego użycia. Najpierw wypróbuj go na komputerze testowym.",
                         "ProfileJanitor is free software under the MIT license. You use it at your own risk: the program is provided \"as is\", without warranty of any kind, and the author is not liable for data loss or any other damage arising from its use. Try it on a test computer first." },
            new[] { "P", "Program zawiera biblioteki WPF-UI, Costura oraz biblioteki .NET (licencje MIT). Ich noty licencyjne są w pliku THIRD-PARTY-NOTICES.txt dołączonym do kodu źródłowego.",
                         "The program embeds WPF-UI, Costura and .NET libraries (MIT licenses). Their license notices are in the THIRD-PARTY-NOTICES.txt file that accompanies the source code." },
        };

        public static Window Create(Window owner)
        {
            var panel = new StackPanel { Margin = new Thickness(28, 4, 28, 28) };
            foreach (var t in Text)
            {
                string s = L.T(t[1], t[2]);
                var tb = new TextBlock { Text = t[0] == "B" ? "•  " + s : s, TextWrapping = TextWrapping.Wrap, FontSize = 14, LineHeight = 21 };
                tb.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
                if (t[0] == "H") { tb.FontSize = 19; tb.FontWeight = FontWeights.SemiBold; tb.Margin = new Thickness(0, 22, 0, 8); }
                else if (t[0] == "B") tb.Margin = new Thickness(6, 0, 0, 7);
                else if (t[0] == "P") tb.Margin = new Thickness(0, 0, 0, 10);
                if (t[0] != "C") { panel.Children.Add(tb); continue; }
                tb.FontFamily = new FontFamily("Consolas");
                tb.FontSize = 13;
                var box = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 2, 0, 12), Child = tb };
                box.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
                box.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
                box.BorderThickness = new Thickness(1);
                panel.Children.Add(box);
            }

            string title = L.T("ProfileJanitor - pomoc", "ProfileJanitor - help");
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition());
            grid.Children.Add(new Wpf.Ui.Controls.TitleBar { Title = title });
            var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Grid.SetRow(scroll, 1);
            grid.Children.Add(scroll);
            return new Wpf.Ui.Controls.FluentWindow
            {
                Title = title, Owner = owner, Width = 860, Height = 720, MinWidth = 520, MinHeight = 360,
                WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
                ExtendsContentIntoTitleBar = true, Content = grid
            };
        }
    }
}

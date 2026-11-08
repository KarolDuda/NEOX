# Plan testów

## Automatyczne (GitHub Actions, `windows-latest`)
- `tests/Neox.Tests`: katalog (253 unikalne, kompletność danych, brak usług krytycznych i kluczy wyłączających Defendera/Update), silnik (zastosuj/cofnij, weryfikacja i automatyczne cofnięcie, odmowa bez admina, historia), profile, porównanie przed/po, magazyn stanu (odzysk po uszkodzeniu).
- `tests/Neox.Tests.Windows`: prawdziwy rejestr (HKCU, wszystkie typy wartości), czyszczenie plików, parsowanie.

## Ręczne na Windows 10/11 (przed uznaniem za gotowe)
1. Utwórz punkt przywracania. Uruchom bez admina: sprawdź, że zmiany wymagające admina są zablokowane z komunikatem.
2. Uruchom jako administrator. Dla 5 pozycji z każdego rodzaju (rejestr, powercfg, usługa, zadanie, bcdedit, NIC): Zastosuj → sprawdź stan w systemie → Cofnij → sprawdź powrót.
3. Profil „Bezpieczny start” → Historia → Cofnij wszystko.
4. DNS: ustaw → cofnij; ping; MTU.
5. Czyszczenie: dwa bezpieczne cele; pliki w użyciu nie mogą przerwać operacji.
6. Wydajność aplikacji: bezczynność na stronie innej niż Panel = brak timerów; pomiar CPU/RAM w Menedżerze zadań.
7. Wszystkie 9 stron: każdy przycisk wykonuje opisaną akcję.
Wyniki wpisuj w `docs/WYNIKI_TESTOW.md` (jeszcze nie istnieje: nie ma czego wpisać).

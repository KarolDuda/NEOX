# NEOX 2.0: optymalizator Windows (C# · .NET 10 · WinUI 3)

Aplikacja do optymalizacji Windows 10/11 pod gry: 253 ustawienia z opisem korzyści i skutków, profile jednym kliknięciem, cofanie każdej zmiany, sieć, monitoring, menedżery startu / usług / procesów, analiza dysku.

## Instalator (gotowy plik, bez budowania u siebie)
1. Utwórz prywatne repozytorium na github.com i wgraj całą zawartość tego folderu (razem z ukrytym `.github`) na gałąź `main`.
2. Workflow **Build and test NEOX** uruchomi się sam (ok. 5–8 min). Gotowy `Neox_Setup.exe` pojawi się w **Releases** repozytorium.
3. Pobierz, uruchom, zainstaluj. Instalator nie jest podpisany certyfikatem, więc SmartScreen może pokazać ostrzeżenie (Więcej informacji → Uruchom mimo to).

## Bezpieczeństwo
- Brak usług krytycznych (Windows Update, Defender, Zapora, EventLog, RPC) i kluczy wyłączających Defendera/aktualizacje; sprawdzają to testy.
- Każda zmiana: pełna migawka przed zmianą, odczyt po zmianie i automatyczne cofnięcie, jeśli system jej nie przyjął.
- Pozycje wysokiego ryzyka wymagają osobnego potwierdzenia i nie wchodzą do żadnego profilu.
- Start bez administratora; uprawnienia podnoszone tylko na żądanie.

## Struktura
```
src/Neox.Core       katalog, silnik, profile, magazyn stanu (bez Windows API)
src/Neox.Windows    rejestr, powercfg, usługi, bcdedit, sieć, monitoring, czyszczenie
src/Neox.App        interfejs WinUI 3 (MVVM, DI, Serilog)
tests/                  xUnit (Core i warstwa Windows)
installer/              Inno Setup
docs/                   architektura, plan testów
```
Logi: `%LOCALAPPDATA%\Neox\logs`, stan i kopie zapasowe: `%LOCALAPPDATA%\Neox\state.json`.

## Auto-Tune, zdrowie systemu i Game Boost

- **Auto-Tune** skanuje sprzęt (laptop/desktop, RAM, SSD, GPU), dobiera wyłącznie bezpieczne zmiany (zalecane i ostrożne, bez usług), pokazuje plan z uzasadnieniem, opcjonalnie mierzy komputer przed i po (CPU, RAM, opóźnienie timera; zmiany poniżej progu szumu nie są podawane jako poprawa) i cofa wszystko jednym przyciskiem. To nie jest obietnica FPS.
- **Zdrowie systemu** (Panel): wynik 0–100 z jawnych reguł (dysk, restart, autostart, zastosowane optymalizacje, RAM) z przyciskami „Napraw”.
- **Game Boost**: na czas działania gry z listy przełącza plan zasilania i priorytet procesu; poprzedni plan zapisany w `state.json`, więc wraca nawet po awarii.
- **Eksport/import profili** (JSON). Import jest walidowany: odrzuca nieznane, usługi, eksperymentalne i wysokiego ryzyka.

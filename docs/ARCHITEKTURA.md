# Faza 2 — Architektura (propozycja)

Stos: C#, .NET 10, WinUI 3 (Windows App SDK), MVVM (CommunityToolkit.Mvvm), Microsoft.Extensions.DependencyInjection/Logging, Serilog do pliku. Brak innych zależności.

```
NeoxNext/
├─ src/
│  ├─ Neox.Core/            czysta logika, bez Windows API
│  │   ├─ Tweaks/               ITweak, TweakDefinition, RiskLevel, TweakState
│  │   ├─ Catalog/              wczytywanie katalogu (tweaks.json, 253 pozycje)
│  │   ├─ Backup/               IBackupStore, snapshot, historia zmian, przywracanie
│  │   ├─ Profiles/             profile one-click, własne, per gra
│  │   └─ Reports/              porównanie przed/po
│  ├─ Neox.Windows/         wszystko, co dotyka systemu
│  │   ├─ Registry/             RegistryService (wszystkie typy wartości)
│  │   ├─ Power/                powercfg przez wywołanie procesu z parsowaniem GUID-ów (bez nazw językowych)
│  │   ├─ Services/             ServiceController + zmiana start type
│  │   ├─ Tasks/ Bcd/ Network/ Storage/ Startup/ Processes/
│  │   ├─ Monitoring/           PerformanceCounter + DXGI/WMI, jedna próbka co 1 s tylko gdy widok jest otwarty
│  │   └─ Elevation/            broker admina: aplikacja startuje bez admina, podnosi uprawnienia tylko do zmian
│  ├─ Neox.App/             WinUI 3: widoki, ViewModele, style (ciemny motyw, cyjan/granat)
│  └─ Neox.Tests/           xUnit; testy Core na Linuksie/Windows, testy Windows tylko na Windows
├─ docs/
```

Zasady:
- UI nie zna rejestru ani powercfg; rozmawia z `ITweakService`.
- Każdy tweak = migawka stanu → zmiana → weryfikacja odczytem → wpis do historii. Cofnięcie odtwarza migawkę. Dane w `%LOCALAPPDATA%\Neox\`.
- Poziomy: Zalecane, Ostrożnie, Eksperymentalne, Wysokie ryzyko (osobne potwierdzenie).
- Monitoring bez stałego pollingu: timer działa tylko, gdy widok jest widoczny.
- Strony: Panel, Gry i profile, Optymalizacje (wyszukiwarka), Usługi, Start i procesy, Dysk, Sieć, Informacje o systemie, Historia i przywracanie, Ustawienia.

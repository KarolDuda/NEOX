namespace Tachyvex.Core.Models;

public enum RiskLevel { Recommended, Caution, Experimental, HighRisk }

public enum TweakCategory { Performance, Input, Network, Privacy, Services, Appearance, Cleanup, Tools }

public enum RestartRequirement { None, Restart, Logout }

/// <summary>Efekt deklarowany przez autora starej wersji. To NIE jest wynik pomiaru i UI nie może go tak przedstawiać.</summary>
public enum EffectClaim { Unknown, Large, Medium, HardwareDependent, Small, Negligible, Tool }

public enum TweakKind
{
    Reg, RegPerAdapter, RegPerGame, PowerIdx, PowerPlan, Service, Task, Bcd, Nic, Cmd, Action
}

public enum ActionKind { Clean, Open, Info, Command, Native }

public enum TweakStatus { Applied, NotApplied, Unknown }

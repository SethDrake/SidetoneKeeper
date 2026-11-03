# SidetoneKeeper

A tiny background utility for Windows that keeps **microphone monitoring (sidetone)** on the **HyperX Cloud Alpha S** headset always enabled — without HyperX NGENUITY.

The headset resets sidetone after a reboot, USB reconnect or resume from sleep, and the only official way to turn it back on is NGENUITY (Legacy). SidetoneKeeper does it automatically and replaces NGENUITY if that was the only reason you had it installed.

## How it works

- Every 10 seconds it looks up the headset's HID interface (`VID_03F0&PID_0C91&MI_05&COL03`) via SetupAPI.
- Queries the device status; if sidetone is off, turns it on and verifies.
- No window, no tray icon; a second instance exits immediately (mutex `Local\HyperXSidetoneKeeper`).
- If the headset is unplugged it just waits and picks it up again once connected.
- Footprint: ~4 MB RAM, ~200 handles, stable (no leaks, unlike `NGenuity2Helper.exe`).

## Requirements

- Windows 10 / 11 x64
- .NET Framework 4.x (built into Windows)
- HyperX Cloud Alpha S (USB sound card, PID `0C91`)

## Build

No SDK needed — the compiler ships with .NET Framework:

```bat
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe -nologo -target:winexe -platform:x64 -out:SidetoneKeeper.exe SidetoneKeeper.cs
```

## Installation

1. Put `SidetoneKeeper.exe` in a folder the user can write to (e.g. `C:\Tools`) — the log is created next to the exe.
2. If the exe was copied from another PC or downloaded, unblock it: *Properties → Unblock* or `Unblock-File`.
3. Create a scheduled task that starts it at logon (PowerShell):

```powershell
$exe  = 'C:\Tools\SidetoneKeeper.exe'
$user = "$env:USERDOMAIN\$env:USERNAME"
$a = New-ScheduledTaskAction -Execute $exe -WorkingDirectory (Split-Path $exe)
$t = New-ScheduledTaskTrigger -AtLogOn -User $user
$s = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 999 `
       -RestartInterval (New-TimeSpan -Minutes 1) -MultipleInstances IgnoreNew `
       -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable
$p = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
Register-ScheduledTask -TaskName 'HyperX Sidetone Keeper' -Action $a -Trigger $t -Settings $s -Principal $p -Force
Start-ScheduledTask -TaskName 'HyperX Sidetone Keeper'
```

Administrator rights are not required; the task runs as the current user.

**Check:** turn sidetone off (headset button or NGENUITY) — within ~10 seconds it comes back on and a line appears in `sidetone.log`.

## Uninstall

```powershell
Unregister-ScheduledTask -TaskName 'HyperX Sidetone Keeper' -Confirm:$false
Stop-Process -Name SidetoneKeeper -ErrorAction SilentlyContinue
```

Then delete `SidetoneKeeper.exe` and `sidetone.log`.

## Log

`sidetone.log` is written next to the exe (UTF-8). It records only startup, each time sidetone was re-enabled, and errors (a repeated error is logged once). The log is reset when it exceeds 256 KB. Messages are currently in Russian:

```
2026-10-18 15:54:11  started
2026-10-18 15:54:12  мониторинг был выключен, включён; проверка: True
```

(“sidetone was off, enabled; check: True”)

## Protocol

Reverse-engineered from `NGenuity2Helper.exe` (commands `CMDHSCloudAlphaSGetDeviceStatus` and `CMDHSCloudAlphaSSetSidetone`).

| | |
|---|---|
| Interface | `vid_03f0&pid_0c91&mi_05&col03` |
| Write | output report id `6`, 62 bytes, `HidD_SetOutputReport` (fallback: `WriteFile`) |
| Read | `HidD_GetInputReport`, report id `6` |
| Command header | `b[0]=06  b[2]=01  b[4]=9B  b[7..9]=68 4A 8E` |
| `b[10]=09` | status query; in the reply `r[14]` bit 0 = sidetone on, bit 1 = mic muted |
| `b[10]=07` | **toggle** sidetone (there are no separate on/off commands) |

That is why the tool always reads the state first and toggles only when it is `off`. If the reply is not recognized (e.g. a running NGENUITY grabbed it), it does nothing and retries on the next cycle.

## Limitations

- Cloud Alpha S only (PID `0C91`); other HyperX models use different commands.
- Without NGENUITY, EQ, 7.1 and game/chat balance stay as stored in the headset itself.
- If NGENUITY is running with sidetone disabled, the two programs will keep toggling it — keep sidetone enabled there as well.

## License

MIT — see [LICENSE](LICENSE).
